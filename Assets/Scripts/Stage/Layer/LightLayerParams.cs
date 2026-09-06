using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class LightLayerParams : StageLayerParams
    {
        public LightType LightType = LightType.Spot;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public PaletteColorParameter Color = new();
        public FloatParameter Intensity = new(3f);
        public FloatParameter Range = new(10f);
        public FloatParameter SpotAngle = new(35f);
        public FloatParameter InnerSpotAngle = new(25f);
        public bool CastShadows;

        public bool VolumetricEnabled = true;
        public FloatParameter VolumetricDensity = new(0.16f);
        public FloatParameter VolumetricIntensity = new(1f);
        public FloatParameter VolumetricNoise = new(0.15f);
        public int VolumetricSteps = 24;

        public void EnsureInitialized()
        {
            Opacity ??= new FloatParameter(1f);
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Color ??= new PaletteColorParameter();
            Intensity ??= new FloatParameter(3f);
            Range ??= new FloatParameter(10f);
            SpotAngle ??= new FloatParameter(35f);
            InnerSpotAngle ??= new FloatParameter(25f);
            VolumetricDensity ??= new FloatParameter(0.16f);
            VolumetricIntensity ??= new FloatParameter(1f);
            VolumetricNoise ??= new FloatParameter(0.15f);
        }
    }
}
