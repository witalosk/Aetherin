using System;
using System.Collections.Generic;
using RosettaUI;
using UnityEngine;

namespace Aetherin
{
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
    }

    [Serializable]
    public class ColorPaletteBinding : IElementCreator
    {
        public ColorPalette Palette = new();
        public MidiBinding Binding = new();
        
        public Element CreateElement(LabelElement label)
        {
            return UI.Column(
                UI.Field(UI.Label(() => Palette.Name), Binder.Create(Palette, typeof(ColorPalette))),
                UI.Field("Binding", Binder.Create(Binding, typeof(MidiBinding)))
            ).SetBackgroundColor(Palette.AccentColor1 * 0.5f);
        }
    }
    
    [Serializable]
    public class ColorPaletteManagerParams : IParams
    {
        public List<ColorPaletteBinding> PaletteBindings = new();
    }
    
    public class ColorPaletteManager : MonoBehaviour, IColorPaletteManager, ISaveAndUiTarget
    {
        public ColorPalette CurrentPalette { get; private set; } = new();

        public IParams Params => _params;
        
        [SerializeField] private ColorPaletteManagerParams _params = new();

        private void Start()
        {
            if (_params.PaletteBindings.Count > 0)
            {
                CurrentPalette = _params.PaletteBindings[0].Palette;
            }
        }

        private void Update()
        {
            foreach (var pair in _params.PaletteBindings)
            {
                pair.Binding.SetLed(CurrentPalette == pair.Palette ? pair.Palette.AccentColor1 * (Mathf.Sin(Time.time * 20f) * 0.5f + 0.5f) : pair.Palette.AccentColor1);
                if (pair.Binding.WasNoteOn)
                {
                    CurrentPalette = pair.Palette;
                }
            }
        }

        public void SetToMaterial(Material material)
        {
            material.SetColor("_BackgroundColor1", CurrentPalette.BackgroundColor1.linear);
            material.SetColor("_BackgroundColor2", CurrentPalette.BackgroundColor2.linear);
            material.SetColor("_AccentColor1", CurrentPalette.AccentColor1.linear);
            material.SetColor("_AccentColor2", CurrentPalette.AccentColor2.linear);
            material.SetColor("_SubAccentColor1", CurrentPalette.SubAccentColor1.linear);
            material.SetColor("_SubAccentColor2", CurrentPalette.SubAccentColor2.linear);
        }
        
    }
}
