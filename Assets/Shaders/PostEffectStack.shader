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
            float _Hue, _Saturation, _Value, _BlackLevel, _WhiteLevel, _Gamma;
            int _ShutterMode;
            float _HandDrawnFrameRate;

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

            float3 rgbToHsv(float3 c)
            {
                float4 k = float4(0.0, -0.3333333, 0.6666667, -1.0);
                float4 p = lerp(float4(c.bg, k.wz), float4(c.gb, k.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + 0.00001)), d / (q.x + 0.00001), q.x);
            }

            float3 hsvToRgb(float3 c)
            {
                float3 p = abs(frac(c.xxx + float3(0.0, 0.6666667, 0.3333333)) * 6.0 - 3.0);
                return c.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), c.y);
            }

            float luminanceAt(float2 uv)
            {
                float3 color = tex2D(_MainTex, saturate(uv)).rgb;
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float sobelEdge(float2 uv)
            {
                float2 texel = _MainTex_TexelSize.xy;
                float topLeft = luminanceAt(uv + texel * float2(-1.0, 1.0));
                float top = luminanceAt(uv + texel * float2(0.0, 1.0));
                float topRight = luminanceAt(uv + texel * float2(1.0, 1.0));
                float left = luminanceAt(uv + texel * float2(-1.0, 0.0));
                float right = luminanceAt(uv + texel * float2(1.0, 0.0));
                float bottomLeft = luminanceAt(uv + texel * float2(-1.0, -1.0));
                float bottom = luminanceAt(uv + texel * float2(0.0, -1.0));
                float bottomRight = luminanceAt(uv + texel * float2(1.0, -1.0));
                float horizontal = topRight + 2.0 * right + bottomRight - topLeft - 2.0 * left - bottomLeft;
                float vertical = bottomLeft + 2.0 * bottom + bottomRight - topLeft - 2.0 * top - topRight;
                return length(float2(horizontal, vertical));
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
                else if (_EffectType == 10) // LED display
                {
                    float leds = max(2.0, abs(_Scale));
                    float2 grid = float2(leds, leds * _MainTex_TexelSize.w / _MainTex_TexelSize.z);
                    float2 cell = frac(uv * grid) - 0.5;
                    float dotRadius = lerp(0.08, 0.5, saturate(_Amount));
                    float dot = 1.0 - smoothstep(dotRadius * 0.82, dotRadius, length(cell));
                    float2 sampleUv = (floor(uv * grid) + 0.5) / grid;
                    fx = tex2D(_MainTex, sampleUv) * dot;
                }
                else if (_EffectType == 11) // Horizontal fold
                {
                    float folds = max(1.0, round(abs(_Scale)));
                    float foldedX = abs(frac(uv.x * folds) * 2.0 - 1.0);
                    float2 foldedUv = float2(foldedX, uv.y);
                    fx = tex2D(_MainTex, lerp(uv, foldedUv, saturate(_Amount)));
                }
                else if (_EffectType == 12) // Hash-selected invert blocks
                {
                    float cells = max(1.0, round(abs(_Scale)));
                    float2 grid = float2(cells, cells * _MainTex_TexelSize.w / _MainTex_TexelSize.z);
                    float frame = floor(_TimeValue * max(0.0, abs(_Speed)));
                    float selected = step(hash21(floor(uv * grid) + frame * 19.17), saturate(_Amount));
                    fx.rgb = lerp(src.rgb, 1.0 - src.rgb, selected);
                }
                else if (_EffectType == 13) // Grid
                {
                    float cells = max(1.0, abs(_Scale));
                    float2 grid = float2(cells, cells * _MainTex_TexelSize.w / _MainTex_TexelSize.z);
                    float2 line = abs(frac(uv * grid) - 0.5);
                    float width = lerp(0.002, 0.18, saturate(_Amount));
                    float gridLine = step(0.5 - width, max(line.x, line.y));
                    fx.rgb *= 1.0 - gridLine;
                }
                else if (_EffectType == 14) // Noise
                {
                    float grain = max(1.0, abs(_Scale));
                    float frame = floor(_TimeValue * max(0.0, abs(_Speed)) * 30.0);
                    float noise = hash21(floor(uv * grain) + frame * 7.31) * 2.0 - 1.0;
                    fx.rgb = saturate(src.rgb + noise * _Amount);
                }
                else if (_EffectType == 15) // Block glitch
                {
                    float blocks = max(1.0, abs(_Scale));
                    float2 grid = float2(blocks, blocks * _MainTex_TexelSize.w / _MainTex_TexelSize.z);
                    float2 block = floor(uv * grid);
                    float frame = floor(_TimeValue * max(0.0, abs(_Speed)) * 12.0);
                    float active = step(1.0 - saturate(_Secondary), hash21(block + frame * 13.37));
                    float offset = (hash21(block + frame * 31.73) * 2.0 - 1.0) * _Amount * active;
                    fx = tex2D(_MainTex, uv + float2(offset, 0.0));
                }
                else if (_EffectType == 16) // HSV levels
                {
                    float black = saturate(_BlackLevel);
                    float white = max(black + 0.0001, saturate(_WhiteLevel));
                    float3 levels = saturate((src.rgb - black) / (white - black));
                    levels = pow(levels, 1.0 / max(0.001, _Gamma));
                    float3 hsv = rgbToHsv(levels);
                    hsv.x = frac(hsv.x + _Hue);
                    hsv.y = saturate(hsv.y * max(0.0, _Saturation));
                    hsv.z = saturate(hsv.z * max(0.0, _Value));
                    fx.rgb = hsvToRgb(hsv);
                }
                else if (_EffectType == 17) // Shutter
                {
                    float close = saturate(_Amount);
                    float openMask;
                    if (_ShutterMode == 0)
                    {
                        openMask = 1.0 - step(0.5 * (1.0 - close), abs(uv.y - 0.5));
                    }
                    else if (_ShutterMode == 1)
                    {
                        openMask = 1.0 - step(0.5 * (1.0 - close), abs(uv.x - 0.5));
                    }
                    else
                    {
                        float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                        float distanceFromCenter = length((uv - 0.5) * float2(aspect, 1.0));
                        openMask = 1.0 - step(0.75 * (1.0 - close), distanceFromCenter);
                    }
                    fx.rgb = src.rgb * openMask;
                }
                else if (_EffectType == 18) // Hand-drawn ink and hatching
                {
                    float fps = max(1.0, _HandDrawnFrameRate);
                    float quantizedTime = floor(_TimeValue * fps) / fps;
                    float wiggle = max(0.0, _Secondary);
                    float2 noiseUv = uv * 7.0 + quantizedTime * _Speed;
                    float2 warp = float2(
                        noise21(noiseUv) - 0.5,
                        noise21(noiseUv + 31.7) - 0.5) * wiggle;
                    float2 sketchUv = saturate(uv + warp);
                    float sourceLuminance = luminanceAt(sketchUv);

                    float edge = sobelEdge(sketchUv);
                    float edgeInk = smoothstep(max(0.0001, _Amount), max(0.0001, _Amount) * 2.5, edge);

                    float density = max(1.0, abs(_Scale));
                    float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                    float2 hatchSpace = sketchUv * float2(density * aspect, density);
                    float diagonalA = abs(frac(dot(hatchSpace, float2(0.7071, 0.7071))) - 0.5);
                    float diagonalB = abs(frac(dot(hatchSpace, float2(0.7071, -0.7071))) - 0.5);
                    float hatchA = 1.0 - smoothstep(0.36, 0.5, diagonalA);
                    float hatchB = 1.0 - smoothstep(0.40, 0.5, diagonalB);
                    float darkness = saturate((0.9 - sourceLuminance) / 0.7);
                    float hatchInk = hatchA * darkness;
                    hatchInk = max(hatchInk, hatchB * saturate((0.45 - sourceLuminance) / 0.35));

                    float ink = saturate(max(edgeInk, hatchInk));
                    fx = float4(1.0 - ink, 1.0 - ink, 1.0 - ink, src.a);
                }

                return lerp(src, fx, saturate(_Strength));
            }
            ENDHLSL
        }
    }
}
