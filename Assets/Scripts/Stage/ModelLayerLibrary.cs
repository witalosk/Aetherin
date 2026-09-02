using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// ModelLayerで利用できるモデルをCameraStageとは独立して管理するライブラリ。
    /// シーンに1つ配置し、InspectorからKeyとPrefabを登録する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModelLayerLibrary : MonoBehaviour
    {
        [SerializeField] private List<ModelLayerModelEntry> _models = new();

        public GameObject Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _models?.Find(entry => entry != null && entry.Key == key)?.Model;
        }

        public IReadOnlyList<string> GetKeys()
        {
            if (_models == null) return Array.Empty<string>();
            var keys = new List<string>(_models.Count);
            foreach (var entry in _models)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Key)) keys.Add(entry.Key);
            return keys;
        }
    }
}
