using System;
using System.Collections.Generic;
using RosettaUI;
using UnityEngine;
using UnitySimpleContainer;

namespace Aetherin
{
    public enum ColorPaletteVariation
    {
        None,
        InvertBackgroundBrightness,
        SwapAccent,
        InvertAll,
    }

    [Serializable]
    public class ColorPalette
    {
        public string Name;
        [Space]
        public Color BackgroundColor1;
        public Color BackgroundColor2;
        public Color AccentColor1;
        public Color AccentColor2;
        public Color SubAccentColor1;
        public Color SubAccentColor2;

        [NonSerialized] public ColorPalette SourcePalette;
        [NonSerialized] public ColorPaletteVariation Variation;

        public ColorPalette CreateVariation(ColorPaletteVariation variation)
        {
            var result = new ColorPalette
            {
                Name = variation == ColorPaletteVariation.None
                    ? Name
                    : $"{Name} / {GetVariationName(variation)}",
                BackgroundColor1 = BackgroundColor1,
                BackgroundColor2 = BackgroundColor2,
                AccentColor1 = AccentColor1,
                AccentColor2 = AccentColor2,
                SubAccentColor1 = SubAccentColor1,
                SubAccentColor2 = SubAccentColor2,
                SourcePalette = this,
                Variation = variation,
            };

            switch (variation)
            {
                case ColorPaletteVariation.InvertBackgroundBrightness:
                    result.BackgroundColor1 = ColorUtility.InvertHslLightness(result.BackgroundColor1);
                    result.BackgroundColor2 = ColorUtility.InvertHslLightness(result.BackgroundColor2);
                    break;
                case ColorPaletteVariation.SwapAccent:
                    (result.AccentColor1, result.AccentColor2) =
                        (result.AccentColor2, result.AccentColor1);
                    break;
                case ColorPaletteVariation.InvertAll:
                    result.BackgroundColor1 = ColorUtility.Invert(result.BackgroundColor1);
                    result.BackgroundColor2 = ColorUtility.Invert(result.BackgroundColor2);
                    result.AccentColor1 = ColorUtility.Invert(result.AccentColor1);
                    result.AccentColor2 = ColorUtility.Invert(result.AccentColor2);
                    result.SubAccentColor1 = ColorUtility.Invert(result.SubAccentColor1);
                    result.SubAccentColor2 = ColorUtility.Invert(result.SubAccentColor2);
                    break;
            }

            return result;
        }

        private static string GetVariationName(ColorPaletteVariation variation) => variation switch
        {
            ColorPaletteVariation.InvertBackgroundBrightness => "Dark / Light",
            ColorPaletteVariation.SwapAccent => "Swap Accent",
            ColorPaletteVariation.InvertAll => "Invert All",
            _ => "Normal",
        };

        public void ApplyToMaterial(Material material)
        {
            material.SetColor("_BackgroundColor1", BackgroundColor1.linear);
            material.SetColor("_BackgroundColor2", BackgroundColor2.linear);
            material.SetColor("_AccentColor1", AccentColor1.linear);
            material.SetColor("_AccentColor2", AccentColor2.linear);
            material.SetColor("_SubAccentColor1", SubAccentColor1.linear);
            material.SetColor("_SubAccentColor2", SubAccentColor2.linear);
        }
    }

    [Serializable]
    public class ColorPaletteBinding : IElementCreator
    {
        public ColorPalette Palette = new();
        public MidiBinding Binding = new();
        
        public Element CreateElement(LabelElement label)
        {
            return UI.Fold(UI.Label(() => $"<color=#{ColorUtility.ToHtmlStringRGB(Palette.BackgroundColor1)}>■</color> <color=#{ColorUtility.ToHtmlStringRGB(Palette.AccentColor1)}>■</color> <color=#{ColorUtility.ToHtmlStringRGB(Palette.SubAccentColor1)}>■</color> {Palette.Name}"),
                UI.Field(null, Binder.Create(Palette, typeof(ColorPalette))),
                UI.Field("Binding", Binder.Create(Binding, typeof(MidiBinding)))
            ).SetBackgroundColor(Palette.AccentColor1 * 0.5f);
        }
    }
    
    [Serializable]
    public class ColorPaletteManagerParams : IParams
    {
        public List<ColorPaletteBinding> PaletteBindings = new();
    }
    
    /// <summary>
    /// パレットの定義とMIDIパッドによる選択だけを担当するマネージャ
    /// 選択結果はNext側のDeckStateに書き込むだけで、Current / Nextの二重管理はStageManagerに閉じている
    /// </summary>
    public class ColorPaletteManager : MonoBehaviour, ISaveAndUiTarget
    {
        public string Category => UiCategory.Main;
        public IParams Params => _params;

        [SerializeField] private ColorPaletteManagerParams _params = new();

        private IDeckStateProvider _deckStateProvider;

        private static readonly ColorPaletteVariation[] Variations =
        {
            ColorPaletteVariation.None,
            ColorPaletteVariation.InvertBackgroundBrightness,
            ColorPaletteVariation.SwapAccent,
            ColorPaletteVariation.InvertAll,
        };

        private ColorPalette CurrentPalette => _deckStateProvider?.GetState(StageDeck.Current).Palette;
        private ColorPalette NextPalette => _deckStateProvider?.NextState.Palette;

        [Inject]
        public void Construct(IDeckStateProvider deckStateProvider)
        {
            _deckStateProvider = deckStateProvider;
        }

        private void Start()
        {
            if (_deckStateProvider == null || _params.PaletteBindings.Count == 0) return;

            _deckStateProvider.GetState(StageDeck.Current).Palette = _params.PaletteBindings[0].Palette;
            _deckStateProvider.NextState.Palette = _params.PaletteBindings[0].Palette;
        }

        private void Update()
        {
            if (_deckStateProvider == null) return;

            foreach (var pair in _params.PaletteBindings)
            {
                // Nextに選択中のものを点滅、Currentに反映済みのものを点灯、それ以外は暗めに表示する
                Color baseColor = Color.Lerp(pair.Palette.BackgroundColor1, pair.Palette.AccentColor1, Mathf.Sin(Time.time * 20f) * 0.5f + 0.5f);
                var ledColor = baseColor * 0.25f;
                bool isNextPalette = IsSelectedPalette(NextPalette, pair.Palette);
                bool isCurrentPalette = IsSelectedPalette(CurrentPalette, pair.Palette);
                if (isNextPalette) ledColor = baseColor * (Mathf.Sin(Time.time * 40f) * 0.4f + 0.5f);
                else if (isCurrentPalette) ledColor = baseColor;
                pair.Binding.SetLed(ledColor);

                if (pair.Binding.WasNoteOn)
                {
                    _deckStateProvider.NextState.Palette = isNextPalette
                        ? CreateNextVariation(NextPalette, pair.Palette)
                        : pair.Palette;
                }
            }
        }

        private static bool IsSelectedPalette(ColorPalette selected, ColorPalette basePalette) =>
            selected == basePalette || selected?.SourcePalette == basePalette;

        private static ColorPalette CreateNextVariation(ColorPalette selected, ColorPalette basePalette)
        {
            int currentIndex = Array.IndexOf(Variations,
                selected?.SourcePalette == basePalette ? selected.Variation : ColorPaletteVariation.None);
            int nextIndex = (currentIndex + 1) % Variations.Length;
            return basePalette.CreateVariation(Variations[nextIndex]);
        }

        public Element AdditiveUi()
        {
            return UI.Column(
                UI.Label(() => $"Current: {CurrentPalette?.Name ?? "-"}"),
                UI.Label(() => $"Next: {NextPalette?.Name ?? "-"}")
            );
        }
    }
}
