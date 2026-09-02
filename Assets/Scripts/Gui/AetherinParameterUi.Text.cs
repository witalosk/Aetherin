using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateTextLayerParamsElement(
            LabelElement label,
            IBinder<TextLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");
            p.EnsureInitialized();

            var animatorListOption = new ListViewOption(
                    reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true)
                .OfType(p.Animators)
                .SetCreateItemInstanceFunc((_, index) => new TextAnimatorParams { Name = $"Animator {index + 1}" });

            return UI.Column(
                UI.Toggle("Visible", () => p.Visible, value => p.Visible = value),
                UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                Param("Opacity", p.Opacity),
                UI.Field("Order", () => p.Order, value => p.Order = value),
                UI.Field("Text", () => p.Text, value => p.Text = value),
                UI.Row(
                    UI.Field("Font", () => p.FontFamily, value => p.FontFamily = value).SetFlexGrow(1f),
                    UI.Field("Style", () => p.FontStyle, value => p.FontStyle = value).SetFlexGrow(1f)),
                Param("Font Size", p.FontSize),
                Param("Character Spacing", p.CharacterSpacing),
                Param("Word Spacing", p.WordSpacing),
                Param("Line Spacing", p.LineSpacing),
                UI.Field("Alignment", () => p.Alignment, value => p.Alignment = value),
                UI.Field("Layout", () => p.Layout, value => p.Layout = value),
                UI.DynamicElementIf(
                    () => p.Layout != TextLayoutMode.Linear,
                    () => UI.Column(
                        Param("Radius", p.PathRadius),
                        Param("Start Angle", p.PathStartAngle),
                        UI.DynamicElementIf(
                            () => p.Layout == TextLayoutMode.Arc,
                            () => Param("End Angle", p.PathEndAngle)),
                        UI.Toggle("Clockwise", () => p.PathClockwise, value => p.PathClockwise = value),
                        UI.Toggle("Orient To Path", () => p.OrientToPath, value => p.OrientToPath = value),
                        UI.DynamicElementIf(
                            () => p.OrientToPath,
                            () => Param("Rotation Offset", p.PathRotationOffset)))),
                Param("Position", p.Position),
                Param("Rotation", p.Rotation),
                Param("Scale", p.Scale),
                Param("Anchor", p.Anchor),
                Param("Color", p.Color),
                UI.List("Animators", () => p.Animators, value => p.Animators = value, animatorListOption));
        }

        private static Element CreateTextAnimatorElement(
            LabelElement label,
            IBinder<TextAnimatorParams> binder)
        {
            var animator = binder.Get();
            if (animator == null) return UI.Label("-");
            animator.EnsureInitialized();

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => animator.Enabled, value => animator.Enabled = value).SetWidth(20f),
                    UI.Field(null, () => animator.Name, value => animator.Name = value).SetFlexGrow(1f)),
                UI.Fold("Range Selector", UI.Field(null,
                    Binder.Create(animator.Selector, typeof(TextRangeSelectorParams)))),
                Param("Position", animator.Position),
                Param("Rotation", animator.Rotation),
                Param("Scale", animator.Scale),
                Param("Opacity", animator.Opacity),
                Param("Color", animator.Color),
                Param("Color Amount", animator.ColorAmount),
                Param("Animation Phase Offset", animator.AnimationPhaseOffset));
        }

        private static Element CreateTextRangeSelectorElement(
            LabelElement label,
            IBinder<TextRangeSelectorParams> binder)
        {
            var selector = binder.Get();
            if (selector == null) return UI.Label("-");

            return UI.Column(
                UI.Field("Based On", () => selector.BasedOn, value => selector.BasedOn = value),
                UI.Field("Shape", () => selector.Shape, value => selector.Shape = value),
                Param("Start", selector.Start),
                Param("End", selector.End),
                Param("Offset", selector.Offset),
                Param("Smoothness", selector.Smoothness),
                UI.Toggle("Randomize Order", () => selector.RandomizeOrder,
                    value => selector.RandomizeOrder = value),
                UI.DynamicElementIf(
                    () => selector.RandomizeOrder,
                    () => UI.Field("Random Seed", () => selector.RandomSeed,
                        value => selector.RandomSeed = value)));
        }
    }
}
