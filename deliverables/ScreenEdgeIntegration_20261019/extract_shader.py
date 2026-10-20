"""One-time promotion of the inspected preview math to a shared include."""
from pathlib import Path
root = Path(__file__).resolve().parents[2]
preview = root / 'Assets/Scripts/Editor/HealingEdgePreview.shader'
s = preview.read_text(encoding='utf-8')
assert 'float speedStreaks(float2 uv)' in s
begin = s.index('            // 沿透視射線')
end = s.index('            // 歷史截圖')
helpers = s[begin:end].replace('float speedStreaks(float2 uv)', 'float speedStreaks(float2 uv, float _Aspect, float _Clock, float _SpeedRate, float _Weight)')
start = s.index('                float sideDistance')
finish = s.index('                if (_OnlyEffect')
body = s[start:finish].replace('speedStreaks(uv)', 'speedStreaks(uv, _Aspect, _Clock, _SpeedRate, _Weight)')
body = '\n'.join(line for line in body.splitlines() if 'hudCoverage' not in line)
signature = '''float4 ScreenEdgeLayer(float2 uv, float _Clock, float _Weight, float _Aspect,
    float4 _Tint, float _Strength, float _Width, float _VerticalWidth, float _Corner,
    float _Symbols, float _Damage, float _Speed, float _SpeedRate)
{
    if (_Weight <= 0.00001 || _Strength <= 0) return 0;
'''
folder = root / 'Assets/Shaders/Presentation'
folder.mkdir(parents=True, exist_ok=True)
(folder / 'ScreenEdgeEffectsCore.cginc').write_text('// Shared approved preview and runtime edge math. No background sampling or HUD mask.\n#ifndef PURGERS_SCREEN_EDGE_CORE\n#define PURGERS_SCREEN_EDGE_CORE\n'+helpers+signature+body+'\nreturn float4(tint, alpha);\n}\n#endif\n', encoding='utf-8')
replacement = '''                float4 effect = ScreenEdgeLayer(uv, _Clock, _Weight, _Aspect, _Tint,
                    _Strength, _Width, _VerticalWidth, _Corner, _Symbols, _Damage, _Speed, _SpeedRate);
                float3 tint = effect.rgb;
                float alpha = effect.a * (1.0 - hudCoverage(uv, background) * _ProtectHUD);
'''
s = s[:start]+replacement+s[finish:]
s = s[:begin]+'            #include "../../Shaders/Presentation/ScreenEdgeEffectsCore.cginc"\n\n'+s[end:]
preview.write_text(s, encoding='utf-8')
