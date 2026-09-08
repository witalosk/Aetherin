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
                UI.Tabs(
                    ("Model", UI.Column(
                        modelSelector,
                        UI.Field("Render Mode", () => p.RenderMode, value => p.RenderMode = value),
                        UI.Field("Material", () => p.MaterialMode, value => p.MaterialMode = value))),
                    ("Transform", UI.Column(
                        Param(UI.Label("Position"), p.Position),
                        Param(UI.Label("Rotation"), p.Rotation),
                        Param(UI.Label("Scale"), p.Scale),
                        Param(UI.Label("Anchor"), p.Anchor))),
                    ("Appearance", UI.Column(
                        Param(UI.Label("Color"), p.Color),
                        Param(UI.Label("Wire Color"), p.WireColor),
                        Param(UI.Label("Opacity"), p.Opacity),
                        UI.DynamicElementIf(() => p.MaterialMode != ModelLayerMaterialMode.Glass,
                            () => UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value)),
                        UI.DynamicElementOnStatusChanged(() => p.MaterialMode, mode => mode == ModelLayerMaterialMode.Lit
                            ? UI.Column(Param("Metallic", p.Metallic), Param("Smoothness", p.Smoothness),
                                UI.Field("Reflection Source", () => p.LitReflectionSource, value => p.LitReflectionSource = value))
                            : mode == ModelLayerMaterialMode.Glass
                                ? UI.Column(Param("Refraction", p.GlassRefraction), Param("Tint", p.GlassTint),
                                    Param("Fresnel Power", p.GlassFresnelPower), Param("Fresnel Intensity", p.GlassFresnelIntensity),
                                    Param("Chromatic Aberration", p.GlassChromaticAberration), Param("Distortion", p.GlassDistortion),
                                    Param("Distortion Scale", p.GlassDistortionScale))
                                : UI.Column()))),
                    ("Animation", UI.Column(
                        UI.Field("Play Animation", () => p.PlayAnimation, value => p.PlayAnimation = value),
                        Param(UI.Label("Animation Speed"), p.AnimationSpeed)))));
        }
    }
}
