using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum ParticleRenderBackend
    {
        IndirectQuad,
        VfxGraph,
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
    }

    public enum ParticleModulationTarget
    {
        Position,
        Velocity,
        Size,
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
