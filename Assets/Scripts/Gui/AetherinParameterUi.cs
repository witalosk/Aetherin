using System;
using System.Collections.Generic;
using RosettaUI;

namespace Aetherin
{
    /// <summary>
    /// パラメータ系の型をFoldに頼らず1行で表示するRosettaUIのカスタム表示
    /// 値は常に手前に置き、Modulationや詳細設定はランチャーから別ウィンドウで開く
    /// </summary>
    public static partial class AetherinParameterUi
    {
        private const float LauncherWidth = 54f;
        private const float DetailWindowWidth = 420f;

        public static void Register()
        {
            UICustom.RegisterElementCreationFunc<FloatParameter>(CreateFloatParameterElement);
            UICustom.RegisterElementCreationFunc<IntParameter>(CreateIntParameterElement);
            UICustom.RegisterElementCreationFunc<Vector2Parameter>(CreateVector2ParameterElement);
            UICustom.RegisterElementCreationFunc<Vector3Parameter>(CreateVector3ParameterElement);
            UICustom.RegisterElementCreationFunc<FloatModulationStack>(CreateModulationStackElement);
            UICustom.RegisterElementCreationFunc<FloatModulator>(CreateModulatorElement);
            UICustom.RegisterElementCreationFunc<PaletteColorParameter>(CreatePaletteColorElement);
            UICustom.RegisterElementCreationFunc<StrokeTrimParams>(CreateStrokeTrimElement);
            UICustom.RegisterElementCreationFunc<RepeaterParams>(CreateRepeaterElement);
            UICustom.RegisterElementCreationFunc<ShapeLayerParams>(CreateShapeLayerParamsElement);
            UICustom.RegisterElementCreationFunc<Primitive3DLayerParams>(CreatePrimitive3DLayerParamsElement);
            UICustom.RegisterElementCreationFunc<PostEffectManagerParams>(CreatePostEffectManagerElement);
            UICustom.RegisterElementCreationFunc<PostEffectStack>(CreatePostEffectStackElement);
            UICustom.RegisterElementCreationFunc<PostEffectModule>(CreatePostEffectModuleElement);
            UICustom.RegisterElementCreationFunc<GpuParticleLayerParams>(CreateGpuParticleLayerParamsElement);
            UICustom.RegisterElementCreationFunc<ParticleSimulationModule>(CreateParticleSimulationModuleElement);
            UICustom.RegisterElementCreationFunc<TextLayerParams>(CreateTextLayerParamsElement);
            UICustom.RegisterElementCreationFunc<TextAnimatorParams>(CreateTextAnimatorElement);
            UICustom.RegisterElementCreationFunc<TextRangeSelectorParams>(CreateTextRangeSelectorElement);
        }

        private static string LabelText(LabelElement label) => label?.Value ?? "Parameter";

        /// <summary>
        /// ラベルをUI.Fieldの第1引数へ渡し、登録済みのカスタム表示を使わせる
        /// </summary>
        private static Element Param(LabelElement label, object parameter) =>
            parameter == null ? null : UI.Field(label, Binder.Create(parameter, parameter.GetType()));
    }
}
