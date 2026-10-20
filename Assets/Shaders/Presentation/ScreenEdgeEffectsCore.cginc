// Shared approved preview and runtime edge math. No background sampling or HUD mask.
#ifndef PURGERS_SCREEN_EDGE_CORE
#define PURGERS_SCREEN_EDGE_CORE
            float box(float2 p, float2 halfSize, float aa)
            {
                float2 d = abs(p) - halfSize;
                return 1.0 - smoothstep(-aa, aa, max(d.x, d.y));
            }

float4 ScreenEdgeLayer(float2 uv, float _Clock, float _Weight, float _Aspect,
    float4 _Tint, float _Strength, float _Width, float _VerticalWidth, float _Corner,
    float _Symbols, float _Damage, float _Speed, float _SpeedRate)
{
    if (_Weight <= 0.00001 || _Strength <= 0) return 0;
                float sideDistance = min(uv.x, 1.0 - uv.x);
                float verticalDistance = min(uv.y, 1.0 - uv.y);
                float width = max(_Width, .001);
                // The mask has an exact zero plateau in the central aiming area.
                float side = pow(1.0 - smoothstep(0.0, width, sideDistance), 1.7);
                // 治療四角與符號隨寬高向外收攏；受傷版維持原尺寸。
                float healingX = _Damage > .5 ? 1.0 : min(1.0, _Width / .14);
                float healingY = _Damage > .5 ? 1.0 : min(1.0, _VerticalWidth / .10);
                float corner = (1.0 - smoothstep(0.0, width * 1.65, sideDistance)) *
                               (1.0 - smoothstep(0.0, .25 * healingY, verticalDistance));
                float vertical = pow(1.0 - smoothstep(0.0, max(.001, _VerticalWidth), verticalDistance), 1.7);
                float breathe = .92 + .08 * sin(_Clock * 2.6);
                float flow = .86 + .09 * sin(uv.y * 17.0 - _Clock * 1.5) +
                            .05 * sin(uv.y * 31.0 + _Clock * .8);
                float mask = saturate(max(side * flow, vertical * .82) + corner * _Corner);
                if (_Damage > .5)
                {
                    // 受傷使用不規則紅色壓邊，不加入治療十字或假造受擊方向。
                    float grain = .91 + .06 * sin(uv.x * 79.0 + uv.y * 51.0) +
                                  .03 * sin(uv.x * 143.0 - uv.y * 117.0);
                    mask *= grain;
                    breathe = 1.0;
                }
                float glowAlpha = saturate(mask * _Strength * _Weight * breathe);
                // A narrow, soft moving light seam gives the wash a little structure.
                float seamPosition = .008 + .002 * sin(uv.y * 16.0 - _Clock * 1.4);
                float seam = exp(-pow((sideDistance - seamPosition) / .005, 2.0));
                seam *= (.25 + .75 * pow(abs(uv.y * 2.0 - 1.0), 2.0));
                if (_Damage > .5) seam *= .30;

                float symbols = 0;
                [unroll] for (int i = 0; i < 14; ++i)
                {
                    float index = floor(i * .5);
                    float right = fmod((float)i, 2.0);
                    float seed = frac(sin(i * 57.31 + 12.9) * 43758.5453);
                    float phase = frac(_Clock * (.12 + seed * .035) + i * .173);
                    float x = .024 + index * .012 + .003 * sin(_Clock * .7 + i);
                    x *= healingX;
                    x = lerp(x, 1.0 - x, right);
                    float y = .22 + phase * .63;
                    if (i >= 8)
                    {
                        x = .34 + floor((i - 8) * .5) * .16 + .008 * sin(_Clock * .6 + i);
                        y = lerp(.035 + phase * .025, .965 - phase * .025, right);
                        if (healingY < 1.0)
                        {
                            float edgeOffset = (.035 + phase * .025) * healingY;
                            y = lerp(edgeOffset, 1.0 - edgeOffset, right);
                        }
                    }
                    float2 p = (uv - float2(x, y)) * float2(_Aspect, 1.0);
                    float size = .0055 + seed * .003;
                    float aa = max(fwidth(p.x), fwidth(p.y));
                    float crossShape = max(box(p, float2(size, size * .30), aa),
                                           box(p, float2(size * .30, size), aa));
                    float fade = smoothstep(0.0, .17, phase) * (1.0 - smoothstep(.73, 1.0, phase));
                    symbols += crossShape * fade * step((float)i + .5, _Symbols);
                }
                float detailAlpha = saturate((symbols * .72 + seam * .16) * _Weight * _Strength);
                float alpha = 1.0 - (1.0 - glowAlpha) * (1.0 - detailAlpha);
                float3 tint = lerp(_Tint.rgb, float3(.88, 1.0, .92),
                                   saturate(detailAlpha / max(alpha, .0001)) * .7);
                if (_Damage > .5)
                {
                    float outer = 1.0 - smoothstep(0.0, .035, min(sideDistance, verticalDistance));
                    tint = lerp(_Tint.rgb, _Tint.rgb * float3(.52, .32, .40), outer * .68);
                }
                if (_Speed > .5)
                {
                    float aura = saturate(max(side * .75, vertical * .82) + corner * _Corner);
                    float rush = .92 + .08 * sin(_Clock * 5.0 + uv.y * 9.0);
                    float auraAlpha = aura * _Strength * _Weight * .72 * rush;
                    // Aura only: no radial streaks or moving line highlights.
                    alpha = auraAlpha;
                    tint = _Tint.rgb;
                }
return float4(tint, alpha);
}
#endif
