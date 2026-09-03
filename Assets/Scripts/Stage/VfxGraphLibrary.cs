using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Aetherin
{
    /// <summary>
    /// GpuParticleLayerで利用できるVFX Graphをキーで管理するライブラリ。
    /// シーンに1つ配置し、InspectorからKeyとVFX Graph Assetを登録する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VfxGraphLibrary : MonoBehaviour
    {
        [SerializeField] private List<VfxGraphLibraryEntry> _vfxGraphs = new();

        public VisualEffectAsset Resolve(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return _vfxGraphs?.Find(entry => entry != null && entry.Key == key)?.VfxGraph;
        }

        public IReadOnlyList<string> GetKeys()
        {
            if (_vfxGraphs == null) return Array.Empty<string>();
            var keys = new List<string>(_vfxGraphs.Count);
            foreach (var entry in _vfxGraphs)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Key)) keys.Add(entry.Key);
            return keys;
        }
    }
}
