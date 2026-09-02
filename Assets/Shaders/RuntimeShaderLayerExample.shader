Shader "Aetherin/Runtime Shader Layer Example"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Includes/AetherinRuntimeShaderInputs.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float spectrum = SAMPLE_TEXTURE2D(_AetherinSpectrumTex, sampler_AetherinSpectrumTex, float2(input.uv.x, 0.5)).r;
                float wave = sin(length(p) * 18.0 - _AetherinTime.x * 3.0 + spectrum * 8.0);
                float beat = pow(saturate(1.0 - _AetherinBeat.x), 3.0);
                float mask = smoothstep(0.15, -0.15, length(p) - (0.42 + wave * 0.08 * _UserFloat0 + beat * 0.12));
                half4 color = lerp(_AccentColor1, _AccentColor2, input.uv.y + wave * 0.15);
                color.rgb *= 0.6 + _AetherinAudio.x * 2.0 + beat;
                color.a *= mask * _AetherinOpacity;
                return color;
            }
            ENDHLSL
        }
    }
}
