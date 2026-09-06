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
            Name "ModelLayerSurface"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; };
            CBUFFER_START(UnityPerMaterial)
            float4 _ColorA, _ColorB, _GradientParams;
            float _UseGradient;
            float _MaterialMode, _Metallic, _Smoothness;
            float _GlassRefraction, _GlassTint, _GlassFresnelPower, _GlassFresnelIntensity;
            float _GlassChromaticAberration, _GlassDistortion, _GlassDistortionScale;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float angle = radians(_GradientParams.x);
                float axis = dot(i.positionOS.xy, float2(cos(angle), sin(angle)));
                float t = saturate(axis / max(0.0001, _GradientParams.z) + 0.5 + _GradientParams.y);
                half4 color = lerp(_ColorA, _ColorB, t * _UseGradient);
                if (_MaterialMode > 0.5 && _MaterialMode < 1.5)
                {
                    float2 screenUv = GetNormalizedScreenSpaceUV(i.positionCS);
                    float3 normalWS = normalize(i.normalWS);
                    float3 normalVS = mul((float3x3)GetWorldToViewMatrix(), normalWS);
                    float wave = sin((screenUv.x + _Time.y * 0.07) * _GlassDistortionScale) * cos((screenUv.y - _Time.y * 0.05) * _GlassDistortionScale * 1.17);
                    float2 distortion = normalVS.xy * _GlassRefraction + wave * _GlassDistortion;
                    float2 chroma = normalize(distortion + float2(0.00001, 0.00001)) * _GlassChromaticAberration;
                    float3 refracted = float3(SampleSceneColor(screenUv + distortion + chroma).r, SampleSceneColor(screenUv + distortion).g, SampleSceneColor(screenUv + distortion - chroma).b);
                    float fresnel = pow(1.0 - saturate(dot(normalWS, normalize(GetWorldSpaceViewDir(i.positionWS)))), _GlassFresnelPower) * _GlassFresnelIntensity;
                    color.rgb = lerp(refracted, refracted * color.rgb, _GlassTint) + _ColorB.rgb * fresnel;
                    return color;
                }
                if (_MaterialMode > 1.5)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS; inputData.positionCS = i.positionCS;
                    inputData.normalWS = NormalizeNormalPerPixel(i.normalWS); inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS); inputData.vertexLighting = VertexLighting(i.positionWS, inputData.normalWS);
                    inputData.bakedGI = SampleSH(inputData.normalWS); inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); inputData.shadowMask = half4(1, 1, 1, 1);
                    SurfaceData surfaceData = (SurfaceData)0;
                    surfaceData.albedo = color.rgb; surfaceData.metallic = saturate(_Metallic); surfaceData.smoothness = saturate(_Smoothness);
                    surfaceData.normalTS = half3(0, 0, 1); surfaceData.occlusion = 1; surfaceData.alpha = color.a;
                    return UniversalFragmentPBR(inputData, surfaceData);
                }
                float light = 0.35 + 0.65 * saturate(dot(normalize(i.normalWS), normalize(float3(0.3,0.8,-0.5))));
                color.rgb *= light;
                return color;
            }
            ENDHLSL
        }

        // Forward+ SSR traces against the camera depth and this DepthNormals buffer.
        // Without this pass ModelLayer is visible in the main pass but cannot be hit by SSR.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            Cull Back
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

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
                float _MaterialMode;
                float _Smoothness;
            CBUFFER_END

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
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
