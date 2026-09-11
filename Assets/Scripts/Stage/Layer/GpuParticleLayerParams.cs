using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum ParticleRenderBackend
    {
        IndirectQuad,
        VfxGraph,
        Trail,
    }

    public enum ParticleRenderShape
    {
        Circle = 0,
        Triangle = 3,
        Square = 4,
        Pentagon = 5,
        Hexagon = 6,
        Octagon = 8,
    }

    public enum ParticleSimulationModuleType
    {
        Integrate,
        ApplyGravity,
        ApplyDrag,
        ApplyCurlNoise,
        ApplyAttractor,
        ApplyModulation,
        WrapBounds,
        ColorOverLife,
        SizeOverLife,
        ApplyLorenzAttractor,
        ApplyVortex,
        ClampVelocity,
        ApplyBoids,
    }

    public enum ParticleModulationTarget
    {
        Position,
        Velocity,
        Size,
    }

    public enum BoidsNeighborSearchMode
    {
        Sampled,
        Exhaustive,
    }

    [Serializable]
    public sealed class ParticleRandomRangeParameter
    {
        [Tooltip("X=Min, Y=Max, Z=乱数分布のPower。Powerが大きいほどMin寄りになります")]
        public Vector3 MinMaxPower = new(1f, 1f, 1f);

        [Tooltip("Min/Maxへ一律に掛けるModulation")]
        public FloatParameter Modulation = new(1f);

        public ParticleRandomRangeParameter()
        {
        }

        public ParticleRandomRangeParameter(float value)
        {
            MinMaxPower = new Vector3(value, value, 1f);
        }

        public void EnsureInitialized(float fallback)
        {
            if (MinMaxPower == Vector3.zero) MinMaxPower = new Vector3(fallback, fallback, 1f);
            Modulation ??= new FloatParameter(1f);
        }

        public Vector3 Evaluate(in ModulationContext context)
        {
            Vector3 range = MinMaxPower;
            float modulation = Modulation?.Evaluate(context) ?? 1f;
            float min = range.x * modulation;
            float max = range.y * modulation;
            return new Vector3(Mathf.Min(min, max), Mathf.Max(min, max), Mathf.Max(0.0001f, range.z));
        }
    }

    [Serializable]
    public sealed class ParticleSimulationModule
    {
        public bool Enabled = true;
        public ParticleSimulationModuleType Type;
        public FloatParameter Strength = new(1f);
        public Vector3Parameter Vector = new(new Vector3(0f, -1f, 0f));
        public Vector3Parameter Axis = new(Vector3.up);
        public FloatParameter Scale = new(1f);
        public FloatParameter Speed = new(1f);
        public FloatParameter Secondary = new(1f);
        [Tooltip("Boids用。X=Separation、Y=Alignment、Z=Cohesion の近傍半径")]
        public Vector3Parameter BoidsNeighborRadii = new(new Vector3(2f, 2f, 2f));
        [Tooltip("Boids用。X=Separation、Y=Alignment、Z=Cohesion の重み")]
        public Vector3Parameter BoidsWeights = new(new Vector3(1.5f, 0.75f, 0.5f));
        [Tooltip("Boids用。X=Separation、Y=Alignment、Z=Cohesion の最大ステアリング力")]
        public Vector3Parameter BoidsMaxForces = new(new Vector3(2f, 1f, 1f));
        public FloatParameter BoidsMaxSpeed = new(2f);
        public FloatParameter BoidsMaxAcceleration = new(4f);
        [Tooltip("Sampled は軽量な近傍サンプル、Exhaustive は全粒子を探索します")]
        public BoidsNeighborSearchMode BoidsNeighborSearch = BoidsNeighborSearchMode.Sampled;
        [Range(1, 32)] public int BoidsNeighborSamples = 12;
        [Tooltip("Boids用。進行方向を中心とする近傍の視野角（度、360で全方向）")]
        public FloatParameter BoidsFieldOfView = new(360f);
        [Tooltip("有効時はSeparationにも視野角を適用します。無効時は衝突回避のため全方位を参照します")]
        public bool BoidsSeparationUsesFieldOfView;
        [Tooltip("OverLifeモジュールが寿命比率(0..1)から値をサンプリングするカーブ")]
        public AnimationCurve OverLifeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        public ParticleModulationTarget Target;

        public void EnsureInitialized()
        {
            Strength ??= new FloatParameter(1f);
            Vector ??= new Vector3Parameter(new Vector3(0f, -1f, 0f));
            Axis ??= new Vector3Parameter(Vector3.up);
            Scale ??= new FloatParameter(1f);
            Speed ??= new FloatParameter(1f);
            Secondary ??= new FloatParameter(1f);
            BoidsNeighborRadii ??= new Vector3Parameter(new Vector3(2f, 2f, 2f));
            BoidsWeights ??= new Vector3Parameter(new Vector3(1.5f, 0.75f, 0.5f));
            BoidsMaxForces ??= new Vector3Parameter(new Vector3(2f, 1f, 1f));
            BoidsMaxSpeed ??= new FloatParameter(2f);
            BoidsMaxAcceleration ??= new FloatParameter(4f);
            BoidsFieldOfView ??= new FloatParameter(360f);
            BoidsNeighborSamples = Mathf.Clamp(BoidsNeighborSamples, 1, 32);
            OverLifeCurve ??= CreateDefaultOverLifeCurve(Type);
        }

        public static AnimationCurve CreateDefaultOverLifeCurve(ParticleSimulationModuleType type)
        {
            return type == ParticleSimulationModuleType.SizeOverLife
                ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f))
                : AnimationCurve.Linear(0f, 1f, 1f, 0f);
        }
    }

    [Serializable]
    public sealed class GpuParticleLayerParams : StageLayerParams
    {
        public ParticleRenderBackend RenderBackend;
        [Tooltip("CameraStageのVFX Graph Libraryに登録したキー")]
        public string VfxGraphKey;
        [Range(2, 64)] public int TrailLength = 12;
        [Min(0f)] public float TrailWidth = 1f;
        [Range(0f, 1f)] public float TrailTailWidth = 0.1f;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);

        [Range(1, 262144)] public int Capacity = 16384;
        public int Seed = 1;
        public Vector3Parameter EmitterOffset = new();
        public Vector3Parameter EmitterSize = new(new Vector3(8f, 5f, 2f));
        public ParticleRandomRangeParameter Lifetime = new(6f);
        public ParticleRandomRangeParameter InitialSpeed = new(0.4f);
        public FloatParameter SimulationSpeed = new(1f);
        public ParticleRandomRangeParameter ParticleSize = new(0.035f);
        public Vector3Parameter InitialRotation = new();
        public Vector3Parameter RotationRandom = new(new Vector3(360f, 360f, 360f));
        public Vector3Parameter AngularVelocity = new();
        public Vector3Parameter AngularVelocityRandom = new();
        public ParticleRenderShape Shape = ParticleRenderShape.Circle;
        public PaletteColorParameter Color = new();

        public List<ParticleSimulationModule> Modules = new()
        {
            new ParticleSimulationModule
            {
                Type = ParticleSimulationModuleType.ApplyCurlNoise,
                Strength = new FloatParameter(0.8f),
                Scale = new FloatParameter(0.35f),
                Speed = new FloatParameter(0.25f),
            },
            new ParticleSimulationModule { Type = ParticleSimulationModuleType.Integrate },
            new ParticleSimulationModule
            {
                Type = ParticleSimulationModuleType.WrapBounds,
                Vector = new Vector3Parameter(new Vector3(8f, 5f, 2f)),
            },
            new ParticleSimulationModule
            {
                Type = ParticleSimulationModuleType.ColorOverLife,
                Strength = new FloatParameter(1f),
            },
        };

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableVfxGraphKeys;

        public void EnsureInitialized()
        {
            TrailLength = Mathf.Clamp(TrailLength, 2, 64);
            TrailWidth = Mathf.Max(0f, TrailWidth);
            TrailTailWidth = Mathf.Clamp01(TrailTailWidth);
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            EmitterOffset ??= new Vector3Parameter();
            EmitterSize ??= new Vector3Parameter(new Vector3(8f, 5f, 2f));
            Lifetime ??= new ParticleRandomRangeParameter(6f);
            Lifetime.EnsureInitialized(6f);
            InitialSpeed ??= new ParticleRandomRangeParameter(0.4f);
            InitialSpeed.EnsureInitialized(0.4f);
            SimulationSpeed ??= new FloatParameter(1f);
            ParticleSize ??= new ParticleRandomRangeParameter(0.035f);
            ParticleSize.EnsureInitialized(0.035f);
            InitialRotation ??= new Vector3Parameter();
            RotationRandom ??= new Vector3Parameter(new Vector3(360f, 360f, 360f));
            AngularVelocity ??= new Vector3Parameter();
            AngularVelocityRandom ??= new Vector3Parameter();
            Color ??= new PaletteColorParameter();
            Color.EnsureInitialized();
            Modules ??= new List<ParticleSimulationModule>();
            foreach (var module in Modules) module?.EnsureInitialized();
            Capacity = Mathf.Clamp(Capacity, 1, 262144);
        }
    }
}
