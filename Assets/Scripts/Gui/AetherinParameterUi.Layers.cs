using System;
using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        #region PaletteColor

        private static Element CreatePaletteColorElement(LabelElement label, IBinder<PaletteColorParameter> binder)
        {
            var parameter = binder.Get();
            if (parameter == null) return UI.Row(label, UI.Label("-"));

            string name = LabelText(label);

            return UI.Row(
                UI.Field(label, () => parameter.Mode, value => parameter.Mode = value).SetFlexGrow(1f),
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => parameter.Mode,
                    build: mode => mode == PaletteColorMode.Single
                        ? UI.Field(null, () => parameter.Color, value => parameter.Color = value).SetFlexGrow(1f)
                        : mode == PaletteColorMode.Gradient ? UI.Row(
                            UI.Field(null, () => parameter.GradientColorA,
                                value => parameter.GradientColorA = value).SetFlexGrow(1f),
                            UI.Field(null, () => parameter.GradientColorB,
                                value => parameter.GradientColorB = value).SetFlexGrow(1f)
                        ) : UI.Field("Seed", () => parameter.RandomSeed,
                            value => parameter.RandomSeed = value).SetFlexGrow(1f)),
                UI.WindowLauncher("...",
                        UI.Window($"{name} Color",
                            UI.Column(
                                Param("Intensity", parameter.Intensity),
                                Param("Alpha", parameter.Alpha),
                                UI.DynamicElementIf(() => parameter.Mode == PaletteColorMode.Gradient,
                                    () => UI.Column(
                                        Param("Angle", parameter.GradientAngle),
                                        Param("Offset", parameter.GradientOffset),
                                        Param("Scale", parameter.GradientScale)
                                    ))
                            )).SetWidth(DetailWindowWidth))
                    .SetWidth(32f)
            );
        }

        #endregion

        #region ShapeLayer

        /// <summary>
        /// 現在の設定で効かない項目は表示しない
        /// (Shapeに関係ないパラメータ、Fill / Stroke無効時の色や幅など)
        /// </summary>
        private static Element CreateShapeLayerParamsElement(LabelElement label, IBinder<ShapeLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");

            return UI.Column(
                Param("Opacity", p.Opacity),
                UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                UI.Field("Shape", () => p.Shape, value => p.Shape = value),
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => p.Shape,
                    build: shape => CreateShapeSpecificElement(p, shape)),
                Param("Size", p.Size),
                Param("Position", p.Position),
                Param("Rotation", p.Rotation),
                Param("Scale", p.Scale),
                Param("Anchor", p.Anchor),
                UI.Toggle("Fill", () => p.FillEnabled, value => p.FillEnabled = value),
                UI.DynamicElementIf(() => p.FillEnabled, () => Param("Fill Color", p.FillColor)),
                UI.Toggle("Stroke", () => p.StrokeEnabled, value => p.StrokeEnabled = value),
                UI.DynamicElementIf(() => p.StrokeEnabled, () => UI.Column(
                    Param("Stroke Width", p.StrokeWidth),
                    Param("Stroke Color", p.StrokeColor),
                    Param("Stroke Trim", p.StrokeTrim)
                )),
                Param("Repeater", p.Repeater)
            );
        }

        private static Element CreateShapeSpecificElement(ShapeLayerParams p, ShapePrimitive shape)
        {
            return shape switch
            {
                ShapePrimitive.Ellipse => UI.Field("Segments",
                    () => p.EllipseSegments, value => p.EllipseSegments = value),
                ShapePrimitive.Polygon => Param("Points", p.Points),
                ShapePrimitive.Star => UI.Column(
                    Param("Points", p.Points),
                    Param("Inner Radius", p.InnerRadius)),
                _ => null,
            };
        }

        /// <summary>
        /// Enabledのときだけ中身を並べる (Foldを増やさずに済ませる)
        /// </summary>
        private static Element CreateStrokeTrimElement(LabelElement label, IBinder<StrokeTrimParams> binder)
        {
            var trim = binder.Get();
            if (trim == null) return UI.Label("-");

            return UI.Column(
                UI.Toggle(label ?? (LabelElement)"Stroke Trim", () => trim.Enabled, value => trim.Enabled = value),
                UI.DynamicElementIf(() => trim.Enabled, () => UI.Column(
                    Param("Trim Start", trim.Start),
                    Param("Trim End", trim.End),
                    Param("Trim Offset", trim.Offset)
                ))
            );
        }

        private static Element CreateRepeaterElement(LabelElement label, IBinder<RepeaterParams> binder)
        {
            var repeater = binder.Get();
            if (repeater == null) return UI.Label("-");

            return UI.Column(
                UI.Toggle(label ?? (LabelElement)"Repeater", () => repeater.Enabled, value => repeater.Enabled = value),
                UI.DynamicElementIf(() => repeater.Enabled, () => UI.Column(
                     Param("Copies", repeater.Copies),
                     UI.Field("Layout", () => repeater.LayoutMode, value => repeater.LayoutMode = value),
                     UI.DynamicElementIf(
                         () => repeater.LayoutMode != RepeaterLayoutMode.Linear,
                         () => UI.Column(
                             Param("Columns", repeater.Columns),
                             UI.DynamicElementIf(
                                 () => repeater.LayoutMode == RepeaterLayoutMode.GridXYZ,
                                 () => Param("Rows", repeater.Rows)))),
                     Param("Position", repeater.Position),
                    Param("Rotation", repeater.Rotation),
                    Param("Scale", repeater.Scale),
                    Param("Anchor", repeater.Anchor),
                    UI.Field("Transform Mode", () => repeater.TransformMode,
                        value => repeater.TransformMode = value),
                     UI.DynamicElementIf(
                         () => repeater.LayoutMode == RepeaterLayoutMode.Linear &&
                               repeater.TransformMode == RepeaterTransformMode.Cumulative,
                        () => UI.Toggle("Rotation Affects Position", () => repeater.RotationAffectsPosition,
                            value => repeater.RotationAffectsPosition = value)),
                    Param("Animation Phase Offset", repeater.AnimationPhaseOffset),
                    Param("Opacity Start", repeater.StartOpacity),
                    Param("Opacity End", repeater.EndOpacity)
                ))
            );
        }

        #endregion

        #region RuntimeShaderLayer

        private static Element CreateRuntimeShaderLayerParamsElement(
            LabelElement label,
            IBinder<RuntimeShaderLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");
            p.EnsureInitialized();

            return UI.Column(
                UI.Field("Shader", () => p.Shader, value =>
                {
                    p.Shader = value;
                    if (value != null) p.ShaderName = value.name;
                }),
                UI.Field("Shader Name", () => p.ShaderName, value =>
                {
                    p.ShaderName = value;
                    p.Shader = null;
                }),
                Param("Opacity", p.Opacity),
                UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                Param("Size", p.Size),
                Param("Position", p.Position),
                Param("Rotation", p.Rotation),
                Param("Scale", p.Scale),
                Param("Anchor", p.Anchor),
                UI.Fold("User Parameters", UI.Column(
                    Param("Float 0", p.UserFloat0),
                    Param("Float 1", p.UserFloat1),
                    Param("Float 2", p.UserFloat2),
                    Param("Float 3", p.UserFloat3),
                    Param("Vector 0", p.UserVector0),
                    Param("Vector 1", p.UserVector1),
                    Param("Vector 2", p.UserVector2),
                    Param("Vector 3", p.UserVector3))));
        }

        #endregion

        #region Primitive3DLayer

        private static Element CreatePrimitive3DLayerParamsElement(
            LabelElement label,
            IBinder<Primitive3DLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");

            return UI.Column(
                Param("Opacity", p.Opacity),
                UI.DynamicElementIf(
                    () => p.MaterialMode != Primitive3DMaterialMode.Glass,
                    () => UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value)),
                UI.Field("Primitive", () => p.Primitive, value => p.Primitive = value),
                UI.Field("Render Mode", () => p.RenderMode, value => p.RenderMode = value),
                UI.DynamicElementIf(
                    () => p.Primitive is Primitive3DType.Sphere or Primitive3DType.Cylinder,
                    () => UI.Field("Radial Segments", () => p.RadialSegments, value => p.RadialSegments = value)),
                UI.DynamicElementIf(
                    () => p.Primitive == Primitive3DType.Sphere,
                    () => UI.Field("Latitude Segments", () => p.LatitudeSegments, value => p.LatitudeSegments = value)),
                Param("Size", p.Size),
                UI.DynamicElementIf(
                    () => p.Primitive == Primitive3DType.RoundedBox,
                    () => UI.Column(
                        Param("Corner Radius", p.CornerRadius),
                        UI.Field("Corner Segments", () => p.CornerSegments,
                            value => p.CornerSegments = value))),
                Param("Position", p.Position),
                Param("Rotation", p.Rotation),
                Param("Scale", p.Scale),
                Param("Anchor", p.Anchor),
                UI.Field("Material", () => p.MaterialMode, value => p.MaterialMode = value),
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => p.MaterialMode,
                    build: mode => mode == Primitive3DMaterialMode.Glass
                        ? CreateGlassMaterialElement(p)
                        : CreateStandardMaterialElement(p)),
                UI.DynamicElementIf(
                    () => p.RenderMode != Primitive3DRenderMode.Surface,
                    () => UI.Column(
                        UI.Field("Wire Color", () => p.WireColor, value => p.WireColor = value),
                        Param("Wire Width", p.WireWidth),
                        Param("Wire Intensity", p.WireColorIntensity),
                        Param("Wire Alpha", p.WireAlpha))),
                Param("Repeater", p.Repeater)
            );
        }

        private static Element CreateStandardMaterialElement(Primitive3DLayerParams p) => UI.Column(
            UI.Field("Color Mode", () => p.ColorMode, value => p.ColorMode = value),
            UI.Field("Color A", () => p.ColorA, value => p.ColorA = value),
            UI.DynamicElementIf(
                () => p.ColorMode == Primitive3DColorMode.PaletteRandom,
                () => UI.Field("Random Seed", () => p.PaletteRandomSeed, value => p.PaletteRandomSeed = value)),
            UI.DynamicElementIf(
                () => p.ColorMode != Primitive3DColorMode.Solid &&
                      p.ColorMode != Primitive3DColorMode.PaletteRandom,
                () => UI.Field("Color B", () => p.ColorB, value => p.ColorB = value)),
            Param("Color Intensity", p.ColorIntensity),
            Param("Alpha", p.Alpha),
            UI.DynamicElementIf(
                () => p.ColorMode == Primitive3DColorMode.UvLerp,
                () => UI.Column(Param("UV Scale", p.UvScale), Param("UV Offset", p.UvOffset))),
            UI.DynamicElementIf(
                () => p.ColorMode is Primitive3DColorMode.ShadedLerp or Primitive3DColorMode.ToonTwoTone,
                () => Param("Light Direction", p.LightDirection)),
            UI.DynamicElementIf(
                () => p.ColorMode == Primitive3DColorMode.ToonTwoTone,
                () => Param("Toon Threshold", p.ToonThreshold)));

        private static Element CreateGlassMaterialElement(Primitive3DLayerParams p) => UI.Column(
            UI.Field("Tint Color", () => p.ColorA, value => p.ColorA = value),
            UI.Field("Edge Color", () => p.ColorB, value => p.ColorB = value),
            Param("Color Intensity", p.ColorIntensity),
            Param("Alpha", p.Alpha),
            Param("Refraction", p.GlassRefraction),
            Param("Tint", p.GlassTint),
            Param("Fresnel Power", p.GlassFresnelPower),
            Param("Fresnel Intensity", p.GlassFresnelIntensity),
            Param("Chromatic Aberration", p.GlassChromaticAberration),
            Param("Distortion", p.GlassDistortion),
            Param("Distortion Scale", p.GlassDistortionScale));

        #endregion
    }
}
