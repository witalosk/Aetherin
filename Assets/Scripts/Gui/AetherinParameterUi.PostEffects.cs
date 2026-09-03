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
                UI.Label("編集対象: Next（フェーダー到達時にCurrentへ昇格）"),
                UI.Fold("Next", UI.Field(null, Binder.Create(manager.Next, typeof(PostEffectStack)))));
        }

        private static Element CreatePostEffectStackElement(LabelElement label, IBinder<PostEffectStack> binder)
        {
            var stack = binder.Get();
            if (stack == null) return UI.Label("-");

            return UI.List("Decks",
                () => stack.Decks,
                value => stack.Decks = value,
                new ListViewOption(reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true));
        }

        private static Element CreatePostEffectDeckElement(LabelElement label, IBinder<PostEffectDeck> binder)
        {
            var deck = binder.Get();
            if (deck == null) return UI.Label("-");

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => deck.Enabled, value => deck.Enabled = value).SetWidth(20f),
                    UI.Field("Name", () => deck.Name, value => deck.Name = value).SetFlexGrow(1f)),
                UI.Row(
                    UI.Slider("Strength", () => deck.Strength.BaseValue,
                        value => deck.Strength.BaseValue = value, 0f, 1f).SetFlexGrow(1f),
                    CreateModulationLauncher($"{deck.Name} Strength", deck.Strength.Modulation)),
                UI.Field("Fader (CC)", Binder.Create(deck.Fader, typeof(MidiCcBinding))),
                UI.List("Modules",
                    () => deck.Modules,
                    value => deck.Modules = value,
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
                case PostEffectType.Bloom:
                    yield return Param("Intensity", module.Amount);
                    yield return Param("Radius", module.Scale);
                    yield return Param("Threshold", module.Secondary);
                    break;
            }
        }

        private static Element Param(string label, object parameter) =>
            UI.Field(label, Binder.Create(parameter, parameter.GetType()));
    }
}
