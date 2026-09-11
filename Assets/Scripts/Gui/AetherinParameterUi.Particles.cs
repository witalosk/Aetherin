using System.Collections.Generic;
using RosettaUI;
using UnityEngine;

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
                UI.Tabs(
                    ("Rendering", UI.Column(
                        UI.Field("Renderer", () => p.RenderBackend, value => p.RenderBackend = value),
                        UI.DynamicElementIf(
                            () => p.RenderBackend == ParticleRenderBackend.VfxGraph,
                            () => vfxGraphSelector),
                        UI.DynamicElementIf(
                            () => p.RenderBackend == ParticleRenderBackend.Trail,
                            () => UI.Column(
                                UI.Field("Trail Length", () => p.TrailLength, value => p.TrailLength = value),
                                UI.Field("Trail Width", () => p.TrailWidth, value => p.TrailWidth = value),
                                UI.Field("Trail Tail Width", () => p.TrailTailWidth, value => p.TrailTailWidth = value))),
                        UI.DynamicElementIf(
                            () => p.RenderBackend == ParticleRenderBackend.Plexus,
                            () => UI.Column(
                                Param("Connection Distance", p.PlexusConnectionDistance),
                                UI.Field("Max Connections", () => p.PlexusMaxConnections,
                                    value => p.PlexusMaxConnections = value),
                                UI.Field("Neighbor Search", () => p.PlexusNeighborSearch,
                                    value => p.PlexusNeighborSearch = value),
                                UI.DynamicElementIf(
                                    () => p.PlexusNeighborSearch == BoidsNeighborSearchMode.Sampled,
                                    () => UI.Field("Neighbor Samples", () => p.PlexusNeighborSamples,
                                        value => p.PlexusNeighborSamples = value)),
                                UI.Field("Line Width", () => p.PlexusLineWidth,
                                    value => p.PlexusLineWidth = value))),
                        UI.Field("Blend Mode", () => p.BlendMode, value => p.BlendMode = value),
                        Param("Opacity", p.Opacity),
                        UI.Field("Order", () => p.Order, value => p.Order = value))),
                    ("Transform", UI.Column(
                        Param("Position", p.Position),
                        Param("Rotation", p.Rotation),
                        Param("Scale", p.Scale))),
                    ("Emission", UI.Column(
                        UI.Field("Capacity", () => p.Capacity, value => p.Capacity = value),
                        UI.Field("Seed", () => p.Seed, value => p.Seed = value),
                        Param("Emitter Offset", p.EmitterOffset),
                        Param("Emitter Size", p.EmitterSize),
                        CreateParticleRandomRangeElement("Lifetime", p.Lifetime),
                        CreateParticleRandomRangeElement("Initial Speed", p.InitialSpeed))),
                    ("Appearance", UI.Column(
                        UI.Field("Particle Shape", () => p.Shape, value => p.Shape = value),
                        Param("Color", p.Color),
                        CreateParticleRandomRangeElement("Particle Size", p.ParticleSize),
                        Param("Initial Rotation", p.InitialRotation),
                        Param("Rotation Random", p.RotationRandom),
                        Param("Angular Velocity", p.AngularVelocity),
                        Param("Angular Velocity Random", p.AngularVelocityRandom))),
                    ("Simulation", UI.Column(
                        Param("Simulation Speed", p.SimulationSpeed),
                        UI.List("Modules", () => p.Modules, value => p.Modules = value, listOption)))));
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
                case ParticleSimulationModuleType.ClampVelocity:
                    module.Strength.BaseValue = 2f;
                    break;
                case ParticleSimulationModuleType.ApplyBoids:
                    module.BoidsNeighborRadii.BaseValue = new UnityEngine.Vector3(2f, 2f, 2f);
                    module.BoidsWeights.BaseValue = new UnityEngine.Vector3(1.5f, 0.75f, 0.5f);
                    module.BoidsMaxForces.BaseValue = new UnityEngine.Vector3(2f, 1f, 1f);
                    module.BoidsMaxSpeed.BaseValue = 2f;
                    module.BoidsMaxAcceleration.BaseValue = 4f;
                    module.BoidsNeighborSamples = 12;
                    module.BoidsFieldOfView.BaseValue = 360f;
                    module.BoidsSeparationUsesFieldOfView = false;
                    break;
                case ParticleSimulationModuleType.ColorOverLife:
                case ParticleSimulationModuleType.SizeOverLife:
                    module.OverLifeCurve = ParticleSimulationModule.CreateDefaultOverLifeCurve(type);
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
                    yield return UI.Field("Opacity Curve", () => module.OverLifeCurve,
                        value => module.OverLifeCurve = value);
                    break;
                case ParticleSimulationModuleType.SizeOverLife:
                    yield return UI.Field("Size Curve", () => module.OverLifeCurve,
                        value => module.OverLifeCurve = value);
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
                case ParticleSimulationModuleType.ClampVelocity:
                    yield return Param("Max Speed", module.Strength);
                    break;
                case ParticleSimulationModuleType.ApplyBoids:
                    yield return Param("Max Speed", module.BoidsMaxSpeed);
                    yield return Param("Max Acceleration", module.BoidsMaxAcceleration);
                    yield return Param("Neighbor Radii (Separation, Alignment, Cohesion)", module.BoidsNeighborRadii);
                    yield return Param("Weights (Separation, Alignment, Cohesion)", module.BoidsWeights);
                    yield return Param("Max Forces (Separation, Alignment, Cohesion)", module.BoidsMaxForces);
                    yield return Param("Field of View (Degrees)", module.BoidsFieldOfView);
                    yield return UI.Toggle("Apply FOV to Separation", () => module.BoidsSeparationUsesFieldOfView,
                        value => module.BoidsSeparationUsesFieldOfView = value);
                    yield return UI.Field("Neighbor Search", () => module.BoidsNeighborSearch,
                        value => module.BoidsNeighborSearch = value);
                    yield return UI.DynamicElementIf(
                        () => module.BoidsNeighborSearch == BoidsNeighborSearchMode.Sampled,
                        () => UI.Field("Neighbor Samples", () => module.BoidsNeighborSamples,
                            value => module.BoidsNeighborSamples = value));
                    break;
            }
        }
    }
}
