using System.Collections.Generic;
using System.Linq;
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
                UI.Label("詳細設定は下の Current / Next エディタから操作できます。"));
        }

        private static Element CreateDeckVolumeEffectsElement(
            LabelElement label, IBinder<DeckVolumeEffects> binder)
        {
            var effects = binder.Get();
            if (effects == null) return UI.Label("-");
            effects.EnsureInitialized();

            return UI.Column(
                label,
                UI.Fold("Bloom", UI.Column(
                    UI.Toggle("Enabled", () => effects.BloomEnabled, value => effects.BloomEnabled = value),
                    UI.Field("Toggle Pad", Binder.Create(effects.BloomToggleButton, typeof(MidiBinding))),
                    Param("Intensity", effects.BloomIntensity),
                    Param("Threshold", effects.BloomThreshold),
                    Param("Scatter", effects.BloomScatter))),
                UI.Fold("Depth Of Field", UI.Column(
                    UI.Field("Mode", () => effects.DepthOfFieldMode, value => effects.DepthOfFieldMode = value))));
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
            deck.EnsureInitialized();

            return UI.Column(
                UI.Row(
                    UI.Toggle(null, () => deck.Enabled, value => deck.Enabled = value).SetWidth(20f),
                    UI.Field("Name", () => deck.Name, value => deck.Name = value).SetFlexGrow(1f)),
                UI.Row(
                    UI.Slider("Strength", () => deck.Strength.BaseValue,
                        value => deck.Strength.BaseValue = value, 0f, 1f).SetFlexGrow(1f),
                    CreateModulationLauncher($"{deck.Name} Strength", deck.Strength.Modulation)),
                UI.Field("Control", () => deck.ControlMode, value => deck.ControlMode = value),
                UI.DynamicElementIf(
                    () => deck.ControlMode == PostEffectControlMode.Fader,
                    () => UI.Column(
                        UI.Field("Fader (CC)", Binder.Create(deck.Fader, typeof(MidiCcBinding))),
                        UI.Field("Toggle Pad", Binder.Create(deck.ToggleButton, typeof(MidiBinding))))),
                UI.DynamicElementIf(
                    () => deck.ControlMode == PostEffectControlMode.OutputPad,
                    () => UI.Field("Output Pad (Hold)", Binder.Create(deck.OutputPad, typeof(MidiBinding)))),
                UI.List("Modules",
                    () => deck.Modules,
                    value => deck.Modules = value,
                    new ListViewOption(reorderable: true, fixedSize: false, header: true, suppressAutoIndent: true)));
        }

        private static Element CreatePostEffectModuleElement(LabelElement label, IBinder<PostEffectModule> binder)
        {
            var module = binder.Get();
            if (module == null) return UI.Label("-");
            module.EnsureInitialized();
            module.GetAvailableLutKeys ??= () =>
                LutLibrary.FindBestAvailable()
                    ?.GetKeys() ?? System.Array.Empty<string>();
            module.GetAvailableTextureKeys ??= () =>
                StageAssetCatalog.FindBestAvailable().GetTextureKeys();

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
                    yield return Param("Zoom", module.Scale);
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
                    yield return Param("Bend", module.Value);
                    yield return Param("Wave Displacement", module.Amount);
                    yield return Param("Lines", module.Scale);
                    yield return Param("Speed", module.Speed);
                    yield return Param("Glitch Chance", module.Secondary);
                    break;
                case PostEffectType.Posterize:
                    yield return Param("Levels", module.Scale);
                    break;
                case PostEffectType.CrossFilter:
                    yield return Param("Threshold", module.CrossFilterThreshold);
                    yield return Param("Exposure", module.CrossFilterExposure);
                    yield return Param("Lines", module.CrossFilterLineCount);
                    yield return Param("Passes", module.CrossFilterPassCount);
                    yield return Param("Sample Length", module.CrossFilterSampleLength);
                    yield return Param("Attenuation", module.CrossFilterAttenuation);
                    yield return Param("Rotation", module.CrossFilterRotation);
                    break;
                case PostEffectType.LedDisplay:
                    yield return Param("Dot Size", module.Amount);
                    yield return Param("LEDs", module.Scale);
                    break;
                case PostEffectType.HorizontalFold:
                    yield return Param("Fold Amount", module.Amount);
                    break;
                case PostEffectType.HashInvertBlocks:
                    yield return Param("Num", module.Scale);
                    yield return Param("Seed", module.Amount);
                    break;
                case PostEffectType.Grid:
                    yield return Param("Cells", module.Scale);
                    break;
                case PostEffectType.Noise:
                    yield return Param("Amount", module.Amount);
                    yield return Param("Grain Size", module.Scale);
                    yield return Param("Speed", module.Speed);
                    break;
                case PostEffectType.BlockGlitch:
                    yield return Param("Displacement", module.Amount);
                    yield return Param("Blocks", module.Scale);
                    yield return Param("Speed", module.Speed);
                    yield return Param("Coverage", module.Secondary);
                    break;
                case PostEffectType.HsvLevels:
                    yield return Param("Hue", module.Hue);
                    yield return Param("Saturation", module.Saturation);
                    yield return Param("Value", module.Value);
                    yield return Param("Black Level", module.BlackLevel);
                    yield return Param("White Level", module.WhiteLevel);
                    yield return Param("Gamma", module.Gamma);
                    break;
                case PostEffectType.Shutter:
                    yield return UI.Field("Mode", () => module.ShutterMode, value => module.ShutterMode = value);
                    yield return Param("Close", module.Amount);
                    break;
                case PostEffectType.HandDrawn:
                    yield return Param("Edge Threshold", module.Amount);
                    yield return Param("Hatch Density", module.Scale);
                    yield return Param("Wiggle Speed", module.Speed);
                    yield return Param("Wiggle Amount", module.Secondary);
                    yield return Param("Wiggle FPS", module.HandDrawnFrameRate);
                    break;
                case PostEffectType.FrameRateDrop:
                    yield return Param("Drop Amount", module.FrameRateDropAmount);
                    yield return UI.Field("Sync", () => module.FrameRateDropSync,
                        value => module.FrameRateDropSync = value);
                    yield return UI.DynamicElementOnStatusChanged(
                        () => module.FrameRateDropSync,
                        sync => sync == FrameRateDropSyncMode.Beat
                            ? Param("Updates Per Beat At Maximum", module.FrameRateDropUpdatesPerBeat)
                            : Param("Minimum Frame Rate", module.FrameRateDropFps));
                    break;
                case PostEffectType.LightLeak:
                    yield return Param("Intensity", module.Amount);
                    yield return Param("Size", module.Scale);
                    yield return Param("Drift Speed", module.Speed);
                    yield return Param("Angle", module.Secondary);
                    yield return Param("Position (Left - Right)", module.LightLeakPosition);
                    yield return Param("Color", module.LightLeakColor);
                    break;
                case PostEffectType.Lut:
                    IReadOnlyList<string> lutKeys = module.GetAvailableLutKeys?.Invoke();
                    if (lutKeys != null && lutKeys.Count > 0)
                    {
                        module.InitializeLutIndex(lutKeys);
                        yield return UI.Row(
                            UI.Dropdown("LUT", () => UnityEngine.Mathf.Clamp(module.LutIndex.BaseValue, 0, lutKeys.Count - 1),
                                value =>
                                {
                                    module.LutIndex.BaseValue = value;
                                    module.LutKey = lutKeys[value];
                                }, lutKeys).SetFlexGrow(1f),
                            CreateModulationLauncher("LUT Index", module.LutIndex.Modulation));
                    }
                    else
                    {
                        yield return UI.Dropdown("LUT", () => 0, _ => { }, new[] { "LUT未登録" })
                            .SetInteractable(false);
                    }
                    yield return Param("LUT Intensity", module.LutIntensity);
                    break;
                case PostEffectType.TextureComposite:
                    IReadOnlyList<string> textureKeys = module.GetAvailableTextureKeys?.Invoke();
                    yield return UI.List("Texture Keys", () => module.CompositeTextureKeys,
                        value => module.CompositeTextureKeys = value);
                    if (textureKeys != null && textureKeys.Count > 0)
                    {
                        yield return UI.Dropdown("Add Key", () => 0,
                                value =>
                                {
                                    if (value > 0 && value <= textureKeys.Count)
                                        module.CompositeTextureKeys.Add(textureKeys[value - 1]);
                                }, new[] { "Select texture" }.Concat(textureKeys).ToArray());
                    }
                    yield return UI.DynamicElementOnStatusChanged(
                        () => string.Join("\u001f", module.CompositeTextureKeys),
                        _ => module.CompositeTextureKeys.Count > 0
                            ? UI.Row(
                                UI.Dropdown("Selected Texture",
                                    () => UnityEngine.Mathf.Clamp(module.CompositeTextureIndex.BaseValue, 0,
                                        module.CompositeTextureKeys.Count - 1),
                                    value => module.CompositeTextureIndex.BaseValue = value,
                                    module.CompositeTextureKeys).SetFlexGrow(1f),
                                CreateModulationLauncher("Texture Index", module.CompositeTextureIndex.Modulation))
                            : UI.Label("Texture Keys にテクスチャを追加してください"));
                    yield return UI.Field("Blend Mode", () => module.CompositeMode, value => module.CompositeMode = value);
                    yield return Param("Tiling", module.CompositeTiling);
                    yield return Param("Offset", module.CompositeOffset);
                    yield return Param("Rotation (Degrees)", module.CompositeRotation);
                    break;
                case PostEffectType.LuminanceDisplacement:
                    IReadOnlyList<string> displacementKeys = module.GetAvailableTextureKeys?.Invoke();
                    if (displacementKeys != null && displacementKeys.Count > 0)
                    {
                        string[] options = new[] { "Input image" }.Concat(displacementKeys).ToArray();
                        yield return UI.Dropdown("Texture", () =>
                            {
                                for (int i = 0; i < displacementKeys.Count; i++)
                                    if (displacementKeys[i] == module.LuminanceDisplacementTextureKey) return i + 1;
                                return 0;
                            }, value => module.LuminanceDisplacementTextureKey = value == 0
                                ? string.Empty : displacementKeys[value - 1], options);
                    }
                    else
                        yield return UI.Dropdown("Texture", () => 0, _ => { }, new[] { "Input image" })
                            .SetInteractable(false);
                    yield return Param("Displacement X/Y", module.LuminanceDisplacementOffset);
                    yield return Param("Texture Tiling", module.LuminanceDisplacementTiling);
                    yield return Param("Texture Offset", module.LuminanceDisplacementMapOffset);
                    break;
                case PostEffectType.RuntimeShader:
                    yield return UI.Toggle("Previous Frame Texture",
                        () => module.RuntimeProvidePreviousFrameTexture,
                        value => module.RuntimeProvidePreviousFrameTexture = value);
                    yield return UI.Fold("Parameters", UI.Column(
                        Param("Float 0", module.RuntimeUserFloat0),
                        Param("Float 1", module.RuntimeUserFloat1),
                        Param("Float 2", module.RuntimeUserFloat2),
                        Param("Float 3", module.RuntimeUserFloat3),
                        Param("Vector 0", module.RuntimeUserVector0),
                        Param("Vector 1", module.RuntimeUserVector1)));
                    yield return UI.ScrollViewVertical(500f,
                        UI.TextArea(null, () => module.RuntimeShaderCode,
                                value => module.RuntimeShaderCode = value)
                            .SetMinHeight(320f),
                        UI.Label(() => module.RuntimeCompileMessage).SetFlexGrow(1f));
                    break;
            }
        }

        private static Element Param(string label, object parameter) =>
            UI.Field(label, Binder.Create(parameter, parameter.GetType()));
    }
}
