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
        private static readonly int KawaseOffsetId = Shader.PropertyToID("_KawaseOffset");

        private Material _material;
        private StackRuntime _current = new();
        private StackRuntime _next = new();
        private StackRuntime _output = new();
        private VolumeProfile _currentVolumeProfile;
        private VolumeProfile _nextVolumeProfile;
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
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false, counter: _counter);
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            PostEffectStack stack = _params.EditMode == PostEffectEditMode.Immediate
                ? _params.Next
                : _params.Current;
            return Process(source, stack, _current, context);
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

            EnsureVolumeProfiles();
            if (_currentVolumeProfile == null || _nextVolumeProfile == null) return;

            // Swap直後はNextを段階的に再生成するため、数フレームはnextCameraがnullになる。
            // それでも昇格したCurrentのCameraには、再分離処理で変更されたVolume用Layerを
            // 即座に戻す必要がある。両Cameraの存在を必須にすると、この間だけURP Volumeが
            // 見つからず、Bloom/DoFなどが一瞬消える。
            if (currentCamera != null)
            {
                var currentContext = new ModulationContext(
                    Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, false, counter: _counter);
                DeckVolumeEffects currentSettings = _params.EditMode == PostEffectEditMode.Immediate
                    ? _params.NextVolume
                    : _params.CurrentVolume;
                ApplyVolumeSettings(_currentVolumeProfile, currentSettings,
                    currentCamera.GetComponentInParent<CameraStage>()?.ActiveCameraWorkRecipe, currentContext);
                ConfigureCameraVolume(currentCamera, _currentVolumeProfile, 30, "Current Deck Volume");
            }

            if (nextCamera != null)
            {
                var nextContext = new ModulationContext(
                    Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter);
                ApplyVolumeSettings(_nextVolumeProfile, _params.NextVolume,
                    nextCamera.GetComponentInParent<CameraStage>()?.ActiveCameraWorkRecipe, nextContext);
                ConfigureCameraVolume(nextCamera, _nextVolumeProfile, 31, "Next Deck Volume");
            }
        }

        private void UpdateNextToggleButtons()
        {
            if (_deckStateProvider.IsPreparingNext) return;

            DeckVolumeEffects effects = _params.NextVolume;
            effects.EnsureInitialized();

            if (effects.BloomToggleButton.WasNoteOn)
                effects.BloomEnabled = !effects.BloomEnabled;

            effects.BloomToggleButton.SetLed(effects.BloomEnabled ? Color.yellow : Color.yellow * 0.15f);

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

        private void SetEditMode(PostEffectEditMode mode)
        {
            if (_params.EditMode == mode) return;
            if (_params.EditMode == PostEffectEditMode.Immediate && mode == PostEffectEditMode.Next)
                CopyNextSettingsToCurrent();
            _params.EditMode = mode;
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
                    ColorPalette palette = _deckStateProvider?.GetState(
                        context.AllowMidi ? StageDeck.Next : StageDeck.Current)?.Palette;
                    Color lightLeakColor = EvaluatedPaletteColor.Evaluate(
                        module.LightLeakColor, palette, moduleContext).ColorA;
                    _material.SetColor(LightLeakColorId, lightLeakColor);
                    ApplyLut(module);
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
            EnsureLutLibrary();
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            _params.CurrentVolume ??= new DeckVolumeEffects();
            _params.NextVolume ??= new DeckVolumeEffects();

            ClampSelectedEditorItem();
            return UI.Column(
                UI.Field("Edit Mode", () => _params.EditMode, SetEditMode),
                UI.Row(
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => _editorRevision, _ => CreateEditorListElement())).SetWidth(190f).SetFlexShrink(0f),
                    UI.Box(UI.DynamicElementOnStatusChanged(
                        () => (_selectedEditorItem, _params.Next.Decks.Count),
                        _ => CreateSelectedEditorElement())).SetMinWidth(420f).SetFlexGrow(1f)));
        }

        private Element CreateEditorListElement()
        {
            var items = new List<Element>
            {
                UI.Label(() => _params.EditMode == PostEffectEditMode.Immediate
                    ? "Immediate Editor"
                    : "Next Editor"),
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
            if (deck.Modules != null)
                foreach (PostEffectModule module in deck.Modules)
                    if (module != null) module.GetAvailableLutKeys = GetLutKeys;
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
            VolumeProfile profile, DeckVolumeEffects settings, CameraWorkRecipe cameraWork,
            in ModulationContext context)
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
            depthOfField.mode.value = cameraWork?.DepthOfFieldEnabled == true
                ? settings.DepthOfFieldMode == VolumeDepthOfFieldMode.Bokeh
                    ? DepthOfFieldMode.Bokeh
                    : DepthOfFieldMode.Gaussian
                : DepthOfFieldMode.Off;
            depthOfField.focusDistance.overrideState = true;
            depthOfField.focusDistance.value = Mathf.Max(0.1f,
                cameraWork?.FocusDistance?.Evaluate(context) ?? 10f);
            depthOfField.aperture.overrideState = true;
            depthOfField.aperture.value = Mathf.Clamp(cameraWork?.Aperture?.Evaluate(context) ?? 5.6f, 1f, 32f);
            depthOfField.focalLength.overrideState = true;
            depthOfField.focalLength.value = Mathf.Clamp(cameraWork?.FocalLength?.Evaluate(context) ?? 50f, 1f, 300f);
        }

        private void EnsureLutLibrary()
        {
            if (_lutLibrary == null)
                _lutLibrary = FindFirstObjectByType<LutLibrary>(FindObjectsInactive.Include);
        }

        private IReadOnlyList<string> GetLutKeys()
        {
            EnsureLutLibrary();
            return _lutLibrary?.GetKeys() ?? Array.Empty<string>();
        }

        private void ApplyLut(PostEffectModule module)
        {
            _material.SetFloat(LutEnabledId, 0f);
            if (module.Type != PostEffectType.Lut) return;

            EnsureLutLibrary();
            Texture2D lut = _lutLibrary?.Resolve(module.LutKey);
            bool horizontal = lut != null && lut.width == lut.height * lut.height;
            bool vertical = lut != null && lut.height == lut.width * lut.width;
            if (!horizontal && !vertical) return;

            float size = horizontal ? lut.height : lut.width;
            _material.SetTexture(LutTexId, lut);
            _material.SetVector(LutParamsId,
                new Vector4(size, vertical ? 1f : 0f, 1f / lut.width, 1f / lut.height));
            _material.SetFloat(LutEnabledId, 1f);
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
