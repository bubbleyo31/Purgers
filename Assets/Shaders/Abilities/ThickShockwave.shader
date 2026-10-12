Shader "Purgers/Abilities/Thick Shockwave"
{
    Properties
    {
        _FlowTex ("流動細節（Alpha）", 2D) = "white" {}
        [HDR] _BaseColor ("波體顏色", Color) = (1,0.24,0.08,1)
        [HDR] _EdgeColor ("亮邊顏色", Color) = (2,1.5,0.7,1)
        _Opacity ("不透明度", Range(0,1)) = 0.65
        _FlowScale ("細節密度", Range(1,12)) = 4
        _FlowRate ("每公尺流動量", Range(0,3)) = 0.35
        [HideInInspector] _WaveDistance ("波前距離", Float) = 0
        [HideInInspector] _WaveOpacity ("波體完整度", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Shockwave"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float4 color : COLOR; };
            TEXTURE2D(_FlowTex); SAMPLER(sampler_FlowTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _EdgeColor;
                float _Opacity, _FlowScale, _FlowRate, _WaveDistance, _WaveOpacity;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float travel = _WaveDistance * _FlowRate;
                float2 flowUV = float2(input.uv.x * _FlowScale + travel * 0.11, input.uv.y * 3 - travel);
                float cloud = SAMPLE_TEXTURE2D(_FlowTex, sampler_FlowTex, flowUV).a;
                float cloud2 = SAMPLE_TEXTURE2D(_FlowTex, sampler_FlowTex, flowUV * 1.73 + float2(0.37, travel * 0.3)).a;
                float ripple = pow(saturate(0.5 + 0.5 * sin(input.uv.y * 95 + cloud * 5 - travel * 8)), 6);
                float fresnel = pow(1 - abs(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS))), 2);
                float energy = saturate(cloud * 0.6 + cloud2 * 0.25 + ripple * 0.5);
                float sideFade = smoothstep(0, 0.025, input.uv.x) * smoothstep(0, 0.025, 1-input.uv.x);
                // 保留連續可見的基底；細節貼圖不能把傷害帶挖成只有零星粒子。
                float alpha = _Opacity * (0.3 + energy * 0.5 + fresnel * 0.2) * input.color.a * _WaveOpacity;
                alpha *= lerp(0.5, 1, sideFade);
                float3 color = lerp(_BaseColor.rgb, _EdgeColor.rgb, saturate(ripple * 0.65 + fresnel * 0.6));
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
