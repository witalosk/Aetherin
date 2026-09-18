Shader "Hidden/Aetherin/CrossFilter"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _CrossTex ("Cross Filter", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: Extract bright pixels. Small point lights are intentionally retained.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragExtract
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex;
            float _CrossThreshold;
            float _CrossExposure;

            float4 fragExtract(v2f i) : SV_Target
            {
                float4 color = tex2D(_MainTex, i.uv);
                float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                return luma < _CrossThreshold ? 0.0 : float4(color.rgb * _CrossExposure, color.a);
            }
            ENDHLSL
        }

        // 1: Eight-tap recursive Kawase star-line blur.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragLine
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _CrossDirection;
            float _CrossStep;
            float _CrossAttenuation;

            float4 fragLine(v2f i) : SV_Target
            {
                float4 result = 0.0;
                float totalWeight = 0.0;
                [unroll]
                for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
                {
                    float weight = pow(_CrossAttenuation, sampleIndex);
                    float2 offset = _CrossDirection * _MainTex_TexelSize.xy * _CrossStep * sampleIndex;
                    result += tex2D(_MainTex, saturate(i.uv + offset)) * weight;
                    totalWeight += weight;
                }
                result.rgb /= max(totalWeight, 0.0001);
                result.a = 1.0;
                return max(result, 0.0);
            }
            ENDHLSL
        }

        // 2: Add one completed star direction to the accumulator.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragAccumulate
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex;
            sampler2D _CrossTex;

            float4 fragAccumulate(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) + tex2D(_CrossTex, i.uv);
            }
            ENDHLSL
        }

        // 3: Composite the averaged star result over the untouched source.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex;
            sampler2D _CrossTex;
            float _CrossIntensity;
            float _Strength;

            float4 fragComposite(v2f i) : SV_Target
            {
                float4 source = tex2D(_MainTex, i.uv);
                float3 filtered = source.rgb + tex2D(_CrossTex, i.uv).rgb * _CrossIntensity;
                return float4(lerp(source.rgb, filtered, saturate(_Strength)), source.a);
            }
            ENDHLSL
        }
    }
}
