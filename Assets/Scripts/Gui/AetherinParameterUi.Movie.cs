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

            return UI.Column(UI.Tabs(
                ("Movie", UI.Column(
                    UI.List("Video Paths", () => p.VideoPaths, value => p.VideoPaths = value),
                    UI.Field("Path Mode", () => p.PathMode, value => p.PathMode = value),
                    UI.Toggle("Loop", () => p.Loop, value => p.Loop = value),
                    UI.Field("Playback Speed", () => p.PlaybackSpeed, value => p.PlaybackSpeed = value),
                    UI.Toggle("Preserve Aspect", () => p.PreserveAspect, value => p.PreserveAspect = value))),
                ("Transform", UI.Column(Param("Size", p.Size), Param("Position", p.Position),
                    Param("Rotation", p.Rotation), Param("Scale", p.Scale), Param("Anchor", p.Anchor))),
                ("Appearance", UI.Column(
                    Param("Opacity", p.Opacity),
                    UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value)))));
        }
    }
}
