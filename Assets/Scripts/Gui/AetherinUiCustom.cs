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

        private static void ToggleLearn(MidiBinding binding)
        {
            if (binding == null) return;

            if (binding.IsLearning) binding.CancelLearn();
            else binding.BeginLearn();
        }
    }
}
