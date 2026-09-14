    
#ifndef HLSL_INCLUDE_PCG
#define HLSL_INCLUDE_PCG

// Permuted Congruential Generator
// Original source code: https://www.shadertoy.com/view/XlGcRh

#define FLOAT_MAX float(0xffffffffu)
#define FLOAT_MAX_INVERT 1.0 / FLOAT_MAX

uint Pcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

uint2 Pcg2d(uint2 v)
{
    v = v * 1664525u + 1013904223u;

    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;

    v = v ^ (v>>16u);

    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;

    v = v ^ (v>>16u);

    return v;
}

uint3 Pcg3d(uint3 v) {

    v = v * 1664525u + 1013904223u;

    v.x += v.y*v.z;
    v.y += v.z*v.x;
    v.z += v.x*v.y;

    v ^= v >> 16u;

    v.x += v.y*v.z;
    v.y += v.z*v.x;
    v.z += v.x*v.y;

    return v;
}

uint4 Pcg4d(uint4 v)
{
    v = v * 1664525u + 1013904223u;

    v.x += v.y*v.w;
    v.y += v.z*v.x;
    v.z += v.x*v.y;
    v.w += v.y*v.z;

    v ^= v >> 16u;

    v.x += v.y*v.w;
    v.y += v.z*v.x;
    v.z += v.x*v.y;
    v.w += v.y*v.z;

    return v;
}

float Pcg01(uint v)
{
    return Pcg(v) * FLOAT_MAX_INVERT;
}

float2 Pcg2d01(uint2 v)
{
    return Pcg2d(v) * FLOAT_MAX_INVERT;
}

float3 Pcg3d01(uint3 v)
{
    return Pcg3d(v) * FLOAT_MAX_INVERT;
}

float4 Pcg4d01(uint4 v)
{
    return Pcg4d(v) * FLOAT_MAX_INVERT;
}

#endif
