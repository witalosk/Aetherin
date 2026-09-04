using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// TextLayerで利用できるTextMeshProフォントアセットをキーで管理するライブラリ。
    /// シーンに1つ配置し、InspectorからKeyとFont Assetを登録する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FontAssetLibrary : MonoBehaviour
    {
        [SerializeField] private List<FontAssetLibraryEntry> _fontAssets = new();

        public TMP_FontAsset Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _fontAssets?.Find(entry => entry != null && entry.Key == key)?.FontAsset;
        }

        public IReadOnlyList<string> GetKeys()
        {
            if (_fontAssets == null) return Array.Empty<string>();
            var keys = new List<string>(_fontAssets.Count);
            foreach (var entry in _fontAssets)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Key)) keys.Add(entry.Key);
            return keys;
        }
    }
}
