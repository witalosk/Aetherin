Shader "Aetherin/Sprite Sheet Billboard"
{
    Properties
    {
        _MainTex ("Sprite Sheet", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [HideInInspector] _ColorMode ("Color Mode", Float) = 0
        [HideInInspector] _UvRect ("UV Rect", Vector) = (1,1,0,0)
        [HideInInspector] _AlphaClip ("Alpha Clip", Float) = 0
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color; float4 _UvRect; float _ColorMode; float _AlphaClip;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _UvRect.xy + _UvRect.zw;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // AccentMaskでは黒を透明、白を指定Accent Colorとして扱う。
                // 輝度をalphaに使うため、グレーのアンチエイリアスも自然に残る。
                half luminance = dot(source.rgb, half3(.2126h, .7152h, .0722h));
                half4 color = _ColorMode > .5h
                    ? half4(_Color.rgb, _Color.a * source.a * luminance)
                    : source * _Color;
                if (_AlphaClip > .5) clip(color.a - .1);
                return color;
            }
            ENDHLSL
        }
    }
}
