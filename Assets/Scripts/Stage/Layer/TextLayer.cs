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
        private string _loadedFontFamily;
        private string _loadedFontStyle;
        private string _fontRequestKey;

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
            _params ??= new TextLayerParams();
            _params.EnsureInitialized();
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

            var context = new ModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                _audio, _beat, Application.isPlaying);
            EvaluateLayout(context);
            ApplyCharacterAnimators(context);
            ApplyTransform(context);
            ApplyAppearance(context);
        }

        private void EnsureFont()
        {
            if (_text == null) return;
            string family = string.IsNullOrWhiteSpace(_params.FontFamily) ? "Arial" : _params.FontFamily.Trim();
            string style = string.IsNullOrWhiteSpace(_params.FontStyle) ? "Regular" : _params.FontStyle.Trim();
            if (_fontAsset != null && _loadedFontFamily == family && _loadedFontStyle == style) return;
            string requestKey = $"{family}\n{style}";
            if (_fontAsset == null && _fontRequestKey == requestKey) return;

            ReleaseFontResources();
            _loadedFontFamily = family;
            _loadedFontStyle = style;
            _fontRequestKey = requestKey;

            try
            {
                _fontAsset = TMP_FontAsset.CreateFontAsset(family, style, 90);
                if (_fontAsset == null)
                {
                    _sourceFont = Font.CreateDynamicFontFromOSFont(family, 90);
                    if (_sourceFont != null)
                    {
                        _sourceFont.hideFlags = HideFlags.HideAndDontSave;
                        _fontAsset = TMP_FontAsset.CreateFontAsset(_sourceFont);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TextLayer] OS font '{family} {style}' could not be loaded: {exception.Message}", this);
            }

            if (_fontAsset == null) return;
            _fontAsset.hideFlags = HideFlags.HideAndDontSave;
            _fontAsset.TryAddCharacters(_params.Text ?? string.Empty, out _);
            _text.font = _fontAsset;
            _material = new Material(_fontAsset.material) { hideFlags = HideFlags.HideAndDontSave };
            _text.fontSharedMaterial = _material;
            _layoutHash = 0;
        }

        private void EvaluateLayout(in ModulationContext context)
        {
            float fontSize = Mathf.Max(0.001f, _params.FontSize?.Evaluate(context) ?? 1f);
            float characterSpacing = _params.CharacterSpacing?.Evaluate(context) ?? 0f;
            float wordSpacing = _params.WordSpacing?.Evaluate(context) ?? 0f;
            float lineSpacing = _params.LineSpacing?.Evaluate(context) ?? 0f;
            int hash = CalculateLayoutHash(fontSize, characterSpacing, wordSpacing, lineSpacing);
            if (hash == _layoutHash && _baseVertices != null) return;

            _fontAsset.TryAddCharacters(_params.Text ?? string.Empty, out _);
            _text.text = _params.Text ?? string.Empty;
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

        private int CalculateLayoutHash(float fontSize, float characterSpacing, float wordSpacing, float lineSpacing)
        {
            unchecked
            {
                int hash = _params.Text?.GetHashCode() ?? 0;
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
            RestoreBaseMesh();

            TMP_TextInfo info = _text.textInfo;
            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Current).Palette;
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
                        animator.Selector, info, characterIndex, _params.Text, context);
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
                _text.UpdateGeometry(info.meshInfo[i].mesh, i);
            }
        }

        private void ApplyTransform(in ModulationContext context)
        {
            transform.localPosition = _params.Position?.Evaluate(context) ?? Vector3.zero;
            transform.localRotation = Quaternion.Euler(_params.Rotation?.Evaluate(context) ?? Vector3.zero);
            transform.localScale = _params.Scale?.Evaluate(context) ?? Vector3.one;
        }

        private void ApplyAppearance(in ModulationContext context)
        {
            if (_material != null) LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
            ApplyLayerState();
        }

        private void OnDisable() => ReleaseFontResources();
        private void OnDestroy() => ReleaseFontResources();

        private void ReleaseFontResources()
        {
            DestroyResource(_material);
            DestroyResource(_fontAsset);
            DestroyResource(_sourceFont);
            _material = null;
            _fontAsset = null;
            _sourceFont = null;
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
