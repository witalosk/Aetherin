using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer), typeof(TextMeshPro))]
    public sealed class TextLayer : StageLayer
    {
        [SerializeField] private TextLayerParams _params = new();

        private TextMeshPro _text;
        private MeshRenderer _renderer;
        private TMP_FontAsset _fontAsset;
        private Font _sourceFont;
        private Material _material;
        private Vector3[][] _baseVertices;
        private Color32[][] _baseColors;
        private int _layoutHash;
        private CameraStage _cameraStage;
        private string _loadedFontAssetKey;
        private string _loadedFontFamily;
        private string _loadedFontStyle;
        private string _fontRequestKey;
        private bool _ownsFontAsset;
        private string _resolvedText = string.Empty;
        private float _smoothedFps;
        // TMPの頂点配列は文字単位のアニメーションでだけ書き換える。静的なテキストまで
        // 毎フレームコピー/再計算しないよう、最後に反映した状態を保持する。
        private bool _geometryDirty = true;
        private int _geometryStateHash;

        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => TextRenderer;

        private MeshRenderer TextRenderer
        {
            get
            {
                if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
                return _renderer;
            }
        }

        [Inject]
        private void Construct(
            IAudioFeatureProvider audio,
            IBeatManager beat,
            IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
        }

        public void Initialize(
            IAudioFeatureProvider audio,
            IBeatManager beat,
            IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            Initialize();
        }

        private void Awake() => Initialize();
        private void OnEnable() => Initialize();

        private void Initialize()
        {
            _stage = GetComponentInParent<StageBase>();
            _cameraStage = GetComponentInParent<CameraStage>();
            _params ??= new TextLayerParams();
            _params.EnsureInitialized();
            _params.GetAvailableFontAssetKeys = _cameraStage != null ? _cameraStage.GetFontAssetKeys : null;
            _params.GetAvailableTextManagerKeys = () => TextManager.Active?.Keys ?? Array.Empty<string>();
            var keys = _params.GetAvailableFontAssetKeys?.Invoke();
            if (string.IsNullOrWhiteSpace(_params.FontAssetKey) && keys != null && keys.Count > 0)
                _params.FontAssetKey = keys[0];
            _text = GetComponent<TextMeshPro>();
            _fontRequestKey = null;
            EnsureFont();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            if (_text == null) _text = GetComponent<TextMeshPro>();
            EnsureFont();
            if (_fontAsset == null || _text == null) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                _audio, _beat,
                Application.isPlaying && (_stage == null || (_deckStateProvider?.IsDeckEditable(_stage.Deck) ?? _stage.Deck == StageDeck.Next)));
            _resolvedText = ResolveText(context);
            EvaluateLayout(context);
            ApplyCharacterAnimators(context);
            ApplyTransform(context);
            ApplyAppearance(context);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (!_params.ScreenSpace) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                _audio, _beat,
                Application.isPlaying && (_stage == null || (_deckStateProvider?.IsDeckEditable(_stage.Deck) ?? _stage.Deck == StageDeck.Next)));
            ApplyTransform(context);
        }

        private void EnsureFont()
        {
            if (_text == null) return;
            _cameraStage ??= GetComponentInParent<CameraStage>();

            string assetKey = _params.FontAssetKey?.Trim();
            if (!string.IsNullOrWhiteSpace(assetKey))
            {
                TMP_FontAsset libraryFontAsset = _cameraStage?.ResolveFontAsset(assetKey);
                string libraryRequestKey = $"asset\n{assetKey}";
                if (_fontAsset != null && !_ownsFontAsset && _loadedFontAssetKey == assetKey &&
                    _fontAsset == libraryFontAsset) return;
                if (_fontAsset == null && _fontRequestKey == libraryRequestKey) return;

                ReleaseFontResources();
                _loadedFontAssetKey = assetKey;
                _fontRequestKey = libraryRequestKey;
                _fontAsset = libraryFontAsset;
                _ownsFontAsset = false;
                ApplyFontAsset();
                return;
            }

            string family = string.IsNullOrWhiteSpace(_params.FontFamily) ? "Arial" : _params.FontFamily.Trim();
            string style = string.IsNullOrWhiteSpace(_params.FontStyle) ? "Regular" : _params.FontStyle.Trim();
            if (_fontAsset != null && _ownsFontAsset && _loadedFontAssetKey == null &&
                _loadedFontFamily == family && _loadedFontStyle == style) return;
            string requestKey = $"{family}\n{style}";
            if (_fontAsset == null && _fontRequestKey == requestKey) return;

            ReleaseFontResources();
            _loadedFontAssetKey = null;
            _loadedFontFamily = family;
            _loadedFontStyle = style;
            _fontRequestKey = requestKey;

            try
            {
                _fontAsset = TMP_FontAsset.CreateFontAsset(family, style, 90);
                _ownsFontAsset = _fontAsset != null;
                if (_fontAsset == null)
                {
                    _sourceFont = Font.CreateDynamicFontFromOSFont(family, 90);
                    if (_sourceFont != null)
                    {
                        _sourceFont.hideFlags = HideFlags.HideAndDontSave;
                        _fontAsset = TMP_FontAsset.CreateFontAsset(_sourceFont);
                        _ownsFontAsset = _fontAsset != null;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TextLayer] OS font '{family} {style}' could not be loaded: {exception.Message}", this);
            }

            ApplyFontAsset();
        }

        private void ApplyFontAsset()
        {
            if (_fontAsset == null) return;
            if (_ownsFontAsset) _fontAsset.hideFlags = HideFlags.HideAndDontSave;
            TryAddCharactersToDynamicFont();
            _text.font = _fontAsset;
            _material = new Material(_fontAsset.material) { hideFlags = HideFlags.HideAndDontSave };
            _text.fontSharedMaterial = _material;
            _layoutHash = 0;
            _geometryDirty = true;
        }

        private void EvaluateLayout(in ModulationContext context)
        {
            float fontSize = Mathf.Max(0.001f, _params.FontSize?.Evaluate(context) ?? 1f);
            float characterSpacing = _params.CharacterSpacing?.Evaluate(context) ?? 0f;
            float wordSpacing = _params.WordSpacing?.Evaluate(context) ?? 0f;
            float lineSpacing = _params.LineSpacing?.Evaluate(context) ?? 0f;
            int hash = CalculateLayoutHash(fontSize, characterSpacing, wordSpacing, lineSpacing);
            if (hash == _layoutHash && _baseVertices != null) return;

            TryAddCharactersToDynamicFont();
            _text.text = _resolvedText ?? string.Empty;
            _text.fontSize = fontSize;
            _text.characterSpacing = characterSpacing;
            _text.wordSpacing = wordSpacing;
            _text.lineSpacing = lineSpacing;
            _text.alignment = _params.Alignment;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.ForceMeshUpdate(true, true);
            CaptureBaseMesh();
            _layoutHash = hash;
        }

        private void TryAddCharactersToDynamicFont()
        {
            if (_fontAsset == null || _fontAsset.atlasPopulationMode == AtlasPopulationMode.Static) return;
            _fontAsset.TryAddCharacters(_resolvedText ?? string.Empty, out _);
        }

        private int CalculateLayoutHash(float fontSize, float characterSpacing, float wordSpacing, float lineSpacing)
        {
            unchecked
            {
                int hash = _resolvedText?.GetHashCode() ?? 0;
                hash = hash * 31 + (_params.FontFamily?.GetHashCode() ?? 0);
                hash = hash * 31 + (_params.FontStyle?.GetHashCode() ?? 0);
                hash = hash * 31 + fontSize.GetHashCode();
                hash = hash * 31 + characterSpacing.GetHashCode();
                hash = hash * 31 + wordSpacing.GetHashCode();
                hash = hash * 31 + lineSpacing.GetHashCode();
                hash = hash * 31 + (int)_params.Alignment;
                return hash == 0 ? 1 : hash;
            }
        }

        private string ResolveText(in ModulationContext context)
        {
            switch (_params.Source)
            {
                case TextSource.TextManager:
                {
                    IReadOnlyList<string> texts = TextManager.Active?.GetTexts(_params.TextManagerKey);
                    if (texts == null || texts.Count == 0) return string.Empty;
                    int index = _params.TextManagerIndex?.Evaluate(context) ?? 0;
                    index = ((index % texts.Count) + texts.Count) % texts.Count;
                    return texts[index] ?? string.Empty;
                }
                case TextSource.LocalClock:
                    try { return DateTime.Now.ToString(_params.ClockFormat); }
                    catch (FormatException) { return DateTime.Now.ToString("HH:mm:ss"); }
                case TextSource.Timecode:
                {
                    double seconds = Math.Max(0d, context.Time);
                    int decimals = Mathf.Clamp(_params.TimecodeDecimals, 0, 3);
                    TimeSpan span = TimeSpan.FromSeconds(seconds);
                    string baseText = $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
                    if (decimals == 0) return baseText;
                    int fraction = (int)(span.Milliseconds / Math.Pow(10, 3 - decimals));
                    return $"{baseText}.{fraction.ToString().PadLeft(decimals, '0')}";
                }
                case TextSource.BeatCounter:
                    return _beat == null ? "-- BPM" : $"{_beat.Bpm:0.0} BPM  {_beat.BeatInBar + 1}/{Mathf.Max(1, _beat.BeatsPerBar)}";
                case TextSource.InputVolume:
                    return $"INPUT {Mathf.Clamp01(_audio?.InputVolume ?? 0f) * 100f:000}";
                case TextSource.Fps:
                {
                    float instant = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f;
                    _smoothedFps = _smoothedFps <= 0f ? instant : Mathf.Lerp(_smoothedFps, instant, 0.08f);
                    return $"{_smoothedFps:0.0} FPS";
                }
                default:
                    return _params.Text ?? string.Empty;
            }
        }

        private void CaptureBaseMesh()
        {
            TMP_MeshInfo[] meshInfo = _text.textInfo.meshInfo;
            _baseVertices = new Vector3[meshInfo.Length][];
            _baseColors = new Color32[meshInfo.Length][];
            for (int i = 0; i < meshInfo.Length; i++)
            {
                _baseVertices[i] = (Vector3[])meshInfo[i].vertices.Clone();
                _baseColors[i] = (Color32[])meshInfo[i].colors32.Clone();
            }
            _geometryDirty = true;
        }

        private void RestoreBaseMesh()
        {
            TMP_MeshInfo[] meshInfo = _text.textInfo.meshInfo;
            for (int i = 0; i < meshInfo.Length && i < _baseVertices.Length; i++)
            {
                Array.Copy(_baseVertices[i], meshInfo[i].vertices, _baseVertices[i].Length);
                Array.Copy(_baseColors[i], meshInfo[i].colors32, _baseColors[i].Length);
            }
        }

        private void ApplyCharacterAnimators(in ModulationContext baseContext)
        {
            if (_baseVertices == null || _text.textInfo == null) return;

            TMP_TextInfo info = _text.textInfo;
            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Current).Palette;
            int stateHash = CalculateGeometryStateHash(palette);
            if (!_geometryDirty && !HasDynamicGeometryInput(baseContext) && stateHash == _geometryStateHash)
                return;

            RestoreBaseMesh();
            ApplyPathLayout(info, baseContext);
            EvaluatedPaletteColor baseColor = EvaluatedPaletteColor.Evaluate(_params.Color, palette, baseContext);
            Vector3 anchor = _params.Anchor?.Evaluate(baseContext) ?? Vector3.zero;

            for (int characterIndex = 0; characterIndex < info.characterCount; characterIndex++)
            {
                TMP_CharacterInfo character = info.characterInfo[characterIndex];
                if (!character.isVisible) continue;
                int materialIndex = character.materialReferenceIndex;
                int vertexIndex = character.vertexIndex;
                Vector3[] vertices = info.meshInfo[materialIndex].vertices;
                Color32[] colors = info.meshInfo[materialIndex].colors32;
                Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;

                Color color = baseColor.IsGradient
                    ? Color.LerpUnclamped(baseColor.ColorA, baseColor.ColorB,
                        info.characterCount <= 1 ? 0f : characterIndex / (float)(info.characterCount - 1))
                    : baseColor.ColorA;
                float opacity = color.a;

                foreach (TextAnimatorParams animator in _params.Animators)
                {
                    if (animator is not { Enabled: true }) continue;
                    float phase = animator.AnimationPhaseOffset?.Evaluate(baseContext) ?? 0f;
                    ModulationContext context = baseContext.WithAnimationPhaseOffset(phase * characterIndex);
                    float weight = TextSelectorUtility.Evaluate(
                        animator.Selector, info, characterIndex, _resolvedText, context);
                    if (Mathf.Approximately(weight, 0f)) continue;

                    Vector3 position = (animator.Position?.Evaluate(context) ?? Vector3.zero) * weight;
                    Vector3 rotation = (animator.Rotation?.Evaluate(context) ?? Vector3.zero) * weight;
                    Vector3 targetScale = animator.Scale?.Evaluate(context) ?? Vector3.one;
                    Vector3 scale = Vector3.LerpUnclamped(Vector3.one, targetScale, weight);
                    Matrix4x4 matrix = Matrix4x4.Translate(center + position) *
                                       Matrix4x4.Rotate(Quaternion.Euler(rotation)) *
                                       Matrix4x4.Scale(scale) *
                                       Matrix4x4.Translate(-center);
                    for (int corner = 0; corner < 4; corner++)
                        vertices[vertexIndex + corner] = matrix.MultiplyPoint3x4(vertices[vertexIndex + corner]);

                    float targetOpacity = Mathf.Clamp01(animator.Opacity?.Evaluate(context) ?? 1f);
                    opacity = Mathf.LerpUnclamped(opacity, targetOpacity * color.a, Mathf.Abs(weight));
                    float colorAmount = Mathf.Clamp01(animator.ColorAmount?.Evaluate(context) ?? 0f) * Mathf.Abs(weight);
                    if (colorAmount > 0f)
                    {
                        Color animatorColor = EvaluatedPaletteColor.Evaluate(animator.Color, palette, context).ColorA;
                        color = Color.LerpUnclamped(color, animatorColor, colorAmount);
                    }
                }

                color.a = opacity * Mathf.Clamp01(_params.Opacity?.Evaluate(baseContext) ?? 1f);
                Color32 color32 = color;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices[vertexIndex + corner] -= anchor;
                    colors[vertexIndex + corner] = color32;
                }
            }

            for (int i = 0; i < info.meshInfo.Length; i++)
            {
                info.meshInfo[i].mesh.vertices = info.meshInfo[i].vertices;
                info.meshInfo[i].mesh.colors32 = info.meshInfo[i].colors32;
                info.meshInfo[i].mesh.RecalculateBounds();
                _text.UpdateGeometry(info.meshInfo[i].mesh, i);
            }

            _geometryDirty = false;
            _geometryStateHash = stateHash;
        }

        // Modulation は外部入力・時刻・拍などで変化し得るため、値が同じに見えるフレームでも
        // 保守的に再評価する。modulation がない構成では、下の状態ハッシュだけで更新を決める。
        private bool HasDynamicGeometryInput(in ModulationContext context)
        {
            if (_params.Source != TextSource.Manual) return true;
            if (HasDynamic(_params.Opacity, context) || HasDynamic(_params.Anchor, context) ||
                HasDynamic(_params.Color, context)) return true;

            if (_params.Layout != TextLayoutMode.Linear &&
                (HasDynamic(_params.PathRadius, context) || HasDynamic(_params.PathStartAngle, context) ||
                 HasDynamic(_params.PathEndAngle, context) || HasDynamic(_params.PathRotationOffset, context)))
                return true;

            if (_params.Animators == null) return false;
            foreach (TextAnimatorParams animator in _params.Animators)
            {
                if (animator is not { Enabled: true }) continue;
                if (HasDynamic(animator.Position, context) || HasDynamic(animator.Rotation, context) ||
                    HasDynamic(animator.Scale, context) || HasDynamic(animator.Opacity, context) ||
                    HasDynamic(animator.Color, context) || HasDynamic(animator.ColorAmount, context) ||
                    HasDynamic(animator.AnimationPhaseOffset, context) ||
                    HasDynamic(animator.Selector, context))
                    return true;
            }

            return false;
        }

        private int CalculateGeometryStateHash(ColorPalette palette)
        {
            unchecked
            {
                int hash = 17;
                AddHash(ref hash, _params.Layout);
                AddHash(ref hash, _params.PathClockwise);
                AddHash(ref hash, _params.OrientToPath);
                AddHash(ref hash, _params.PathRadius);
                AddHash(ref hash, _params.PathStartAngle);
                AddHash(ref hash, _params.PathEndAngle);
                AddHash(ref hash, _params.PathRotationOffset);
                AddHash(ref hash, _params.Anchor);
                AddHash(ref hash, _params.Opacity);
                AddHash(ref hash, _params.Color);
                AddHash(ref hash, palette);

                if (_params.Animators != null)
                {
                    AddHash(ref hash, _params.Animators.Count);
                    foreach (TextAnimatorParams animator in _params.Animators)
                        AddHash(ref hash, animator);
                }

                return hash;
            }
        }

        private static bool HasDynamic(FloatParameter parameter, in ModulationContext context) =>
            HasDynamic(parameter?.Modulation, context);

        private static bool HasDynamic(Vector3Parameter parameter, in ModulationContext context) =>
            HasDynamic(parameter?.XModulation, context) || HasDynamic(parameter?.YModulation, context) ||
            HasDynamic(parameter?.ZModulation, context);

        private static bool HasDynamic(PaletteColorParameter parameter, in ModulationContext context) =>
            parameter != null && (HasDynamic(parameter.GradientAngle, context) ||
                HasDynamic(parameter.GradientOffset, context) || HasDynamic(parameter.GradientScale, context) ||
                HasDynamic(parameter.Intensity, context) || HasDynamic(parameter.Alpha, context));

        private static bool HasDynamic(TextRangeSelectorParams selector, in ModulationContext context) =>
            selector != null && (HasDynamic(selector.Start, context) || HasDynamic(selector.End, context) ||
                HasDynamic(selector.Offset, context) || HasDynamic(selector.Smoothness, context));

        private static bool HasDynamic(FloatModulationStack stack, in ModulationContext context)
        {
            if (stack?.Modulators == null) return false;
            foreach (FloatModulator modulator in stack.Modulators)
                if (modulator is { Enabled: true } && modulator.IsAvailable(context)) return true;
            return false;
        }

        private static void AddHash<T>(ref int hash, T value)
        {
            unchecked { hash = hash * 31 + EqualityComparer<T>.Default.GetHashCode(value); }
        }

        private static void AddHash(ref int hash, FloatParameter parameter) =>
            AddHash(ref hash, parameter?.BaseValue ?? 0f);

        private static void AddHash(ref int hash, Vector3Parameter parameter) =>
            AddHash(ref hash, parameter?.BaseValue ?? Vector3.zero);

        private static void AddHash(ref int hash, PaletteColorParameter parameter)
        {
            if (parameter == null)
            {
                AddHash(ref hash, 0);
                return;
            }

            AddHash(ref hash, parameter.Mode);
            AddHash(ref hash, parameter.ColorReference);
            AddHash(ref hash, parameter.Color);
            AddHash(ref hash, parameter.CustomColor);
            AddHash(ref hash, parameter.GradientColorAReference);
            AddHash(ref hash, parameter.GradientColorA);
            AddHash(ref hash, parameter.CustomGradientColorA);
            AddHash(ref hash, parameter.GradientColorBReference);
            AddHash(ref hash, parameter.GradientColorB);
            AddHash(ref hash, parameter.CustomGradientColorB);
            AddHash(ref hash, parameter.RandomSeed);
            AddHash(ref hash, parameter.GradientAngle);
            AddHash(ref hash, parameter.GradientOffset);
            AddHash(ref hash, parameter.GradientScale);
            AddHash(ref hash, parameter.Intensity);
            AddHash(ref hash, parameter.Alpha);
        }

        private static void AddHash(ref int hash, TextAnimatorParams animator)
        {
            if (animator == null)
            {
                AddHash(ref hash, 0);
                return;
            }

            AddHash(ref hash, animator.Enabled);
            AddHash(ref hash, animator.Selector);
            AddHash(ref hash, animator.Position);
            AddHash(ref hash, animator.Rotation);
            AddHash(ref hash, animator.Scale);
            AddHash(ref hash, animator.Opacity);
            AddHash(ref hash, animator.Color);
            AddHash(ref hash, animator.ColorAmount);
            AddHash(ref hash, animator.AnimationPhaseOffset);
        }

        private static void AddHash(ref int hash, TextRangeSelectorParams selector)
        {
            if (selector == null)
            {
                AddHash(ref hash, 0);
                return;
            }

            AddHash(ref hash, selector.BasedOn);
            AddHash(ref hash, selector.Shape);
            AddHash(ref hash, selector.Start);
            AddHash(ref hash, selector.End);
            AddHash(ref hash, selector.Offset);
            AddHash(ref hash, selector.Smoothness);
            AddHash(ref hash, selector.RandomizeOrder);
            AddHash(ref hash, selector.RandomSeed);
        }

        private static void AddHash(ref int hash, ColorPalette palette)
        {
            if (palette == null)
            {
                AddHash(ref hash, 0);
                return;
            }

            AddHash(ref hash, palette.BackgroundColor1);
            AddHash(ref hash, palette.BackgroundColor2);
            AddHash(ref hash, palette.AccentColor1);
            AddHash(ref hash, palette.AccentColor2);
            AddHash(ref hash, palette.SubAccentColor1);
            AddHash(ref hash, palette.SubAccentColor2);
        }

        private void ApplyPathLayout(TMP_TextInfo info, in ModulationContext context)
        {
            if (_params.Layout == TextLayoutMode.Linear || info.characterCount == 0) return;

            int visibleCount = 0;
            for (int i = 0; i < info.characterCount; i++)
                if (info.characterInfo[i].isVisible) visibleCount++;
            if (visibleCount == 0) return;

            float radius = Mathf.Max(0f, _params.PathRadius?.Evaluate(context) ?? 0f);
            float startAngle = _params.PathStartAngle?.Evaluate(context) ?? 90f;
            float endAngle = _params.PathEndAngle?.Evaluate(context) ?? -90f;
            float rotationOffset = _params.PathRotationOffset?.Evaluate(context) ?? 0f;
            float direction = _params.PathClockwise ? -1f : 1f;
            float span = _params.Layout == TextLayoutMode.Circle
                ? 360f
                : Mathf.Abs(endAngle - startAngle);
            int visibleIndex = 0;

            for (int characterIndex = 0; characterIndex < info.characterCount; characterIndex++)
            {
                TMP_CharacterInfo character = info.characterInfo[characterIndex];
                if (!character.isVisible) continue;

                float t = _params.Layout == TextLayoutMode.Circle
                    ? visibleIndex / (float)visibleCount
                    : visibleCount <= 1 ? 0f : visibleIndex / (float)(visibleCount - 1);
                float angle = startAngle + direction * span * t;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 targetCenter = new(Mathf.Cos(radians) * radius, Mathf.Sin(radians) * radius, 0f);

                int materialIndex = character.materialReferenceIndex;
                int vertexIndex = character.vertexIndex;
                Vector3[] vertices = info.meshInfo[materialIndex].vertices;
                Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;
                float glyphRotation = _params.OrientToPath
                    ? angle + (_params.PathClockwise ? -90f : 90f) + rotationOffset
                    : 0f;
                Matrix4x4 matrix = Matrix4x4.Translate(targetCenter) *
                                   Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, glyphRotation)) *
                                   Matrix4x4.Translate(-center);
                for (int corner = 0; corner < 4; corner++)
                    vertices[vertexIndex + corner] = matrix.MultiplyPoint3x4(vertices[vertexIndex + corner]);
                visibleIndex++;
            }
        }

        private void ApplyTransform(in ModulationContext context)
        {
            Vector3 position = _params.Position?.Evaluate(context) ?? Vector3.zero;
            Vector3 rotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            Vector3 scale = _params.Scale?.Evaluate(context) ?? Vector3.one;

            if (!_params.ScreenSpace)
            {
                transform.localPosition = position;
                transform.localRotation = Quaternion.Euler(rotation);
                transform.localScale = scale;
                return;
            }

            _cameraStage ??= GetComponentInParent<CameraStage>();
            Camera camera = _cameraStage?.StageCamera;
            if (camera == null) return;

            float depth = camera.orthographic
                ? Mathf.Max(camera.nearClipPlane + 0.01f, 1f)
                : Mathf.Max(camera.nearClipPlane + 0.01f, 10f);
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 cameraSpacePosition = new(
                position.x * halfHeight,
                position.y * halfHeight,
                depth + position.z * halfHeight);

            transform.SetPositionAndRotation(
                camera.transform.TransformPoint(cameraSpacePosition),
                camera.transform.rotation * Quaternion.Euler(rotation));
            // ShapeLayerのScreen Spaceは親Transformも行列で打ち消す。
            // TextMeshProは通常Transformで描画するため、親Groupのスケールをここで相殺して揃える。
            Vector3 desiredWorldScale = scale * halfHeight;
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(
                DivideByParentScale(desiredWorldScale.x, parentScale.x),
                DivideByParentScale(desiredWorldScale.y, parentScale.y),
                DivideByParentScale(desiredWorldScale.z, parentScale.z));
        }

        private static float DivideByParentScale(float value, float parentScale) =>
            Mathf.Abs(parentScale) > 0.0001f ? value / parentScale : value;

        private void ApplyAppearance(in ModulationContext context)
        {
            if (_material != null) LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
            ApplyLayerState();
        }

        private void OnDisable() => ReleaseFontResources();
        private void OnDestroy() => ReleaseFontResources();

        private void ReleaseFontResources()
        {
            if (_text != null)
            {
                _text.font = null;
                _text.fontSharedMaterial = null;
            }
            DestroyResource(_material);
            if (_ownsFontAsset) DestroyResource(_fontAsset);
            DestroyResource(_sourceFont);
            _material = null;
            _fontAsset = null;
            _sourceFont = null;
            _ownsFontAsset = false;
            _loadedFontAssetKey = null;
            _loadedFontFamily = null;
            _loadedFontStyle = null;
            _baseVertices = null;
            _baseColors = null;
            _layoutHash = 0;
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }
    }
}
