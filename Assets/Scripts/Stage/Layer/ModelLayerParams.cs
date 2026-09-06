using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    public enum ModelLayerRenderMode
    {
        Surface,
        Wireframe,
        SurfaceAndWireframe,
    }

    public enum ModelLayerMaterialMode
    {
        Standard,
        Glass,
        Lit,
    }

    [Serializable]
    public sealed class ModelLayerParams : StageLayerParams
    {
        [Tooltip("CameraStageのModel Libraryに登録したキー")]
        public string ModelKey;
        public ModelLayerRenderMode RenderMode;
        public ModelLayerMaterialMode MaterialMode;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public PaletteColorParameter Color = new();
        public PaletteColorParameter WireColor = new();
        [Range(0f, 1f)] public FloatParameter Metallic = new(0f);
        [Range(0f, 1f)] public FloatParameter Smoothness = new(0.5f);
        public FloatParameter GlassRefraction = new(0.025f);
        public FloatParameter GlassTint = new(0.2f);
        public FloatParameter GlassFresnelPower = new(3f);
        public FloatParameter GlassFresnelIntensity = new(0.8f);
        public FloatParameter GlassChromaticAberration = new(0.002f);
        public FloatParameter GlassDistortion = new(0.003f);
        public FloatParameter GlassDistortionScale = new(12f);
        public FloatParameter AnimationSpeed = new(1f);
        public bool PlayAnimation = true;

        [NonSerialized] public Func<IReadOnlyList<string>> GetAvailableModelKeys;
    }
}
