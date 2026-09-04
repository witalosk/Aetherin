using System;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>
    /// Nextへ直列ポストエフェクトを実行する。
    /// 一時RTと前フレーム履歴を保持し、フレーム中のGCを発生させない。
    /// </summary>
    public sealed class PostEffectManager : MonoBehaviour, IPostEffectManager, ISaveAndUiTarget, IDisposable
    {
        public IParams Params => _params;
        public string Category => UiCategory.Main;

        [SerializeField] private Shader _shader;
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

        private Material _material;
        private StackRuntime _current = new();
        private StackRuntime _next = new();
        private StackRuntime _output = new();
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;

        [Inject]
        public void Construct(IAudioFeatureProvider audioFeatureProvider, IBeatManager beatManager)
        {
            _audioFeatureProvider = audioFeatureProvider;
            _beatManager = beatManager;
        }

        private void Awake()
        {
            Shader shader = _shader != null ? _shader : Shader.Find("Hidden/Aetherin/PostEffectStack");
            if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public Texture ProcessCurrent(Texture source)
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false);
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            return Process(source, _params.Current, _current, context);
        }

        public Texture ProcessNext(Texture source) 
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true);
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            return Process(source, _params.Next, _next, context);
        }

        /// <summary>
        /// クロスフェード後の最終Outputへ、Pad押下中のDeckだけを即時適用する。
        /// 既存のCurrent / Nextのポストエフェクト経路とは別のパス。
        /// </summary>
        public Texture ProcessOutput(Texture source)
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true);
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            return Process(source, _params.Next, _output, context, true);
        }

        /// <summary>
        /// フェーダー到達時に、編集対象だったNextをCurrentへ昇格する。
        /// PreviousFrameBlendの履歴も一緒に移し、新しいNextは履歴なしで開始する。
        /// </summary>
        public void PromoteNextToCurrent()
        {
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_params.Next), _params.Current);
            if (_params.Current.Decks != null)
            {
                foreach (var deck in _params.Current.Decks)
                {
                    if (deck == null) continue;
                    deck.CurrentFaderValue = deck.Fader?.IsAssigned == true
                        ? deck.Fader.GetValue(1f)
                        : 1f;
                }
            }

            _current.Dispose();
            _current = _next;
            _next = new StackRuntime();
        }

        private Texture Process(
            Texture source,
            PostEffectStack stack,
            StackRuntime runtime,
            in ModulationContext context,
            bool outputOnly = false)
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
                if (deck == null || !deck.Enabled) continue;
                deck.EnsureInitialized();
                if (deck.Modules == null) continue;

                bool isOutputPadDeck = deck.ControlMode == PostEffectControlMode.OutputPad;
                if (outputOnly != isOutputPadDeck) continue;
                if (outputOnly && deck.OutputPad?.IsNoteOn != true) continue;

                float deckStrength = Mathf.Clamp01(deck.Strength?.Evaluate(context) ?? 1f);
                if (!outputOnly)
                {
                    deckStrength *= context.AllowMidi
                        ? deck.Fader?.IsAssigned == true ? deck.Fader.GetValue(1f) : 1f
                        : deck.CurrentFaderValue;
                }
                if (deckStrength <= 0f) continue;

                foreach (var module in deck.Modules)
                {
                    if (module == null || !module.Enabled) continue;
                    float strength = deckStrength * Mathf.Clamp01(module.Strength?.Evaluate(context) ?? 1f);
                    if (strength <= 0f) continue;

                    RenderTexture target = runtime.NextTarget(input);
                    _material.SetTexture(SourceTexId, input);
                    _material.SetTexture(HistoryTexId, runtime.HistoryValid ? runtime.History : input);
                    _material.SetInt(EffectTypeId, (int)module.Type);
                    _material.SetFloat(StrengthId, strength);
                    _material.SetFloat(AmountId, module.Amount?.Evaluate(context) ?? 0f);
                    _material.SetFloat(ScaleId, module.Scale?.Evaluate(context) ?? 1f);
                    _material.SetFloat(SpeedId, module.Speed?.Evaluate(context) ?? 1f);
                    _material.SetFloat(SecondaryId, module.Secondary?.Evaluate(context) ?? 0f);
                    _material.SetFloat(TimeValueId, (float)context.Time);
                    Graphics.Blit(input, target, _material);
                    input = target;
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
            if (_material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
        }

        private void OnDestroy() => Dispose();

        private sealed class StackRuntime : IDisposable
        {
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
    }
}
