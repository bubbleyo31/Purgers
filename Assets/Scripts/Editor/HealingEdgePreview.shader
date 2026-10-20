Shader "Hidden/Purgers/Editor/HealingEdgePreview"
{
    Properties { _MainTex ("Background", 2D) = "black" {} }
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
            sampler2D _MainTex;
            float4 _Tint;
            float _Strength, _Width, _Corner, _Clock, _Weight, _Aspect, _Symbols, _VerticalWidth;
            float _BackgroundMode, _OnlyEffect, _ProtectHUD, _Damage, _Speed, _SpeedRate;

            #include "../../Shaders/Presentation/ScreenEdgeEffectsCore.cginc"

            // 歷史截圖的 HUD 已烘焙在背景內。以保留區模擬 HUD 位於特效上層；
            // 正式整合必須改用真正的 Canvas 排序，不能沿用這些截圖座標。
            float hudRect(float2 uv, float4 bounds)
            {
                float2 pixel = float2(uv.x, 1.0 - uv.y) * float2(1920, 1080);
                float2 outside = max(bounds.xy - pixel, pixel - bounds.zw);
                return 1.0 - step(.01, max(outside.x, outside.y));
            }

            float hudCoverage(float2 uv, float3 background)
            {
                #ifndef UNITY_COLORSPACE_GAMMA
                background = LinearToGammaSpace(background);
                #endif
                // 下方無底板 HUD 依截圖色彩保留圖形本身，避免留下大型矩形孔洞。
                float lo = min(background.r, min(background.g, background.b));
                float hi = max(background.r, max(background.g, background.b));
                float blue = step(background.r + .025, background.b) * step(background.g + .015, background.b);
                float red = step(.60, background.r) * step(background.g * 1.5, background.r);
                float white = step(.53, lo);
                float hudInk = max(max(blue, red), white);
                float h = hudRect(uv, float4(20, 20, 244, 245));
                h = max(h, hudRect(uv, float4(20, 258, 520, 357)));
                h = max(h, hudRect(uv, float4(68, 868, 712, 952)) * hudInk);
                h = max(h, hudRect(uv, float4(70, 950, 514, 1068)) * hudInk);
                h = max(h, hudRect(uv, float4(898, 880, 1027, 1044)) * max(hudInk, 1.0 - step(.14, hi)));
                h = max(h, hudRect(uv, float4(1600, 862, 1890, 1044)) * step(.40, lo));
                h = max(h, hudRect(uv, float4(1336, 909, 1419, 991)));
                h = max(h, hudRect(uv, float4(1448, 909, 1531, 991)));
                h = max(h, hudRect(uv, float4(1357, 1002, 1399, 1044)));
                h = max(h, hudRect(uv, float4(1474, 1002, 1516, 1044)));
                h = max(h, hudRect(uv, float4(1336, 992, 1531, 998)) * step(.5, background.r));
                return h;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float3 background = tex2D(_MainTex, uv).rgb;
                float4 effect = ScreenEdgeLayer(uv, _Clock, _Weight, _Aspect, _Tint,
                    _Strength, _Width, _VerticalWidth, _Corner, _Symbols, _Damage, _Speed, _SpeedRate);
                float3 tint = effect.rgb;
                float alpha = effect.a * (1.0 - hudCoverage(uv, background) * _ProtectHUD);
                if (_OnlyEffect > .5) return float4(tint, alpha);

                if (_BackgroundMode > .5 && _BackgroundMode < 1.5) background = float3(.055, .068, .075);
                if (_BackgroundMode > 1.5) background = float3(.82, .84, .80);
                return float4(lerp(background, tint, alpha), 1.0);
            }
            ENDCG
        }
    }
}
