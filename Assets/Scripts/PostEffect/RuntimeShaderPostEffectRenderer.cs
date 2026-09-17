using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityRuntimeShader;

namespace Aetherin
{
    internal sealed class RuntimeShaderPostEffectRenderer : IDisposable
    {
        public const string DefaultShaderCode = @"cbuffer AetherinGlobals : register(b0)
{
    float4 _Time;       // time, deltaTime, sin(time), cos(time)
    float4 _Frame;      // frame, timeScale, unscaledTime, unscaledDeltaTime
    float4 _Resolution; // width, height, 1/width, 1/height
    float4 _Audio;      // volume, kick, snare/clap, audio trigger
    float4 _Beat;       // phase, count, beat in bar, beat trigger
    float4 _Bar;        // phase, count, beats per bar, bar trigger
    float4 _BackgroundColor1;
    float4 _BackgroundColor2;
    float4 _AccentColor1;
    float4 _AccentColor2;
    float4 _SubAccentColor1;
    float4 _SubAccentColor2;
    float4 _UserFloat;  // UserFloat 0..3
    float4 _UserVector0;
    float4 _UserVector1;
};

Texture2D _SourceTexture : register(t0);
Texture2D _WaveformTexture : register(t1);
Texture2D _SpectrumTexture : register(t2);
Texture2D _PreviousFrameTexture : register(t3);
SamplerState _SourceTextureSampler : register(s0);
SamplerState _WaveformTextureSampler : register(s1);
SamplerState _SpectrumTextureSampler : register(s2);
SamplerState _PreviousFrameTextureSampler : register(s3);

float4 Frag(VsOutput input) : SV_TARGET
{
    return _SourceTexture.Sample(_SourceTextureSampler, input.uv);
}";

        private readonly GameObject _gameObject;
        private readonly ShaderRenderer _renderer;
        private RenderTexture _source;
        private RenderTexture _output;
        private RenderTexture _previousFrame;
        private RenderTexture _waveform;
        private int _width;
        private int _height;
        private int _shaderCodeHash;
        private bool _compileAttempted;
        private bool _compiled;

        public RuntimeShaderPostEffectRenderer(Transform parent)
        {
            _gameObject = new GameObject("Runtime Shader Post Effect")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (parent != null) _gameObject.transform.SetParent(parent, false);
            _renderer = _gameObject.AddComponent<ShaderRenderer>();
            _renderer.RenderEveryFrame = true;
        }

        public Texture Process(Texture input, PostEffectModule module, in ModulationContext context,
            IAudioFeatureProvider audio, IBeatManager beat, ColorPalette palette)
        {
            if (input == null || module == null || !Application.isPlaying) return input;
            EnsureTextures(input.width, input.height, input);
            CompileIfNeeded(module);
            if (!_compiled) return input;

            if (module.RuntimeProvidePreviousFrameTexture)
                Graphics.Blit(_output, _previousFrame);
            Graphics.Blit(input, _source);

            _renderer.SetConstantBuffer(0, CreateConstantBuffer(
                module, context, audio, beat, palette, _width, _height));
            _renderer.SetTexture(0, _source);
            _renderer.SetTexture(1, GetWaveformTexture(audio));
            _renderer.SetTexture(2, audio?.SpectrumTexture ?? Texture2D.blackTexture);
            _renderer.SetTexture(3, module.RuntimeProvidePreviousFrameTexture
                ? _previousFrame
                : Texture2D.blackTexture);
            return _output;
        }

        private void CompileIfNeeded(PostEffectModule module)
        {
            string code = module.RuntimeShaderCode ?? DefaultShaderCode;
            int hash = code.GetHashCode();
            if (_compileAttempted && hash == _shaderCodeHash) return;

            _shaderCodeHash = hash;
            _compileAttempted = true;
            module.RuntimeCompileMessage = "Compiling...";
            _compiled = _renderer.CompileShaderFromString(code, out string error);
            module.RuntimeLastCompileSucceeded = _compiled;
            module.RuntimeCompileMessage = _compiled ? "Compiled" : error ?? "Unknown shader compilation error";
            if (!_compiled)
                Debug.LogError($"[RuntimeShaderPostEffect] Shader compilation failed: {error}");
        }

        private void EnsureTextures(int width, int height, Texture initialSource)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (_output != null && _width == width && _height == height) return;

            Release(_source);
            Release(_output);
            Release(_previousFrame);
            _width = width;
            _height = height;
            _source = Create(width, height, "Runtime Post FX Source");
            _output = Create(width, height, "Runtime Post FX Output");
            _previousFrame = Create(width, height, "Runtime Post FX Previous Frame");
            Graphics.Blit(initialSource, _output);
            Graphics.Blit(initialSource, _previousFrame);
            _renderer.TargetTexture = _output;
        }

        private static RenderTexture Create(int width, int height, string name)
        {
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            return texture;
        }

        private Texture GetWaveformTexture(IAudioFeatureProvider audio)
        {
            Texture source = audio?.WaveformTexture;
            if (source == null) return Texture2D.blackTexture;
            if (source is RenderTexture renderTexture) return renderTexture;

            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            if (_waveform == null || _waveform.width != width || _waveform.height != height)
            {
                Release(_waveform);
                _waveform = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
                {
                    name = "Runtime Post FX Waveform",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                _waveform.Create();
            }
            Graphics.Blit(source, _waveform);
            return _waveform;
        }

        private static RuntimeShaderConstant CreateConstantBuffer(PostEffectModule module,
            in ModulationContext context, IAudioFeatureProvider audio, IBeatManager beat,
            ColorPalette palette, int width, int height)
        {
            palette ??= PaletteColorParameter.FallbackPalette;
            float time = (float)context.ElapsedTime;
            return new RuntimeShaderConstant
            {
                Time = new Vector4(time, Time.unscaledDeltaTime, Mathf.Sin(time), Mathf.Cos(time)),
                Frame = new Vector4(Time.frameCount, Time.timeScale, Time.unscaledTime, Time.unscaledDeltaTime),
                Resolution = new Vector4(width, height, 1f / width, 1f / height),
                Audio = new Vector4(audio?.InputVolume ?? 0f, audio?.Kick ?? 0f, audio?.SnareClap ?? 0f,
                    audio?.WasKick == true || audio?.WasSnareClap == true ? 1f : 0f),
                Beat = new Vector4(beat?.BeatPhase ?? 1f, beat?.BeatCount ?? 0, beat?.BeatInBar ?? 0,
                    beat?.WasBeat == true ? 1f : 0f),
                Bar = new Vector4(beat?.BarPhase ?? 1f, beat?.BarCount ?? 0, beat?.BeatsPerBar ?? 4,
                    beat?.WasBar == true ? 1f : 0f),
                BackgroundColor1 = palette.BackgroundColor1.linear,
                BackgroundColor2 = palette.BackgroundColor2.linear,
                AccentColor1 = palette.AccentColor1.linear,
                AccentColor2 = palette.AccentColor2.linear,
                SubAccentColor1 = palette.SubAccentColor1.linear,
                SubAccentColor2 = palette.SubAccentColor2.linear,
                UserFloat = new Vector4(
                    module.RuntimeUserFloat0.Evaluate(context), module.RuntimeUserFloat1.Evaluate(context),
                    module.RuntimeUserFloat2.Evaluate(context), module.RuntimeUserFloat3.Evaluate(context)),
                UserVector0 = module.RuntimeUserVector0.Evaluate(context),
                UserVector1 = module.RuntimeUserVector1.Evaluate(context),
            };
        }

        public void Dispose()
        {
            Release(_source);
            Release(_output);
            Release(_previousFrame);
            Release(_waveform);
            if (_gameObject != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_gameObject);
                else UnityEngine.Object.DestroyImmediate(_gameObject);
            }
        }

        private static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RuntimeShaderConstant
        {
            public Vector4 Time;
            public Vector4 Frame;
            public Vector4 Resolution;
            public Vector4 Audio;
            public Vector4 Beat;
            public Vector4 Bar;
            public Vector4 BackgroundColor1;
            public Vector4 BackgroundColor2;
            public Vector4 AccentColor1;
            public Vector4 AccentColor2;
            public Vector4 SubAccentColor1;
            public Vector4 SubAccentColor2;
            public Vector4 UserFloat;
            public Vector4 UserVector0;
            public Vector4 UserVector1;
        }
    }
}
