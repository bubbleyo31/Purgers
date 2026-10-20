"""Encode Unity-rendered frames and verify pixels; no painting or image edits."""
from pathlib import Path
import json
from PIL import Image, ImageChops

root = Path(__file__).resolve().parent
def delta(a, b):
    return max(v[1] for v in ImageChops.difference(a.convert('RGB'), b.convert('RGB')).getextrema())

result = {'source': 'Unity Graphics.Blit; static historical screenshot', 'modes': {}}
for name, count in [('single', 40), ('continuous', 88)]:
    files = sorted((root / name).glob('*.png'))
    assert len(files) == count
    frames = [Image.open(p).convert('RGB') for p in files]
    atlas = Image.new('RGB', (960, 1620))
    for i, f in enumerate([frames[0], frames[9], frames[18]]):
        atlas.paste(f, (0, i * 540))
    palette = atlas.quantize(colors=256)
    encoded = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    target = root / (name + '-healing.gif')
    encoded[0].save(target, save_all=True, append_images=encoded[1:], duration=50,
                    loop=0, disposal=1, optimize=False)
    data = {'frames': count, 'fps': 20, 'duration_seconds': count / 20,
            'start_end_same': delta(frames[0], frames[-1]) == 0,
            'at_1_5_seconds_active': delta(frames[0], frames[30]) > 0,
            'gif_bytes': target.stat().st_size}
    assert data['start_end_same']
    assert data['at_1_5_seconds_active'] == (name == 'continuous')
    result['modes'][name] = data

before = Image.open(root / 'before.png').convert('RGB')
peak = Image.open(root / 'healing-peak.png').convert('RGB')
alpha = Image.open(root / 'overlay.png').getchannel('A')
old_alpha = Image.open(root.parent / 'overlay.png').getchannel('A')
center = (462, 280, 1458, 800)
result['center_alpha_max'] = alpha.crop(center).getextrema()[1]
result['center_rgb_difference_max'] = delta(before.crop(center), peak.crop(center))
result['top_alpha'] = alpha.getpixel((960, 10))
result['bottom_alpha'] = alpha.getpixel((1150, 1070))
result['pixels_alpha_above_8_old'] = sum(old_alpha.histogram()[9:])
result['pixels_alpha_above_8_new'] = sum(alpha.histogram()[9:])
assert result['center_alpha_max'] == result['center_rgb_difference_max'] == 0
assert result['top_alpha'] > 0 and result['bottom_alpha'] > 0
assert result['pixels_alpha_above_8_new'] < result['pixels_alpha_above_8_old']
regions = {'minimap': (20, 20, 244, 245), 'quest': (20, 258, 520, 357),
           'ability_q': (1336, 909, 1419, 991), 'ability_e': (1448, 909, 1531, 991)}
result['hud_rect_rgb_difference_max'] = {key: delta(before.crop(rect), peak.crop(rect)) for key, rect in regions.items()}
assert all(v == 0 for v in result['hud_rect_rgb_difference_max'].values())
points = [(200, 920), (400, 910), (155, 1000), (205, 1000), (120, 1000),
          (960, 900), (920, 940), (1780, 905), (1710, 980), (1370, 1020), (1490, 1020)]
result['hud_foreground_samples_unchanged'] = all(before.getpixel(p) == peak.getpixel(p) for p in points)
assert result['hud_foreground_samples_unchanged']
old_damage = Image.open(root.parent.parent / 'DamageEdgePreview_20261019' / 'single-peak.png')
result['damage_control_difference_max'] = delta(old_damage, Image.open(root / 'damage-control.png'))
assert result['damage_control_difference_max'] == 0
(root / 'pixel-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
