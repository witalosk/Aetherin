using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateGroupLayerParamsElement(LabelElement label, IBinder binder)
        {
            var p = (GroupLayerParams)binder.GetObject();
            return UI.Column(
                Param(UI.Label("Position"), p.Position),
                Param(UI.Label("Rotation"), p.Rotation),
                Param(UI.Label("Scale"), p.Scale),
                Param(UI.Label("Anchor"), p.Anchor),
                Param(UI.Label("Opacity"), p.Opacity));
        }
    }
}
