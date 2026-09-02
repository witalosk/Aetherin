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
                float initialSize;
                float3 rotation;
                float3 angularVelocity;
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
                int _ParticleShape;
                float _Opacity;
                int _PaletteRandomMode;
                int _PaletteRandomSeed;
                half4 _PaletteColor0;
                half4 _PaletteColor1;
                half4 _PaletteColor2;
                half4 _PaletteColor3;
                half4 _PaletteColor4;
                half4 _PaletteColor5;
            CBUFFER_END

            float3 RotateEuler(float3 value, float3 degrees)
            {
                float3 angle = radians(degrees);
                float3 s, c;
                sincos(angle, s, c);
                value = float3(value.x, value.y * c.x - value.z * s.x, value.y * s.x + value.z * c.x);
                value = float3(value.x * c.y + value.z * s.y, value.y, -value.x * s.y + value.z * c.y);
                return float3(value.x * c.z - value.y * s.z, value.x * s.z + value.y * c.z, value.z);
            }

            uint AetherinParticleHash(uint value)
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            half4 PaletteColor(int index)
            {
                if (index == 0) return _PaletteColor0;
                if (index == 1) return _PaletteColor1;
                if (index == 2) return _PaletteColor2;
                if (index == 3) return _PaletteColor3;
                if (index == 4) return _PaletteColor4;
                return _PaletteColor5;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                ParticleData particle = _Particles[input.instanceID];
                float size = _ParticleSize * particle.size * (particle.alive != 0u ? 1.0 : 0.0);
                float3 corner = RotateEuler(float3(input.positionOS.xy, 0.0) * size, particle.rotation);

                float3 centerWS = mul(_LayerMatrix, float4(particle.position, 1.0)).xyz;
                float3 cameraRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 cameraUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 cameraForward = -UNITY_MATRIX_I_V._m02_m12_m22;
                float3 positionWS = centerWS + cameraRight * corner.x + cameraUp * corner.y + cameraForward * corner.z;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = lerp(_ColorA, _ColorB, particle.color.r);
                if (_PaletteRandomMode != 0)
                {
                    uint random = AetherinParticleHash(particle.seed + (uint)_PaletteRandomSeed * 747796405u);
                    int first = _PaletteRandomMode == 2 ? 2 : (_PaletteRandomMode == 3 ? 4 : 0);
                    int count = _PaletteRandomMode == 1 ? 6 : 2;
                    output.color = PaletteColor(first + (int)(random % (uint)count));
                }
                output.color.a *= particle.color.a * _Opacity;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float alpha;
                if (_ParticleShape == 0)
                {
                    alpha = saturate(1.0 - dot(p, p));
                    alpha *= alpha;
                }
                else
                {
                    float sides = max(3.0, (float)_ParticleShape);
                    float sector = TWO_PI / sides;
                    float angle = atan2(p.y, p.x) + HALF_PI;
                    float polygonDistance = cos(floor(0.5 + angle / sector) * sector - angle) * length(p);
                    float radius = cos(PI / sides);
                    float edge = max(fwidth(polygonDistance), 0.001);
                    alpha = 1.0 - smoothstep(radius - edge, radius + edge, polygonDistance);
                }
                half4 color = input.color;
                color.a *= alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
