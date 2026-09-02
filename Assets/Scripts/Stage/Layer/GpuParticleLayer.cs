using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using UnitySimpleContainer;

namespace Aetherin
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GpuParticleLayer : StageLayer
    {
        private const int ThreadGroupSize = 256;
        private const int ParticleStride = 64;
        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int CapacityId = Shader.PropertyToID("_ParticleCapacity");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");
        private static readonly int ModuleTypeId = Shader.PropertyToID("_ModuleType");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int VectorId = Shader.PropertyToID("_Vector");
        private static readonly int ScaleValueId = Shader.PropertyToID("_ScaleValue");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int SecondaryId = Shader.PropertyToID("_Secondary");
        private static readonly int TargetId = Shader.PropertyToID("_Target");
        private static readonly int EmitterSizeId = Shader.PropertyToID("_EmitterSize");
        private static readonly int LifetimeId = Shader.PropertyToID("_Lifetime");
        private static readonly int InitialSpeedId = Shader.PropertyToID("_InitialSpeed");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int ParticleSizeId = Shader.PropertyToID("_ParticleSize");
        private static readonly int ParticleShapeId = Shader.PropertyToID("_ParticleShape");
        private static readonly int LayerMatrixId = Shader.PropertyToID("_LayerMatrix");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        [SerializeField] private GpuParticleLayerParams _params = new();
        [SerializeField] private ComputeShader _simulationShader;
        [SerializeField] private Shader _renderShader;
        [Tooltip("Render BackendがVfxGraphのとき、ParticleBufferを公開GraphicsBufferへ渡すVFX Asset")]
        [SerializeField] private VisualEffectAsset _vfxAsset;

        private GraphicsBuffer _particles;
        private GraphicsBuffer _args;
        private Mesh _quad;
        private Material _material;
        private ComputeShader _compute;
        private VisualEffect _visualEffect;
        private VisualEffectAsset _resolvedVfxAsset;
        private string _resolvedVfxPath;
        private int _resetKernel;
        private int _moduleKernel;
        private int _allocatedCapacity;
        private bool _renderEnabled = true;
        private double _lastEditorTime;

        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private StageBase _stage;

        public override IParams Params => _params;
        protected override StageLayerParams LayerParams => _params;
        public GraphicsBuffer ParticleBuffer => _particles;
        public int ParticleCapacity => _allocatedCapacity;

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
            _params ??= new GpuParticleLayerParams();
            _params.EnsureInitialized();
            EnsureResources();
            ApplyLayerState();
        }

        private void Update()
        {
            _params.EnsureInitialized();
            EnsureResources();
            if (_compute == null || _particles == null || _material == null) return;

            double now = Application.isPlaying ? Time.unscaledTimeAsDouble : UnityEngine.Time.realtimeSinceStartupAsDouble;
            float rawDelta = Application.isPlaying
                ? Time.unscaledDeltaTime
                : (float)Math.Max(0.0, Math.Min(0.05, now - _lastEditorTime));
            _lastEditorTime = now;

            var context = new ModulationContext(now, _audio, _beat, Application.isPlaying);
            float deltaTime = rawDelta * Mathf.Max(0f, _params.SimulationSpeed?.Evaluate(context) ?? 1f);
            DispatchModules(context, deltaTime, now);
            ApplyRendering(context);
        }

        private void EnsureResources()
        {
            _compute ??= _simulationShader != null
                ? Instantiate(_simulationShader)
                : Instantiate(Resources.Load<ComputeShader>("ParticleSimulation"));
            if (_compute == null) return;

            if (_resetKernel == 0 && _moduleKernel == 0)
            {
                _resetKernel = _compute.FindKernel("ResetParticles");
                _moduleKernel = _compute.FindKernel("ApplyModule");
            }

            int capacity = Mathf.Clamp(_params.Capacity, 1, 262144);
            if (_particles == null || _allocatedCapacity != capacity)
            {
                ReleaseBuffers();
                _allocatedCapacity = capacity;
                _particles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, ParticleStride)
                {
                    name = $"{name} Particles"
                };
                _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint))
                {
                    name = $"{name} Draw Args"
                };
                EnsureQuad();
                _args.SetData(new uint[] { _quad.GetIndexCount(0), (uint)capacity, 0, 0, 0 });
                ResetParticles();
                BindVfxGraph();
            }

            if (_material == null)
            {
                Shader shader = _renderShader != null ? _renderShader : Shader.Find("Aetherin/GPU Particle");
                if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private void ResetParticles()
        {
            if (_particles == null || _compute == null) return;
            var context = new ModulationContext(Time.unscaledTimeAsDouble, _audio, _beat, Application.isPlaying);
            _compute.SetBuffer(_resetKernel, ParticlesId, _particles);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.SetVector(EmitterSizeId, _params.EmitterSize?.Evaluate(context) ?? Vector3.one);
            _compute.SetFloat(LifetimeId, Mathf.Max(0.01f, _params.Lifetime?.Evaluate(context) ?? 5f));
            _compute.SetFloat(InitialSpeedId, _params.InitialSpeed?.Evaluate(context) ?? 0f);
            _compute.SetInt(SeedId, _params.Seed);
            _compute.Dispatch(_resetKernel, Groups, 1, 1);
        }

        private void DispatchModules(in ModulationContext context, float deltaTime, double now)
        {
            if (deltaTime <= 0f || _params.Modules == null) return;
            _compute.SetBuffer(_moduleKernel, ParticlesId, _particles);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.SetFloat(DeltaTimeId, deltaTime);
            _compute.SetFloat(TimeValueId, (float)now);
            _compute.SetVector(EmitterSizeId, _params.EmitterSize?.Evaluate(context) ?? Vector3.one);
            _compute.SetFloat(LifetimeId, Mathf.Max(0.01f, _params.Lifetime?.Evaluate(context) ?? 5f));
            _compute.SetFloat(InitialSpeedId, _params.InitialSpeed?.Evaluate(context) ?? 0f);
            _compute.SetInt(SeedId, _params.Seed);

            foreach (var module in _params.Modules)
            {
                if (module == null || !module.Enabled) continue;
                _compute.SetInt(ModuleTypeId, (int)module.Type);
                _compute.SetFloat(StrengthId, module.Strength?.Evaluate(context) ?? 1f);
                _compute.SetVector(VectorId, module.Vector?.Evaluate(context) ?? Vector3.zero);
                _compute.SetFloat(ScaleValueId, module.Scale?.Evaluate(context) ?? 1f);
                _compute.SetFloat(SpeedId, module.Speed?.Evaluate(context) ?? 1f);
                _compute.SetFloat(SecondaryId, module.Secondary?.Evaluate(context) ?? 1f);
                _compute.SetInt(TargetId, (int)module.Target);
                _compute.Dispatch(_moduleKernel, Groups, 1, 1);
            }
        }

        private void ApplyRendering(in ModulationContext context)
        {
            EnsureVfxGraph();
            bool useVfxGraph = _params.RenderBackend == ParticleRenderBackend.VfxGraph &&
                               _visualEffect != null && _visualEffect.visualEffectAsset != null;
            if (_visualEffect != null) _visualEffect.enabled = _renderEnabled && useVfxGraph;
            if (!_renderEnabled) return;

            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Current).Palette;
            EvaluatedPaletteColor color = EvaluatedPaletteColor.Evaluate(_params.Color, palette, context);
            Vector3 position = _params.Position?.Evaluate(context) ?? Vector3.zero;
            Vector3 rotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            Vector3 scale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            Matrix4x4 local = Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale);
            Matrix4x4 layerMatrix = transform.localToWorldMatrix * local;

            float particleSize = Mathf.Max(0f, _params.ParticleSize?.Evaluate(context) ?? 0.03f);
            float opacity = Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
            if (useVfxGraph)
            {
                SetVfxGraphProperties(layerMatrix, color, particleSize, opacity);
                return;
            }

            if (_quad == null || _args == null || _material == null) return;

            _material.SetBuffer(ParticlesId, _particles);
            _material.SetMatrix(LayerMatrixId, layerMatrix);
            _material.SetColor(ColorAId, color.ColorA);
            _material.SetColor(ColorBId, color.ColorB);
            _material.SetFloat(ParticleSizeId, particleSize);
            _material.SetInt(ParticleShapeId, (int)_params.Shape);
            _material.SetFloat(OpacityId, opacity);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);

            Vector3 extent = Vector3.Scale(_params.EmitterSize?.Evaluate(context) ?? Vector3.one,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 1.5f + Vector3.one * 4f;
            var bounds = new Bounds(layerMatrix.MultiplyPoint3x4(Vector3.zero), extent * 2f);
#pragma warning disable 0618
            Graphics.DrawMeshInstancedIndirect(_quad, 0, _material, bounds, _args, 0, null,
                ShadowCastingMode.Off, false, gameObject.layer);
#pragma warning restore 0618
        }

        protected override void ApplyCustomLayerState(bool visible, int order) => _renderEnabled = visible;

        private void EnsureVfxGraph()
        {
            if (_resolvedVfxPath != _params.VfxGraphResourcePath)
            {
                _resolvedVfxPath = _params.VfxGraphResourcePath;
                _resolvedVfxAsset = string.IsNullOrWhiteSpace(_resolvedVfxPath)
                    ? null
                    : Resources.Load<VisualEffectAsset>(_resolvedVfxPath);
            }

            VisualEffectAsset asset = _resolvedVfxAsset != null ? _resolvedVfxAsset : _vfxAsset;
            if (asset == null)
            {
                if (_visualEffect != null) _visualEffect.enabled = false;
                return;
            }

            if (_visualEffect == null) _visualEffect = GetComponent<VisualEffect>() ?? gameObject.AddComponent<VisualEffect>();
            if (_visualEffect.visualEffectAsset != asset)
            {
                _visualEffect.visualEffectAsset = asset;
                _visualEffect.Reinit();
                BindVfxGraph();
            }
        }

        private void BindVfxGraph()
        {
            if (_visualEffect == null || _particles == null) return;
            if (_visualEffect.HasGraphicsBuffer("ParticleBuffer"))
                _visualEffect.SetGraphicsBuffer("ParticleBuffer", _particles);
            if (_visualEffect.HasInt("ParticleCapacity"))
                _visualEffect.SetInt("ParticleCapacity", _allocatedCapacity);
        }

        private void SetVfxGraphProperties(
            Matrix4x4 layerMatrix,
            in EvaluatedPaletteColor color,
            float particleSize,
            float opacity)
        {
            BindVfxGraph();
            if (_visualEffect.HasMatrix4x4("LayerMatrix")) _visualEffect.SetMatrix4x4("LayerMatrix", layerMatrix);
            if (_visualEffect.HasVector4("ColorA")) _visualEffect.SetVector4("ColorA", color.ColorA);
            if (_visualEffect.HasVector4("ColorB")) _visualEffect.SetVector4("ColorB", color.ColorB);
            if (_visualEffect.HasFloat("ParticleSize")) _visualEffect.SetFloat("ParticleSize", particleSize);
            if (_visualEffect.HasInt("ParticleShape")) _visualEffect.SetInt("ParticleShape", (int)_params.Shape);
            if (_visualEffect.HasFloat("Opacity")) _visualEffect.SetFloat("Opacity", opacity);
        }

        private int Groups => Mathf.CeilToInt(_allocatedCapacity / (float)ThreadGroupSize);

        private void EnsureQuad()
        {
            if (_quad != null) return;
            _quad = new Mesh { name = "GPU Particle Quad", hideFlags = HideFlags.HideAndDontSave };
            _quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f),
            });
            _quad.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.RecalculateBounds();
        }

        private void ReleaseBuffers()
        {
            _particles?.Release();
            _args?.Release();
            _particles = null;
            _args = null;
            _allocatedCapacity = 0;
        }

        private void OnDisable() => ReleaseResources();
        private void OnDestroy() => ReleaseResources();

        private void ReleaseResources()
        {
            ReleaseBuffers();
            DestroyResource(_material);
            DestroyResource(_quad);
            DestroyResource(_compute);
            if (_visualEffect != null) _visualEffect.enabled = false;
            _material = null;
            _quad = null;
            _compute = null;
        }

        private static void DestroyResource(UnityEngine.Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }
    }
}
