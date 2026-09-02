using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    public static partial class AetherinParameterUi
    {
        private static Element CreatePostEffectManagerElement(
            LabelElement label,
            IBinder<PostEffectManagerParams> binder)
        {
            var manager = binder.Get();
            if (manager == null) return UI.Label("-");

            return UI.Column(
                label,
                UI.Fold("Next (before crossfade)", UI.Field(null, Binder.Create(manager.Next, typeof(PostEffectStack)))));
        }

        private static Element CreatePostEffectStackElement(LabelElement label, IBinder<PostEffectStack> binder)
        {
            var stack = binder.Get();
            if (stack == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle("Enabled", () => stack.Enabled, value => stack.Enabled = value),
                    UI.Slider("Fader", () => stack.Strength.BaseValue,
                        value => stack.Strength.BaseValue = value, 0f, 1f).SetFlexGrow(1f),
                    CreateModulationLauncher("Post Effect Strength", stack.Strength.Modulation)),
                UI.Field("FX (CC)", Binder.Create(stack.FxCc, typeof(MidiCcBinding))),
                UI.List("Modules",
                    () => stack.Modules,
                    value => stack.Modules = value,
                    new ListViewOption(reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true)));
        }

        private static Element CreatePostEffectModuleElement(LabelElement label, IBinder<PostEffectModule> binder)
        {
            var module = binder.Get();
            if (module == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => module.Enabled, value => module.Enabled = value).SetWidth(20f),
                    UI.Field(null, () => module.Type, value => module.Type = value).SetFlexGrow(1f)),
                Param("Strength", module.Strength),
                UI.DynamicElementOnStatusChanged(
                    () => module.Type,
                    type => UI.Column(CreatePostEffectFields(module, type))));
        }

        private static IEnumerable<Element> CreatePostEffectFields(PostEffectModule module, PostEffectType type)
        {
            switch (type)
            {
                case PostEffectType.ChromaticAberration:
                    yield return Param("Separation", module.Amount);
                    break;
                case PostEffectType.PreviousFrameBlend:
                    yield return Param("Feedback", module.Secondary);
                    yield return Param("Drift", module.Amount);
                    yield return Param("Drift Speed", module.Speed);
                    break;
                case PostEffectType.DomainWarp:
                    yield return Param("Warp", module.Amount);
                    yield return Param("Noise Scale", module.Scale);
                    yield return Param("Speed", module.Speed);
                    break;
                case PostEffectType.ScreenShake:
                    yield return Param("Distance", module.Amount);
                    yield return Param("Speed", module.Speed);
                    break;
                case PostEffectType.Kaleidoscope:
                    yield return Param("Segments", module.Scale);
                    yield return Param("Rotation Speed", module.Speed);
                    break;
                case PostEffectType.Pixelate:
                    yield return Param("Pixels", module.Scale);
                    break;
                case PostEffectType.Scanline:
                    yield return Param("Displacement", module.Amount);
                    yield return Param("Lines", module.Scale);
                    yield return Param("Speed", module.Speed);
                    yield return Param("Glitch Chance", module.Secondary);
                    break;
                case PostEffectType.Posterize:
                    yield return Param("Levels", module.Scale);
                    break;
            }
        }

        private static Element Param(string label, object parameter) =>
            UI.Field(label, Binder.Create(parameter, parameter.GetType()));
    }
}
