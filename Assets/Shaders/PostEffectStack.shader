Shader "Hidden/Aetherin/PostEffectStack"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _HistoryTex ("Previous Frame", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex, _HistoryTex;
            float4 _MainTex_TexelSize;
            int _EffectType;
            float _Strength, _Amount, _Scale, _Speed, _Secondary, _TimeValue;

            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float3 bloomSample(float2 uv)
            {
                float3 color = tex2D(_MainTex, uv).rgb;
                float brightness = max(color.r, max(color.g, color.b));
                float threshold = saturate(_Secondary);
                float contribution = saturate((brightness - threshold) / max(0.0001, 1.0 - threshold));
                return color * contribution;
            }

            float noise21(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1,0)), f.x),
                            lerp(hash21(i + float2(0,1)), hash21(i + 1), f.x), f.y);
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 src = tex2D(_MainTex, uv);
                float4 fx = src;

                if (_EffectType == 0) // Chromatic aberration
                {
                    float2 dir = uv - 0.5;
                    float2 offset = dir * _Amount;
                    fx = float4(tex2D(_MainTex, uv + offset).r, src.g, tex2D(_MainTex, uv - offset).b, src.a);
                }
                else if (_EffectType == 1) // Previous frame blend
                {
                    float2 drift = float2(cos(_TimeValue * _Speed), sin(_TimeValue * _Speed)) * _Amount;
                    fx = lerp(src, tex2D(_HistoryTex, uv + drift), saturate(_Secondary));
                }
                else if (_EffectType == 2) // Domain warp
                {
                    float scale = max(0.01, abs(_Scale));
                    float2 q = float2(noise21(uv * scale + _TimeValue * _Speed),
                                      noise21(uv * scale + 17.3 - _TimeValue * _Speed));
                    fx = tex2D(_MainTex, uv + (q - 0.5) * _Amount);
                }
                else if (_EffectType == 3) // Screen shake
                {
                    float frame = floor(_TimeValue * max(1.0, abs(_Speed)) * 12.0);
                    float2 shake = float2(hash21(float2(frame, 1.2)), hash21(float2(frame, 8.7))) - 0.5;
                    fx = tex2D(_MainTex, uv + shake * _Amount);
                }
                else if (_EffectType == 4) // Kaleidoscope
                {
                    float2 p = uv - 0.5;
                    float radius = length(p);
                    float angle = atan2(p.y, p.x) + _TimeValue * _Speed;
                    float sectors = max(1.0, round(abs(_Scale)));
                    float wedge = 6.2831853 / sectors;
                    angle = abs(fmod(angle + wedge * 0.5, wedge) - wedge * 0.5);
                    fx = tex2D(_MainTex, 0.5 + radius * float2(cos(angle), sin(angle)));
                }
                else if (_EffectType == 5) // Pixelate
                {
                    float pixels = max(2.0, abs(_Scale));
                    float2 aspect = float2(pixels, pixels * _MainTex_TexelSize.w / _MainTex_TexelSize.z);
                    fx = tex2D(_MainTex, (floor(uv * aspect) + 0.5) / aspect);
                }
                else if (_EffectType == 6) // Scanline / horizontal glitch
                {
                    float scanWave = sin((uv.y * max(1.0, _Scale) + _TimeValue * _Speed) * 6.2831853);
                    float band = step(1.0 - saturate(_Secondary), hash21(float2(floor(uv.y * _Scale), floor(_TimeValue * _Speed * 8.0))));
                    fx = tex2D(_MainTex, uv + float2(scanWave * _Amount * band, 0));
                    fx.rgb *= 1.0 - saturate(_Amount) * 0.5 * (scanWave * 0.5 + 0.5);
                }
                else if (_EffectType == 7) // Posterize
                {
                    float levels = max(2.0, round(abs(_Scale)));
                    fx.rgb = floor(src.rgb * levels) / (levels - 1.0);
                }
                else if (_EffectType == 8) // Invert
                {
                    fx.rgb = 1.0 - src.rgb;
                }
                else if (_EffectType == 9) // Bloom
                {
                    float2 offset = _MainTex_TexelSize.xy * max(0.0, abs(_Scale));
                    float3 bloom = bloomSample(uv) * 4.0;
                    bloom += bloomSample(uv + float2( offset.x, 0.0)) * 2.0;
                    bloom += bloomSample(uv + float2(-offset.x, 0.0)) * 2.0;
                    bloom += bloomSample(uv + float2(0.0,  offset.y)) * 2.0;
                    bloom += bloomSample(uv + float2(0.0, -offset.y)) * 2.0;
                    bloom += bloomSample(uv + float2( offset.x,  offset.y));
                    bloom += bloomSample(uv + float2(-offset.x,  offset.y));
                    bloom += bloomSample(uv + float2( offset.x, -offset.y));
                    bloom += bloomSample(uv + float2(-offset.x, -offset.y));
                    fx.rgb = src.rgb + bloom * (max(0.0, _Amount) / 16.0);
                }

                return lerp(src, fx, saturate(_Strength));
            }
            ENDHLSL
        }
    }
}
