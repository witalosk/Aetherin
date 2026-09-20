using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>横方向にフレームを並べたスプライトシートを、常に水平を保ってカメラへ向けて描画する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SpriteSheetLayer : StageLayer, IRepeaterCopyProvider
    {
        private const int MaxRepeaterCopies = 128;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int AnimationRowId = Shader.PropertyToID("_AnimationRow");
        private static readonly int RowCountId = Shader.PropertyToID("_RowCount");
        private static readonly int RepeaterRowIncrementId = Shader.PropertyToID("_RepeaterRowIncrement");

        private static readonly Vector3[] QuadVertices =
        {
            new(-.5f, -.5f), new(.5f, -.5f), new(.5f, .5f), new(-.5f, .5f),
        };
        private static readonly Vector2[] QuadUvs = { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
        private static readonly int[] QuadTriangles = { 0, 2, 1, 0, 3, 2 };

        [SerializeField] private SpriteSheetLayerParams _params = new() { BlendMode = LayerBlendMode.Transparent };
        [SerializeField] private Shader _shader;

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private CameraStage _cameraStage;
        private StageBase _stage;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckState;
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color> _vertexColors = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _triangles = new();
        private EvaluatedRepeater _evaluatedRepeater;
        private ModulationContext _modulationContext;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => _meshRenderer;

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckState) =>
            Initialize(audio, beat, deckState);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckState)
        {
            _audio = audio;
            _beat = beat;
            _deckState = deckState;
            InitializeLayer();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new SpriteSheetLayerParams { BlendMode = LayerBlendMode.Transparent };
            _params.EnsureInitialized();
            _cameraStage = GetComponentInParent<CameraStage>();
            _stage = GetComponentInParent<StageBase>();
            _params.GetAvailableSpriteSheetKeys = _cameraStage != null ? _cameraStage.GetSpriteSheetKeys : null;
            EnsureSpriteSheetKey();
            EnsureResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureSpriteSheetKey();
            EnsureResources();
            if (_material == null) return;

            var context = CreateModulationContext(
                Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble,
                Application.isPlaying ? _audio : null, Application.isPlaying ? _beat : null,
                Application.isPlaying && (_stage == null || (_deckState?.IsDeckEditable(_stage.Deck) ?? _stage.Deck == StageDeck.Next)));
            ApplyTransform(context);
            ApplyRepeater(context);
            ApplyAppearance(context);
        }

        protected override void OnValidate()
        {
            InitializeLayer();
            base.OnValidate();
        }

        private void EnsureResources()
        {
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Sprite Sheet Quad", hideFlags = HideFlags.DontSave };
                _meshFilter.sharedMesh = _mesh;
            }
            if (_material != null) return;
            _shader ??= Shader.Find("Aetherin/Sprite Sheet Billboard");
            if (_shader == null) return;
            _material = new Material(_shader) { name = $"{name} Sprite Sheet Material", hideFlags = HideFlags.DontSave };
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void ApplyRepeater(in ModulationContext context)
        {
            _modulationContext = context;
            _evaluatedRepeater = EvaluatedRepeater.Evaluate(_params.Repeater, context, MaxRepeaterCopies);

            _vertices.Clear();
            _vertexColors.Clear();
            _uvs.Clear();
            _triangles.Clear();
            _vertices.AddRange(QuadVertices);
            _uvs.AddRange(QuadUvs);
            _triangles.AddRange(QuadTriangles);

            int baseVertexCount = RepeaterMeshUtility.ApplyVertices(
                _vertices, _vertexColors, _uvs, _evaluatedRepeater,
                _evaluatedRepeater.TransformMode == RepeaterTransformMode.FromSource ? this : null);
            RepeaterMeshUtility.ApplyIndices(
                _triangles, QuadTriangles.Length, baseVertexCount, _evaluatedRepeater.Copies);

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_vertexColors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// UIのDropdownは空キーでも先頭項目を表示するため、初期値も同じキーへ明示的に揃える。
        /// ライブラリがレイヤーより後に初期化される場合にもUpdateで解決される。
        /// </summary>
        private void EnsureSpriteSheetKey()
        {
            if (!string.IsNullOrWhiteSpace(_params.SpriteSheetKey) || _cameraStage == null) return;
            var keys = _cameraStage.GetSpriteSheetKeys();
            if (keys != null && keys.Count > 0) _params.SpriteSheetKey = keys[0];
        }

        private void ApplyTransform(in ModulationContext context)
        {
            Vector3 position = _params.Position.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Vector2 size = _params.Size.Evaluate(context);
            Texture2D texture = _cameraStage != null ? _cameraStage.ResolveSpriteSheet(_params.SpriteSheetKey) : null;
            int frameCount = Mathf.Max(1, _params.FrameCount);
            if (_params.PreserveAspect && texture != null && texture.height > 0)
                size.x = size.y * (texture.width / (float)frameCount) / texture.height;
            size.x = Mathf.Max(0f, size.x);
            size.y = Mathf.Max(0f, size.y);

            Quaternion parentRotation = transform.parent != null
                ? transform.parent.rotation
                : Quaternion.identity;
            Quaternion rotationOffset = Quaternion.Euler(_params.Rotation.Evaluate(context));
            Quaternion rotation;
            if (_params.FaceCamera)
            {
                Camera camera = _cameraStage != null ? _cameraStage.StageCamera : null;
                Vector3 forward = camera != null ? camera.transform.position - transform.position : Vector3.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < .000001f) forward = Vector3.forward;
                rotation = Quaternion.LookRotation(forward.normalized, Vector3.up) * rotationOffset;
            }
            else
            {
                rotation = parentRotation * rotationOffset;
            }
            transform.localPosition = position - Quaternion.Inverse(parentRotation) *
                rotation * Vector3.Scale(anchor, new Vector3(size.x * scale.x, size.y * scale.y, scale.z));
            transform.rotation = rotation;
            transform.localScale = new Vector3(size.x * scale.x, size.y * scale.y, scale.z);
        }

        private void ApplyAppearance(in ModulationContext context)
        {
            Texture2D texture = _cameraStage != null ? _cameraStage.ResolveSpriteSheet(_params.SpriteSheetKey) : null;
            _material.SetTexture(MainTexId, texture != null ? texture : Texture2D.whiteTexture);
            int frameCount = Mathf.Max(1, _params.FrameCount);
            int rowCount = Mathf.Max(1, _params.RowCount);
            int frame = _params.PlayAnimation
                ? _params.StartFrame + Mathf.FloorToInt((float)context.ElapsedTime * Mathf.Max(0f, _params.FramesPerSecond.Evaluate(context)))
                : _params.CurrentFrame.Evaluate(context);
            frame = _params.Loop ? Mod(frame, frameCount) : Mathf.Clamp(frame, 0, frameCount - 1);
            int row = Mod(_params.AnimationRow.Evaluate(context), rowCount);
            // UnityのUV原点は下なので、UI上の行番号0を画像の最上段に対応させる。
            _material.SetVector(UvRectId, new Vector4(1f / frameCount, 1f / rowCount,
                frame / (float)frameCount, 0f));
            _material.SetFloat(AnimationRowId, row);
            _material.SetFloat(RowCountId, rowCount);
            _material.SetFloat(RepeaterRowIncrementId, _params.RepeaterRowIncrement.Evaluate(context));
            ColorPalette palette = Application.isPlaying && _deckState != null
                ? _deckState.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette : PaletteColorParameter.FallbackPalette;
            PaletteColorParameter colorParameter = _params.ColorMode == SpriteSheetColorMode.AccentMask
                ? _params.AccentColor : _params.Color;
            Color color = EvaluatedPaletteColor.Evaluate(colorParameter, palette, context).ColorA;
            color.a *= Mathf.Clamp01(_params.Opacity.Evaluate(context));
            _material.SetColor(ColorId, color);
            _material.SetFloat(ColorModeId, (float)_params.ColorMode);
            _material.SetFloat(AlphaClipId, _params.BlendMode == LayerBlendMode.Opaque ? 1f : 0f);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
        }

        private static int Mod(int value, int divisor) => ((value % divisor) + divisor) % divisor;

        public Matrix4x4 GetRepeaterCopyTransform(int copyIndex, float phaseOffset)
        {
            if (copyIndex == 0) return Matrix4x4.identity;
            ModulationContext copyContext = _modulationContext.WithAnimationPhaseOffset(phaseOffset);
            Matrix4x4 baseMatrix = EvaluateLayerMatrix(_modulationContext);
            Matrix4x4 copyMatrix = EvaluateLayerMatrix(copyContext);
            return baseMatrix.inverse * copyMatrix;
        }

        public float GetRepeaterCopyOpacity(int copyIndex, float phaseOffset)
        {
            float baseOpacity = Mathf.Clamp01(_params.Opacity?.Evaluate(_modulationContext) ?? 1f);
            ModulationContext copyContext = _modulationContext.WithAnimationPhaseOffset(phaseOffset);
            float copyOpacity = Mathf.Clamp01(_params.Opacity?.Evaluate(copyContext) ?? 1f);
            return baseOpacity > Mathf.Epsilon ? copyOpacity / baseOpacity : copyOpacity;
        }

        private Matrix4x4 EvaluateLayerMatrix(in ModulationContext context)
        {
            return Matrix4x4.TRS(
                       _params.Position?.Evaluate(context) ?? Vector3.zero,
                       Quaternion.Euler(_params.Rotation?.Evaluate(context) ?? Vector3.zero),
                       _params.Scale?.Evaluate(context) ?? Vector3.one) *
                   Matrix4x4.Translate(-(_params.Anchor?.Evaluate(context) ?? Vector3.zero));
        }

        private void OnDestroy()
        {
            if (_material != null) DestroyResource(_material);
            if (_mesh != null) DestroyResource(_mesh);
        }

        private static void DestroyResource(Object resource)
        {
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
