Shader "Purgers/UI/Screen Edge Effects"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "ScreenEdgeEffectsCore.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 local:TEXCOORD1; };
            float4 _Weights, _HealTint, _HurtTint, _BoostTint, _HealShape, _HurtShape, _BoostShape, _ClipRect;
            float _Clock, _Aspect, _MotionRate, _MaximumOpacity, _HealSymbols;
            v2f vert(appdata v) { v2f o; o.local=v.vertex; o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; return o; }
            float4 Over(float4 under, float4 top)
            {
                float a=top.a+under.a*(1-top.a);
                return float4((top.rgb*top.a+under.rgb*under.a*(1-top.a))/max(a,.00001),a);
            }
            float4 frag(v2f i):SV_Target
            {
                float4 boost=ScreenEdgeLayer(i.uv,_Clock,_Weights.z,_Aspect,_BoostTint,_BoostShape.x,_BoostShape.y,_BoostShape.z,_BoostShape.w,0,0,1,_MotionRate);
                float4 heal=ScreenEdgeLayer(i.uv,_Clock,_Weights.x,_Aspect,_HealTint,_HealShape.x,_HealShape.y,_HealShape.z,_HealShape.w,_HealSymbols,0,0,1);
                float4 hurt=ScreenEdgeLayer(i.uv,_Clock,_Weights.y,_Aspect,_HurtTint,_HurtShape.x,_HurtShape.y,_HurtShape.z,_HurtShape.w,0,1,0,1);
                float4 c=Over(Over(boost,heal),hurt);
                c.a=min(c.a,_MaximumOpacity); c*=i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a-.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
