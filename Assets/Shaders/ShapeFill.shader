Shader "Aetherin/Shape Fill"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Gradient Color B", Color) = (1, 1, 1, 1)
        // xy: グラデーションの向き / z: オフセット / w: 横切る幅
        _GradientParams ("Gradient Params", Vector) = (1, 0, 0, 2)
        [Toggle] _UseGradient ("Use Gradient", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
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

            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ColorB;
                float4 _GradientParams;
                float _UseGradient;
                float _UsePaletteRandom;
                float _PaletteRandomSeed;
                half4 _PaletteColor0;
                half4 _PaletteColor1;
                half4 _PaletteColor2;
                half4 _PaletteColor3;
                half4 _PaletteColor4;
                half4 _PaletteColor5;
                float4x4 _ShapeMatrix;
                float4x4 _ShapeNormalMatrix;
                float _MaterialMode;
                float _Metallic;
                float _Smoothness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                output.positionCS = TransformObjectToHClip(shapePosition);
                output.color = input.color;
                output.shapePositionXY = shapePosition.xy;
                output.positionWS = TransformObjectToWorld(shapePosition);
                float3 shapeNormal = normalize(mul((float3x3)_ShapeNormalMatrix, float3(0.0, 0.0, 1.0)));
                output.normalWS = TransformObjectToWorldNormal(shapeNormal);
                return output;
            }

            int PaletteIndexForCopy(float copyValue, float seedValue)
            {
                int copyIndex = max(0, (int)round(copyValue));
                int seed = abs((int)round(seedValue));
                int start = (seed * 5 + 3) % 6;
                int step = ((seed / 6) % 2) == 0 ? 1 : 5;
                return (start + copyIndex * step) % 6;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;

                if (_UsePaletteRandom > 0.5)
                {
                    // 6コピーまでは重複させず、Seedで開始位置と巡回方向を変える。
                    int paletteIndex = PaletteIndexForCopy(input.color.r, _PaletteRandomSeed);
                    color = paletteIndex == 0 ? _PaletteColor0 :
                            paletteIndex == 1 ? _PaletteColor1 :
                            paletteIndex == 2 ? _PaletteColor2 :
                            paletteIndex == 3 ? _PaletteColor3 :
                            paletteIndex == 4 ? _PaletteColor4 : _PaletteColor5;
                }

                else if (_UseGradient > 0.5)
                {
                    // シェイプ空間で、向きベクトルへの射影を0-1に正規化して混ぜる
                    float projection = dot(input.shapePositionXY, _GradientParams.xy) - _GradientParams.z;
                    float t = saturate(projection / _GradientParams.w + 0.5);
                    color = lerp(_BaseColor, _ColorB, t);
                }

                color.a *= input.color.a;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.vertexLighting = VertexLighting(input.positionWS, inputData.normalWS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                bool detailedLit = _MaterialMode > 0.5;
                surfaceData.albedo = color.rgb;
                surfaceData.metallic = detailedLit ? saturate(_Metallic) : 0;
                surfaceData.smoothness = detailedLit ? saturate(_Smoothness) : 0.2;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.alpha = color.a;
                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        // SSR in Forward+ obtains the surface normal and smoothness from the
        // DepthNormals texture. Shape geometry is generated at runtime, so it
        // needs an explicit pass instead of relying on URP's fallback.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ShapeMatrix;
                float4x4 _ShapeNormalMatrix;
            CBUFFER_END

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                output.positionCS = TransformObjectToHClip(shapePosition);
                float3 shapeNormal = normalize(mul((float3x3)_ShapeNormalMatrix, float3(0.0, 0.0, 1.0)));
                output.normalWS = TransformObjectToWorldNormal(shapeNormal);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half4 output;
#if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                output = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 1.0);
#else
                output = half4(normalWS, 1.0);
#endif
                return output;
            }
            ENDHLSL
        }
    }
}
