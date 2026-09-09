using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateSpriteSheetLayerParamsElement(LabelElement label, IBinder<SpriteSheetLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");
            p.EnsureInitialized();
            var keys = p.GetAvailableSpriteSheetKeys?.Invoke();
            Element selector = keys != null && keys.Count > 0
                ? UI.Dropdown("Sprite Sheet", () =>
                    {
                        for (int i = 0; i < keys.Count; i++) if (keys[i] == p.SpriteSheetKey) return i;
                        return 0;
                    }, value => p.SpriteSheetKey = keys[value], keys)
                : UI.Field("Sprite Sheet Key", () => p.SpriteSheetKey, value => p.SpriteSheetKey = value);

            return UI.Column(UI.Tabs(
                ("Sprite", UI.Column(selector,
                    UI.Field("Frames", () => p.FrameCount, value => p.FrameCount = System.Math.Max(1, value)),
                    UI.Field("Rows", () => p.RowCount, value => p.RowCount = System.Math.Max(1, value)),
                    Param("Animation Row", p.AnimationRow),
                    UI.Toggle("Play Animation", () => p.PlayAnimation, value => p.PlayAnimation = value),
                    UI.DynamicElementIf(() => p.PlayAnimation,
                        () => Param("Frames Per Second", p.FramesPerSecond)),
                    UI.DynamicElementIf(() => !p.PlayAnimation,
                        () => Param("Current Frame", p.CurrentFrame)),
                    UI.Toggle("Loop", () => p.Loop, value => p.Loop = value),
                    UI.Field("Start Frame", () => p.StartFrame, value => p.StartFrame = System.Math.Max(0, value)),
                    UI.Toggle("Preserve Aspect", () => p.PreserveAspect, value => p.PreserveAspect = value))),
                ("Transform", UI.Column(Param("Size", p.Size), Param("Position", p.Position),
                    Param("Rotation", p.Rotation), Param("Scale", p.Scale), Param("Anchor", p.Anchor))),
                ("Appearance", UI.Column(
                    UI.Field("Color Mode", () => p.ColorMode, value => p.ColorMode = value),
                    UI.DynamicElementOnStatusChanged(() => p.ColorMode, mode =>
                        mode == SpriteSheetColorMode.AccentMask
                            ? Param("Accent Color", p.AccentColor)
                            : Param("Color", p.Color)),
                    Param("Opacity", p.Opacity),
                    UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value)))));
        }
    }
}
