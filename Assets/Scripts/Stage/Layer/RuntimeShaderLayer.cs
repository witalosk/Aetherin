using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RuntimeShaderLayer : StageLayer
    {
        private static readonly int AetherinTimeId = Shader.PropertyToID("_AetherinTime");
        private static readonly int AetherinFrameId = Shader.PropertyToID("_AetherinFrame");
        private static readonly int AetherinResolutionId = Shader.PropertyToID("_AetherinResolution");
        private static readonly int AetherinQuadId = Shader.PropertyToID("_AetherinQuad");
        private static readonly int AetherinAudioId = Shader.PropertyToID("_AetherinAudio");
        private static readonly int AetherinBeatId = Shader.PropertyToID("_AetherinBeat");
        private static readonly int AetherinBarId = Shader.PropertyToID("_AetherinBar");
        private static readonly int WaveformTexId = Shader.PropertyToID("_AetherinWaveformTex");
        private static readonly int SpectrumTexId = Shader.PropertyToID("_AetherinSpectrumTex");
        private static readonly int OpacityId = Shader.PropertyToID("_AetherinOpacity");
        private static readonly int[] UserFloatIds = CreatePropertyIds("_UserFloat");
        private static readonly int[] UserVectorIds = CreatePropertyIds("_UserVector");

        [SerializeField] private RuntimeShaderLayerParams _params = new();

        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private Material _material;
        private Shader _activeShader;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => RendererComponent;

        private MeshRenderer RendererComponent =>
            _meshRenderer != null ? _meshRenderer : _meshRenderer = GetComponent<MeshRenderer>();

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider) =>
            Initialize(audio, beat, deckStateProvider);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new RuntimeShaderLayerParams();
            _params.EnsureInitialized();
            _stage = GetComponentInParent<StageBase>();
            EnsureResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureResources();
            if (_material == null) return;

            bool runtime = Application.isPlaying;
            double time = runtime ? Time.timeAsDouble : 0d;
            var context = new ModulationContext(time, runtime ? _audio : null, runtime ? _beat : null,
                runtime && (_stage == null || _stage.Deck == StageDeck.Next));

            Vector3 position = _params.Position.Evaluate(context);
            Vector3 rotation = _params.Rotation.Evaluate(context);
            Vector3 scale = _params.Scale.Evaluate(context);
            Vector3 anchor = _params.Anchor.Evaluate(context);
            Vector2 size = _params.Size.Evaluate(context);
            size.x = Mathf.Max(0f, size.x);
            size.y = Mathf.Max(0f, size.y);
            Quaternion orientation = Quaternion.Euler(
                Mathf.Repeat(rotation.x, 360f), Mathf.Repeat(rotation.y, 360f), Mathf.Repeat(rotation.z, 360f));
            transform.localPosition = position - orientation * Vector3.Scale(anchor, scale);
            transform.localRotation = orientation;
            transform.localScale = new Vector3(size.x * scale.x, size.y * scale.y, scale.z);

            float opacity = Mathf.Clamp01(_params.Opacity.Evaluate(context));
            Vector2Int resolution = GetResolution();
            float width = Mathf.Max(1, resolution.x);
            float height = Mathf.Max(1, resolution.y);
            _material.SetVector(AetherinTimeId, new Vector4((float)time, Time.deltaTime, Mathf.Sin((float)time), Mathf.Cos((float)time)));
            _material.SetVector(AetherinFrameId, new Vector4(Time.frameCount, Time.timeScale, Time.unscaledTime, Time.unscaledDeltaTime));
            _material.SetVector(AetherinResolutionId, new Vector4(width, height, 1f / width, 1f / height));
            _material.SetVector(AetherinQuadId, new Vector4(size.x, size.y, size.y > 0f ? size.x / size.y : 0f, opacity));
            _material.SetVector(AetherinAudioId, new Vector4(_audio?.InputVolume ?? 0f, _audio?.Kick ?? 0f,
                _audio?.SnareClap ?? 0f, (_audio?.WasKick ?? false) || (_audio?.WasSnareClap ?? false) ? 1f : 0f));
            _material.SetVector(AetherinBeatId, new Vector4(_beat?.BeatPhase ?? 1f, _beat?.BeatCount ?? 0,
                _beat?.BeatInBar ?? 0, _beat?.WasBeat == true ? 1f : 0f));
            _material.SetVector(AetherinBarId, new Vector4(_beat?.BarPhase ?? 1f, _beat?.BarCount ?? 0,
                _beat?.BeatsPerBar ?? 4, _beat?.WasBar == true ? 1f : 0f));
            _material.SetTexture(WaveformTexId, _audio?.WaveformTexture ?? Texture2D.blackTexture);
            _material.SetTexture(SpectrumTexId, _audio?.SpectrumTexture ?? Texture2D.blackTexture);
            _material.SetFloat(OpacityId, opacity);
            SetUserParameters(context);

            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Next).Palette;
            palette?.ApplyToMaterial(_material);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);
            ApplyLayerState();
        }

        private void EnsureResources()
        {
            if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
            if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();
            if (_mesh == null)
            {
                _mesh = CreateQuad();
                _meshFilter.sharedMesh = _mesh;
            }

            Shader shader = _params.Shader;
            if (shader != null) _params.ShaderName = shader.name;
            else if (!string.IsNullOrWhiteSpace(_params.ShaderName)) shader = Shader.Find(_params.ShaderName);
            if (shader == null) shader = Shader.Find("Aetherin/Runtime Shader Layer Example");
            if (shader == null || shader == _activeShader && _material != null) return;

            DestroyResource(_material);
            _activeShader = shader;
            _params.Shader = shader;
            _params.ShaderName = shader.name;
            _material = new Material(shader) { name = $"{name} Runtime Shader Material", hideFlags = HideFlags.DontSave };
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void SetUserParameters(in ModulationContext context)
        {
            FloatParameter[] floats = { _params.UserFloat0, _params.UserFloat1, _params.UserFloat2, _params.UserFloat3 };
            Vector3Parameter[] vectors = { _params.UserVector0, _params.UserVector1, _params.UserVector2, _params.UserVector3 };
            for (int i = 0; i < 4; i++)
            {
                _material.SetFloat(UserFloatIds[i], floats[i].Evaluate(context));
                _material.SetVector(UserVectorIds[i], vectors[i].Evaluate(context));
            }
        }

        private Vector2Int GetResolution()
        {
            RenderTexture texture = _stage != null ? _stage.OutputTexture : null;
            return texture != null ? new Vector2Int(texture.width, texture.height) : new Vector2Int(Screen.width, Screen.height);
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "Runtime Shader Quad", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
            mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int[] CreatePropertyIds(string prefix) => new[]
        {
            Shader.PropertyToID(prefix + "0"), Shader.PropertyToID(prefix + "1"),
            Shader.PropertyToID(prefix + "2"), Shader.PropertyToID(prefix + "3"),
        };

        private void OnDestroy()
        {
            DestroyResource(_material);
            DestroyResource(_mesh);
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
