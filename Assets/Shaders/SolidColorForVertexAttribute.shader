Shader "Custom/SolidColor For VertexAttribute"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 texcoord0 : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 texcoord0 : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.texcoord0 = input.texcoord0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Intentionally meaningless use of TEXCOORD0 to discourage it from being optimized away.
                half texcoordBias = frac(dot(input.texcoord0.xy, half2(1.0h / 1024.0h, 1.0h / 2048.0h))) * 1.0e-6h;
                return half4(_Color.rgb + texcoordBias.xxx, _Color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
