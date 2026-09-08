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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            float4 _VolumeColor;
            float _Density, _VolumeIntensity, _NoiseAmount;
            int _Steps;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
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
                        float noise = lerp(1.0 - _NoiseAmount, 1.0, Hash(floor(p * 14.0) + _Time.y));
                        accumulated += noise * stepLength;
                    }
                }
                float alpha = (1.0 - exp(-accumulated * _Density * 5.0)) * _VolumeColor.a;
                return half4(_VolumeColor.rgb * _VolumeIntensity, alpha);
            }
            ENDHLSL
        }
    }
}
