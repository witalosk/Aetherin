Shader "Aetherin/Model Layer Wire"
{
    Properties
    {
        _ColorA("Color", Color) = (1,1,1,1)
        [HideInInspector] _SrcBlend("Src Blend", Float) = 5
        [HideInInspector] _DstBlend("Dst Blend", Float) = 10
        [HideInInspector] _ZWrite("Z Write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            float4 _ColorA;
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return _ColorA; }
            ENDHLSL
        }
    }
}
