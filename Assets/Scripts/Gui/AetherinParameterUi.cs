using System;
using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    /// <summary>
    /// パラメータ系の型をFoldに頼らず1行で表示するRosettaUIのカスタム表示
    /// 値は常に手前に置き、Modulationや詳細設定はランチャーから別ウィンドウで開く
    /// </summary>
    public static class AetherinParameterUi
    {
        private const float LauncherWidth = 54f;
        private const float DetailWindowWidth = 420f;

        public static void Register()
        {
            UICustom.RegisterElementCreationFunc<FloatParameter>(CreateFloatParameterElement);
            UICustom.RegisterElementCreationFunc<IntParameter>(CreateIntParameterElement);
            UICustom.RegisterElementCreationFunc<Vector2Parameter>(CreateVector2ParameterElement);
            UICustom.RegisterElementCreationFunc<Vector3Parameter>(CreateVector3ParameterElement);
            UICustom.RegisterElementCreationFunc<FloatModulationStack>(CreateModulationStackElement);
            UICustom.RegisterElementCreationFunc<FloatModulator>(CreateModulatorElement);
            UICustom.RegisterElementCreationFunc<PaletteColorParameter>(CreatePaletteColorElement);
            UICustom.RegisterElementCreationFunc<StrokeTrimParams>(CreateStrokeTrimElement);
            UICustom.RegisterElementCreationFunc<RepeaterParams>(CreateRepeaterElement);
            UICustom.RegisterElementCreationFunc<ShapeLayerParams>(CreateShapeLayerParamsElement);
        }

        #region AnimatedParameter

        private static Element CreateFloatParameterElement(LabelElement label, IBinder<FloatParameter> binder)
        {
            var parameter = binder.Get();
            if (parameter == null) return UI.Row(label, UI.Label("-"));

            return UI.Row(
                UI.Field(label, () => parameter.BaseValue, value => parameter.BaseValue = value).SetFlexGrow(1f),
                CreateModulationLauncher(LabelText(label), parameter.Modulation)
            );
        }

        private static Element CreateIntParameterElement(LabelElement label, IBinder<IntParameter> binder)
        {
            var parameter = binder.Get();
            if (parameter == null) return UI.Row(label, UI.Label("-"));

            return UI.Row(
                UI.Field(label, () => parameter.BaseValue, value => parameter.BaseValue = value).SetFlexGrow(1f),
                CreateModulationLauncher(LabelText(label), parameter.Modulation)
            );
        }

        private static Element CreateVector2ParameterElement(LabelElement label, IBinder<Vector2Parameter> binder)
        {
            var parameter = binder.Get();
            if (parameter == null) return UI.Row(label, UI.Label("-"));

            return UI.Row(
                UI.Field(label, () => parameter.BaseValue, value => parameter.BaseValue = value).SetFlexGrow(1f),
                CreateAxisModulationLauncher(LabelText(label),
                    () => parameter.ApplyXModulationToBothAxes,
                    value => parameter.ApplyXModulationToBothAxes = value,
                    new[] { ("X", parameter.XModulation), ("Y", parameter.YModulation) })
            );
        }

        private static Element CreateVector3ParameterElement(LabelElement label, IBinder<Vector3Parameter> binder)
        {
            var parameter = binder.Get();
            if (parameter == null) return UI.Row(label, UI.Label("-"));

            return UI.Row(
                UI.Field(label, () => parameter.BaseValue, value => parameter.BaseValue = value).SetFlexGrow(1f),
                CreateAxisModulationLauncher(LabelText(label),
                    () => parameter.ApplyXModulationToAllAxes,
                    value => parameter.ApplyXModulationToAllAxes = value,
                    new[]
                    {
                        ("X", parameter.XModulation),
                        ("Y", parameter.YModulation),
                        ("Z", parameter.ZModulation),
                    })
            );
        }

        #endregion

        #region Modulation

        /// <summary>
        /// Modulationの有無と数だけを1つのボタンに集約し、中身は別ウィンドウで編集する
        /// </summary>
        private static Element CreateModulationLauncher(string name, FloatModulationStack stack)
        {
            if (stack == null) return UI.Space().SetWidth(LauncherWidth);

            return UI.WindowLauncher(
                    UI.Label(() => DescribeStack(stack)),
                    UI.Window($"{name} Modulation", CreateModulationStackBody(stack))
                        .SetWidth(DetailWindowWidth))
                .SetWidth(LauncherWidth);
        }

        private static Element CreateAxisModulationLauncher(
            string name,
            Func<bool> readShared,
            Action<bool> writeShared,
            IReadOnlyList<(string Axis, FloatModulationStack Stack)> axes)
        {
            return UI.WindowLauncher(
                    UI.Label(() => DescribeAxes(readShared(), axes)),
                    UI.Window($"{name} Modulation",
                        UI.Column(
                            UI.Toggle("X を全軸へ適用", readShared, writeShared),
                            UI.DynamicElementOnStatusChanged(
                                readStatus: readShared,
                                build: shared => UI.Column(CreateAxisStackElements(shared, axes)))
                        )).SetWidth(DetailWindowWidth))
                .SetWidth(LauncherWidth);
        }

        private static IEnumerable<Element> CreateAxisStackElements(
            bool shared,
            IReadOnlyList<(string Axis, FloatModulationStack Stack)> axes)
        {
            for (int i = 0; i < axes.Count; i++)
            {
                if (shared && i > 0) yield break;

                var (axis, stack) = axes[i];
                yield return UI.Column(
                    UI.Label(shared ? "All Axes" : axis),
                    CreateModulationStackBody(stack)
                );
            }
        }

        private static Element CreateModulationStackElement(LabelElement label, IBinder<FloatModulationStack> binder)
        {
            var stack = binder.Get();
            return stack == null ? UI.Label("-") : CreateModulationStackBody(stack);
        }

        private static Element CreateModulationStackBody(FloatModulationStack stack)
        {
            if (stack == null) return UI.Label("-");

            return UI.List(null,
                () => stack.Modulators,
                value => stack.Modulators = value,
                new ListViewOption(reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true));
        }

        /// <summary>
        /// Modulator1つを、ソースに関係する項目だけの数行で表示する
        /// </summary>
        private static Element CreateModulatorElement(LabelElement label, IBinder<FloatModulator> binder)
        {
            var modulator = binder.Get();
            if (modulator == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => modulator.Enabled, value => modulator.Enabled = value).SetWidth(20f),
                    UI.Field(null, () => modulator.Source, value => modulator.Source = value).SetFlexGrow(1f),
                    UI.Field(null, () => modulator.Operation, value => modulator.Operation = value).SetFlexGrow(1f)
                ),
                UI.Row(
                    UI.Field("Amount", () => modulator.Amount, value => modulator.Amount = value).SetFlexGrow(1f),
                    UI.Field("Offset", () => modulator.Offset, value => modulator.Offset = value).SetFlexGrow(1f)
                ),
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => modulator.Source,
                    build: source => CreateModulatorSourceElement(modulator, source))
            );
        }

        private static Element CreateModulatorSourceElement(FloatModulator modulator, FloatModulationSource source)
        {
            switch (source)
            {
                case FloatModulationSource.Lfo:
                    return UI.Column(
                        UI.Row(
                            UI.Field(null, () => modulator.LfoWaveform, value => modulator.LfoWaveform = value)
                                .SetFlexGrow(1f),
                            UI.Toggle("Unipolar", () => modulator.LfoUnipolar, value => modulator.LfoUnipolar = value)
                        ),
                        UI.Row(
                            UI.Field("Freq", () => modulator.LfoFrequency, value => modulator.LfoFrequency = value)
                                .SetFlexGrow(1f),
                            UI.Slider("Phase", () => modulator.LfoPhase, value => modulator.LfoPhase = value, 0f, 1f)
                                .SetFlexGrow(1f)
                        ));

                case FloatModulationSource.Beat:
                case FloatModulationSource.Bar:
                    return UI.Field("Sharpness",
                        () => modulator.BeatPulseSharpness,
                        value => modulator.BeatPulseSharpness = value);

                case FloatModulationSource.MidiCc:
                    return UI.Field("Fader", Binder.Create(modulator.Midi, typeof(MidiCcBinding)));

                default:
                    return null;
            }
        }

        private static string DescribeStack(FloatModulationStack stack)
        {
            int count = stack?.Modulators?.Count ?? 0;
            return count == 0 ? "fx" : $"fx {count}";
        }

        private static string DescribeAxes(
            bool shared,
            IReadOnlyList<(string Axis, FloatModulationStack Stack)> axes)
        {
            int count = 0;
            for (int i = 0; i < axes.Count; i++)
            {
                count += axes[i].Stack?.Modulators?.Count ?? 0;
                if (shared) break;
            }

            return count == 0 ? "fx" : $"fx {count}";
        }

        #endregion

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
                        : UI.Row(
                            UI.Field(null, () => parameter.GradientColorA,
                                value => parameter.GradientColorA = value).SetFlexGrow(1f),
                            UI.Field(null, () => parameter.GradientColorB,
                                value => parameter.GradientColorB = value).SetFlexGrow(1f)
                        )),
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
                    Param("Position", repeater.Position),
                    Param("Rotation", repeater.Rotation),
                    Param("Scale", repeater.Scale),
                    Param("Anchor", repeater.Anchor),
                    Param("Opacity Start", repeater.StartOpacity),
                    Param("Opacity End", repeater.EndOpacity)
                ))
            );
        }

        #endregion

        private static string LabelText(LabelElement label) => label?.Value ?? "Parameter";

        /// <summary>
        /// ラベルをUI.Fieldの第1引数へ渡し、登録済みのカスタム表示を使わせる
        /// </summary>
        private static Element Param(LabelElement label, object parameter) =>
            parameter == null ? null : UI.Field(label, Binder.Create(parameter, parameter.GetType()));
    }
}
