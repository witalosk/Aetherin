using System;
using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
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
                    UI.Window($"{name} Modulation", UI.Lazy(() => CreateModulationStackBody(stack)))
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
                        UI.Lazy(() => UI.Column(
                            UI.Toggle("X を全軸へ適用", readShared, writeShared),
                            UI.DynamicElementOnStatusChanged(
                                readStatus: readShared,
                                build: shared => UI.Column(CreateAxisStackElements(shared, axes)))
                        ))).SetWidth(DetailWindowWidth))
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
                UI.DynamicElementOnStatusChanged(
                    readStatus: () => modulator.Source,
                    build: source => UI.Column(
                        UI.Row(
                            UI.Field(IsAccumulator(source) ? "Step" : "Amount",
                                () => modulator.Amount, value => modulator.Amount = value).SetFlexGrow(1f),
                            UI.Field("Offset", () => modulator.Offset,
                                value => modulator.Offset = value).SetFlexGrow(1f)
                        ),
                        CreateModulatorSourceElement(modulator, source)))
            );
        }

        private static bool IsAccumulator(FloatModulationSource source) =>
            source == FloatModulationSource.BeatAccumulator ||
            source == FloatModulationSource.BarAccumulator;

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

                case FloatModulationSource.BeatAccumulator:
                case FloatModulationSource.BarAccumulator:
                    return UI.Column(
                        UI.Field("Initial", () => modulator.AccumulatorInitialValue,
                            value => modulator.AccumulatorInitialValue = value),
                        UI.Field("Reset", () => modulator.AccumulatorReset,
                            value => modulator.AccumulatorReset = value),
                        UI.DynamicElementIf(
                            () => modulator.AccumulatorReset == AccumulatorResetMode.AfterNEvents,
                            () => UI.Field("Reset After", () => modulator.AccumulatorResetAfterEvents,
                                value => modulator.AccumulatorResetAfterEvents = Math.Max(1, value))),
                        UI.Field("Limit", () => modulator.AccumulatorLimit,
                            value => modulator.AccumulatorLimit = value),
                        UI.DynamicElementIf(
                            () => modulator.AccumulatorLimit != AccumulatorLimitMode.None,
                            () => UI.Row(
                                UI.Field("Min", () => modulator.AccumulatorMin,
                                    value => modulator.AccumulatorMin = value).SetFlexGrow(1f),
                                UI.Field("Max", () => modulator.AccumulatorMax,
                                    value => modulator.AccumulatorMax = value).SetFlexGrow(1f)
                            )),
                        UI.Field("Transition", () => modulator.AccumulatorTransition,
                            value => modulator.AccumulatorTransition = value),
                        UI.DynamicElementIf(
                            () => modulator.AccumulatorTransition != AccumulatorTransitionMode.Instant,
                            () => UI.Field("Duration", () => modulator.AccumulatorTransitionDuration,
                                value => modulator.AccumulatorTransitionDuration = Math.Max(0.001f, value))),
                        UI.DynamicElementIf(
                            () => modulator.AccumulatorTransition == AccumulatorTransitionMode.EaseOut,
                            () => UI.Slider("Sharpness", () => modulator.AccumulatorTransitionSharpness,
                                value => modulator.AccumulatorTransitionSharpness = value, 1f, 8f)));

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
    }
}
