using System;
using UnityEngine;

namespace Aetherin
{
    public enum Primitive3DType
    {
        Cube,
        RoundedBox,
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

    public enum Primitive3DMaterialMode
    {
        Standard,
        Glass,
    }

    [Serializable]
    public class Primitive3DLayerParams : StageLayerParams
    {
        public Primitive3DType Primitive;
        public Primitive3DRenderMode RenderMode;
        public Primitive3DMaterialMode MaterialMode;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector3Parameter Size = new(Vector3.one);

        [Tooltip("Rounded Boxの角丸半径。Sizeの最短辺の半分までです")]
        public FloatParameter CornerRadius = new(0.15f);

        [Min(1)]
        public int CornerSegments = 6;

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

        [Tooltip("背景をずらして見せる疑似屈折の強さ")]
        public FloatParameter GlassRefraction = new(0.025f);
        public FloatParameter GlassTint = new(0.2f);
        public FloatParameter GlassFresnelPower = new(3f);
        public FloatParameter GlassFresnelIntensity = new(0.8f);
        public FloatParameter GlassChromaticAberration = new(0.002f);
        public FloatParameter GlassDistortion = new(0.003f);
        public FloatParameter GlassDistortionScale = new(12f);

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
