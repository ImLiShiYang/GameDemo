Shader "Custom/PickupPulseUnlit"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        _PulseAmplitude("Pulse Amplitude", Range(0, 0.5)) = 0.08
        _PulseSpeed("Pulse Speed", Range(0, 10)) = 3
        _PulsePhase("Pulse Phase", Range(0, 6.28318)) = 0

        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _PulseAmplitude;
                float _PulseSpeed;
                float _PulsePhase;
                float _AlphaCutoff;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float pulse =
                    1.0 + sin(_Time.y * _PulseSpeed + _PulsePhase) * _PulseAmplitude;

                float3 positionOS = input.positionOS.xyz;
                positionOS.xy *= pulse;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(positionOS);

                output.positionHCS = positionInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                clip(color.a - _AlphaCutoff);

                return color;
            }
            ENDHLSL
        }
    }
}