using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Aetherin
{
    /// <summary>Screen Space layer rendering and shared stage assets used by visual layers.</summary>
    public interface IStageLayerHost
    {
        Camera LayerCamera { get; }
        bool LayerAllowsMidi { get; }
        ColorPalette LayerPalette { get; }
        int ResolveLayerSortingOrder(StageLayer layer, int localOrder);
        Texture2D ResolveLayerTexture(string key);
        IReadOnlyList<string> GetLayerTextureKeys();
        TMP_FontAsset ResolveLayerFont(string key);
        IReadOnlyList<string> GetLayerFontKeys();
    }
}
