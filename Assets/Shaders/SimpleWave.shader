Shader "Hidden/SimpleWave"
{
    Properties
    {
        _WaveTex ("_WaveTex", 2D) = "white" {}
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "Assets/Scripts/Color/ColorPalette.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _WaveTex;

            float4 frag(v2f i) : SV_Target
            {
                float wave = tex2D(_WaveTex, i.uv).r;
                
                return abs(wave * 0.5 + 0.5 - i.uv.y) < 0.01 ? _AccentColor1 : _BackgroundColor1;
            }
            ENDHLSL
        }
    }
}
