using System;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// Nextへ直列ポストエフェクトを実行する。
    /// 一時RTと前フレーム履歴を保持し、フレーム中のGCを発生させない。
    /// </summary>
    public sealed class PostEffectManager : IDisposable
    {
        private static readonly int SourceTexId = Shader.PropertyToID("_MainTex");
        private static readonly int HistoryTexId = Shader.PropertyToID("_HistoryTex");
        private static readonly int EffectTypeId = Shader.PropertyToID("_EffectType");
        private static readonly int StrengthId = Shader.PropertyToID("_Strength");
        private static readonly int AmountId = Shader.PropertyToID("_Amount");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int SecondaryId = Shader.PropertyToID("_Secondary");
        private static readonly int TimeValueId = Shader.PropertyToID("_TimeValue");

        private readonly Material _material;
        private readonly StackRuntime _next = new();

        public PostEffectManager(Shader shader)
        {
            if (shader != null) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        public Texture ProcessNext(Texture source, PostEffectStack stack, in ModulationContext context) =>
            Process(source, stack, _next, context);

        private Texture Process(Texture source, PostEffectStack stack, StackRuntime runtime, in ModulationContext context)
        {
            if (source == null || _material == null || stack == null || !stack.Enabled || stack.Modules == null)
                return source;

            float stackStrength = Mathf.Clamp01(stack.Strength?.Evaluate(context) ?? 1f);
            if (context.AllowMidi && stack.FxCc?.IsAssigned == true)
                stackStrength *= stack.FxCc.GetValue(1f);
            if (stackStrength <= 0f) return source;

            int width = source.width;
            int height = source.height;
            runtime.Ensure(width, height);
            Texture input = source;
            bool wroteAny = false;

            foreach (var module in stack.Modules)
            {
                if (module == null || !module.Enabled) continue;
                float strength = stackStrength * Mathf.Clamp01(module.Strength?.Evaluate(context) ?? 1f);
                if (strength <= 0f) continue;

                RenderTexture target = runtime.NextTarget(input);
                _material.SetTexture(SourceTexId, input);
                _material.SetTexture(HistoryTexId, runtime.HistoryValid ? runtime.History : input);
                _material.SetInt(EffectTypeId, (int)module.Type);
                _material.SetFloat(StrengthId, strength);
                _material.SetFloat(AmountId, module.Amount?.Evaluate(context) ?? 0f);
                _material.SetFloat(ScaleId, module.Scale?.Evaluate(context) ?? 1f);
                _material.SetFloat(SpeedId, module.Speed?.Evaluate(context) ?? 1f);
                _material.SetFloat(SecondaryId, module.Secondary?.Evaluate(context) ?? 0f);
                _material.SetFloat(TimeValueId, (float)context.Time);
                Graphics.Blit(input, target, _material);
                input = target;
                wroteAny = true;
            }

            if (wroteAny)
            {
                Graphics.Blit(input, runtime.History);
                runtime.HistoryValid = true;
            }

            return input;
        }

        public void Dispose()
        {
            _next.Dispose();
            if (_material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
        }

        private sealed class StackRuntime : IDisposable
        {
            public RenderTexture History { get; private set; }
            public bool HistoryValid { get; set; }
            private RenderTexture _ping;
            private RenderTexture _pong;

            public void Ensure(int width, int height)
            {
                if (_ping != null && _ping.width == width && _ping.height == height) return;
                Dispose();
                _ping = Create(width, height, "Post FX Ping");
                _pong = Create(width, height, "Post FX Pong");
                History = Create(width, height, "Post FX History");
            }

            public RenderTexture NextTarget(Texture input) => ReferenceEquals(input, _ping) ? _pong : _ping;

            public void Dispose()
            {
                Release(_ping);
                Release(_pong);
                Release(History);
                _ping = null;
                _pong = null;
                History = null;
                HistoryValid = false;
            }

            private static RenderTexture Create(int width, int height, string name)
            {
                var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.Create();
                return texture;
            }

            private static void Release(RenderTexture texture)
            {
                if (texture == null) return;
                texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
