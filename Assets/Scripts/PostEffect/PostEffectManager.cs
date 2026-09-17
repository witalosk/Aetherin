using System;
using System.Collections.Generic;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// Current / Nextへ独立した直列ポストエフェクトを実行する。
    /// 一時RTと前フレーム履歴を保持し、フレーム中のGCを発生させない。
    /// </summary>
    public sealed partial class PostEffectManager : MonoBehaviour, IPostEffectManager, ISaveAndUiTarget, IDisposable
    {
        public IParams Params => _params;
        public string Category => UiCategory.Main;
        public bool FoldParams => true;

        [SerializeField] private Shader _shader;
        [SerializeField] private LutLibrary _lutLibrary;
        [SerializeField] private PostEffectManagerParams _params = new();

        private static readonly int SourceTexId = Shader.PropertyToID("_MainTex");
        private static readonly int HistoryTexId = Shader.PropertyToID("_HistoryTex");
        private static readonly int EffectTypeId = Shader.PropertyToID("_EffectType");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int AmountId = Shader.PropertyToID("_Amount");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int SecondaryId = Shader.PropertyToID("_Secondary");
        private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");
        private static readonly int HueId = Shader.PropertyToID("_Hue");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int ValueId = Shader.PropertyToID("_Value");
        private static readonly int BlackLevelId = Shader.PropertyToID("_BlackLevel");
        private static readonly int WhiteLevelId = Shader.PropertyToID("_WhiteLevel");
        private static readonly int GammaId = Shader.PropertyToID("_Gamma");
        private static readonly int ShutterModeId = Shader.PropertyToID("_ShutterMode");
        private static readonly int HandDrawnFrameRateId = Shader.PropertyToID("_HandDrawnFrameRate");
        private static readonly int LightLeakPositionId = Shader.PropertyToID("_LightLeakPosition");
        private static readonly int LightLeakColorId = Shader.PropertyToID("_LightLeakColor");
        private static readonly int LutTexId = Shader.PropertyToID("_LutTex");
        private static readonly int LutParamsId = Shader.PropertyToID("_LutParams");
        private static readonly int LutEnabledId = Shader.PropertyToID("_LutEnabled");
        private static readonly int LutIntensityId = Shader.PropertyToID("_LutIntensity");
        private static readonly int KawaseOffsetId = Shader.PropertyToID("_KawaseOffset");

        private Material _material;
        private StackRuntime _current = new();
        private StackRuntime _next = new();
        private StackRuntime _output = new();
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private ICounter _counter;
        private IDeckStateProvider _deckStateProvider;
        private StageDeck? _lastEditingDeck;

        private StageDeck EditingDeck => _deckStateProvider?.EditingDeck ?? StageDeck.Next;

        [Inject]
        public void Construct(
            IAudioFeatureProvider audioFeatureProvider,
            IBeatManager beatManager,
            ICounter counter,
            IDeckStateProvider deckStateProvider)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
            _counter = counter;
            _deckStateProvider = deckStateProvider;
        }

        private void Awake()
        {
            EnsureLutLibrary();
            Shader shader = _shader != null ? _shader : Shader.Find("Hidden/Aetherin/PostEffectStack");
            if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void Start()
        {
            _deckStateProvider.NextPromoted += PromoteNextToCurrent;
        }

        public Texture ProcessCurrent(Texture source)
        {
            RefreshEditingDeckState();
            bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Current) ?? false;
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter);
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            return Process(source, _params.Current, _current, context, StageDeck.Current);
        }

        public Texture ProcessNext(Texture source) 
        {
            RefreshEditingDeckState();
            bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Next) ?? true;
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter);
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            return Process(source, _params.Next, _next, context, StageDeck.Next);
        }

        /// <summary>
        /// クロスフェード後の最終Outputへ、Pad押下中のDeckだけを即時適用する。
        /// 既存のCurrent / Nextのポストエフェクト経路とは別のパス。
        /// </summary>
        public Texture ProcessOutput(Texture source)
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter);
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            return Process(source, _params.Next, _output, context, StageDeck.Next, true);
        }

        private void RefreshEditingDeckState()
        {
            StageDeck editingDeck = EditingDeck;
            if (_lastEditingDeck == editingDeck) return;

            _lastEditingDeck = editingDeck;
            if (editingDeck != StageDeck.Current) return;

            // Immediateへ入る直前にNextプレビューへ使われていたフェーダー値を固定する。
            // HideInInspectorの古いシリアライズ値を使うと、意図しない強度でエフェクトが復活する。
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            if (_params.Next.Decks == null) return;
            foreach (PostEffectDeck deck in _params.Next.Decks)
            {
                if (deck == null || deck.ControlMode == PostEffectControlMode.OutputPad) continue;
                deck.EnsureInitialized();
                deck.CurrentFaderValue = deck.Fader.IsAssigned ? deck.Fader.GetValue(0f) : 1f;
            }
        }

        /// <summary>
        /// フェーダー到達時に、編集対象だったNextをCurrentへ昇格する。
        /// PreviousFrameBlendの履歴も一緒に移し、新しいNextは履歴なしで開始する。
        /// </summary>
        private void PromoteNextToCurrent()
        {
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();
            CopyNextSettingsToCurrent();
            if (_params.Current.Decks != null)
            {
                foreach (var deck in _params.Current.Decks)
                {
                    if (deck == null) continue;
                    deck.CurrentFaderValue = deck.Fader?.IsAssigned == true
                        ? deck.Fader.GetValue(0f)
                        : 1f;
                }
            }

            _current.Dispose();
            _current = _next;
            // Deck切り替え後は、Current側のElapsed Timeを切り替え時から数え直す。
            _current.Activations.Clear();
            _next = new StackRuntime();
        }

        private void CopyNextSettingsToCurrent()
        {
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_params.Next), _params.Current);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_params.NextVolume), _params.CurrentVolume);
        }

        private Texture Process(Texture source, PostEffectStack stack, StackRuntime runtime,
            in ModulationContext context, StageDeck deckType, bool outputOnly = false)
        {
            if (source == null || _material == null || stack?.Decks == null)
                return source;

            int width = source.width;
            int height = source.height;
            runtime.Ensure(width, height);
            Texture input = source;
            bool wroteAny = false;

            foreach (var deck in stack.Decks)
            {
                if (deck == null) continue;
                deck.EnsureInitialized();
                bool isOutputPadDeck = deck.ControlMode == PostEffectControlMode.OutputPad;
                bool deckIsActive = deck.Enabled && (!outputOnly
                    ? !isOutputPadDeck || (context.AllowMidi && deck.OutputPad?.IsNoteOn == true)
                    : isOutputPadDeck && deck.OutputPad?.IsNoteOn == true);
                ModulationContext deckContext = context.WithElapsedTime(
                    runtime.Activations.Evaluate(deck, deckIsActive, context.Time));
                if (!deckIsActive)
                {
                    if (deck.Modules != null)
                        foreach (PostEffectModule module in deck.Modules)
                            if (module != null) runtime.Activations.Evaluate(module, false, context.Time);
                    continue;
                }
                if (deck.Modules == null) continue;

                if (outputOnly)
                {
                    if (!isOutputPadDeck || deck.OutputPad?.IsNoteOn != true) continue;
                }
                else if (isOutputPadDeck && (!context.AllowMidi || deck.OutputPad?.IsNoteOn != true))
                {
                    // Output PadはNextのプレビューにも同時に掛けるが、Currentには掛けない。
                    continue;
                }

                float deckStrength = Mathf.Clamp01(deck.Strength?.Evaluate(deckContext) ?? 1f);
                if (!outputOnly)
                {
                    deckStrength *= context.AllowMidi
                        ? deck.Fader?.IsAssigned == true ? deck.Fader.GetValue(0f) : 1f
                        : deck.CurrentFaderValue;
                }
                if (deckStrength <= 0f) continue;

                foreach (var module in deck.Modules)
                {
                    if (module == null) continue;
                    ModulationContext moduleContext = deckContext.WithElapsedTime(
                        runtime.Activations.Evaluate(module, module.Enabled && deckIsActive, context.Time));
                    if (!module.Enabled) continue;
                    module.EnsureInitialized();
                    module.GetAvailableLutKeys = GetLutKeys;
                    float strength = deckStrength * Mathf.Clamp01(module.Strength?.Evaluate(moduleContext) ?? 1f);
                    if (strength <= 0f) continue;

                    _material.SetTexture(SourceTexId, input);
                    _material.SetTexture(HistoryTexId, runtime.HistoryValid ? runtime.History : input);
                    _material.SetInt(EffectTypeId, (int)module.Type);
                    _material.SetFloat(StrengthId, strength);
                    _material.SetFloat(AmountId, module.Amount?.Evaluate(moduleContext) ?? 0f);
                    _material.SetFloat(ScaleId, module.Scale?.Evaluate(moduleContext) ?? 1f);
                    _material.SetFloat(SpeedId, module.Speed?.Evaluate(moduleContext) ?? 1f);
                    _material.SetFloat(SecondaryId, module.Secondary?.Evaluate(moduleContext) ?? 0f);
                    _material.SetFloat(TimeValueId, (float)moduleContext.ElapsedTime);
                    _material.SetFloat(HueId, module.Hue?.Evaluate(moduleContext) ?? 0f);
                    _material.SetFloat(SaturationId, module.Saturation?.Evaluate(moduleContext) ?? 1f);
                    _material.SetFloat(ValueId, module.Value?.Evaluate(moduleContext) ?? 1f);
                    _material.SetFloat(BlackLevelId, module.BlackLevel?.Evaluate(moduleContext) ?? 0f);
                    _material.SetFloat(WhiteLevelId, module.WhiteLevel?.Evaluate(moduleContext) ?? 1f);
                    _material.SetFloat(GammaId, module.Gamma?.Evaluate(moduleContext) ?? 1f);
                    _material.SetInt(ShutterModeId, (int)module.ShutterMode);
                    _material.SetFloat(HandDrawnFrameRateId, module.HandDrawnFrameRate?.Evaluate(moduleContext) ?? 8f);
                    _material.SetFloat(LightLeakPositionId, module.LightLeakPosition?.Evaluate(moduleContext) ?? 0.5f);
                    ColorPalette palette = _deckStateProvider?.GetState(deckType)?.Palette;
                    Color lightLeakColor = EvaluatedPaletteColor.Evaluate(
                        module.LightLeakColor, palette, moduleContext).ColorA;
                    _material.SetColor(LightLeakColorId, lightLeakColor);
                    ApplyLut(module, moduleContext);
                    if (module.Type == PostEffectType.CrossBlur)
                    {
                        int iterations = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(
                            module.Scale?.Evaluate(moduleContext) ?? 1f)), 1, 12);
                        float radius = Mathf.Max(0f, module.Amount?.Evaluate(moduleContext) ?? 0f);
                        float passStrength = 1f - Mathf.Pow(1f - strength, 1f / iterations);
                        _material.SetFloat(StrengthId, passStrength);
                        for (int pass = 0; pass < iterations; pass++)
                        {
                            RenderTexture target = runtime.NextTarget(input);
                            _material.SetTexture(SourceTexId, input);
                            _material.SetFloat(KawaseOffsetId,
                                radius * Mathf.Min(input.width, input.height) * (pass + 0.5f) / iterations);
                            Graphics.Blit(input, target, _material);
                            input = target;
                        }
                    }
                    else
                    {
                        RenderTexture target = runtime.NextTarget(input);
                        Graphics.Blit(input, target, _material);
                        input = target;
                    }
                    wroteAny = true;
                }
            }

            if (wroteAny)
            {
                Graphics.Blit(input, runtime.History);
                runtime.HistoryValid = true;
            }
            else if (outputOnly)
            {
                // Padを離した後に、前回の押下中の履歴を次回へ持ち越さない。
                runtime.HistoryValid = false;
            }

            return input;
        }

        public void Dispose()
        {
            _current.Dispose();
            _next.Dispose();
            _output.Dispose();
            DisposeDeckVolumes();
            if (_material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
        }

        private void OnDestroy()
        {
            if (_deckStateProvider != null)
                _deckStateProvider.NextPromoted -= PromoteNextToCurrent;

            Dispose();
        }

        private void EnsureLutLibrary()
        {
            _lutLibrary = LutLibrary.FindBestAvailable(_lutLibrary);
        }

        private IReadOnlyList<string> GetLutKeys()
        {
            EnsureLutLibrary();
            return _lutLibrary?.GetKeys() ?? Array.Empty<string>();
        }

        private void ApplyLut(PostEffectModule module, in ModulationContext context)
        {
            _material.SetFloat(LutEnabledId, 0f);
            if (module.Type != PostEffectType.Lut) return;

            EnsureLutLibrary();
            IReadOnlyList<string> keys = _lutLibrary?.GetKeys();
            if (keys == null || keys.Count == 0) return;
            module.InitializeLutIndex(keys);
            int lutIndex = Mathf.Clamp(module.LutIndex.Evaluate(context), 0, keys.Count - 1);
            module.LutKey = keys[lutIndex];
            Texture2D lut = _lutLibrary.Resolve(module.LutKey);
            bool horizontal = lut != null && lut.width == lut.height * lut.height;
            bool vertical = lut != null && lut.height == lut.width * lut.width;
            if (!horizontal && !vertical) return;

            float size = horizontal ? lut.height : lut.width;
            _material.SetTexture(LutTexId, lut);
            _material.SetVector(LutParamsId,
                new Vector4(size, vertical ? 1f : 0f, 1f / lut.width, 1f / lut.height));
            _material.SetFloat(LutIntensityId, Mathf.Clamp01(module.LutIntensity.Evaluate(context)));
            _material.SetFloat(LutEnabledId, 1f);
        }
        private sealed class StackRuntime : IDisposable
        {
            public readonly ActivationTracker Activations = new();
            public RenderTexture History { get; private set; }
            public bool HistoryValid { get; set; }
            private RenderTexture _ping;
            private RenderTexture _pong;

            public void Ensure(int width, int height)
            {
                if (_ping != null && _ping.width == width && _ping.height == height) return;
                Dispose();
                _ping = Create(width, height, "Post FX Ping");
                _pong = Create(width, height, "Post FX Pong");
                History = Create(width, height, "Post FX History");
            }

            public RenderTexture NextTarget(Texture input) => ReferenceEquals(input, _ping) ? _pong : _ping;

            public void Dispose()
            {
                Release(_ping);
                Release(_pong);
                Release(History);
                _ping = null;
                _pong = null;
                History = null;
                HistoryValid = false;
                Activations.Clear();
            }

            private static RenderTexture Create(int width, int height, string name)
            {
                var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.Create();
                return texture;
            }

            private static void Release(RenderTexture texture)
            {
                if (texture == null) return;
                texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private sealed class ActivationTracker
        {
            private readonly Dictionary<object, Entry> _entries = new();

            public double Evaluate(object key, bool active, double time)
            {
                if (!_entries.TryGetValue(key, out Entry entry))
                {
                    entry = new Entry { Active = active, StartTime = time };
                    _entries.Add(key, entry);
                }
                else if (active && !entry.Active)
                {
                    entry.StartTime = time;
                }

                entry.Active = active;
                return active ? Math.Max(0d, time - entry.StartTime) : 0d;
            }

            public void Clear() => _entries.Clear();

            private sealed class Entry
            {
                public bool Active;
                public double StartTime;
            }
        }

    }
}
