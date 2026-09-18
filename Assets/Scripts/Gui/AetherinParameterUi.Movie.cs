using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateMovieLayerParamsElement(LabelElement label, IBinder<MovieLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");
            p.EnsureInitialized();
            var lutKeys = p.GetAvailableLutKeys?.Invoke();
            Element lutSelector = lutKeys != null && lutKeys.Count > 0
                ? UI.Dropdown("LUT", () =>
                    {
                        for (int i = 0; i < lutKeys.Count; i++) if (lutKeys[i] == p.LutKey) return i;
                        return 0;
                    }, value => p.LutKey = lutKeys[value], lutKeys)
                : UI.Dropdown("LUT", () => 0, _ => { }, new[] { "LUT未登録" })
                    .SetInteractable(false);

            return UI.Column(UI.Tabs(
                ("Movie", UI.Column(
                    UI.List("Video Paths (.mov or folder)", () => p.VideoPaths, value => p.VideoPaths = value),
                    UI.Field("Path Mode", () => p.PathMode, value => p.PathMode = value),
                    UI.Toggle("Loop", () => p.Loop, value => p.Loop = value),
                    UI.Field("Playback Speed", () => p.PlaybackSpeed, value => p.PlaybackSpeed = value),
                    UI.Toggle("Preserve Aspect", () => p.PreserveAspect, value => p.PreserveAspect = value))),
                ("Transform", UI.Column(Param("Size", p.Size), Param("Position", p.Position),
                    Param("Rotation", p.Rotation), Param("Scale", p.Scale), Param("Anchor", p.Anchor))),
                ("Appearance", UI.Column(
                    UI.Toggle("LUT", () => p.LutEnabled, value => p.LutEnabled = value),
                    UI.DynamicElementIf(() => p.LutEnabled,
                        () => UI.Column(lutSelector, Param("LUT Intensity", p.LutIntensity))),
                    Param("Opacity", p.Opacity),
                    UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value)))));
        }
    }
}
