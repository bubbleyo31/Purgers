Shader "Purgers/TeammateOutline"
{
    Properties
    {
        _OutlineColor("Outline Color",Color)=(1,1,1,1)
        _OutlinePixels("Outline Pixels",Float)=2
        _VisibleAlpha("Visible Alpha",Float)=0.28
        _HiddenAlpha("Hidden Alpha",Float)=0.9
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _OutlineColor;
        float _OutlinePixels, _VisibleAlpha, _HiddenAlpha;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS=TransformObjectToHClip(v.positionOS.xyz);return o;
        }
        Varyings Expanded(Attributes v)
        {
            Varyings o=Vert(v);
            float3 normalVS=TransformWorldToViewDir(TransformObjectToWorldNormal(v.normalOS));
            float2 projected=mul((float2x2)UNITY_MATRIX_P,normalVS.xy);
            projected/=max(length(projected),0.00001);
            o.positionCS.xy+=projected*(_OutlinePixels*2.0/_ScaledScreenParams.xy)*o.positionCS.w;
            return o;
        }
        Varyings Fullscreen(uint id:SV_VertexID)
        {Varyings o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=GetFullScreenTriangleVertexPosition(id);return o;}
        half4 Empty(Varyings i):SV_Target{return 0;}
        half4 Visible(Varyings i):SV_Target{return half4(_OutlineColor.rgb,_VisibleAlpha);}
        half4 Hidden(Varyings i):SV_Target{return half4(_OutlineColor.rgb,_HiddenAlpha);}
        ENDHLSL
        Pass
        {
            Name "ClearReservedStencil"
            Cull Off ZWrite Off ZTest Always ColorMask 0
            Stencil { Ref 0 ReadMask 128 WriteMask 128 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Fullscreen
            #pragma fragment Empty
            ENDHLSL
        }
        Pass
        {
            Name "SilhouetteMask"
            Cull Off ZWrite Off ZTest Always ColorMask 0
            Stencil { Ref 128 ReadMask 128 WriteMask 128 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Empty
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "VisibleContour"
            Cull Front ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 128 ReadMask 128 WriteMask 0 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Expanded
            #pragma fragment Visible
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "OccludedContour"
            Cull Front ZWrite Off ZTest Greater Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 128 ReadMask 128 WriteMask 0 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Expanded
            #pragma fragment Hidden
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
