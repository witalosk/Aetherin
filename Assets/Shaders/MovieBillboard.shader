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

    // RとGのピクセル内座標 (0.0 ～ size - 1.0)
    float redOffset = saturate(color.r) * (size - 1.0);
    // This LUT library stores G=0 on the top row, while texture UV.y=0 is the bottom row.
    float greenOffset = (1.0 - saturate(color.g)) * (size - 1.0);

    if (_LutParams.y < 0.5) // 横長LUT (Horizontal Strip)
    {
        // 修正点: (ピクセル位置 + 0.5) * texel
        uv0 = float2((slice0 * size + redOffset + 0.5) * texel.x,
                     (greenOffset + 0.5) * texel.y);
        uv1 = float2((slice1 * size + redOffset + 0.5) * texel.x,
                     uv0.y);
    }
    else // 縦長LUT (Vertical Strip)
    {
        uv0 = float2((redOffset + 0.5) * texel.x,
                     (slice0 * size + greenOffset + 0.5) * texel.y);
        uv1 = float2(uv0.x,
                     (slice1 * size + greenOffset + 0.5) * texel.y);
    }

    // The flattened LUT is an atlas. Force mip 0 so screen-space derivatives at movie edges
    // cannot make the sampler bleed into an adjacent blue slice.
    half3 color0 = SAMPLE_TEXTURE2D_LOD(_LutTex, sampler_LutTex, uv0, 0).rgb;
    half3 color1 = SAMPLE_TEXTURE2D_LOD(_LutTex, sampler_LutTex, uv1, 0).rgb;

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
