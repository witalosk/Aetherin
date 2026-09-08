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
        private static readonly int HueId = Shader.PropertyToID("_Hue");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int ValueId = Shader.PropertyToID("_Value");
        private static readonly int BlackLevelId = Shader.PropertyToID("_BlackLevel");
        private static readonly int WhiteLevelId = Shader.PropertyToID("_WhiteLevel");
        private static readonly int GammaId = Shader.PropertyToID("_Gamma");
        private static readonly int ShutterModeId = Shader.PropertyToID("_ShutterMode");
        private static readonly int HandDrawnFrameRateId = Shader.PropertyToID("_HandDrawnFrameRate");

        private Material _material;
        private StackRuntime _current = new();
        private StackRuntime _next = new();
        private StackRuntime _output = new();
        private VolumeProfile _currentVolumeProfile;
        private VolumeProfile _nextVolumeProfile;
        private readonly AutoFocusState _currentAutoFocus = new();
        private readonly AutoFocusState _nextAutoFocus = new();
        private IAudioFeatureProvider _audioFeatureProvider;
        private IBeatManager _beatManager;
        private ICounter _counter;
        private IDeckStateProvider _deckStateProvider;
        // 0はVolume、1以降はNextのDeckインデックス + 1。
        private int _selectedEditorItem;
        private int _editorRevision;

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
            Shader shader = _shader != null ? _shader : Shader.Find("Hidden/Aetherin/PostEffectStack");
            if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void Start()
        {
            _deckStateProvider.NextPromoted += PromoteNextToCurrent;
        }

        public Texture ProcessCurrent(Texture source)
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false, counter: _counter);
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            return Process(source, _params.Current, _current, context);
        }

        public Texture ProcessNext(Texture source) 
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter);
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
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter);
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
            UpdateNextToggleButtons();
            if (currentCamera == null || nextCamera == null) return;

            EnsureVolumeProfiles();
            if (_currentVolumeProfile == null || _nextVolumeProfile == null) return;

            var currentContext = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false, counter: _counter);
            var nextContext = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter);
            ApplyVolumeSettings(_currentVolumeProfile, _params.CurrentVolume, currentCamera, currentContext, _currentAutoFocus);
            ApplyVolumeSettings(_nextVolumeProfile, _params.NextVolume, nextCamera, nextContext, _nextAutoFocus);
            ConfigureCameraVolume(currentCamera, _currentVolumeProfile, 30, "Current Deck Volume");
            ConfigureCameraVolume(nextCamera, _nextVolumeProfile, 31, "Next Deck Volume");
        }

        private void UpdateNextToggleButtons()
        {
            if (_deckStateProvider.IsPreparingNext) return;

            DeckVolumeEffects effects = _params.NextVolume;
            effects.EnsureInitialized();

            if (effects.BloomToggleButton.WasNoteOn)
                effects.BloomEnabled = !effects.BloomEnabled;
            if (effects.DepthOfFieldToggleButton.WasNoteOn)
                effects.DepthOfFieldEnabled = !effects.DepthOfFieldEnabled;

            effects.BloomToggleButton.SetLed(effects.BloomEnabled ? Color.yellow : Color.yellow * 0.15f);
            effects.DepthOfFieldToggleButton.SetLed(
                effects.DepthOfFieldEnabled ? Color.cyan : Color.cyan * 0.15f);

            if (_params.Next?.Decks == null) return;
            foreach (PostEffectDeck deck in _params.Next.Decks)
            {
                if (deck == null) continue;
                deck.EnsureInitialized();
                if (deck.ControlMode == PostEffectControlMode.OutputPad)
                {
                    deck.OutputPad.SetLed(deck.OutputPad.IsNoteOn ? Color.white : Color.white * 0.15f);
                    continue;
                }
                if (deck.ToggleButton.WasNoteOn) deck.Enabled = !deck.Enabled;
                deck.ToggleButton.SetLed(deck.Enabled ? Color.white : Color.white * 0.15f);
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

        private Texture Process(Texture source, PostEffectStack stack, StackRuntime runtime, in ModulationContext context, bool outputOnly = false)
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
                if (outputOnly)
                {
                    if (!isOutputPadDeck || deck.OutputPad?.IsNoteOn != true) continue;
                }
                else if (isOutputPadDeck && (!context.AllowMidi || deck.OutputPad?.IsNoteOn != true))
                {
                    // Output PadはNextのプレビューにも同時に掛けるが、Currentには掛けない。
                    continue;
                }

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
                    module.EnsureInitialized();
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
                    _material.SetFloat(HueId, module.Hue?.Evaluate(context) ?? 0f);
                    _material.SetFloat(SaturationId, module.Saturation?.Evaluate(context) ?? 1f);
                    _material.SetFloat(ValueId, module.Value?.Evaluate(context) ?? 1f);
                    _material.SetFloat(BlackLevelId, module.BlackLevel?.Evaluate(context) ?? 0f);
                    _material.SetFloat(WhiteLevelId, module.WhiteLevel?.Evaluate(context) ?? 1f);
                    _material.SetFloat(GammaId, module.Gamma?.Evaluate(context) ?? 1f);
                    _material.SetInt(ShutterModeId, (int)module.ShutterMode);
                    _material.SetFloat(HandDrawnFrameRateId, module.HandDrawnFrameRate?.Evaluate(context) ?? 8f);
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

        private void OnDestroy()
        {
            if (_deckStateProvider != null)
                _deckStateProvider.NextPromoted -= PromoteNextToCurrent;

            Dispose();
        }

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

        private void ApplyVolumeSettings(
            VolumeProfile profile, DeckVolumeEffects settings, Camera camera,
            in ModulationContext context, AutoFocusState autoFocus)
        {
            settings.EnsureInitialized();
            if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
            // Keep the override active and control the effect with its intensity, as DoF does
            // with DepthOfFieldMode. Toggling VolumeComponent.active causes the inherited
            // Bloom state to win on some URP volume-stack updates.
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = settings.BloomEnabled
                ? Mathf.Max(0f, settings.BloomIntensity.Evaluate(context))
                : 0f;
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
            depthOfField.focusDistance.value = EvaluateFocusDistance(settings, camera, context, autoFocus);
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = Mathf.Clamp(settings.Aperture.Evaluate(context), 1f, 32f);
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = Mathf.Clamp(settings.FocalLength.Evaluate(context), 1f, 300f);
        }

        private static float EvaluateFocusDistance(
            DeckVolumeEffects settings, Camera camera, in ModulationContext context, AutoFocusState state)
        {
            float manualDistance = Mathf.Max(0.1f, settings.FocusDistance.Evaluate(context));
            if (!settings.AutoFocusEnabled || camera == null)
            {
                state.Initialized = false;
                return manualDistance;
            }

            int hitCount = 0;
            float maxDistance = Mathf.Max(0.1f, settings.AutoFocusMaxDistance.Evaluate(context));
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    Ray ray = camera.ViewportPointToRay(new Vector3(x * 0.5f, y * 0.5f, 0f));
                    float distance = FindFocusDistance(ray, camera, maxDistance);
                    if (distance >= 0f) state.HitDistances[hitCount++] = distance;
                }
            }

            float targetDistance = hitCount > 0
                ? GetMedianDistance(state.HitDistances, hitCount)
                : manualDistance;
            if (!state.Initialized)
            {
                state.SmoothedDistance = targetDistance;
                state.Initialized = true;
            }
            else
            {
                float deltaTime = Application.isPlaying ? Time.unscaledDeltaTime : 1f / 60f;
                float lerpFactor = 1f - Mathf.Exp(-Mathf.Max(0f, settings.AutoFocusLerpSpeed.Evaluate(context)) * deltaTime);
                state.SmoothedDistance = Mathf.Lerp(state.SmoothedDistance, targetDistance, lerpFactor);
            }

            return Mathf.Max(0.1f, state.SmoothedDistance);
        }

        private static float FindFocusDistance(Ray ray, Camera camera, float maxDistance)
        {
            float nearestDistance = maxDistance + 1f;
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                nearestDistance = hit.distance;

            // Stage layers are draw-only MeshRenderers and do not own physics colliders.
            // Their world-space bounds are sufficient for selecting a practical focus depth.
            int cameraMask = camera.cullingMask;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || renderer.forceRenderingOff ||
                    (cameraMask & (1 << renderer.gameObject.layer)) == 0) continue;
                if (renderer.bounds.IntersectRay(ray, out float distance) && distance < nearestDistance)
                    nearestDistance = distance;
            }

            return nearestDistance <= maxDistance ? nearestDistance : -1f;
        }

        private static float GetMedianDistance(float[] distances, int count)
        {
            Array.Sort(distances, 0, count);
            int middle = count / 2;
            return count % 2 == 0
                ? (distances[middle - 1] + distances[middle]) * 0.5f
                : distances[middle];
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

        private sealed class AutoFocusState
        {
            public readonly float[] HitDistances = new float[9];
            public float SmoothedDistance;
            public bool Initialized;
        }
    }
}
