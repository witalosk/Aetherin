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

            return UI.Column(
                UI.Toggle("Visible", () => p.Visible, value => p.Visible = value),
                UI.Field("Renderer", () => p.RenderBackend, value => p.RenderBackend = value),
                UI.DynamicElementIf(
                    () => p.RenderBackend == ParticleRenderBackend.VfxGraph,
                    () => UI.Field("VFX Resources Path", () => p.VfxGraphResourcePath,
                        value => p.VfxGraphResourcePath = value)),
                UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                Param("Opacity", p.Opacity),
                UI.Field("Order", () => p.Order, value => p.Order = value),
                Param("Position", p.Position),
                Param("Rotation", p.Rotation),
                Param("Scale", p.Scale),
                UI.Field("Capacity", () => p.Capacity, value => p.Capacity = value),
                UI.Field("Seed", () => p.Seed, value => p.Seed = value),
                Param("Emitter Size", p.EmitterSize),
                Param("Lifetime", p.Lifetime),
                Param("Initial Speed", p.InitialSpeed),
                Param("Simulation Speed", p.SimulationSpeed),
                Param("Particle Size", p.ParticleSize),
                Param("Color", p.Color),
                UI.List("Simulation Modules", () => p.Modules, value => p.Modules = value, listOption));
        }

        private static Element CreateParticleSimulationModuleElement(
            LabelElement label,
            IBinder<ParticleSimulationModule> binder)
        {
            var module = binder.Get();
            if (module == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => module.Enabled, value => module.Enabled = value).SetWidth(20f),
                    UI.Field(null, () => module.Type, value => module.Type = value).SetFlexGrow(1f)),
                UI.DynamicElementOnStatusChanged(
                    () => module.Type,
                    type => UI.Column(CreateParticleModuleFields(module, type))));
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
            }
        }
    }
}
