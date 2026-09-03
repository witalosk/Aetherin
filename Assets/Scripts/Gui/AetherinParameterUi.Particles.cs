using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreateGpuParticleLayerParamsElement(
            LabelElement label,
            IBinder<GpuParticleLayerParams> binder)
        {
            var p = binder.Get();
            if (p == null) return UI.Label("-");
            p.EnsureInitialized();

            var listOption = new ListViewOption(
                    reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true)
                .OfType(p.Modules)
                .SetCreateItemInstanceFunc((_, _) => new ParticleSimulationModule());

            var keys = p.GetAvailableVfxGraphKeys?.Invoke();
            Element vfxGraphSelector = keys != null && keys.Count > 0
                ? UI.Dropdown("VFX Graph", () =>
                    {
                        int index = -1;
                        for (int i = 0; i < keys.Count; i++)
                            if (keys[i] == p.VfxGraphKey) { index = i; break; }
                        return index < 0 ? 0 : index;
                    }, value => p.VfxGraphKey = keys[value], keys)
                : UI.Field("VFX Graph Key", () => p.VfxGraphKey, value => p.VfxGraphKey = value);

            return UI.Column(
                UI.Toggle("Visible", () => p.Visible, value => p.Visible = value),
                UI.Fold("Rendering", UI.Column(
                    UI.Field("Renderer", () => p.RenderBackend, value => p.RenderBackend = value),
                    UI.DynamicElementIf(
                        () => p.RenderBackend == ParticleRenderBackend.VfxGraph,
                        () => vfxGraphSelector),
                    UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                    Param("Opacity", p.Opacity),
                    UI.Field("Order", () => p.Order, value => p.Order = value))),
                UI.Fold("Transform", UI.Column(
                    Param("Position", p.Position),
                    Param("Rotation", p.Rotation),
                    Param("Scale", p.Scale))),
                UI.Fold("Emission", UI.Column(
                    UI.Field("Capacity", () => p.Capacity, value => p.Capacity = value),
                    UI.Field("Seed", () => p.Seed, value => p.Seed = value),
                    Param("Emitter Offset", p.EmitterOffset),
                    Param("Emitter Size", p.EmitterSize),
                    CreateParticleRandomRangeElement("Lifetime", p.Lifetime),
                    CreateParticleRandomRangeElement("Initial Speed", p.InitialSpeed))),
                UI.Fold("Appearance", UI.Column(
                    UI.Field("Particle Shape", () => p.Shape, value => p.Shape = value),
                    Param("Color", p.Color),
                    CreateParticleRandomRangeElement("Particle Size", p.ParticleSize),
                    Param("Initial Rotation", p.InitialRotation),
                    Param("Rotation Random", p.RotationRandom),
                    Param("Angular Velocity", p.AngularVelocity),
                    Param("Angular Velocity Random", p.AngularVelocityRandom))),
                UI.Fold("Simulation", UI.Column(
                    Param("Simulation Speed", p.SimulationSpeed),
                    UI.List("Modules", () => p.Modules, value => p.Modules = value, listOption))));
        }

        private static Element CreateParticleRandomRangeElement(string label, ParticleRandomRangeParameter parameter) =>
            UI.Column(
                UI.Field($"{label} Min / Max / Power", () => parameter.MinMaxPower,
                    value => parameter.MinMaxPower = value),
                Param($"{label} Modulation", parameter.Modulation));

        private static Element CreateParticleSimulationModuleElement(
            LabelElement label,
            IBinder<ParticleSimulationModule> binder)
        {
            var module = binder.Get();
            if (module == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => module.Enabled, value => module.Enabled = value).SetWidth(20f),
                    UI.Field(null, () => module.Type, value => SetParticleModuleType(module, value)).SetFlexGrow(1f)),
                UI.DynamicElementOnStatusChanged(
                    () => module.Type,
                    type => UI.Column(CreateParticleModuleFields(module, type))));
        }

        private static void SetParticleModuleType(
            ParticleSimulationModule module,
            ParticleSimulationModuleType type)
        {
            if (module.Type == type) return;
            module.Type = type;

            switch (type)
            {
                case ParticleSimulationModuleType.ApplyLorenzAttractor:
                    module.Strength.BaseValue = 0.1f;
                    module.Vector.BaseValue = UnityEngine.Vector3.zero;
                    module.Scale.BaseValue = 10f;
                    module.Speed.BaseValue = 28f;
                    module.Secondary.BaseValue = 8f / 3f;
                    break;
                case ParticleSimulationModuleType.ApplyVortex:
                    module.Strength.BaseValue = 1f;
                    module.Vector.BaseValue = UnityEngine.Vector3.zero;
                    module.Axis.BaseValue = UnityEngine.Vector3.up;
                    module.Scale.BaseValue = 0.25f;
                    module.Speed.BaseValue = 0f;
                    break;
            }
        }

        private static IEnumerable<Element> CreateParticleModuleFields(
            ParticleSimulationModule module,
            ParticleSimulationModuleType type)
        {
            switch (type)
            {
                case ParticleSimulationModuleType.Integrate:
                    yield return Param("Speed", module.Strength);
                    break;
                case ParticleSimulationModuleType.ApplyGravity:
                    yield return Param("Acceleration", module.Vector);
                    yield return Param("Strength", module.Strength);
                    break;
                case ParticleSimulationModuleType.ApplyDrag:
                    yield return Param("Drag", module.Strength);
                    break;
                case ParticleSimulationModuleType.ApplyCurlNoise:
                    yield return Param("Strength", module.Strength);
                    yield return Param("Frequency", module.Scale);
                    yield return Param("Speed", module.Speed);
                    break;
                case ParticleSimulationModuleType.ApplyAttractor:
                    yield return Param("Point", module.Vector);
                    yield return Param("Strength", module.Strength);
                    yield return Param("Falloff", module.Scale);
                    break;
                case ParticleSimulationModuleType.ApplyModulation:
                    yield return UI.Field("Target", () => module.Target, value => module.Target = value);
                    yield return Param("Value", module.Vector);
                    yield return Param("Strength", module.Strength);
                    break;
                case ParticleSimulationModuleType.WrapBounds:
                    yield return Param("Size", module.Vector);
                    break;
                case ParticleSimulationModuleType.ColorOverLife:
                    yield return Param("Fade Power", module.Strength);
                    break;
                case ParticleSimulationModuleType.SizeOverLife:
                    yield return Param("Size", module.Strength);
                    yield return Param("Curve Power", module.Secondary);
                    break;
                case ParticleSimulationModuleType.ApplyLorenzAttractor:
                    yield return Param("Center", module.Vector);
                    yield return Param("Strength", module.Strength);
                    yield return Param("Sigma", module.Scale);
                    yield return Param("Rho", module.Speed);
                    yield return Param("Beta", module.Secondary);
                    break;
                case ParticleSimulationModuleType.ApplyVortex:
                    yield return Param("Center", module.Vector);
                    yield return Param("Axis", module.Axis);
                    yield return Param("Orbit Force", module.Strength);
                    yield return Param("Radial Pull", module.Scale);
                    yield return Param("Falloff", module.Speed);
                    break;
            }
        }
    }
}
