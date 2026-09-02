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
    }

    public enum ParticleModulationTarget
    {
        Position,
        Velocity,
        Size,
    }

    [Serializable]
    public sealed class ParticleSimulationModule
    {
        public bool Enabled = true;
        public ParticleSimulationModuleType Type;
        public FloatParameter Strength = new(1f);
        public Vector3Parameter Vector = new(new Vector3(0f, -1f, 0f));
        public FloatParameter Scale = new(1f);
        public FloatParameter Speed = new(1f);
        public FloatParameter Secondary = new(1f);
        public ParticleModulationTarget Target;
    }

    [Serializable]
    public sealed class GpuParticleLayerParams : StageLayerParams
    {
        public ParticleRenderBackend RenderBackend;
        [Tooltip("VfxGraph時にAssets/Resourcesから読み込むVisualEffectAssetの拡張子なし相対パス")]
        public string VfxGraphResourcePath;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);

        [Range(1, 262144)] public int Capacity = 16384;
        public int Seed = 1;
        public Vector3Parameter EmitterSize = new(new Vector3(8f, 5f, 2f));
        public FloatParameter Lifetime = new(6f);
        public FloatParameter InitialSpeed = new(0.4f);
        public FloatParameter SimulationSpeed = new(1f);
        public FloatParameter ParticleSize = new(0.035f);
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

        public void EnsureInitialized()
        {
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            EmitterSize ??= new Vector3Parameter(new Vector3(8f, 5f, 2f));
            Lifetime ??= new FloatParameter(6f);
            InitialSpeed ??= new FloatParameter(0.4f);
            SimulationSpeed ??= new FloatParameter(1f);
            ParticleSize ??= new FloatParameter(0.035f);
            Color ??= new PaletteColorParameter();
            Color.EnsureInitialized();
            Modules ??= new List<ParticleSimulationModule>();
            Capacity = Mathf.Clamp(Capacity, 1, 262144);
        }
    }
}
