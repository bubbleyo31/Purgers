"""Encode original Unity frames, without painting or editing their content."""
from pathlib import Path
import json
from PIL import Image, ImageChops

root = Path(__file__).resolve().parent
before = Image.open(root / 'before.png').convert('RGB')
center = (462, 280, 1458, 800)
regions = {'minimap': (20, 20, 244, 245), 'quest': (20, 258, 520, 357),
           'ability_q': (1336, 909, 1419, 991), 'ability_e': (1448, 909, 1531, 991)}
samples = [(200, 920), (400, 910), (155, 1000), (205, 1000), (120, 1000),
           (960, 900), (920, 940), (1780, 905), (1710, 980), (1370, 1020), (1490, 1020)]

def delta(a, b):
    return max(v[1] for v in ImageChops.difference(a, b).getextrema())

result = {'source': 'Unity Graphics.Blit; historical static gameplay background', 'modes': {}}
for name, count in [('single', 70), ('continuous', 125)]:
    paths = sorted((root / name).glob('*.png'))
    assert len(paths) == count
    frames = [Image.open(p).convert('RGB') for p in paths]
    atlas = Image.new('RGB', (768, 1296))
    for index, frame in enumerate([frames[0], frames[12], frames[18]]):
        atlas.paste(frame, (0, index * 432))
    palette = atlas.quantize(colors=256)
    encoded = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
    target = root / (name + '-damage.gif')
    encoded[0].save(target, save_all=True, append_images=encoded[1:],
                    duration=40, loop=0, disposal=1, optimize=False)
    peak = Image.open(root / (name + '-peak.png')).convert('RGB')
    alpha = Image.open(root / (name + '-overlay.png')).getchannel('A')
    data = {
        'frames': count, 'fps': 25, 'duration_seconds': count / 25,
        'center_alpha_max': alpha.crop(center).getextrema()[1],
        'center_rgb_difference_max': delta(before.crop(center), peak.crop(center)),
        'hud_rect_rgb_difference_max': {key: delta(before.crop(rect), peak.crop(rect)) for key, rect in regions.items()},
        'hud_foreground_samples_unchanged': all(before.getpixel(p) == peak.getpixel(p) for p in samples),
        'top_alpha': alpha.getpixel((960, 10)), 'bottom_alpha': alpha.getpixel((1150, 1070)),
        'start_end_same': delta(frames[0], frames[-1]) == 0,
        'at_1_2_seconds_active': delta(frames[0], frames[30]) > 0,
        'gif_bytes': target.stat().st_size,
    }
    assert data['center_alpha_max'] == data['center_rgb_difference_max'] == 0
    assert all(v == 0 for v in data['hud_rect_rgb_difference_max'].values())
    assert data['hud_foreground_samples_unchanged'] and data['start_end_same']
    assert data['top_alpha'] > 0 and data['bottom_alpha'] > 0
    assert data['at_1_2_seconds_active'] == (name == 'continuous')
    result['modes'][name] = data

old_healing = Image.open(root.parent / 'HealingEdgePreview_20261019' / 'healing-peak.png').convert('RGB')
control = Image.open(root / 'healing-control.png').convert('RGB')
result['healing_control_difference_max'] = delta(old_healing, control)
assert result['healing_control_difference_max'] == 0
result['ultrawide_size'] = Image.open(root / 'ultrawide.png').size
(root / 'pixel-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
