using UnityEngine;

namespace Aetherin
{
    public interface IColorPaletteManager
    {
        ColorPalette CurrentPalette { get; }
        void SetToMaterial(Material material);
    }
}
