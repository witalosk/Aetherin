#ifndef AETHERIN_CURL_NOISE_INCLUDED
#define AETHERIN_CURL_NOISE_INCLUDED

// 3D simplex noise implementation adapted from Keijiro Takahashi's NoiseShader:
// https://github.com/keijiro/NoiseShader/blob/master/Packages/jp.keijiro.noiseshader/Shader/SimplexNoise3D.hlsl
// Original work: Ashima Arts / Stefan Gustavson (webgl-noise), MIT License.
// Keijiro's HLSL modifications retain the MIT license; see:
// https://github.com/keijiro/NoiseShader/blob/master/LICENSE

float3 AetherinNoiseMod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 AetherinNoiseMod289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 AetherinNoisePermute(float4 x) { return AetherinNoiseMod289((x * 34.0 + 1.0) * x); }

// Returns xyz = analytic gradient and w = simplex noise value.
float4 AetherinSimplexNoiseGrad(float3 v)
{
    float3 i = floor(v + dot(v, 1.0 / 3.0));
    float3 x0 = v - i + dot(i, 1.0 / 6.0);
    float3 g = x0.yzx <= x0.xyz;
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    float3 x1 = x0 - i1 + 1.0 / 6.0;
    float3 x2 = x0 - i2 + 1.0 / 3.0;
    float3 x3 = x0 - 0.5;

    i = AetherinNoiseMod289(i);
    float4 p = AetherinNoisePermute(i.z + float4(0.0, i1.z, i2.z, 1.0));
    p = AetherinNoisePermute(p + i.y + float4(0.0, i1.y, i2.y, 1.0));
    p = AetherinNoisePermute(p + i.x + float4(0.0, i1.x, i2.x, 1.0));

    float4 gx = lerp(-1.0, 1.0, frac(p / 7.0));
    float4 gy = lerp(-1.0, 1.0, frac(floor(p / 7.0) / 7.0));
    float4 gz = 1.0 - abs(gx) - abs(gy);
    float4 negativeZ = step(gz, 0.0);
    gx += negativeZ * lerp(1.0, -1.0, step(0.0, gx));
    gy += negativeZ * lerp(1.0, -1.0, step(0.0, gy));

    float3 g0 = normalize(float3(gx.x, gy.x, gz.x));
    float3 g1 = normalize(float3(gx.y, gy.y, gz.y));
    float3 g2 = normalize(float3(gx.z, gy.z, gz.z));
    float3 g3 = normalize(float3(gx.w, gy.w, gz.w));

    float4 m = float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3));
    float4 px = float4(dot(g0, x0), dot(g1, x1), dot(g2, x2), dot(g3, x3));
    m = max(0.5 - m, 0.0);
    float4 m3 = m * m * m;
    float4 m4 = m * m3;
    float4 temp = -8.0 * m3 * px;
    float3 gradient = m4.x * g0 + temp.x * x0 + m4.y * g1 + temp.y * x1 +
                      m4.z * g2 + temp.z * x2 + m4.w * g3 + temp.w * x3;
    return 107.0 * float4(gradient, dot(m4, px));
}

float3 AetherinCurlNoise(float3 position, float time, float frequency, float speed)
{
    float3 p = position * frequency + time * speed * float3(0.73, 1.13, 0.91);
    float3 gradient1 = AetherinSimplexNoiseGrad(p + float3(17.1, 31.7, 47.2)).xyz;
    float3 gradient2 = AetherinSimplexNoiseGrad(p + float3(53.4, 11.8, 29.6)).xyz;
    float3 gradient3 = AetherinSimplexNoiseGrad(p + float3(7.3, 61.9, 43.5)).xyz;

    return frequency * float3(
        gradient3.y - gradient2.z,
        gradient1.z - gradient3.x,
        gradient2.x - gradient1.y);
}

#endif
