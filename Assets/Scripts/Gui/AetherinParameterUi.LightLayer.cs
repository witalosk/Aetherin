using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateLightLayerParamsElement(LabelElement label, IBinder binder)
        {
            var p = (LightLayerParams)binder.GetObject();
            p.EnsureInitialized();
            Element lightTab = UI.Column(
                        UI.Field("Type", () => p.LightType, value => p.LightType = value),
                        Param("Color", p.Color),
                        Param("Intensity", p.Intensity),
                        Param("Range", p.Range),
                        UI.Toggle("Cast Shadows", () => p.CastShadows, value => p.CastShadows = value),
                        UI.DynamicElementIf(() => p.LightType == LightType.Spot, () => UI.Column(
                            Param("Spot Angle", p.SpotAngle),
                            Param("Inner Spot Angle", p.InnerSpotAngle))));
            Element transformTab = UI.Column(
                        Param("Position", p.Position),
                        Param("Rotation", p.Rotation));
            Element volumeTab = UI.DynamicElementIf(() => p.LightType == LightType.Spot,
                () => UI.Column(
                            UI.Toggle("Enabled", () => p.VolumetricEnabled, value => p.VolumetricEnabled = value),
                            UI.DynamicElementIf(() => p.VolumetricEnabled, () => UI.Column(
                                Param("Opacity", p.Opacity),
                                Param("Density", p.VolumetricDensity),
                                Param("Intensity", p.VolumetricIntensity),
                                Param("Noise", p.VolumetricNoise),
                                UI.Field("Steps", () => p.VolumetricSteps,
                                    value => p.VolumetricSteps = Mathf.Clamp(value, 4, 64))))));
            return UI.Column(UI.Tabs(
                ("Light", lightTab),
                ("Transform", transformTab),
                ("Volumetric Spot", volumeTab)));
        }
    }
}
