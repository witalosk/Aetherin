using UnityEngine;
using UnityEngine.Rendering;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class LightLayer : StageLayer
    {
        private static readonly int VolumeColorId = Shader.PropertyToID("_VolumeColor");
        private static readonly int DensityId = Shader.PropertyToID("_Density");
        private static readonly int VolumeIntensityId = Shader.PropertyToID("_VolumeIntensity");
        private static readonly int NoiseAmountId = Shader.PropertyToID("_NoiseAmount");
        private static readonly int StepsId = Shader.PropertyToID("_Steps");

        [SerializeField] private LightLayerParams _params = new();
        [SerializeField] private Shader _volumetricShader;

        private Light _light;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _coneMesh;
        private Material _volumeMaterial;
        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        protected override Renderer LayerRenderer => RendererComponent;

        private MeshRenderer RendererComponent => _meshRenderer != null ? _meshRenderer : _meshRenderer = GetComponent<MeshRenderer>();

        [Inject]
        private void Construct(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider) =>
            Initialize(audio, beat, deckStateProvider);

        public void Initialize(IAudioFeatureProvider audio, IBeatManager beat, IDeckStateProvider deckStateProvider)
        {
            _audio = audio;
            _beat = beat;
            _deckStateProvider = deckStateProvider;
            InitializeLayer();
        }

        private void Awake() => InitializeLayer();
        private void OnEnable() => InitializeLayer();

        private void InitializeLayer()
        {
            _params ??= new LightLayerParams();
            _params.EnsureInitialized();
            _stage = GetComponentInParent<StageBase>();
            _light ??= GetComponent<Light>();
            _meshFilter ??= GetComponent<MeshFilter>();
            _meshRenderer ??= GetComponent<MeshRenderer>();
            EnsureVolumeResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureVolumeResources();
            double time = Application.isPlaying ? Time.unscaledTimeAsDouble : Time.realtimeSinceStartupAsDouble;
            var context = CreateModulationContext(time, _audio, _beat, Application.isPlaying);
            transform.localPosition = _params.Position.Evaluate(context);
            transform.localRotation = Quaternion.Euler(_params.Rotation.Evaluate(context));

            float range = Mathf.Max(0.01f, _params.Range.Evaluate(context));
            float spotAngle = Mathf.Clamp(_params.SpotAngle.Evaluate(context), 1f, 179f);
            float radius = range * Mathf.Tan(spotAngle * Mathf.Deg2Rad * .5f);
            transform.localScale = new Vector3(radius, radius, range);
            _light.type = _params.LightType;
            _light.color = ResolveColor(context);
            _light.intensity = Mathf.Max(0f, _params.Intensity.Evaluate(context));
            _light.range = range;
            _light.spotAngle = spotAngle;
            _light.innerSpotAngle = Mathf.Clamp(_params.InnerSpotAngle.Evaluate(context), 0f, spotAngle);
            _light.shadows = _params.CastShadows ? LightShadows.Soft : LightShadows.None;

            bool volume = _params.LightType == LightType.Spot && _params.VolumetricEnabled;
            if (_volumeMaterial != null)
            {
                Color color = _light.color * Mathf.Max(0f, _params.VolumetricIntensity.Evaluate(context));
                color.a = Mathf.Clamp01(_params.Opacity.Evaluate(context));
                _volumeMaterial.SetColor(VolumeColorId, color);
                _volumeMaterial.SetFloat(DensityId, Mathf.Max(0f, _params.VolumetricDensity.Evaluate(context)));
                _volumeMaterial.SetFloat(VolumeIntensityId, Mathf.Max(0f, _params.VolumetricIntensity.Evaluate(context)));
                _volumeMaterial.SetFloat(NoiseAmountId, Mathf.Clamp01(_params.VolumetricNoise.Evaluate(context)));
                _volumeMaterial.SetInt(StepsId, Mathf.Clamp(_params.VolumetricSteps, 4, 64));
            }
            _meshRenderer.enabled = volume;
            ApplyLayerState();
        }

        private Color ResolveColor(in ModulationContext context)
        {
            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Current).Palette;
            return EvaluatedPaletteColor.Evaluate(_params.Color, palette, context).ColorA;
        }

        private void EnsureVolumeResources()
        {
            if (_meshFilter == null || _meshRenderer == null) return;
            if (_coneMesh == null)
            {
                _coneMesh = CreateConeMesh();
                _meshFilter.sharedMesh = _coneMesh;
            }
            if (_volumeMaterial == null)
            {
                Shader shader = _volumetricShader != null ? _volumetricShader : Shader.Find("Aetherin/Volumetric Spot Light");
                if (shader != null)
                {
                    _volumeMaterial = new Material(shader) { name = $"{name} Volumetric Spot", hideFlags = HideFlags.DontSave };
                    _meshRenderer.sharedMaterial = _volumeMaterial;
                    _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    _meshRenderer.receiveShadows = false;
                }
            }
        }

        protected override void ApplyCustomLayerState(bool visible, int order)
        {
            if (_light != null) _light.enabled = visible;
            if (_meshRenderer != null)
            {
                _meshRenderer.forceRenderingOff = !visible || _params == null || _params.LightType != LightType.Spot || !_params.VolumetricEnabled;
                _meshRenderer.sortingOrder = order;
            }
        }

        private static Mesh CreateConeMesh()
        {
            const int sides = 32;
            var vertices = new Vector3[sides + 2];
            vertices[0] = Vector3.zero;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 1f);
            }
            var triangles = new int[sides * 3];
            for (int i = 0; i < sides; i++)
            {
                int offset = i * 3;
                triangles[offset] = 0;
                // Outward-facing winding. The volume shader culls front faces so
                // the remaining back face represents the ray's cone exit point.
                triangles[offset + 1] = i + 2;
                triangles[offset + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Volumetric Spot Cone", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        private void OnDestroy()
        {
            DestroyResource(_coneMesh);
            DestroyResource(_volumeMaterial);
        }

        private static void DestroyResource(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource); else DestroyImmediate(resource);
        }
    }
}
