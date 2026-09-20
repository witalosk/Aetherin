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
        [HideInInspector] _BlendOp ("Blend Op", Float) = 0
        [HideInInspector] _InvertBlend ("Invert Blend", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
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
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float opacity : TEXCOORD1; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Color; float4 _UvRect; float _ColorMode; float _AlphaClip; float _InvertBlend;
            float _AnimationRow; float _RowCount; float _RepeaterRowIncrement;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float rowCount = max(1.0, round(_RowCount));
                float row = _AnimationRow + round(input.color.r) * round(_RepeaterRowIncrement);
                row = fmod(fmod(row, rowCount) + rowCount, rowCount);
                output.uv = float2(
                    input.uv.x * _UvRect.x + _UvRect.z,
                    input.uv.y * _UvRect.y + (rowCount - 1.0 - row) / rowCount);
                output.opacity = input.color.a;
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
                color.a *= input.opacity;
                if (_AlphaClip > .5) clip(color.a - .1);
                if (_InvertBlend > 0.5) color.rgb = color.aaa;
                return color;
            }
            ENDHLSL
        }
    }
}
