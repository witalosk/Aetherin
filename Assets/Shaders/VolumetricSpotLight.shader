Shader "Aetherin/Volumetric Spot Light"
{
    Properties { _VolumeColor("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            // The cone's exit face is usually behind the opaque surface hit by
            // the beam. Always run the fragment and clamp the march to depth.
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/AetherinNoise.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _VolumeColor;
            float _Density, _VolumeIntensity, _NoiseAmount;
            float4 _VolumetricLightPositionWS;
            float _VolumetricLightRange;
            float _VolumetricShadowsEnabled;
            int _Steps;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half SampleVolumetricShadow(float3 positionWS)
            {
#if (defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)) && defined(_ADDITIONAL_LIGHT_SHADOWS)
                if (_VolumetricShadowsEnabled < 0.5) return 1.0;

                float3 toThisLight = _VolumetricLightPositionWS.xyz - positionWS;
                toThisLight *= rsqrt(max(dot(toThisLight, toThisLight), 1e-6));
                uint lightCount = GetAdditionalLightsCount();
                int matchingLightIndex = -1;

                [loop] for (uint i = 0; i < lightCount; i++)
                {
                    Light candidate = GetAdditionalLight(i, positionWS);
                    // Match this layer's additional light by its direction at the
                    // sample point; the matching index owns the shadow map entry.
                    if (dot(candidate.direction, toThisLight) > 0.9999)
                    {
                        matchingLightIndex = GetPerObjectLightIndex(i);
                        break;
                    }
                }

                if (matchingLightIndex >= 0)
                    return AdditionalLightRealtimeShadow(matchingLightIndex, positionWS, toThisLight);
#endif
                return 1.0;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float3 originOS = TransformWorldToObject(GetCameraPositionWS());
                float3 exitOS = TransformWorldToObject(input.positionWS);
                float3 ray = exitOS - originOS;
                float lengthToExit = length(ray);
                if (lengthToExit < 0.0001) discard;
                ray /= lengthToExit;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
#if UNITY_REVERSED_Z
                bool hasSceneDepth = rawDepth > 0.0001;
#else
                bool hasSceneDepth = rawDepth < 0.9999;
#endif
                if (hasSceneDepth)
                {
                    float3 sceneWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                    float3 sceneOS = TransformWorldToObject(sceneWS);
                    float lengthToScene = dot(sceneOS - originOS, ray);
                    lengthToExit = min(lengthToExit, max(0.0, lengthToScene));
                }
                if (lengthToExit < 0.0001) discard;

                float accumulated = 0;
                int steps = clamp(_Steps, 4, 64);
                float stepLength = lengthToExit / steps;
                [loop] for (int i = 0; i < 64; i++)
                {
                    if (i >= steps) break;
                    float3 p = originOS + ray * ((i + 0.5) * stepLength);
                    float radius = p.z;
                    if (p.z > 0 && p.z < 1 && dot(p.xy, p.xy) < radius * radius)
                    {
                        // Sample in world space so the noise reads as smoke fixed
                        // in the scene rather than a pattern attached to the cone.
                        float3 noisePositionWS = TransformObjectToWorld(p);
                        float fractalNoise = AN_FractalNoise(noisePositionWS * 0.1 + float3(0.0, 0.0, _Time.y * 0.1));
                        float noise = lerp(1.0 - _NoiseAmount, 1.0, saturate(fractalNoise));
                        float distanceToLight = distance(noisePositionWS, _VolumetricLightPositionWS.xyz);
                        float rangeFalloff = saturate(1.0 - distanceToLight / max(_VolumetricLightRange, 0.001));
                        accumulated += noise * rangeFalloff * rangeFalloff * SampleVolumetricShadow(noisePositionWS) * stepLength;
                    }
                }
                float alpha = (1.0 - exp(-accumulated * _Density * 5.0)) * _VolumeColor.a;
                return half4(_VolumeColor.rgb * _VolumeIntensity, alpha);
            }
            ENDHLSL
        }
    }
}
