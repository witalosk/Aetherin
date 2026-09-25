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
        [SerializeField] private Shader _crossFilterShader;
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
        private static readonly int CrossTexId = Shader.PropertyToID("_CrossTex");
        private static readonly int CrossThresholdId = Shader.PropertyToID("_CrossThreshold");
        private static readonly int CrossExposureId = Shader.PropertyToID("_CrossExposure");
        private static readonly int CrossDirectionId = Shader.PropertyToID("_CrossDirection");
        private static readonly int CrossStepId = Shader.PropertyToID("_CrossStep");
        private static readonly int CrossAttenuationId = Shader.PropertyToID("_CrossAttenuation");
        private static readonly int CrossIntensityId = Shader.PropertyToID("_CrossIntensity");
        private static readonly int RuntimeTexId = Shader.PropertyToID("_RuntimeTex");
        private static readonly int CompositeTexId = Shader.PropertyToID("_CompositeTex");
        private static readonly int CompositeModeId = Shader.PropertyToID("_CompositeMode");
        private static readonly int CompositeTilingOffsetId = Shader.PropertyToID("_CompositeTilingOffset");
        private static readonly int CompositeRotationId = Shader.PropertyToID("_CompositeRotation");
        private static readonly int CompositeEnabledId = Shader.PropertyToID("_CompositeEnabled");
        private static readonly int LuminanceDisplacementOffsetId = Shader.PropertyToID("_LuminanceDisplacementOffset");
        private static readonly int LuminanceDisplacementTexId = Shader.PropertyToID("_LuminanceDisplacementTex");
        private static readonly int LuminanceDisplacementEnabledId = Shader.PropertyToID("_LuminanceDisplacementEnabled");
        private static readonly int LuminanceDisplacementTilingOffsetId = Shader.PropertyToID("_LuminanceDisplacementTilingOffset");

        private Material _material;
        private Material _crossFilterMaterial;
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
            Shader crossFilterShader = _crossFilterShader != null
                ? _crossFilterShader
                : Shader.Find("Hidden/Aetherin/CrossFilter");
            if (crossFilterShader != null)
                _crossFilterMaterial = new Material(crossFilterShader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void Start()
        {
            _deckStateProvider.NextPromoted += PromoteNextToCurrent;
        }

        public Texture ProcessCurrent(Texture source, StageDefaultLutSettings stageLut)
        {
            RefreshEditingDeckState();
            bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Current) ?? false;
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter,
                cameraWorkChangeCount: _deckStateProvider?.GetCameraWorkChangeCount(StageDeck.Current) ?? 0);
            _params ??= new PostEffectManagerParams();
            _params.Current ??= new PostEffectStack();
            _params.Next ??= new PostEffectStack();
            Texture input = ProcessStageDefaultLut(source, stageLut, _current, context);
            return Process(input, _params.Current, _current, context, StageDeck.Current);
        }

        public Texture ProcessNext(Texture source, StageDefaultLutSettings stageLut)
        {
            RefreshEditingDeckState();
            bool allowMidi = _deckStateProvider?.IsDeckEditable(StageDeck.Next) ?? true;
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, allowMidi, counter: _counter,
                cameraWorkChangeCount: _deckStateProvider?.GetCameraWorkChangeCount(StageDeck.Next) ?? 0);
            _params ??= new PostEffectManagerParams();
            _params.Next ??= new PostEffectStack();
            Texture input = ProcessStageDefaultLut(source, stageLut, _next, context);
            return Process(input, _params.Next, _next, context, StageDeck.Next);
        }

        /// <summary>
        /// クロスフェード後の最終Outputへ、Pad押下中のDeckだけを即時適用する。
        /// 既存のCurrent / Nextのポストエフェクト経路とは別のパス。
        /// </summary>
        public Texture ProcessOutput(Texture source)
        {
            var context = new ModulationContext(
                Time.unscaledTimeAsDouble, _audioFeatureProvider, _beatManager, true, counter: _counter,
                cameraWorkChangeCount: _deckStateProvider?.GetCameraWorkChangeCount(StageDeck.Next) ?? 0);
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
                    runtime.Activations.Evaluate(deck, deckIsActive, context.Time))
                    .WithOverlayCueTriggerEventId(runtime.Activations.GetActivationCount(deck));
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
                        runtime.Activations.Evaluate(module, module.Enabled && deckIsActive, context.Time))
                        .WithOverlayCueTriggerEventId(runtime.Activations.GetActivationCount(module));
                    if (!module.Enabled) continue;
                    module.EnsureInitialized();
                    module.GetAvailableLutKeys = GetLutKeys;
                    module.GetAvailableTextureKeys = GetTextureKeys;
                    float strength = deckStrength * Mathf.Clamp01(module.Strength?.Evaluate(moduleContext) ?? 1f);
                    if (strength <= 0f) continue;

                    _material.SetTexture(SourceTexId, input);
                    _material.SetTexture(HistoryTexId,
                        runtime.BackBufferValid ? runtime.BackBuffer : input);
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
                    ApplyTextureComposite(module, moduleContext);
                    ApplyLuminanceDisplacement(module, moduleContext);
                    if (module.Type == PostEffectType.RuntimeShader)
                    {
                        RuntimeShaderPostEffectRenderer runtimeShader = runtime.GetRuntimeShader(module, transform);
                        Texture runtimeOutput = runtimeShader.Process(input, module, moduleContext,
                            _audioFeatureProvider, _beatManager,
                            _deckStateProvider?.GetState(deckType)?.Palette);
                        runtime.Buffers.Reset(input);
                        RenderTexture target = runtime.Buffers.Write;
                        _material.SetTexture(RuntimeTexId, runtimeOutput != null ? runtimeOutput : input);
                        Graphics.Blit(input, target, _material);
                        runtime.Buffers.Swap();
                        input = runtime.Buffers.Read;
                    }
                    else if (module.Type == PostEffectType.CrossFilter)
                    {
                        input = ProcessCrossFilter(input, module, runtime, moduleContext, strength);
                    }
                    else if (module.Type == PostEffectType.FrameRateDrop)
                    {
                        input = ProcessFrameRateDrop(input, module, runtime, moduleContext);
                    }
                    else
                    {
                        runtime.Buffers.Reset(input);
                        RenderTexture target = runtime.Buffers.Write;
                        Graphics.Blit(input, target, _material);
                        runtime.Buffers.Swap();
                        input = runtime.Buffers.Read;
                    }
                    wroteAny = true;
                }
            }

            if (wroteAny)
            {
                Graphics.Blit(input, runtime.BackBuffer);
                runtime.BackBufferValid = true;
            }
            else if (outputOnly)
            {
                // Padを離した後に、前回の押下中の履歴を次回へ持ち越さない。
                runtime.BackBufferValid = false;
            }

            return input;
        }

        private Texture ProcessFrameRateDrop(Texture source, PostEffectModule module, StackRuntime runtime,
            in ModulationContext context)
        {
            FrameRateDropRuntime hold = runtime.GetFrameRateDrop(module, source.width, source.height);
            float dropAmount = Mathf.Clamp01(module.FrameRateDropAmount.Evaluate(context));
            if (dropAmount <= 0f)
            {
                // CC=0から上げ直した際、古いホールド画像を一瞬表示しない。
                hold.Valid = false;
                hold.Bucket = long.MinValue;
                return source;
            }

            long bucket;
            if (module.FrameRateDropSync == FrameRateDropSyncMode.Beat && context.Beat?.IsRunning == true)
            {
                int maximumUpdatesPerBeat = Mathf.Clamp(
                    module.FrameRateDropUpdatesPerBeat.Evaluate(context), 1, 16);
                int updatesPerBeat = Mathf.Clamp(
                    Mathf.CeilToInt(maximumUpdatesPerBeat / dropAmount), maximumUpdatesPerBeat, 128);
                float phase = Mathf.Clamp01(context.Beat.BeatPhase);
                bucket = context.Beat.BeatEventId * updatesPerBeat +
                    Mathf.Min(updatesPerBeat - 1, Mathf.FloorToInt(phase * updatesPerBeat));
            }
            else
            {
                float minimumFps = Mathf.Clamp(module.FrameRateDropFps.Evaluate(context), 0.1f, 120f);
                float effectiveFps = minimumFps / dropAmount;
                bucket = (long)Math.Floor(context.ElapsedTime * effectiveFps);
            }

            if (!hold.Valid || hold.Bucket != bucket)
            {
                Graphics.Blit(source, hold.Texture);
                hold.Bucket = bucket;
                hold.Valid = true;
            }

            _material.SetTexture(HistoryTexId, hold.Texture);
            runtime.Buffers.Reset(source);
            Graphics.Blit(source, runtime.Buffers.Write, _material);
            runtime.Buffers.Swap();
            return runtime.Buffers.Read;
        }

        private Texture ProcessCrossFilter(Texture source, PostEffectModule module, StackRuntime runtime,
            in ModulationContext context, float strength)
        {
            if (_crossFilterMaterial == null) return source;

            int lineCount = Mathf.Clamp(module.CrossFilterLineCount.Evaluate(context), 1, 12);
            int passCount = Mathf.Clamp(module.CrossFilterPassCount.Evaluate(context), 1, 6);
            float sampleLength = Mathf.Max(0f, module.CrossFilterSampleLength.Evaluate(context));
            float attenuation = Mathf.Clamp(module.CrossFilterAttenuation.Evaluate(context), 0.01f, 1f);
            float rotation = module.CrossFilterRotation.Evaluate(context) * Mathf.Deg2Rad;

            RenderTexture original = GetCrossFilterTemporary(source, "Cross Filter Original");
            RenderTexture bright = GetCrossFilterTemporary(source, "Cross Filter Bright");
            RenderTexture accumulation = GetCrossFilterTemporary(source, "Cross Filter Accumulation");
            try
            {
                Graphics.Blit(source, original);
                _crossFilterMaterial.SetFloat(CrossThresholdId,
                    Mathf.Max(0f, module.CrossFilterThreshold.Evaluate(context)));
                _crossFilterMaterial.SetFloat(CrossExposureId,
                    Mathf.Max(0f, module.CrossFilterExposure.Evaluate(context)));
                Graphics.Blit(original, bright, _crossFilterMaterial, 0);
                Graphics.Blit(Texture2D.blackTexture, accumulation);

                for (int directionIndex = 0; directionIndex < lineCount; directionIndex++)
                {
                    float angle = rotation + Mathf.PI * 2f * directionIndex / lineCount;
                    _crossFilterMaterial.SetVector(CrossDirectionId,
                        new Vector4(Mathf.Sin(angle), Mathf.Cos(angle), 0f, 0f));
                    Texture lineInput = bright;
                    float step = sampleLength;
                    float passAttenuation = attenuation;

                    for (int pass = 0; pass < passCount; pass++)
                    {
                        runtime.Buffers.Reset(lineInput);
                        _crossFilterMaterial.SetFloat(CrossStepId, step);
                        _crossFilterMaterial.SetFloat(CrossAttenuationId, passAttenuation);
                        Graphics.Blit(lineInput, runtime.Buffers.Write, _crossFilterMaterial, 1);
                        runtime.Buffers.Swap();
                        lineInput = runtime.Buffers.Read;
                        step *= 8f;
                        passAttenuation = Mathf.Pow(passAttenuation, 8f);
                    }

                    runtime.Buffers.Reset(lineInput);
                    _crossFilterMaterial.SetTexture(CrossTexId, lineInput);
                    Graphics.Blit(accumulation, runtime.Buffers.Write, _crossFilterMaterial, 2);
                    runtime.Buffers.Swap();
                    Graphics.Blit(runtime.Buffers.Read, accumulation);
                }

                runtime.Buffers.Reset(runtime.Buffers.Read);
                _crossFilterMaterial.SetTexture(CrossTexId, accumulation);
                _crossFilterMaterial.SetFloat(CrossIntensityId, 1f / lineCount);
                _crossFilterMaterial.SetFloat(StrengthId, strength);
                Graphics.Blit(original, runtime.Buffers.Write, _crossFilterMaterial, 3);
                runtime.Buffers.Swap();
                return runtime.Buffers.Read;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(original);
                RenderTexture.ReleaseTemporary(bright);
                RenderTexture.ReleaseTemporary(accumulation);
            }
        }

        private static RenderTexture GetCrossFilterTemporary(Texture source, string name)
        {
            RenderTexture texture = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.sRGB);
            texture.name = name;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
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
            if (_crossFilterMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_crossFilterMaterial);
                else UnityEngine.Object.DestroyImmediate(_crossFilterMaterial);
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

        private IReadOnlyList<string> GetTextureKeys() =>
            StageAssetCatalog.FindBestAvailable().GetTextureKeys();

        private void ApplyTextureComposite(PostEffectModule module, in ModulationContext context)
        {
            _material.SetFloat(CompositeEnabledId, 0f);
            if (module.Type != PostEffectType.TextureComposite) return;
            IStageAssetCatalog catalog = StageAssetCatalog.FindBestAvailable();
            IReadOnlyList<string> keys = module.CompositeTextureKeys;
            if (keys == null || keys.Count == 0) return;
            int index = Mathf.Clamp(module.CompositeTextureIndex.Evaluate(context), 0, keys.Count - 1);
            Texture2D texture = catalog.ResolveTexture(keys[index]);
            if (texture == null) return;
            Vector2 tiling = module.CompositeTiling.Evaluate(context);
            Vector2 offset = module.CompositeOffset.Evaluate(context);
            _material.SetTexture(CompositeTexId, texture);
            _material.SetInt(CompositeModeId, (int)module.CompositeMode);
            _material.SetVector(CompositeTilingOffsetId, new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            _material.SetFloat(CompositeRotationId, module.CompositeRotation.Evaluate(context) * Mathf.Deg2Rad);
            _material.SetFloat(CompositeEnabledId, 1f);
        }

        private void ApplyLuminanceDisplacement(PostEffectModule module, in ModulationContext context)
        {
            _material.SetFloat(LuminanceDisplacementEnabledId, 0f);
            if (module.Type != PostEffectType.LuminanceDisplacement) return;
            Vector2 offset = module.LuminanceDisplacementOffset.Evaluate(context);
            Vector2 tiling = module.LuminanceDisplacementTiling.Evaluate(context);
            Vector2 mapOffset = module.LuminanceDisplacementMapOffset.Evaluate(context);
            _material.SetVector(LuminanceDisplacementOffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
            _material.SetVector(LuminanceDisplacementTilingOffsetId,
                new Vector4(tiling.x, tiling.y, mapOffset.x, mapOffset.y));
            if (string.IsNullOrWhiteSpace(module.LuminanceDisplacementTextureKey)) return;
            Texture2D texture = StageAssetCatalog.FindBestAvailable()
                .ResolveTexture(module.LuminanceDisplacementTextureKey);
            if (texture == null) return;
            _material.SetTexture(LuminanceDisplacementTexId, texture);
            _material.SetFloat(LuminanceDisplacementEnabledId, 1f);
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
            ConfigureLut(lut, module.LutIntensity.Evaluate(context));
        }

        private Texture ProcessStageDefaultLut(Texture source, StageDefaultLutSettings settings,
            StackRuntime runtime, in ModulationContext context)
        {
            if (source == null || _material == null || settings?.Enabled != true) return source;
            settings.EnsureInitialized();
            if (string.IsNullOrWhiteSpace(settings.LutKey)) return source;

            EnsureLutLibrary();
            Texture2D lut = _lutLibrary?.Resolve(settings.LutKey);
            if (!ConfigureLut(lut, settings.Intensity.Evaluate(context))) return source;

            runtime.Ensure(source.width, source.height);
            _material.SetTexture(SourceTexId, source);
            _material.SetTexture(HistoryTexId, source);
            _material.SetInt(EffectTypeId, (int)PostEffectType.Lut);
            _material.SetFloat(StrengthId, 1f);
            runtime.Buffers.Reset(source);
            Graphics.Blit(source, runtime.Buffers.Write, _material);
            runtime.Buffers.Swap();
            return runtime.Buffers.Read;
        }

        private bool ConfigureLut(Texture2D lut, float intensity)
        {
            _material.SetFloat(LutEnabledId, 0f);
            bool horizontal = lut != null && lut.width == lut.height * lut.height;
            bool vertical = lut != null && lut.height == lut.width * lut.width;
            if (!horizontal && !vertical) return false;

            float size = horizontal ? lut.height : lut.width;
            _material.SetTexture(LutTexId, lut);
            _material.SetVector(LutParamsId,
                new Vector4(size, vertical ? 1f : 0f, 1f / lut.width, 1f / lut.height));
            _material.SetFloat(LutIntensityId, Mathf.Clamp01(intensity));
            _material.SetFloat(LutEnabledId, 1f);
            return true;
        }
        private sealed class StackRuntime : IDisposable
        {
            public readonly ActivationTracker Activations = new();
            public readonly SwapableRenderTexture Buffers = new();
            public RenderTexture BackBuffer { get; private set; }
            public bool BackBufferValid { get; set; }
            private readonly Dictionary<PostEffectModule, RuntimeShaderPostEffectRenderer> _runtimeShaders = new();
            private readonly Dictionary<PostEffectModule, FrameRateDropRuntime> _frameRateDrops = new();

            public void Ensure(int width, int height)
            {
                if (Buffers.Matches(width, height) && BackBuffer != null &&
                    BackBuffer.width == width && BackBuffer.height == height) return;
                DisposeResources();
                // CrossFilterの高輝度成分を保持できるよう、共有バッファはHDR形式にする。
                Buffers.Ensure(width, height, "Post FX Swap", RenderTextureFormat.ARGBHalf);
                BackBuffer = Create(width, height, "Post FX Back Buffer");
            }

            public RuntimeShaderPostEffectRenderer GetRuntimeShader(PostEffectModule module, Transform parent)
            {
                if (_runtimeShaders.TryGetValue(module, out RuntimeShaderPostEffectRenderer renderer))
                    return renderer;
                renderer = new RuntimeShaderPostEffectRenderer(parent);
                _runtimeShaders.Add(module, renderer);
                return renderer;
            }

            public FrameRateDropRuntime GetFrameRateDrop(PostEffectModule module, int width, int height)
            {
                if (!_frameRateDrops.TryGetValue(module, out FrameRateDropRuntime runtime))
                {
                    runtime = new FrameRateDropRuntime();
                    _frameRateDrops.Add(module, runtime);
                }
                runtime.Ensure(width, height);
                return runtime;
            }

            public void Dispose()
            {
                DisposeResources();
                Activations.Clear();
            }

            private void DisposeResources()
            {
                Buffers.Dispose();
                Release(BackBuffer);
                BackBuffer = null;
                BackBufferValid = false;
                foreach (RuntimeShaderPostEffectRenderer renderer in _runtimeShaders.Values)
                    renderer.Dispose();
                _runtimeShaders.Clear();
                foreach (FrameRateDropRuntime runtime in _frameRateDrops.Values)
                    runtime.Dispose();
                _frameRateDrops.Clear();
            }

            private static RenderTexture Create(int width, int height, string name,
                RenderTextureFormat format = RenderTextureFormat.ARGB32)
            {
                var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.sRGB)
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

        private sealed class FrameRateDropRuntime : IDisposable
        {
            public RenderTexture Texture { get; private set; }
            public long Bucket { get; set; } = long.MinValue;
            public bool Valid { get; set; }

            public void Ensure(int width, int height)
            {
                if (Texture != null && Texture.width == width && Texture.height == height) return;
                Dispose();
                Texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.sRGB)
                {
                    name = "Post FX Frame Hold",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                Texture.Create();
            }

            public void Dispose()
            {
                if (Texture == null) return;
                Texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(Texture);
                else UnityEngine.Object.DestroyImmediate(Texture);
                Texture = null;
                Bucket = long.MinValue;
                Valid = false;
            }
        }

        private sealed class ActivationTracker
        {
            private readonly Dictionary<object, Entry> _entries = new();

            public double Evaluate(object key, bool active, double time)
            {
                if (!_entries.TryGetValue(key, out Entry entry))
                {
                    entry = new Entry { Active = active, StartTime = time, ActivationCount = active ? 1 : 0 };
                    _entries.Add(key, entry);
                }
                else if (active && !entry.Active)
                {
                    entry.StartTime = time;
                    entry.ActivationCount++;
                }

                entry.Active = active;
                return active ? Math.Max(0d, time - entry.StartTime) : 0d;
            }

            public long GetActivationCount(object key) =>
                _entries.TryGetValue(key, out Entry entry) ? entry.ActivationCount : 0;

            public void Clear() => _entries.Clear();

            private sealed class Entry
            {
                public bool Active;
                public double StartTime;
                public long ActivationCount;
            }
        }

    }
}
