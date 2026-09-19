Shader "Hidden/Aetherin/OutputOverlayComposite"
{
    Properties
    {
        _MainTex ("Base", 2D) = "black" {}
        _OverlayTex ("Overlay", 2D) = "black" {}
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
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _OverlayTex;
            float4 frag(v2f i) : SV_Target
            {
                float4 baseColor = tex2D(_MainTex, i.uv);
                float4 overlay = tex2D(_OverlayTex, i.uv);
                return float4(overlay.rgb * overlay.a + baseColor.rgb * (1.0 - overlay.a),
                              overlay.a + baseColor.a * (1.0 - overlay.a));
            }
            ENDHLSL
        }
    }
}
