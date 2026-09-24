#ifndef AETHERIN_PRIMITIVE_3D_VERTEX_NOISE_INCLUDED
#define AETHERIN_PRIMITIVE_3D_VERTEX_NOISE_INCLUDED

float AN_PrimitiveVertexNoiseDisplacement(
    float3 positionOS,
    float4 frequency,
    float4 speed,
    float4 offset,
    float stageTime,
    int noiseType,
    float amount)
{
    float4 samplePosition = float4(positionOS * frequency.xyz + offset.xyz, 0.0) + speed * stageTime;
    return AN_VertexNoise(samplePosition, noiseType) * amount;
}

void AN_ApplyPrimitiveVertexNoise(
    float3 positionOS,
    float3 normalOS,
    float3 shapeNormal,
    float3 displacementDirection,
    float4x4 shapeMatrix,
    float4 frequency,
    float4 speed,
    float4 offset,
    float stageTime,
    int noiseType,
    float amount,
    out float3 displacedPosition,
    out float3 displacedNormal)
{
    float3 referenceAxis = abs(normalOS.y) < 0.999
        ? float3(0.0, 1.0, 0.0)
        : float3(1.0, 0.0, 0.0);
    float3 tangentOS = normalize(cross(referenceAxis, normalOS));
    float3 bitangentOS = normalize(cross(normalOS, tangentOS));

    // Keep the finite-difference interval small in noise space at high frequencies.
    float maxFrequency = max(max(abs(frequency.x), abs(frequency.y)), abs(frequency.z));
    float sampleStep = 0.01 / max(1.0, maxFrequency);

    float centerDisplacement = AN_PrimitiveVertexNoiseDisplacement(
        positionOS, frequency, speed, offset, stageTime, noiseType, amount);
    displacedPosition = mul(shapeMatrix, float4(positionOS, 1.0)).xyz
        + displacementDirection * centerDisplacement;

    float3 tangentPositiveOS = positionOS + tangentOS * sampleStep;
    float3 tangentNegativeOS = positionOS - tangentOS * sampleStep;
    float3 bitangentPositiveOS = positionOS + bitangentOS * sampleStep;
    float3 bitangentNegativeOS = positionOS - bitangentOS * sampleStep;

    float3 tangentPositive = mul(shapeMatrix, float4(tangentPositiveOS, 1.0)).xyz
        + displacementDirection * AN_PrimitiveVertexNoiseDisplacement(
            tangentPositiveOS, frequency, speed, offset, stageTime, noiseType, amount);
    float3 tangentNegative = mul(shapeMatrix, float4(tangentNegativeOS, 1.0)).xyz
        + displacementDirection * AN_PrimitiveVertexNoiseDisplacement(
            tangentNegativeOS, frequency, speed, offset, stageTime, noiseType, amount);
    float3 bitangentPositive = mul(shapeMatrix, float4(bitangentPositiveOS, 1.0)).xyz
        + displacementDirection * AN_PrimitiveVertexNoiseDisplacement(
            bitangentPositiveOS, frequency, speed, offset, stageTime, noiseType, amount);
    float3 bitangentNegative = mul(shapeMatrix, float4(bitangentNegativeOS, 1.0)).xyz
        + displacementDirection * AN_PrimitiveVertexNoiseDisplacement(
            bitangentNegativeOS, frequency, speed, offset, stageTime, noiseType, amount);

    float3 tangent = tangentPositive - tangentNegative;
    float3 bitangent = bitangentPositive - bitangentNegative;
    float3 reconstructedNormal = cross(tangent, bitangent);
    float normalLengthSquared = dot(reconstructedNormal, reconstructedNormal);
    displacedNormal = normalLengthSquared > 1e-10
        ? reconstructedNormal * rsqrt(normalLengthSquared)
        : shapeNormal;
    if (dot(displacedNormal, shapeNormal) < 0.0)
        displacedNormal = -displacedNormal;
}

#endif
