using System;
using UnityEngine;

namespace Aetherin
{
    [Serializable]
    public sealed class RuntimeShaderLayerParams : StageLayerParams
    {
        public const string DefaultShaderCode = @"cbuffer AetherinGlobals : register(b0)
{
    float4 AetherinTime;       // time, deltaTime, sin(time), cos(time)
    float4 AetherinFrame;      // frame, timeScale, unscaledTime, unscaledDeltaTime
    float4 AetherinResolution; // width, height, 1/width, 1/height
    float4 AetherinAudio;      // volume, kick, snare/clap, audio trigger
    float4 AetherinBeat;       // phase, count, beat in bar, beat trigger
    float4 AetherinBar;        // phase, count, beats per bar, bar trigger
    float4 AetherinUserFloat;  // UserFloat 0..3
    float4 AetherinUserVector0;
    float4 AetherinUserVector1;
    float4 AetherinUserVector2;
    float4 AetherinUserVector3;
};

float4 Frag(VsOutput input) : SV_TARGET
{
    float pulse = pow(1.0 - saturate(AetherinBeat.x), 3.0);
    return float4(input.uv, 0.5 + 0.5 * sin(AetherinTime.x), 1.0) + pulse * 0.25;
}";

        [NonSerialized] public Shader Shader;
        public string ShaderName = "Aetherin/Runtime Shader Layer Example";
        [TextArea(12, 40)] public string ShaderCode = DefaultShaderCode;

        [NonSerialized] public Action CompileRequested;
        [NonSerialized] public string CompileMessage = "Not compiled";
        [NonSerialized] public bool LastCompileSucceeded;

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
        public Vector3Parameter UserVector2 = new();
        public Vector3Parameter UserVector3 = new();

        public void EnsureInitialized()
        {
            Opacity ??= new FloatParameter(1f);
            ShaderName ??= string.Empty;
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
            UserVector2 ??= new Vector3Parameter();
            UserVector3 ??= new Vector3Parameter();
        }
    }
}
