Shader "Aetherin/Primitive 3D Unlit"
{
    Properties
    {
        [HDR] _BaseColor ("Color A", Color) = (1, 1, 1, 1)
        [HDR] _ColorB ("Color B", Color) = (0.5, 0.5, 0.5, 1)
        _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _UseTexture ("Use Texture", Float) = 0
        [HideInInspector] _TextureTransform ("Texture Transform", Vector) = (1,1,0,0)
        [HideInInspector] _StageTime ("Stage Time", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _BlendOp ("Blend Op", Float) = 0
        [HideInInspector] _InvertBlend ("Invert Blend", Float) = 0
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

            BlendOp [_BlendOp]
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
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Includes/AetherinNoise.hlsl"
            #include "Includes/Primitive3DVertexNoise.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

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
                float _InvertBlend;
                float4 _UvParams;
                float4 _LightDirection;
                float _ColorMode;
                float _ToonThreshold;
                float _Metallic;
                float _Smoothness;
                float _UseTexture;
                float4 _TextureTransform;
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
                float _ReflectionSource;
                half4 _SolidReflectionColor;
                float _VertexNoiseEnabled;
                float _VertexNoiseType;
                float _VertexNoiseAmount;
                float4 _VertexNoiseFrequency;
                float4 _VertexNoiseSpeed;
                float4 _VertexNoiseOffset;
                float _VertexNoiseDirection;
                float _StageTime;
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
                float3 shapeNormal = normalize(mul((float3x3)_ShapeNormalMatrix, input.normalOS));
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                if (_VertexNoiseEnabled > 0.5)
                {
                    float3 worldAxis = _VertexNoiseDirection < 1.5 ? float3(1,0,0) :
                        _VertexNoiseDirection < 2.5 ? float3(0,1,0) : float3(0,0,1);
                    float3 direction = _VertexNoiseDirection < 0.5 ? shapeNormal :
                        normalize(TransformWorldToObjectDir(worldAxis));
                    AN_ApplyPrimitiveVertexNoise(
                        input.positionOS.xyz, normalize(input.normalOS), shapeNormal, direction,
                        _ShapeMatrix, _VertexNoiseFrequency, _VertexNoiseSpeed, _VertexNoiseOffset,
                        _StageTime, (int)_VertexNoiseType, _VertexNoiseAmount,
                        shapePosition, shapeNormal);
                }
                output.positionCS = TransformObjectToHClip(shapePosition);
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
                float2 textureUv = input.uv * _TextureTransform.xy + _TextureTransform.zw;
                half4 mappedTexture = _UseTexture > 0.5
                    ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, textureUv)
                    : half4(1, 1, 1, 1);

                if (_MaterialMode > 0.5 && _MaterialMode < 1.5)
                {
                    color *= mappedTexture;
                    float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                    float3 normalWS = normalize(input.normalWS);
                    float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);
                    float wave = sin((screenUv.x + _StageTime * 0.07) * _GlassDistortionScale) *
                                 cos((screenUv.y - _StageTime * 0.05) * _GlassDistortionScale * 1.17);
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
                else if (_UsePaletteRandom <= 0.5 && _ColorMode > 4.5)
                {
                    float2 rotatedUv = float2(
                        dot(input.uv, _UvParams.zw),
                        dot(input.uv, float2(-_UvParams.w, _UvParams.z)));
                    float2 patternUv = rotatedUv * max(0.001, _UvParams.x) + _UvParams.y;
                    float pattern = 0.0;
                    if (_ColorMode < 5.5) // Check
                        pattern = fmod(floor(patternUv.x) + floor(patternUv.y), 2.0);
                    else if (_ColorMode < 6.5) // Dots
                        pattern = step(length(frac(patternUv) - 0.5), 0.28);
                    else // Diagonal Stripes
                        pattern = step(0.5, frac(patternUv.x + patternUv.y));
                    color = lerp(_BaseColor, _ColorB, pattern);
                }
                else if (_UsePaletteRandom <= 0.5 && _ColorMode > 1.5)
                {
                    float lighting = saturate(dot(normalize(input.normalWS), normalize(_LightDirection.xyz)));
                    float t = _ColorMode > 2.5 ? step(_ToonThreshold, lighting) : lighting;
                    color = lerp(_BaseColor, _ColorB, t);
                }

                color *= mappedTexture;

                if (_MaterialMode > 2.5)
                {
                    color.a *= input.color.a;
                    if (_InvertBlend > 0.5) color.rgb = color.aaa;
                    return color;
                }

                if (_MaterialMode < 0.5 || _MaterialMode > 1.5)
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
                    bool detailedLit = _MaterialMode > 1.5;
                    surfaceData.metallic = detailedLit ? saturate(_Metallic) : 0;
                    surfaceData.smoothness = detailedLit ? saturate(_Smoothness) : 0.2;
                    surfaceData.normalTS = half3(0, 0, 1);
                    surfaceData.emission = half3(0, 0, 0);
                    surfaceData.occlusion = 1;
                    surfaceData.alpha = color.a * input.color.a;
                    surfaceData.clearCoatMask = 0;
                    surfaceData.clearCoatSmoothness = 0;
                    if (detailedLit && _ReflectionSource > 0.5)
                    {
                        // SampleSH is generated from the scene skybox. Replace that diffuse
                        // environment term as well as the glossy cubemap reflection.
                        inputData.bakedGI = _SolidReflectionColor.rgb;
                        BRDFData brdfData;
                        InitializeBRDFData(surfaceData, brdfData);
                        half3 reflection = GlossyEnvironmentReflection(reflect(-inputData.viewDirectionWS, inputData.normalWS),
                            inputData.positionWS, brdfData.perceptualRoughness, 1.0h, inputData.normalizedScreenSpaceUV);
                        half fresnel = Pow4(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)));
                        surfaceData.emission += EnvironmentBRDFSpecular(brdfData, fresnel) * (_SolidReflectionColor.rgb - reflection);
                    }
                    half4 outputColor = UniversalFragmentPBR(inputData, surfaceData);
                    if (_InvertBlend > 0.5) outputColor.rgb = outputColor.aaa;
                    return outputColor;
                }

                color.a *= input.color.a;
                if (_InvertBlend > 0.5) color.rgb = color.aaa;
                return color;
            }
            ENDHLSL
        }

        // Forward+ SSR reads the normal and smoothness values from the DepthNormals buffer.
        // The surface pass above is a custom shader, so URP cannot provide this pass for us.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Back
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            #include "Includes/AetherinNoise.hlsl"
            #include "Includes/Primitive3DVertexNoise.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ShapeMatrix;
                float4x4 _ShapeNormalMatrix;
                float _MaterialMode;
                float _Smoothness;
                float _UseTexture;
                float4 _TextureTransform;
                float _VertexNoiseEnabled;
                float _VertexNoiseType;
                float _VertexNoiseAmount;
                float4 _VertexNoiseFrequency;
                float4 _VertexNoiseSpeed;
                float4 _VertexNoiseOffset;
                float _VertexNoiseDirection;
                float _StageTime;
            CBUFFER_END

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                float3 shapeNormal = normalize(mul((float3x3)_ShapeNormalMatrix, input.normalOS));
                float3 shapePosition = mul(_ShapeMatrix, input.positionOS).xyz;
                if (_VertexNoiseEnabled > 0.5)
                {
                    float3 worldAxis = _VertexNoiseDirection < 1.5 ? float3(1,0,0) :
                        _VertexNoiseDirection < 2.5 ? float3(0,1,0) : float3(0,0,1);
                    float3 direction = _VertexNoiseDirection < 0.5 ? shapeNormal :
                        normalize(TransformWorldToObjectDir(worldAxis));
                    AN_ApplyPrimitiveVertexNoise(
                        input.positionOS.xyz, normalize(input.normalOS), shapeNormal, direction,
                        _ShapeMatrix, _VertexNoiseFrequency, _VertexNoiseSpeed, _VertexNoiseOffset,
                        _StageTime, (int)_VertexNoiseType, _VertexNoiseAmount,
                        shapePosition, shapeNormal);
                }
                output.positionCS = TransformObjectToHClip(shapePosition);
                output.normalWS = TransformObjectToWorldNormal(shapeNormal);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                // Only the custom Lit mode should contribute to SSR's normal buffer.
                if (_MaterialMode <= 1.5) discard;

                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half4 output;
#if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                output = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), _Smoothness);
#else
                output = half4(normalWS, _Smoothness);
#endif
                return output;
            }
            ENDHLSL
        }
    }
}
