Shader "Aetherin/GPU Particle"
{
    Properties
    {
        [HDR] _ColorA ("Color A", Color) = (1,1,1,1)
        [HDR] _ColorB ("Color B", Color) = (0.5,0.5,1,1)
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "GpuParticle"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ParticleData
            {
                float3 position;
                float age;
                float3 velocity;
                float lifetime;
                float4 color;
                float size;
                float rotation;
                uint alive;
                uint seed;
            };

            StructuredBuffer<ParticleData> _Particles;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                float4x4 _LayerMatrix;
                float _ParticleSize;
                float _Opacity;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                ParticleData particle = _Particles[input.instanceID];
                float size = _ParticleSize * particle.size * (particle.alive != 0u ? 1.0 : 0.0);
                float s, c;
                sincos(particle.rotation, s, c);
                float2 corner = float2(
                    input.positionOS.x * c - input.positionOS.y * s,
                    input.positionOS.x * s + input.positionOS.y * c) * size;

                float3 centerWS = mul(_LayerMatrix, float4(particle.position, 1.0)).xyz;
                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 positionWS = centerWS + cameraRight * corner.x + cameraUp * corner.y;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = lerp(_ColorA, _ColorB, particle.color.r);
                output.color.a *= particle.color.a * _Opacity;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float alpha = saturate(1.0 - dot(p, p));
                alpha = alpha * alpha;
                half4 color = input.color;
                color.a *= alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
