using System;
using UnityEngine;

namespace Aetherin
{
    public enum ShapePrimitive
    {
        Rectangle,
        Ellipse,
        Polygon,
        Star,
    }

    [Serializable]
    public class StrokeTrimParams
    {
        public bool Enabled;
        public FloatParameter Start = new(0f);
        public FloatParameter End = new(1f);

        [Tooltip("周長に対するオフセット。1で一周します")]
        public FloatParameter Offset = new(0f);
    }

    [Serializable]
    public class ShapeLayerParams : StageLayerParams
    {
        public ShapePrimitive Shape = ShapePrimitive.Rectangle;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));

        public IntParameter Points = new(5);

        public FloatParameter InnerRadius = new(0.5f);

        [Min(3)]
        public int EllipseSegments = 64;

        public PaletteColorParameter FillColor = new();

        public bool FillEnabled = true;
        public bool StrokeEnabled;

        [Min(0f)]
        public FloatParameter StrokeWidth = new(0.05f);

        public PaletteColorParameter StrokeColor = new() { Color = PaletteColorSource.AccentColor2 };
        public StrokeTrimParams StrokeTrim = new();

        public RepeaterParams Repeater = new();
    }

}
