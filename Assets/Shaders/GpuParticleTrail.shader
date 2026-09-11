Shader "Aetherin/GPU Particle Trail"
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
            Name "GpuParticleTrail"
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
                float3 position; float age;
                float3 velocity; float lifetime;
                float4 color;
                float size; float initialSize;
                float3 rotation;
                float3 angularVelocity;
                uint alive; uint seed; uint generation;
            };
            struct TrailSample { float3 position; uint seed; };
            StructuredBuffer<ParticleData> _Particles;
            StructuredBuffer<TrailSample> _TrailSamples;

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; uint instanceID : SV_InstanceID; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                float4x4 _LayerMatrix;
                float _ParticleSize;
                float _Opacity;
                float _TrailWidth;
                float _TrailTailWidth;
                int _TrailLength;
                int _TrailFrameIndex;
                int _PaletteRandomMode;
                int _PaletteRandomSeed;
                half4 _PaletteColor0;
                half4 _PaletteColor1;
                half4 _PaletteColor2;
                half4 _PaletteColor3;
                half4 _PaletteColor4;
                half4 _PaletteColor5;
            CBUFFER_END

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

            uint TrailSlot(int frameIndex, int pointsBehind)
            {
                int slot = frameIndex - pointsBehind;
                slot %= _TrailLength;
                return (uint)(slot < 0 ? slot + _TrailLength : slot);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint segmentsPerParticle = (uint)(_TrailLength - 1);
                uint particleIndex = input.instanceID / segmentsPerParticle;
                uint segmentIndex = input.instanceID % segmentsPerParticle;
                uint olderSlot = TrailSlot(_TrailFrameIndex, _TrailLength - 1 - (int)segmentIndex);
                uint newerSlot = TrailSlot(_TrailFrameIndex, _TrailLength - 2 - (int)segmentIndex);
                TrailSample older = _TrailSamples[particleIndex * (uint)_TrailLength + olderSlot];
                TrailSample newer = _TrailSamples[particleIndex * (uint)_TrailLength + newerSlot];
                ParticleData particle = _Particles[particleIndex];
                bool valid = older.seed == newer.seed && newer.seed == particle.generation && particle.alive != 0u;

                float3 startWS = mul(_LayerMatrix, float4(older.position, 1.0)).xyz;
                float3 endWS = mul(_LayerMatrix, float4(newer.position, 1.0)).xyz;
                float3 direction = endWS - startWS;
                float lengthSq = dot(direction, direction);
                direction = lengthSq > 0.0000001 ? normalize(direction) : float3(0.0, 1.0, 0.0);
                float3 midpoint = (startWS + endWS) * 0.5;
                float3 viewDirection = normalize(_WorldSpaceCameraPos - midpoint);
                float3 side = cross(viewDirection, direction);
                side = dot(side, side) > 0.0000001 ? normalize(side) : float3(1.0, 0.0, 0.0);

                float segmentT = (float)segmentIndex / max(1.0, (float)(_TrailLength - 1));
                float widthScale = lerp(_TrailTailWidth, 1.0, segmentT);
                float width = particle.size * _ParticleSize * _TrailWidth * widthScale;
                float along = input.uv.x;
                float3 center = lerp(startWS, endWS, along);
                float3 positionWS = center + side * ((input.uv.y - 0.5) * width);
                output.positionCS = TransformWorldToHClip(positionWS);
                half4 color = lerp(_ColorA, _ColorB, particle.color.r);
                if (_PaletteRandomMode != 0)
                {
                    uint random = AetherinParticleHash(particle.seed + (uint)_PaletteRandomSeed * 747796405u);
                    int first = _PaletteRandomMode == 2 ? 2 : (_PaletteRandomMode == 3 ? 4 : 0);
                    int count = _PaletteRandomMode == 1 ? 6 : 2;
                    color = PaletteColor(first + (int)(random % (uint)count));
                }
                color.a *= particle.color.a * _Opacity * segmentT * (valid ? 1.0 : 0.0);
                output.color = color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target 
            {
                if (input.color.a <= 0.0) discard;
                return input.color;
            }
            ENDHLSL
        }
    }
}
