Shader "Hidden/StageCrossFade"
{
    Properties
    {
        _TexA ("Current", 2D) = "black" {}
        _TexB ("Next", 2D) = "black" {}
        _Fade ("Fade", Range(0, 1)) = 0
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

            sampler2D _TexA;
            sampler2D _TexB;
            float _Fade;

            float4 frag(v2f i) : SV_Target
            {
                return lerp(tex2D(_TexA, i.uv), tex2D(_TexB, i.uv), saturate(_Fade));
            }
            ENDHLSL
        }
    }
}
