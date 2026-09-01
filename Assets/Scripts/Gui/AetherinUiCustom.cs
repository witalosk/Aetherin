using System.Collections.Generic;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// 独自の型に対するRosettaUIの表示を登録する
    /// </summary>
    public static class AetherinUiCustom
    {
        /// <summary>
        /// シーンのUIが構築される前に登録する
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterUiCustomFuncs()
        {
            UICustom.RegisterElementCreationFunc<MidiBinding>(CreateMidiBindingElement);
            UICustom.RegisterElementCreationFunc<MidiCcBinding>(CreateMidiCcBindingElement);
            AetherinParameterUi.Register();
        }

        private static Element CreateMidiBindingElement(LabelElement label, IBinder<MidiBinding> binder)
        {
            var elements = new List<Element>();

            if (label != null) elements.Add(label.SetWidth(120f));

            elements.Add(UI.Label(() => binder.Get()?.Describe() ?? "-").SetWidth(160f));

            elements.Add(UI.Button(
                UI.Label(() => binder.Get() is { IsLearning: true } ? "Waiting..." : "Learn"),
                () => ToggleLearn(binder.Get())));

            elements.Add(UI.Button("Clear", () => binder.Get()?.Clear()));

            return UI.Row(elements);
        }

        private static Element CreateMidiCcBindingElement(LabelElement label, IBinder<MidiCcBinding> binder)
        {
            var elements = new List<Element>();

            if (label != null) elements.Add(label.SetWidth(120f));

            elements.Add(UI.Label(() => binder.Get()?.Describe() ?? "-").SetWidth(160f));
            elements.Add(UI.SliderReadOnly(null, () => binder.Get()?.Value ?? 0f, 0f, 1f).SetWidth(80f));

            elements.Add(UI.Button(
                UI.Label(() => binder.Get() is { IsLearning: true } ? "Move..." : "Learn"),
                () => ToggleLearn(binder.Get())));

            elements.Add(UI.Button("Clear", () => binder.Get()?.Clear()));

            return UI.Row(elements);
        }

        private static void ToggleLearn(MidiBinding binding)
        {
            if (binding == null) return;

            if (binding.IsLearning) binding.CancelLearn();
            else binding.BeginLearn();
        }

        private static void ToggleLearn(MidiCcBinding binding)
        {
            if (binding == null) return;

            if (binding.IsLearning) binding.CancelLearn();
            else binding.BeginLearn();
        }
    }
}
