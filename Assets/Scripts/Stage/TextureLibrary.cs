using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Aetherin
{
    /// <summary>レイヤーで利用するテクスチャをキーで管理するシーンライブラリ。</summary>
    [DisallowMultipleComponent]
    public sealed class TextureLibrary : MonoBehaviour
    {
        // SpriteSheetLibrary からのリネーム前に登録されたテクスチャを保持する。
        [FormerlySerializedAs("_spriteSheets")]
        [SerializeField] private List<TextureLibraryEntry> _textures = new();

        public Texture2D Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _textures?.Find(entry => entry != null && entry.Key == key)?.Texture;
        }

        public IReadOnlyList<string> GetKeys()
        {
            if (_textures == null) return Array.Empty<string>();
            var keys = new List<string>(_textures.Count);
            foreach (var entry in _textures)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Key) && entry.Texture != null) keys.Add(entry.Key);
            return keys;
        }
    }
}
