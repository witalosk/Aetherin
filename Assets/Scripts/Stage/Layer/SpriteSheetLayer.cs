using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    /// <summary>横方向にフレームを並べたスプライトシートを、常に水平を保ってカメラへ向けて描画する。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SpriteSheetLayer : StageLayer
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ColorModeId = Shader.PropertyToID("_ColorMode");
        private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");

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
                Application.isPlaying ? _audio : null, Application.isPlaying ? _beat : null, Application.isPlaying);
            ApplyTransform(context);
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
                _mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
                _mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                _mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                _mesh.RecalculateBounds();
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

            Camera camera = _cameraStage != null ? _cameraStage.StageCamera : null;
            Vector3 forward = camera != null ? camera.transform.position - transform.position : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .000001f) forward = Vector3.forward;
            Quaternion billboard = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Quaternion rotation = billboard * Quaternion.Euler(_params.Rotation.Evaluate(context));
            transform.localPosition = position - Quaternion.Inverse(transform.parent != null ? transform.parent.rotation : Quaternion.identity) *
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
                frame / (float)frameCount, (rowCount - 1 - row) / (float)rowCount));
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
