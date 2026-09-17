using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>LUTテクスチャをファイル名（Texture.name）で管理するシーンライブラリ。</summary>
    [DisallowMultipleComponent]
    public sealed class LutLibrary : MonoBehaviour
    {
        [SerializeField] private List<Texture2D> _textures = new();

        public int Count
        {
            get
            {
                if (_textures == null) return 0;
                int count = 0;
                foreach (Texture2D texture in _textures)
                    if (texture != null) count++;
                return count;
            }
        }

        public static LutLibrary FindBestAvailable(LutLibrary preferred = null)
        {
            if (preferred != null && preferred.Count > 0) return preferred;

            LutLibrary best = preferred;
            foreach (LutLibrary library in FindObjectsByType<LutLibrary>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (best == null || library.Count > best.Count) best = library;
            }

            return best;
        }

        public Texture2D Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _textures?.Find(texture => texture != null && texture.name == key);
        }

        public IReadOnlyList<string> GetKeys()
        {
            if (_textures == null) return Array.Empty<string>();
            var keys = new List<string>(_textures.Count);
            foreach (Texture2D texture in _textures)
                if (texture != null && !string.IsNullOrWhiteSpace(texture.name)) keys.Add(texture.name);
            return keys;
        }
    }
}
