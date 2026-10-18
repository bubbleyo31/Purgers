Shader "Purgers/BeamEnergy"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1,1,1,1)
        _Radial ("Particle radial shape", Float) = 0
        _Softness ("Edge softness", Range(.1,4)) = 1.6
        _Streak ("Energy streak strength", Range(0,1)) = .15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Radial;
            float _Softness;
            float _Streak;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv; o.color = v.color; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float distanceFromCenter = lerp(abs(i.uv.y * 2 - 1), length(i.uv * 2 - 1), saturate(_Radial));
                float alpha = pow(saturate(1 - distanceFromCenter), _Softness);
                float streak = 1 - _Streak * (.5 + .5 * sin(i.uv.x * 180 - _Time.y * 34 + i.uv.y * 12));
                return half4(i.color.rgb * _Tint.rgb * streak, i.color.a * _Tint.a * alpha);
            }
            ENDHLSL
        }
    }
}

