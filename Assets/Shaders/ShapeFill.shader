Shader "Aetherin/Shape Fill"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Gradient Color B", Color) = (1, 1, 1, 1)
        // xy: グラデーションの向き / z: オフセット / w: 横切る幅
        _GradientParams ("Gradient Params", Vector) = (1, 0, 0, 2)
        [Toggle] _UseGradient ("Use Gradient", Float) = 0
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
            Name "ShapeFill"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 shapePositionXY : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ColorB;
                float4 _GradientParams;
                float _UseGradient;
                float4x4 _ShapeMatrix;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                output.positionCS = TransformObjectToHClip(shapePosition);
                output.color = input.color;
                output.shapePositionXY = shapePosition.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;

                if (_UseGradient > 0.5)
                {
                    // シェイプ空間で、向きベクトルへの射影を0-1に正規化して混ぜる
                    float projection = dot(input.shapePositionXY, _GradientParams.xy) - _GradientParams.z;
                    float t = saturate(projection / _GradientParams.w + 0.5);
                    color = lerp(_BaseColor, _ColorB, t);
                }

                return color * input.color;
            }
            ENDHLSL
        }
    }
}
