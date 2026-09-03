Shader "Aetherin/Model Layer Surface"
{
    Properties
    {
        _ColorA("Color A", Color) = (1,1,1,1)
        _ColorB("Color B", Color) = (1,1,1,1)
        _UseGradient("Use Gradient", Float) = 0
        _GradientParams("Gradient", Vector) = (0,0,2,0)
        [HideInInspector] _SrcBlend("Src Blend", Float) = 1
        [HideInInspector] _DstBlend("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite("Z Write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
            float4 _ColorA, _ColorB, _GradientParams;
            float _UseGradient;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float angle = radians(_GradientParams.x);
                float axis = dot(i.positionOS.xy, float2(cos(angle), sin(angle)));
                float t = saturate(axis / max(0.0001, _GradientParams.z) + 0.5 + _GradientParams.y);
                half4 color = lerp(_ColorA, _ColorB, t * _UseGradient);
                float light = 0.35 + 0.65 * saturate(dot(normalize(i.normalWS), normalize(float3(0.3,0.8,-0.5))));
                color.rgb *= light;
                return color;
            }
            ENDHLSL
        }
    }
}
