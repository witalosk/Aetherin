Shader "Aetherin/Primitive 3D Unlit"
{
    Properties
    {
        [HDR] _BaseColor ("Color A", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Color B", Color) = (0.5, 0.5, 0.5, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Primitive3DUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ColorB;
                float4 _UvParams;
                float4 _LightDirection;
                float _ColorMode;
                float _ToonThreshold;
                float4x4 _ShapeMatrix;
                float4x4 _ShapeNormalMatrix;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                output.positionCS = TransformObjectToHClip(shapePosition);
                float3 shapeNormal = normalize(mul((float3x3)_ShapeNormalMatrix, input.normalOS));
                output.normalWS = TransformObjectToWorldNormal(shapeNormal);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;

                if (_ColorMode > 0.5 && _ColorMode < 1.5)
                {
                    float t = saturate(input.uv.x * _UvParams.x + _UvParams.y);
                    color = lerp(_BaseColor, _ColorB, t);
                }
                else if (_ColorMode > 1.5)
                {
                    float lighting = saturate(dot(normalize(input.normalWS), normalize(_LightDirection.xyz)));
                    float t = _ColorMode > 2.5 ? step(_ToonThreshold, lighting) : lighting;
                    color = lerp(_BaseColor, _ColorB, t);
                }

                return color * input.color;
            }
            ENDHLSL
        }
    }
}
