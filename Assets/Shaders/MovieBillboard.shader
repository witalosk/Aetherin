Shader "Aetherin/Movie Billboard"
{
    Properties
    {
        _MainTex ("Movie", 2D) = "black" {}
        _Color ("Color", Color) = (1,1,1,1)
        [NoScaleOffset] _LutTex ("LUT", 2D) = "gray" {}
        [HideInInspector] _LutParams ("LUT Params", Vector) = (0,0,0,0)
        [HideInInspector] _LutEnabled ("LUT Enabled", Float) = 0
        [HideInInspector] _LutIntensity ("LUT Intensity", Range(0,1)) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _BlendOp ("Blend Op", Float) = 0
        [HideInInspector] _InvertBlend ("Invert Blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_LutTex); SAMPLER(sampler_LutTex);
            float4 _Color;
            float4 _LutParams;
            float _LutEnabled;
            float _LutIntensity;
            float _InvertBlend;

            half3 ApplyLut(half3 color)
            {
                float size = _LutParams.x;
                float blue = saturate(color.b) * (size - 1.0);
                float slice0 = floor(blue);
                float slice1 = min(slice0 + 1.0, size - 1.0);
                float2 texel = _LutParams.zw;
                float2 uv0;
                float2 uv1;

                if (_LutParams.y < 0.5)
                {
                    uv0 = float2((slice0 * size + saturate(color.r) * (size - 1.0) + 0.5) * texel.x,
                        (saturate(color.g) * (size - 1.0) + 0.5) * texel.y);
                    uv1 = float2((slice1 * size + saturate(color.r) * (size - 1.0) + 0.5) * texel.x,
                        uv0.y);
                }
                else
                {
                    uv0 = float2((saturate(color.r) * (size - 1.0) + 0.5) * texel.x,
                        (slice0 * size + saturate(color.g) * (size - 1.0) + 0.5) * texel.y);
                    uv1 = float2(uv0.x,
                        (slice1 * size + saturate(color.g) * (size - 1.0) + 0.5) * texel.y);
                }

                half3 color0 = SAMPLE_TEXTURE2D(_LutTex, sampler_LutTex, uv0).rgb;
                half3 color1 = SAMPLE_TEXTURE2D(_LutTex, sampler_LutTex, uv1).rgb;
                return lerp(color0, color1, frac(blue));
            }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                if (_LutEnabled > 0.5)
                    color.rgb = lerp(color.rgb, ApplyLut(color.rgb), saturate(_LutIntensity));
                if (_InvertBlend > 0.5) color.rgb = color.aaa;
                return color;
            }
            ENDHLSL
        }
    }
}
