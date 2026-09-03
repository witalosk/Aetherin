Shader "Aetherin/Primitive 3D Unlit"
{
    Properties
    {
        [HDR] _BaseColor ("Color A", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Color B", Color) = (0.5, 0.5, 0.5, 1)
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
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

            Blend [_SrcBlend] [_DstBlend]
            Cull Back
            ZTest LEqual
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ColorB;
                float4 _UvParams;
                float4 _LightDirection;
                float _ColorMode;
                float _ToonThreshold;
                float _Metallic;
                float _Smoothness;
                float4x4 _ShapeMatrix;
                float4x4 _ShapeNormalMatrix;
                float _UsePaletteRandom;
                float _PaletteRandomSeed;
                float _MaterialMode;
                float _GlassRefraction;
                float _GlassTint;
                float _GlassFresnelPower;
                float _GlassFresnelIntensity;
                float _GlassChromaticAberration;
                float _GlassDistortion;
                float _GlassDistortionScale;
                half4 _PaletteColor0;
                half4 _PaletteColor1;
                half4 _PaletteColor2;
                half4 _PaletteColor3;
                half4 _PaletteColor4;
                half4 _PaletteColor5;
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
                output.positionWS = TransformObjectToWorld(shapePosition);
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

            half4 PaletteColorForCopy(float copyValue)
            {
                int paletteIndex = PaletteIndexForCopy(copyValue, _PaletteRandomSeed);
                return paletteIndex == 0 ? _PaletteColor0 :
                       paletteIndex == 1 ? _PaletteColor1 :
                       paletteIndex == 2 ? _PaletteColor2 :
                       paletteIndex == 3 ? _PaletteColor3 :
                       paletteIndex == 4 ? _PaletteColor4 : _PaletteColor5;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = _BaseColor;
                if (_UsePaletteRandom > 0.5) color = PaletteColorForCopy(input.color.r);

                if (_MaterialMode > 0.5 && _MaterialMode < 1.5)
                {
                    float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                    float3 normalWS = normalize(input.normalWS);
                    float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);
                    float wave = sin((screenUv.x + _Time.y * 0.07) * _GlassDistortionScale) *
                                 cos((screenUv.y - _Time.y * 0.05) * _GlassDistortionScale * 1.17);
                    float2 distortion = normalVS.xy * _GlassRefraction + wave * _GlassDistortion;
                    float2 chroma = normalize(distortion + float2(0.00001, 0.00001)) *
                                    _GlassChromaticAberration;
                    float3 refracted;
                    refracted.r = SampleSceneColor(screenUv + distortion + chroma).r;
                    refracted.g = SampleSceneColor(screenUv + distortion).g;
                    refracted.b = SampleSceneColor(screenUv + distortion - chroma).b;

                    float3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                    float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirection)),
                                        _GlassFresnelPower) * _GlassFresnelIntensity;
                    float3 tinted = lerp(refracted, refracted * color.rgb, _GlassTint);
                    color.rgb = tinted + _ColorB.rgb * fresnel;
                    color.a *= input.color.a;
                    return color;
                }

                if (_UsePaletteRandom <= 0.5 && _ColorMode > 0.5 && _ColorMode < 1.5)
                {
                    float t = saturate(input.uv.x * _UvParams.x + _UvParams.y);
                    color = lerp(_BaseColor, _ColorB, t);
                }
                else if (_UsePaletteRandom <= 0.5 && _ColorMode > 1.5)
                {
                    float lighting = saturate(dot(normalize(input.normalWS), normalize(_LightDirection.xyz)));
                    float t = _ColorMode > 2.5 ? step(_ToonThreshold, lighting) : lighting;
                    color = lerp(_BaseColor, _ColorB, t);
                }

                if (_MaterialMode > 1.5)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.positionCS = input.positionCS;
                    inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    inputData.fogCoord = 0;
                    inputData.vertexLighting = VertexLighting(input.positionWS, inputData.normalWS);
                    inputData.bakedGI = SampleSH(inputData.normalWS);
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    inputData.shadowMask = half4(1, 1, 1, 1);

                    SurfaceData surfaceData = (SurfaceData)0;
                    surfaceData.albedo = color.rgb;
                    surfaceData.specular = half3(0, 0, 0);
                    surfaceData.metallic = saturate(_Metallic);
                    surfaceData.smoothness = saturate(_Smoothness);
                    surfaceData.normalTS = half3(0, 0, 1);
                    surfaceData.emission = half3(0, 0, 0);
                    surfaceData.occlusion = 1;
                    surfaceData.alpha = color.a * input.color.a;
                    surfaceData.clearCoatMask = 0;
                    surfaceData.clearCoatSmoothness = 0;
                    return UniversalFragmentPBR(inputData, surfaceData);
                }

                color.a *= input.color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
