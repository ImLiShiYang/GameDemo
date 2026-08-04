Shader "Game/CrosshairAlwaysOnTop"
{
    Properties
    {
        _BaseMap ("Crosshair Texture", 2D) = "white" {}
        _BaseColor ("Crosshair Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "CrosshairAlwaysOnTop"

            Blend SrcAlpha OneMinusSrcAlpha

            Cull Off
            ZWrite Off
            ZTest Always

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
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv =
                    input.uv * _BaseMap_ST.xy +
                    _BaseMap_ST.zw;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 textureColor =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv
                    );

                /*
                 * 使用贴图的 Alpha 作为准星形状，
                 * 使用 BaseColor 决定准星最终颜色。
                 */
                half alpha =
                    textureColor.a *
                    _BaseColor.a;

                return half4(
                    _BaseColor.rgb,
                    alpha
                );
            }

            ENDHLSL
        }
    }
}