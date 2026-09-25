using System;
using System.Collections.Generic;
using UnityEngine;
using UnityRuntimeShader;

namespace Aetherin
{
    // Renderer objects stay active outside Stage hierarchies so ShaderRenderer's
    // end-of-frame coroutine survives Current/Next deactivation and promotion.
    internal static class RuntimeShaderRendererPool
    {
        internal sealed class Entry
        {
            internal ShaderRenderer Renderer;
            internal string Code;
            internal string Error;
            internal bool Compiled;
            internal bool HasCompileResult;
            internal bool Leased;
            internal int AvailableFrame;
        }

        private const int MaxIdleRenderers = 8;
        private static readonly List<Entry> Entries = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (Entry entry in Entries)
            {
                if (entry.Renderer == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(entry.Renderer.gameObject);
                else UnityEngine.Object.DestroyImmediate(entry.Renderer.gameObject);
            }
            Entries.Clear();
        }

        internal static Entry Rent(string code)
        {
            Entries.RemoveAll(entry => entry.Renderer == null);

            Entry selected = null;
            int idleCount = 0;
            foreach (Entry entry in Entries)
            {
                if (entry.Leased || entry.AvailableFrame > Time.frameCount) continue;
                idleCount++;
                if (entry.HasCompileResult && string.Equals(entry.Code, code, StringComparison.Ordinal))
                {
                    selected = entry;
                    break;
                }
            }

            if (selected == null && idleCount >= MaxIdleRenderers)
            {
                foreach (Entry entry in Entries)
                {
                    if (!entry.Leased && entry.AvailableFrame <= Time.frameCount)
                    {
                        selected = entry;
                        break;
                    }
                }
            }

            if (selected == null)
            {
                var owner = new GameObject("Runtime Shader Renderer")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                UnityEngine.Object.DontDestroyOnLoad(owner);
                ShaderRenderer renderer = owner.AddComponent<ShaderRenderer>();
                renderer.enabled = false;
                renderer.RenderEveryFrame = false;
                selected = new Entry { Renderer = renderer };
                Entries.Add(selected);
            }

            if (!string.Equals(selected.Code, code, StringComparison.Ordinal))
            {
                selected.Code = code;
                selected.Error = null;
                selected.Compiled = false;
                selected.HasCompileResult = false;
            }
            selected.Leased = true;
            return selected;
        }

        internal static void Return(Entry entry)
        {
            if (entry == null || !entry.Leased) return;
            entry.Leased = false;
            entry.AvailableFrame = Time.frameCount + 1;
            if (entry.Renderer == null) return;
            entry.Renderer.RenderEveryFrame = false;
            entry.Renderer.enabled = false;

            int idleCount = 0;
            foreach (Entry candidate in Entries)
                if (!candidate.Leased) idleCount++;
            if (idleCount <= MaxIdleRenderers) return;

            foreach (Entry candidate in Entries)
            {
                if (candidate.Leased || ReferenceEquals(candidate, entry)) continue;
                Entries.Remove(candidate);
                if (candidate.Renderer != null)
                    UnityEngine.Object.Destroy(candidate.Renderer.gameObject);
                break;
            }
        }
    }
}
