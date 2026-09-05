using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnitySimpleContainer;
using RosettaUI;

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
        public bool FoldParams => true;

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
        private VolumeProfile _currentVolumeProfile;
        private VolumeProfile _nextVolumeProfile;
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        // 0はVolume、1以降はNextのDeckインデックス + 1。
        private int _selectedEditorItem;
        private int _editorRevision;

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
        /// Current / Nextの各カメラが別々のRuntime Volume Profileだけを読むようにする。
        /// プロファイルはシーンのGlobal Volumeから複製するため、SSRなど未公開の設定も保たれる。
        /// </summary>
        public void ApplyDeckVolumes(Camera currentCamera, Camera nextCamera)
        {
            _params ??= new PostEffectManagerParams();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();
            UpdateNextVolumeToggleButtons();
            if (currentCamera == null || nextCamera == null) return;

            EnsureVolumeProfiles();
            if (_currentVolumeProfile == null || _nextVolumeProfile == null) return;

            var currentContext = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false);
            var nextContext = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true);
            ApplyVolumeSettings(_currentVolumeProfile, _params.CurrentVolume, currentContext);
            ApplyVolumeSettings(_nextVolumeProfile, _params.NextVolume, nextContext);
            ConfigureCameraVolume(currentCamera, _currentVolumeProfile, 30, "Current Deck Volume");
            ConfigureCameraVolume(nextCamera, _nextVolumeProfile, 31, "Next Deck Volume");
        }

        private void UpdateNextVolumeToggleButtons()
        {
            DeckVolumeEffects effects = _params.NextVolume;
            effects.EnsureInitialized();

            if (effects.BloomToggleButton.WasNoteOn)
                effects.BloomEnabled = !effects.BloomEnabled;
            if (effects.DepthOfFieldToggleButton.WasNoteOn)
                effects.DepthOfFieldEnabled = !effects.DepthOfFieldEnabled;

            effects.BloomToggleButton.SetLed(effects.BloomEnabled ? Color.yellow : Color.yellow * 0.15f);
            effects.DepthOfFieldToggleButton.SetLed(
                effects.DepthOfFieldEnabled ? Color.cyan : Color.cyan * 0.15f);
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
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_params.Next), _params.Current);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(_params.NextVolume), _params.CurrentVolume);
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
            DestroyRuntimeProfile(_currentVolumeProfile);
            DestroyRuntimeProfile(_nextVolumeProfile);
            _currentVolumeProfile = null;
            _nextVolumeProfile = null;
            if (_material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
        }

        private void OnDestroy() => Dispose();

        public Element AdditiveUi()
        {
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();

            ClampSelectedEditorItem();
            return UI.Row(
                UI.Box(UI.DynamicElementOnStatusChanged(
                    () => _editorRevision, _ => CreateEditorListElement())).SetWidth(190f).SetFlexShrink(0f),
                UI.Box(UI.DynamicElementOnStatusChanged(
                    () => (_selectedEditorItem, _params.Next.Decks.Count),
                    _ => CreateSelectedEditorElement())).SetMinWidth(420f).SetFlexGrow(1f));
        }

        private Element CreateEditorListElement()
        {
            var items = new List<Element>
            {
                UI.Label("Next Editor"),
                UI.Button(UI.Label(() => $"{(_selectedEditorItem == 0 ? "▶ " : "  ")}Volume Effects"),
                    () => _selectedEditorItem = 0),
                UI.Button("+ Add Deck", AddDeck),
            };

            for (int i = 0; i < _params.Next.Decks.Count; i++)
            {
                int deckIndex = i;
                items.Add(UI.Button(UI.Label(() =>
                    {
                        PostEffectDeck deck = _params.Next.Decks[deckIndex];
                        string name = string.IsNullOrWhiteSpace(deck?.Name) ? $"Deck {deckIndex + 1}" : deck.Name;
                        return $"{(_selectedEditorItem == deckIndex + 1 ? "▶ " : "  ")}{name}";
                    }),
                    () => _selectedEditorItem = deckIndex + 1));
            }

            return UI.Column(items);
        }

        private Element CreateSelectedEditorElement()
        {
            ClampSelectedEditorItem();
            if (_selectedEditorItem == 0)
            {
                return UI.Column(
                    UI.Label("URP Volume Effects"),
                    UI.Field(null, Binder.Create(_params.NextVolume, typeof(DeckVolumeEffects))));
            }

            int deckIndex = _selectedEditorItem - 1;
            PostEffectDeck deck = _params.Next.Decks[deckIndex];
            deck ??= _params.Next.Decks[deckIndex] = new PostEffectDeck();
            deck.EnsureInitialized();
            return UI.Column(
                UI.Row(
                    UI.Field("Name", () => deck.Name, value => deck.Name = value).SetFlexGrow(1f),
                    UI.Button("▲", () => MoveSelectedDeck(-1)).SetWidth(32f),
                    UI.Button("▼", () => MoveSelectedDeck(1)).SetWidth(32f),
                    UI.Button("Delete", DeleteSelectedDeck)),
                UI.Field(null, Binder.Create(deck, typeof(PostEffectDeck))));
        }

        private void AddDeck()
        {
            _params.Next.Decks.Add(new PostEffectDeck { Name = $"Deck {_params.Next.Decks.Count + 1}" });
            _selectedEditorItem = _params.Next.Decks.Count;
            _editorRevision++;
        }

        private void MoveSelectedDeck(int direction)
        {
            int index = _selectedEditorItem - 1;
            int destination = index + direction;
            if (index < 0 || destination < 0 || destination >= _params.Next.Decks.Count) return;
            PostEffectDeck deck = _params.Next.Decks[index];
            _params.Next.Decks.RemoveAt(index);
            _params.Next.Decks.Insert(destination, deck);
            _selectedEditorItem = destination + 1;
            _editorRevision++;
        }

        private void DeleteSelectedDeck()
        {
            int index = _selectedEditorItem - 1;
            if (index < 0 || index >= _params.Next.Decks.Count) return;
            _params.Next.Decks.RemoveAt(index);
            _selectedEditorItem = 0;
            _editorRevision++;
        }

        private void ClampSelectedEditorItem()
        {
            _params.Next.Decks ??= new List<PostEffectDeck>();
            _selectedEditorItem = Mathf.Clamp(_selectedEditorItem, 0, _params.Next.Decks.Count);
        }

        private void EnsureVolumeProfiles()
        {
            if (_currentVolumeProfile != null && _nextVolumeProfile != null) return;

            VolumeProfile source = FindGlobalVolumeProfile();
            if (source == null) return;
            _currentVolumeProfile ??= CreateRuntimeProfile(source, "Current Deck Volume Profile");
            _nextVolumeProfile ??= CreateRuntimeProfile(source, "Next Deck Volume Profile");
        }

        private static VolumeProfile FindGlobalVolumeProfile()
        {
            foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (volume.isGlobal && volume.sharedProfile != null)
                    return volume.sharedProfile;
            }
            return null;
        }

        private static VolumeProfile CreateRuntimeProfile(VolumeProfile source, string profileName)
        {
            // VolumeProfileを単にInstantiateすると、componentsリスト内のSubAsset参照が
            // Current / Next間で共有され得る。各Componentも複製して完全に分離する。
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = profileName;
            profile.hideFlags = HideFlags.HideAndDontSave;
            foreach (VolumeComponent component in source.components)
            {
                if (component == null) continue;
                var copy = Instantiate(component);
                copy.hideFlags = HideFlags.HideAndDontSave;
                profile.components.Add(copy);
            }
            return profile;
        }

        private static void ConfigureCameraVolume(Camera camera, VolumeProfile profile, int layer, string volumeName)
        {
            Transform child = camera.transform.Find(volumeName);
            if (child == null)
            {
                var volumeObject = new GameObject(volumeName) { hideFlags = HideFlags.DontSave };
                child = volumeObject.transform;
                child.SetParent(camera.transform, false);
                volumeObject.layer = layer;
                Volume volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 100f;
            }

            child.gameObject.layer = layer;
            Volume deckVolume = child.GetComponent<Volume>();
            deckVolume.sharedProfile = profile;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.volumeLayerMask = 1 << layer;
        }

        private static void ApplyVolumeSettings(
            VolumeProfile profile, DeckVolumeEffects settings, in ModulationContext context)
        {
            settings.EnsureInitialized();
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            bloom.active = settings.BloomEnabled;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = Mathf.Max(0f, settings.BloomIntensity.Evaluate(context));
            bloom.threshold.overrideState = true;
            bloom.threshold.value = Mathf.Max(0f, settings.BloomThreshold.Evaluate(context));
            bloom.scatter.overrideState = true;
            bloom.scatter.value = Mathf.Clamp01(settings.BloomScatter.Evaluate(context));

            if (!profile.TryGet(out DepthOfField depthOfField)) depthOfField = profile.Add<DepthOfField>(true);
            depthOfField.active = true;
            depthOfField.mode.overrideState = true;
            depthOfField.mode.value = settings.DepthOfFieldEnabled
                ? settings.DepthOfFieldMode == VolumeDepthOfFieldMode.Bokeh
                    ? DepthOfFieldMode.Bokeh
                    : DepthOfFieldMode.Gaussian
                : DepthOfFieldMode.Off;
            depthOfField.focusDistance.overrideState = true;
            depthOfField.focusDistance.value = Mathf.Max(0.1f, settings.FocusDistance.Evaluate(context));
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = Mathf.Clamp(settings.Aperture.Evaluate(context), 1f, 32f);
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = Mathf.Clamp(settings.FocalLength.Evaluate(context), 1f, 300f);
        }

        private static void DestroyRuntimeProfile(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (VolumeComponent component in profile.components)
            {
                if (component == null) continue;
                if (Application.isPlaying) Destroy(component);
                else DestroyImmediate(component);
            }
            if (Application.isPlaying) Destroy(profile);
            else DestroyImmediate(profile);
        }

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
