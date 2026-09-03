using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateModelLayerParamsElement(LabelElement label, IBinder binder)
        {
            var p = (ModelLayerParams)binder.GetObject();
            var keys = p.GetAvailableModelKeys?.Invoke();
            Element modelSelector = keys != null && keys.Count > 0
                ? UI.Dropdown("Model", () =>
                    {
                        int index = -1;
                        for (int i = 0; i < keys.Count; i++) if (keys[i] == p.ModelKey) { index = i; break; }
                        return index < 0 ? 0 : index;
                    }, value => p.ModelKey = keys[value], keys)
                : UI.Field("Model Key", () => p.ModelKey, value => p.ModelKey = value);
            return UI.Column(
                UI.Field("Visible", () => p.Visible, value => p.Visible = value),
                modelSelector,
                UI.Field("Render Mode", () => p.RenderMode, value => p.RenderMode = value),
                Param(UI.Label("Position"), p.Position),
                Param(UI.Label("Rotation"), p.Rotation),
                Param(UI.Label("Scale"), p.Scale),
                Param(UI.Label("Anchor"), p.Anchor),
                Param(UI.Label("Color"), p.Color),
                Param(UI.Label("Wire Color"), p.WireColor),
                UI.Field("Play Animation", () => p.PlayAnimation, value => p.PlayAnimation = value),
                Param(UI.Label("Animation Speed"), p.AnimationSpeed),
                Param(UI.Label("Opacity"), p.Opacity),
                UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                UI.Field("Order", () => p.Order, value => p.Order = value));
        }
    }
}
