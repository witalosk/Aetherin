using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class TextCollection
    {
        public string Key = "default";
        public List<string> Texts = new() { "Aetherin" };
        public int SelectedIndex;
    }

    [Serializable]
    public sealed class TextManagerParams : IParams
    {
        public List<TextCollection> Collections = new() { new TextCollection() };
    }

    [DisallowMultipleComponent]
    public sealed class TextManager : MonoBehaviour, ITextManager, ISaveAndUiTarget
    {
        [SerializeField] private TextManagerParams _params = new();
        private readonly List<string> _keys = new();
        private readonly Dictionary<string, IReadOnlyList<string>> _textsByKey = new();
        private int _cacheHash;

        public static ITextManager Active { get; private set; }
        public IParams Params => _params;
        public string Category => UiCategory.Settings;
        public IReadOnlyList<string> Keys { get { RefreshCache(); return _keys; } }

        private void Awake()
        {
            _params ??= new TextManagerParams();
            _params.Collections ??= new List<TextCollection>();
            Active = this;
            RefreshCache(true);
        }

        private void OnEnable() => Active = this;
        private void OnDisable() { if (ReferenceEquals(Active, this)) Active = null; }

        public IReadOnlyList<string> GetTexts(string key)
        {
            RefreshCache();
            return !string.IsNullOrWhiteSpace(key) && _textsByKey.TryGetValue(key.Trim(), out var texts)
                ? texts : Array.Empty<string>();
        }

        public string GetSelectedText(string key)
        {
            RefreshCache();
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            string normalizedKey = key.Trim();
            if (!_textsByKey.TryGetValue(normalizedKey, out var texts) || texts.Count == 0)
                return string.Empty;

            foreach (TextCollection collection in _params.Collections)
            {
                if (collection?.Key?.Trim() != normalizedKey) continue;
                int index = Mathf.Clamp(collection.SelectedIndex, 0, texts.Count - 1);
                return texts[index] ?? string.Empty;
            }
            return string.Empty;
        }

        private void RefreshCache(bool force = false)
        {
            _params ??= new TextManagerParams();
            _params.Collections ??= new List<TextCollection>();
            int hash = 17;
            foreach (TextCollection collection in _params.Collections)
            {
                hash = hash * 31 + (collection?.Key?.GetHashCode() ?? 0);
                hash = hash * 31 + (collection?.Texts?.Count ?? 0);
                hash = hash * 31 + (collection?.Texts?.GetHashCode() ?? 0);
            }
            if (!force && hash == _cacheHash) return;

            _cacheHash = hash;
            _keys.Clear();
            _textsByKey.Clear();
            foreach (TextCollection collection in _params.Collections)
            {
                string key = collection?.Key?.Trim();
                if (string.IsNullOrEmpty(key) || _textsByKey.ContainsKey(key)) continue;
                collection.Texts ??= new List<string>();
                _keys.Add(key);
                _textsByKey.Add(key, collection.Texts);
            }
        }
    }
}
