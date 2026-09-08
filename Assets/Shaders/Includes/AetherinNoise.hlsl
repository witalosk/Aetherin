#ifndef AETHERIN_VERTEX_NOISE_INCLUDED
#define AETHERIN_VERTEX_NOISE_INCLUDED

// 3D simplex noise adapted for HLSL from Ashima Arts / Stefan Gustavson's
// textureless webgl-noise implementation: https://github.com/ashima/webgl-noise
// Curl field uses the finite-difference curl construction described by Bridson.
float3 AN_Mod289(float3 x) { return x - floor(x / 289.0) * 289.0; }
float4 AN_Mod289(float4 x) { return x - floor(x / 289.0) * 289.0; }
float4 AN_Permute(float4 x) { return AN_Mod289((x * 34.0 + 1.0) * x); }
float4 AN_InvSqrt(float4 x) { return 1.79284291400159 - 0.85373472095314 * x; }

float AN_SimplexNoise(float3 v)
{
    const float2 c = float2(1.0 / 6.0, 1.0 / 3.0);
    const float4 d = float4(0.0, 0.5, 1.0, 2.0);
    float3 i = floor(v + dot(v, c.yyy));
    float3 x0 = v - i + dot(i, c.xxx);
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    float3 x1 = x0 - i1 + c.xxx;
    float3 x2 = x0 - i2 + c.yyy;
    float3 x3 = x0 - d.yyy;
    i = AN_Mod289(i);
    float4 p = AN_Permute(AN_Permute(AN_Permute(i.z + float4(0, i1.z, i2.z, 1)) + i.y + float4(0, i1.y, i2.y, 1)) + i.x + float4(0, i1.x, i2.x, 1));
    float n = 1.0 / 7.0;
    float3 ns = n * d.wyz - d.xzx;
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);
    float4 x = floor(j * ns.z) * ns.x + ns.y;
    float4 y = (floor(j - 7.0 * floor(j * ns.z))) * ns.x + ns.y;
    float4 h = 1.0 - abs(x) - abs(y);
    float4 b0 = float4(x.xy, y.xy), b1 = float4(x.zw, y.zw);
    float4 s0 = floor(b0) * 2.0 + 1.0, s1 = floor(b1) * 2.0 + 1.0;
    float4 sh = -step(h, 0.0);
    float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
    float3 p0 = float3(a0.xy, h.x), p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z), p3 = float3(a1.zw, h.w);
    float4 norm = AN_InvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
    p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
    float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
    m *= m;
    return 42.0 * dot(m * m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
}

// A 4D domain assembled from three skewed simplex projections.  Every input
// component affects more than one projection, avoiding a simple 3D slide when
// the W axis is animated.
float AN_SimplexNoise(float4 p)
{
    return (AN_SimplexNoise(p.xyz + p.www * float3(0.1031, 0.11369, 0.13787)) +
            AN_SimplexNoise(p.yzw + p.xxx * float3(0.1099, 0.1277, 0.1513)) +
            AN_SimplexNoise(float3(p.x + p.z * 0.1732, p.y + p.w * 0.1919,
                                   p.z + p.x * 0.2113))) / 3.0;
}

// Smooth 3D fractal Brownian motion (fBm) built from the simplex function above.
// The normalization keeps the return value approximately in [-1, 1] regardless
// of the number of octaves, which makes it safe to map directly to a density.
float AN_FractalNoise(float3 p)
{
    const int octaves = 4;
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    float amplitudeSum = 0.0;

    [unroll] for (int octave = 0; octave < octaves; octave++)
    {
        value += AN_SimplexNoise(p * frequency) * amplitude;
        amplitudeSum += amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }

    return value / amplitudeSum;
}

float AN_BlockNoise(float3 p)
{
    p = floor(p);
    return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453123) * 2.0 - 1.0;
}

float AN_BlockNoise(float4 p)
{
    p = floor(p);
    return frac(sin(dot(p, float4(127.1, 311.7, 74.7, 269.5))) * 43758.5453123) * 2.0 - 1.0;
}

float3 AN_CurlNoise(float3 p)
{
    const float e = 0.08;
    float3 dx = float3(e,0,0), dy = float3(0,e,0), dz = float3(0,0,e);
    float3 a = float3(AN_SimplexNoise(p + float3(19, 0, 0)), AN_SimplexNoise(p + float3(0, 47, 0)), AN_SimplexNoise(p + float3(0, 0, 83)));
    float3 ax = float3(AN_SimplexNoise(p+dx+float3(19,0,0)), AN_SimplexNoise(p+dx+float3(0,47,0)), AN_SimplexNoise(p+dx+float3(0,0,83)));
    float3 ay = float3(AN_SimplexNoise(p+dy+float3(19,0,0)), AN_SimplexNoise(p+dy+float3(0,47,0)), AN_SimplexNoise(p+dy+float3(0,0,83)));
    float3 az = float3(AN_SimplexNoise(p+dz+float3(19,0,0)), AN_SimplexNoise(p+dz+float3(0,47,0)), AN_SimplexNoise(p+dz+float3(0,0,83)));
    return float3((az.y-ay.z), (ax.z-az.x), (ay.x-ax.y)) / e;
}

float AN_VertexNoise(float3 p, int type)
{
    if (type == 0) return AN_BlockNoise(p);
    if (type == 1) return AN_SimplexNoise(p);
    return dot(normalize(AN_CurlNoise(p) + 1e-5), normalize(float3(0.57735, 0.57735, 0.57735)));
}

float AN_VertexNoise(float4 p, int type)
{
    if (type == 0) return AN_BlockNoise(p);
    if (type == 1) return AN_SimplexNoise(p);
    float3 curl = AN_CurlNoise(float3(p.x + p.w * 0.1031, p.y + p.w * 0.11369,
                                     p.z + p.w * 0.13787));
    return dot(normalize(curl + 1e-5), normalize(float3(0.57735, 0.57735, 0.57735)));
}
#endif
