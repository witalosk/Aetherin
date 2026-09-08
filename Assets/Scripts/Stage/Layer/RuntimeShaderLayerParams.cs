using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class RuntimeShaderLayerParams : StageLayerParams
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

Texture2D _WaveformTexture : register(t0);
Texture2D _SpectrumTexture : register(t1);
SamplerState _WaveformTextureSampler : register(s0);
SamplerState _SpectrumTextureSampler : register(s1);

float4 Frag(VsOutput input) : SV_TARGET
{
    float pulse = pow(1.0 - saturate(_Beat.x), 3.0);
    pulse += _WaveformTexture.Sample(_WaveformTextureSampler, input.uv).r;
    return float4(input.uv, 0.5 + 0.5 * sin(_Time.x), 1.0) + pulse * 0.25;
}";
        
        public bool ScreenSpace;
        [Tooltip("Runtime Texture の解像度。幅・高さは Size × Pixel Per Unit で決まります")]
        [Min(1f)] public float PixelPerUnit = 125f;
        public Vector3Parameter Position = new();
        public Vector3Parameter Rotation = new();
        public Vector3Parameter Scale = new(Vector3.one);
        public Vector3Parameter Anchor = new();
        public Vector2Parameter Size = new(new Vector2(2f, 2f));

        public FloatParameter UserFloat0 = new(1f);
        public FloatParameter UserFloat1 = new(0f);
        public FloatParameter UserFloat2 = new(0f);
        public FloatParameter UserFloat3 = new(0f);
        public Vector3Parameter UserVector0 = new();
        public Vector3Parameter UserVector1 = new();
        
        [TextArea(12, 40)] public string ShaderCode = DefaultShaderCode;

        [NonSerialized] public string CompileMessage = "Play Modeでコンパイルされます";
        [NonSerialized] public bool LastCompileSucceeded;
        [NonSerialized] public int TextureRebuildRevision;

        public void RequestTextureRebuild() => TextureRebuildRevision++;

        public void EnsureInitialized()
        {
            Opacity ??= new FloatParameter(1f);
            ShaderCode ??= DefaultShaderCode;
            Position ??= new Vector3Parameter();
            Rotation ??= new Vector3Parameter();
            Scale ??= new Vector3Parameter(Vector3.one);
            Anchor ??= new Vector3Parameter();
            Size ??= new Vector2Parameter(new Vector2(2f, 2f));
            UserFloat0 ??= new FloatParameter(1f);
            UserFloat1 ??= new FloatParameter();
            UserFloat2 ??= new FloatParameter();
            UserFloat3 ??= new FloatParameter();
            UserVector0 ??= new Vector3Parameter();
            UserVector1 ??= new Vector3Parameter();
            CompileMessage ??= string.Empty;
        }
    }
}
