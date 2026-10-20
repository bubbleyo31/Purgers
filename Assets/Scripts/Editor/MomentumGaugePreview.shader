Shader "Hidden/Purgers/Editor/MomentumGaugePreview"
{
    Properties { _MainTex ("Existing Momentum silhouette", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "../../Shaders/Presentation/MomentumGaugeFlowCore.cginc"
            sampler2D _MainTex;
            float4 _Tint, _NormalTint, _Background;
            float _Clock, _Energy, _Speed, _Strength;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float mask = tex2D(_MainTex, uv).a;
                float filled = step(uv.x, _Energy) * step(.00001, _Energy);
                float full = step(1.0, _Energy);
                // Only the interior color travels. Neither the source alpha nor its UVs move.
                float3 tint = lerp(_NormalTint.rgb, _Tint.rgb, full);
                tint = MomentumGaugeFlow(uv, tint, _Clock * _Speed, _Strength);
                float3 track = _Background.rgb * .48;
                return float4(lerp(_Background.rgb, lerp(track, tint, filled), mask), 1);
            }
            ENDCG
        }
    }
}
