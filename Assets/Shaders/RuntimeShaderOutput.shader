Shader "Hidden/Aetherin/Runtime Shader Output"
{
    Properties
    {
        _MainTex ("Runtime Output", 2D) = "black" {}
        _AetherinOpacity ("Opacity", Range(0, 1)) = 1
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _AetherinOpacity;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                color.a *= _AetherinOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
