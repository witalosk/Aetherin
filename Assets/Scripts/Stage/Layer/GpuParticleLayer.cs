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
        private const int ParticleStride = 92;
        private const int OverLifeCurveSampleCount = 64;
        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int CapacityId = Shader.PropertyToID("_ParticleCapacity");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");
        private static readonly int ModuleTypeId = Shader.PropertyToID("_ModuleType");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int VectorId = Shader.PropertyToID("_Vector");
        private static readonly int AxisId = Shader.PropertyToID("_Axis");
        private static readonly int ScaleValueId = Shader.PropertyToID("_ScaleValue");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int SecondaryId = Shader.PropertyToID("_Secondary");
        private static readonly int OverLifeCurveId = Shader.PropertyToID("_OverLifeCurve");
        private static readonly int TargetId = Shader.PropertyToID("_Target");
        private static readonly int EmitterSizeId = Shader.PropertyToID("_EmitterSize");
        private static readonly int EmitterOffsetId = Shader.PropertyToID("_EmitterOffset");
        private static readonly int LifetimeRangeId = Shader.PropertyToID("_LifetimeRange");
        private static readonly int InitialSpeedRangeId = Shader.PropertyToID("_InitialSpeedRange");
        private static readonly int ParticleSizeRangeId = Shader.PropertyToID("_ParticleSizeRange");
        private static readonly int InitialRotationId = Shader.PropertyToID("_InitialRotation");
        private static readonly int RotationRandomId = Shader.PropertyToID("_RotationRandom");
        private static readonly int AngularVelocityId = Shader.PropertyToID("_AngularVelocity");
        private static readonly int AngularVelocityRandomId = Shader.PropertyToID("_AngularVelocityRandom");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
        private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
        private static readonly int ParticleSizeId = Shader.PropertyToID("_ParticleSize");
        private static readonly int ParticleShapeId = Shader.PropertyToID("_ParticleShape");
        private static readonly int LayerMatrixId = Shader.PropertyToID("_LayerMatrix");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int AlphaClipId = Shader.PropertyToID("_AlphaClip");
        private static readonly int PaletteRandomModeId = Shader.PropertyToID("_PaletteRandomMode");
        private static readonly int PaletteRandomSeedId = Shader.PropertyToID("_PaletteRandomSeed");
        private static readonly int[] PaletteColorIds = CreatePropertyIds("_PaletteColor", 6);
        private static readonly int TrailSamplesId = Shader.PropertyToID("_TrailSamples");
        private static readonly int TrailLengthId = Shader.PropertyToID("_TrailLength");
        private static readonly int TrailFrameIndexId = Shader.PropertyToID("_TrailFrameIndex");
        private static readonly int TrailWidthId = Shader.PropertyToID("_TrailWidth");
        private static readonly int TrailTailWidthId = Shader.PropertyToID("_TrailTailWidth");
        private static readonly int BoidParticlesId = Shader.PropertyToID("_BoidParticles");
        private static readonly int BoidNeighborRadiiId = Shader.PropertyToID("_BoidNeighborRadii");
        private static readonly int BoidNeighborSearchModeId = Shader.PropertyToID("_BoidNeighborSearchMode");
        private static readonly int BoidFieldOfViewId = Shader.PropertyToID("_BoidFieldOfView");
        private static readonly int BoidWeightsId = Shader.PropertyToID("_BoidWeights");
        private static readonly int BoidMaxForcesId = Shader.PropertyToID("_BoidMaxForces");
        private static readonly int BoidMaxSpeedId = Shader.PropertyToID("_BoidMaxSpeed");
        private static readonly int BoidMaxAccelerationId = Shader.PropertyToID("_BoidMaxAcceleration");
        private static readonly int BoidNeighborSamplesId = Shader.PropertyToID("_BoidNeighborSamples");
        private static readonly int BoidSeparationUsesFieldOfViewId =
            Shader.PropertyToID("_BoidSeparationUsesFieldOfView");

        [SerializeField] private GpuParticleLayerParams _params = new();
        [SerializeField] private ComputeShader _simulationShader;
        [SerializeField] private Shader _renderShader;

        private GraphicsBuffer _particles;
        private GraphicsBuffer _args;
        private GraphicsBuffer _trailSamples;
        private GraphicsBuffer _trailArgs;
        private GraphicsBuffer _boidParticles;
        private Mesh _quad;
        private Material _material;
        private Material _trailMaterial;
        private ComputeShader _compute;
        private VisualEffect _visualEffect;
        private int _resetKernel;
        private int _moduleKernel;
        private int _initializeTrailsKernel;
        private int _recordTrailsKernel;
        private int _copyBoidSnapshotKernel;
        private int _applyBoidsKernel;
        private int _allocatedCapacity;
        private int _trailAllocatedCapacity;
        private int _trailAllocatedLength;
        private int _trailFrameIndex;
        private bool _kernelsInitialized;
        private bool _renderEnabled = true;
        private double _lastEditorTime;
        private readonly float[] _overLifeCurveSamples = new float[OverLifeCurveSampleCount];

        private IAudioFeatureProvider _audio;
        private IBeatManager _beat;
        private IDeckStateProvider _deckStateProvider;
        private CameraStage _cameraStage;
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
            _cameraStage = GetComponentInParent<CameraStage>();
            _params ??= new GpuParticleLayerParams();
            _params.EnsureInitialized();
            _params.GetAvailableVfxGraphKeys = _cameraStage != null ? _cameraStage.GetVfxGraphKeys : null;
            var keys = _params.GetAvailableVfxGraphKeys?.Invoke();
            if (string.IsNullOrWhiteSpace(_params.VfxGraphKey) && keys != null && keys.Count > 0)
                _params.VfxGraphKey = keys[0];
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

            var context = CreateModulationContext(now, _audio, _beat, Application.isPlaying);
            float deltaTime = rawDelta * Mathf.Max(0f, _params.SimulationSpeed?.Evaluate(context) ?? 1f);
            DispatchModules(context, deltaTime, now);
            if (_params.RenderBackend == ParticleRenderBackend.Trail)
            {
                EnsureTrailResources();
                RecordTrails();
            }
            else
            {
                ReleaseTrailResources();
            }
            ApplyRendering(context);
        }

        private void EnsureResources()
        {
            _compute ??= _simulationShader != null
                ? Instantiate(_simulationShader)
                : Instantiate(Resources.Load<ComputeShader>("ParticleSimulation"));
            if (_compute == null) return;

            if (!_kernelsInitialized)
            {
                _resetKernel = _compute.FindKernel("ResetParticles");
                _moduleKernel = _compute.FindKernel("ApplyModule");
                _initializeTrailsKernel = _compute.FindKernel("InitializeTrails");
                _recordTrailsKernel = _compute.FindKernel("RecordTrails");
                _copyBoidSnapshotKernel = _compute.FindKernel("CopyBoidSnapshot");
                _applyBoidsKernel = _compute.FindKernel("ApplyBoids");
                _kernelsInitialized = true;
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
            var context = CreateModulationContext(Time.unscaledTimeAsDouble, _audio, _beat, Application.isPlaying);
            _compute.SetBuffer(_resetKernel, ParticlesId, _particles);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            SetSpawnParameters(context);
            _compute.SetInt(SeedId, _params.Seed);
            _compute.Dispatch(_resetKernel, Groups, 1, 1);
        }

        private void DispatchModules(in ModulationContext context, float deltaTime, double now)
        {
            if (deltaTime <= 0f || _params.Modules == null) return;
            bool hasBoids = false;
            foreach (var module in _params.Modules)
            {
                if (module != null && module.Enabled && module.Type == ParticleSimulationModuleType.ApplyBoids)
                {
                    hasBoids = true;
                    break;
                }
            }
            if (hasBoids) EnsureBoidBuffer();
            else ReleaseBoidResources();
            _compute.SetBuffer(_moduleKernel, ParticlesId, _particles);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.SetFloat(DeltaTimeId, deltaTime);
            _compute.SetFloat(TimeValueId, (float)now);
            SetSpawnParameters(context);
            _compute.SetInt(SeedId, _params.Seed);

            foreach (var module in _params.Modules)
            {
                if (module == null || !module.Enabled) continue;
                if (module.Type == ParticleSimulationModuleType.ApplyBoids)
                    CopyBoidSnapshot();
                _compute.SetInt(ModuleTypeId, (int)module.Type);
                _compute.SetFloat(StrengthId, module.Strength?.Evaluate(context) ?? 1f);
                _compute.SetVector(VectorId, module.Vector?.Evaluate(context) ?? Vector3.zero);
                _compute.SetVector(AxisId, module.Axis?.Evaluate(context) ?? Vector3.up);
                _compute.SetFloat(ScaleValueId, module.Scale?.Evaluate(context) ?? 1f);
                _compute.SetFloat(SpeedId, module.Speed?.Evaluate(context) ?? 1f);
                _compute.SetFloat(SecondaryId, module.Secondary?.Evaluate(context) ?? 1f);
                _compute.SetVector(BoidNeighborRadiiId,
                    module.BoidsNeighborRadii?.Evaluate(context) ?? Vector3.one);
                _compute.SetInt(BoidNeighborSearchModeId, (int)module.BoidsNeighborSearch);
                _compute.SetFloat(BoidFieldOfViewId, module.BoidsFieldOfView?.Evaluate(context) ?? 360f);
                _compute.SetVector(BoidWeightsId,
                    module.BoidsWeights?.Evaluate(context) ?? new Vector3(1.5f, 0.75f, 0.5f));
                _compute.SetVector(BoidMaxForcesId,
                    module.BoidsMaxForces?.Evaluate(context) ?? new Vector3(2f, 1f, 1f));
                _compute.SetFloat(BoidMaxSpeedId, module.BoidsMaxSpeed?.Evaluate(context) ?? 2f);
                _compute.SetFloat(BoidMaxAccelerationId,
                    module.BoidsMaxAcceleration?.Evaluate(context) ?? 4f);
                _compute.SetInt(BoidNeighborSamplesId, Mathf.Clamp(module.BoidsNeighborSamples, 1, 32));
                _compute.SetInt(BoidSeparationUsesFieldOfViewId,
                    module.BoidsSeparationUsesFieldOfView ? 1 : 0);
                if (module.Type is ParticleSimulationModuleType.ColorOverLife
                    or ParticleSimulationModuleType.SizeOverLife)
                    SetOverLifeCurve(module.OverLifeCurve);
                _compute.SetInt(TargetId, (int)module.Target);
                if (module.Type == ParticleSimulationModuleType.ApplyBoids)
                {
                    _compute.SetBuffer(_applyBoidsKernel, ParticlesId, _particles);
                    _compute.SetBuffer(_applyBoidsKernel, BoidParticlesId, _boidParticles);
                    _compute.Dispatch(_applyBoidsKernel, Groups, 1, 1);
                }
                else
                {
                    _compute.Dispatch(_moduleKernel, Groups, 1, 1);
                }
            }
        }

        private void CopyBoidSnapshot()
        {
            if (_particles == null || _compute == null) return;
            EnsureBoidBuffer();

            _compute.SetBuffer(_copyBoidSnapshotKernel, ParticlesId, _particles);
            _compute.SetBuffer(_copyBoidSnapshotKernel, BoidParticlesId, _boidParticles);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.Dispatch(_copyBoidSnapshotKernel, Groups, 1, 1);
        }

        private void EnsureBoidBuffer()
        {
            if (_boidParticles != null && _boidParticles.count == _allocatedCapacity) return;
            _boidParticles?.Release();
            _boidParticles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _allocatedCapacity, ParticleStride)
            {
                name = $"{name} Boid Snapshot"
            };
        }

        private void SetOverLifeCurve(AnimationCurve curve)
        {
            curve ??= ParticleSimulationModule.CreateDefaultOverLifeCurve(ParticleSimulationModuleType.ColorOverLife);
            for (int i = 0; i < OverLifeCurveSampleCount; i++)
            {
                float time = i / (float)(OverLifeCurveSampleCount - 1);
                _overLifeCurveSamples[i] = curve.Evaluate(time);
            }

            _compute.SetFloats(OverLifeCurveId, _overLifeCurveSamples);
        }

        private void SetSpawnParameters(in ModulationContext context)
        {
            _compute.SetVector(EmitterOffsetId, _params.EmitterOffset?.Evaluate(context) ?? Vector3.zero);
            _compute.SetVector(EmitterSizeId, _params.EmitterSize?.Evaluate(context) ?? Vector3.one);
            _compute.SetVector(LifetimeRangeId, _params.Lifetime?.Evaluate(context) ?? new Vector3(5f, 5f, 1f));
            _compute.SetVector(InitialSpeedRangeId,
                _params.InitialSpeed?.Evaluate(context) ?? new Vector3(0f, 0f, 1f));
            _compute.SetVector(ParticleSizeRangeId,
                _params.ParticleSize?.Evaluate(context) ?? new Vector3(0.03f, 0.03f, 1f));
            _compute.SetVector(InitialRotationId, _params.InitialRotation?.Evaluate(context) ?? Vector3.zero);
            _compute.SetVector(RotationRandomId, _params.RotationRandom?.Evaluate(context) ?? Vector3.zero);
            _compute.SetVector(AngularVelocityId, _params.AngularVelocity?.Evaluate(context) ?? Vector3.zero);
            _compute.SetVector(AngularVelocityRandomId,
                _params.AngularVelocityRandom?.Evaluate(context) ?? Vector3.zero);
        }

        private void ApplyRendering(in ModulationContext context)
        {
            EnsureVfxGraph();
            bool useVfxGraph = _params.RenderBackend == ParticleRenderBackend.VfxGraph &&
                               _visualEffect != null && _visualEffect.visualEffectAsset != null;
            bool useTrail = _params.RenderBackend == ParticleRenderBackend.Trail &&
                            _trailSamples != null && _trailArgs != null && _trailMaterial != null;
            if (_visualEffect != null) _visualEffect.enabled = _renderEnabled && useVfxGraph;
            if (!_renderEnabled) return;

            ColorPalette palette = _deckStateProvider?.GetState(_stage != null ? _stage.Deck : StageDeck.Current).Palette;
            EvaluatedPaletteColor color = EvaluatedPaletteColor.Evaluate(_params.Color, palette, context);
            Vector3 position = _params.Position?.Evaluate(context) ?? Vector3.zero;
            Vector3 rotation = _params.Rotation?.Evaluate(context) ?? Vector3.zero;
            Vector3 scale = _params.Scale?.Evaluate(context) ?? Vector3.one;
            Matrix4x4 local = Matrix4x4.TRS(position, Quaternion.Euler(rotation), scale);
            Matrix4x4 layerMatrix = transform.localToWorldMatrix * local;

            float particleSize = 1f;
            float opacity = Mathf.Clamp01(_params.Opacity?.Evaluate(context) ?? 1f);
            if (useVfxGraph)
            {
                SetVfxGraphProperties(layerMatrix, color, particleSize, opacity);
                return;
            }

            if (useTrail)
            {
                DrawTrails(layerMatrix, color, particleSize, opacity, scale, context);
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
            _material.SetFloat(AlphaClipId, _params.BlendMode == LayerBlendMode.Opaque ? 1f : 0f);
            ApplyPaletteRandom(_material, color);
            LayerMaterialUtility.ApplyBlendMode(_material, _params.BlendMode);

            Vector3 extent = Vector3.Scale(_params.EmitterSize?.Evaluate(context) ?? Vector3.one,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 1.5f + Vector3.one * 4f;
            Vector3 emitterOffset = _params.EmitterOffset?.Evaluate(context) ?? Vector3.zero;
            var bounds = new Bounds(layerMatrix.MultiplyPoint3x4(emitterOffset), extent * 2f);
#pragma warning disable 0618
            Graphics.DrawMeshInstancedIndirect(_quad, 0, _material, bounds, _args, 0, null,
                ShadowCastingMode.Off, false, gameObject.layer);
#pragma warning restore 0618
        }

        private void EnsureTrailResources()
        {
            if (_particles == null || _compute == null) return;
            int length = Mathf.Clamp(_params.TrailLength, 2, 64);
            if (_trailSamples != null && _trailAllocatedCapacity == _allocatedCapacity && _trailAllocatedLength == length)
                return;

            ReleaseTrailResources();
            _trailAllocatedCapacity = _allocatedCapacity;
            _trailAllocatedLength = length;
            _trailFrameIndex = 0;
            _trailSamples = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _allocatedCapacity * length, 16)
            {
                name = $"{name} Trail Samples"
            };
            _trailArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint))
            {
                name = $"{name} Trail Args"
            };
            EnsureQuad();
            _trailArgs.SetData(new uint[]
            {
                _quad.GetIndexCount(0), (uint)(_allocatedCapacity * (length - 1)), 0, 0, 0
            });
            Shader shader = Shader.Find("Aetherin/GPU Particle Trail");
            if (shader != null) _trailMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

            _compute.SetBuffer(_initializeTrailsKernel, ParticlesId, _particles);
            _compute.SetBuffer(_initializeTrailsKernel, TrailSamplesId, _trailSamples);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.SetInt(TrailLengthId, length);
            _compute.Dispatch(_initializeTrailsKernel, Mathf.CeilToInt(_allocatedCapacity * length / (float)ThreadGroupSize), 1, 1);
        }

        private void RecordTrails()
        {
            if (_trailSamples == null || _trailAllocatedLength < 2) return;
            _compute.SetBuffer(_recordTrailsKernel, ParticlesId, _particles);
            _compute.SetBuffer(_recordTrailsKernel, TrailSamplesId, _trailSamples);
            _compute.SetInt(CapacityId, _allocatedCapacity);
            _compute.SetInt(TrailLengthId, _trailAllocatedLength);
            _compute.SetInt(TrailFrameIndexId, _trailFrameIndex);
            _compute.Dispatch(_recordTrailsKernel, Groups, 1, 1);
            _trailFrameIndex = _trailFrameIndex == int.MaxValue ? 0 : _trailFrameIndex + 1;
        }

        private void DrawTrails(
            Matrix4x4 layerMatrix,
            in EvaluatedPaletteColor color,
            float particleSize,
            float opacity,
            Vector3 scale,
            in ModulationContext context)
        {
            _trailMaterial.SetBuffer(ParticlesId, _particles);
            _trailMaterial.SetBuffer(TrailSamplesId, _trailSamples);
            _trailMaterial.SetMatrix(LayerMatrixId, layerMatrix);
            _trailMaterial.SetColor(ColorAId, color.ColorA);
            _trailMaterial.SetColor(ColorBId, color.ColorB);
            _trailMaterial.SetFloat(ParticleSizeId, particleSize);
            _trailMaterial.SetFloat(OpacityId, opacity);
            _trailMaterial.SetInt(TrailLengthId, _trailAllocatedLength);
            _trailMaterial.SetInt(TrailFrameIndexId, _trailFrameIndex - 1);
            _trailMaterial.SetFloat(TrailWidthId, _params.TrailWidth);
            _trailMaterial.SetFloat(TrailTailWidthId, _params.TrailTailWidth);
            ApplyPaletteRandom(_trailMaterial, color);
            LayerMaterialUtility.ApplyBlendMode(_trailMaterial, _params.BlendMode);

            Vector3 extent = Vector3.Scale(_params.EmitterSize?.Evaluate(context) ?? Vector3.one,
                new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 2f + Vector3.one * 8f;
            Vector3 emitterOffset = _params.EmitterOffset?.Evaluate(context) ?? Vector3.zero;
            var bounds = new Bounds(layerMatrix.MultiplyPoint3x4(emitterOffset), extent * 2f);
#pragma warning disable 0618
            Graphics.DrawMeshInstancedIndirect(_quad, 0, _trailMaterial, bounds, _trailArgs, 0, null,
                ShadowCastingMode.Off, false, gameObject.layer);
#pragma warning restore 0618
        }

        private static void ApplyPaletteRandom(Material material, in EvaluatedPaletteColor color)
        {
            int mode = color.RandomMode switch
            {
                PaletteColorMode.PaletteRandom => 1,
                PaletteColorMode.AccentRandom => 2,
                PaletteColorMode.SubAccentRandom => 3,
                _ => 0,
            };
            material.SetInt(PaletteRandomModeId, mode);
            material.SetInt(PaletteRandomSeedId, color.RandomSeed);
            if (mode == 0 || color.PaletteColors == null) return;
            for (int i = 0; i < PaletteColorIds.Length; i++)
                material.SetColor(PaletteColorIds[i], color.PaletteColors[i]);
        }

        protected override void ApplyCustomLayerState(bool visible, int order) => _renderEnabled = visible;

        private void EnsureVfxGraph()
        {
            _cameraStage ??= GetComponentInParent<CameraStage>();
            VisualEffectAsset asset = _cameraStage?.ResolveVfxGraph(_params.VfxGraphKey);
            if (asset == null)
            {
                if (_visualEffect != null)
                {
                    _visualEffect.enabled = false;
                    _visualEffect.visualEffectAsset = null;
                }
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

        private static int[] CreatePropertyIds(string prefix, int count)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++) ids[i] = Shader.PropertyToID(prefix + i);
            return ids;
        }

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
            ReleaseTrailResources();
            ReleaseBoidResources();
            _particles?.Release();
            _args?.Release();
            _particles = null;
            _args = null;
            _allocatedCapacity = 0;
        }

        private void ReleaseBoidResources()
        {
            _boidParticles?.Release();
            _boidParticles = null;
        }

        private void ReleaseTrailResources()
        {
            _trailSamples?.Release();
            _trailArgs?.Release();
            DestroyResource(_trailMaterial);
            _trailSamples = null;
            _trailArgs = null;
            _trailMaterial = null;
            _trailAllocatedCapacity = 0;
            _trailAllocatedLength = 0;
            _trailFrameIndex = 0;
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
