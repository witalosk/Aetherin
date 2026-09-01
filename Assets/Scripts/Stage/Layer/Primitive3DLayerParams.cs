using System;
using UnityEngine;

namespace Aetherin
{
    public enum Primitive3DType
    {
        Cube,
        Sphere,
        Tetrahedron,
        Cylinder,
    }

    public enum Primitive3DColorMode
    {
        Solid,
        UvLerp,
        ShadedLerp,
        ToonTwoTone,
        PaletteRandom,
    }

    public enum Primitive3DRenderMode
    {
        Surface,
        Wireframe,
        SurfaceAndWireframe,
    }

    [Serializable]
    public class Primitive3DLayerParams : StageLayerParams
    {
        public Primitive3DType Primitive;
        public Primitive3DRenderMode RenderMode;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector3Parameter Size = new(Vector3.one);

        [Min(3)]
        public int RadialSegments = 32;

        [Min(2)]
        public int LatitudeSegments = 16;

        public Primitive3DColorMode ColorMode;
        public PaletteColorSource ColorA = PaletteColorSource.AccentColor1;
        public PaletteColorSource ColorB = PaletteColorSource.AccentColor2;
        public int PaletteRandomSeed;
        public FloatParameter ColorIntensity = new(1f);
        public FloatParameter Alpha = new(1f);

        public PaletteColorSource WireColor = PaletteColorSource.AccentColor1;
        [Tooltip("ワイヤーの太さ（プリミティブのローカル空間）")]
        public FloatParameter WireWidth = new(0.015f);
        public FloatParameter WireColorIntensity = new(1f);
        public FloatParameter WireAlpha = new(1f);

        [Tooltip("UVのU座標へ掛ける値")]
        public FloatParameter UvScale = new(1f);
        public FloatParameter UvOffset = new(0f);

        [Tooltip("Shadingで使う、面から光へ向かう方向")]
        public Vector3Parameter LightDirection = new(new Vector3(0.3f, 0.8f, -0.5f));

        [Range(0f, 1f)]
        public FloatParameter ToonThreshold = new(0.5f);

        public RepeaterParams Repeater = new();
    }

}
