"""Encode unmodified Unity frames and verify screen/HUD preservation."""
from pathlib import Path
import json
from PIL import Image, ImageChops

root = Path(__file__).resolve().parent
def delta(a, b):
    return max(v[1] for v in ImageChops.difference(a.convert('RGB'), b.convert('RGB')).getextrema())

paths = sorted((root / 'frames').glob('*.png'))
assert len(paths) == 145
frames = [Image.open(p).convert('RGB') for p in paths]
atlas = Image.new('RGB', (960, 1620))
for i, frame in enumerate([frames[0], frames[45], frames[80]]):
    atlas.paste(frame, (0, 540 * i))
palette = atlas.quantize(colors=256)
encoded = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
target = root / 'momentum-preview.gif'
encoded[0].save(target, save_all=True, append_images=encoded[1:], duration=40,
                loop=0, disposal=1, optimize=False)
before = Image.open(root / 'before.png').convert('RGB')
peak = Image.open(root / 'peak.png').convert('RGB')
alpha = Image.open(root / 'overlay.png').getchannel('A')
center = (462, 280, 1458, 800)
regions = {'minimap': (20, 20, 244, 245), 'quest': (20, 258, 520, 357),
           'ability_q': (1336, 909, 1419, 991), 'ability_e': (1448, 909, 1531, 991)}
samples = [(200, 920), (400, 910), (155, 1000), (205, 1000), (120, 1000),
           (960, 900), (920, 940), (1780, 905), (1710, 980), (1370, 1020), (1490, 1020)]
result = {
    'source': 'Unity Graphics.Blit; historical screenshot, no runtime integration',
    'frames': len(frames), 'fps': 25, 'duration_seconds': 5.8,
    'center_alpha_max': alpha.crop(center).getextrema()[1],
    'center_rgb_difference_max': delta(before.crop(center), peak.crop(center)),
    'all_frames_center_unchanged': all(delta(frames[0].crop((231, 140, 729, 400)), f.crop((231, 140, 729, 400))) == 0 for f in frames),
    'hud_rect_rgb_difference_max': {key: delta(before.crop(rect), peak.crop(rect)) for key, rect in regions.items()},
    'hud_foreground_samples_unchanged': all(before.getpixel(p) == peak.getpixel(p) for p in samples),
    'top_alpha': alpha.getpixel((960, 10)), 'bottom_alpha': alpha.getpixel((1150, 1070)),
    'start_end_same': delta(frames[0], frames[-1]) == 0,
    'full_state_has_motion': delta(frames[80], frames[85]) > 0,
    'healing_control_difference_max': delta(Image.open(root / 'healing-control.png'), Image.open(root.parent / 'HealingEdgePreview_20261019/v1.2/healing-peak.png')),
    'damage_control_difference_max': delta(Image.open(root / 'damage-control.png'), Image.open(root.parent / 'DamageEdgePreview_20261019/single-peak.png')),
    'gif_bytes': target.stat().st_size,
}
assert result['center_alpha_max'] == result['center_rgb_difference_max'] == 0
assert result['all_frames_center_unchanged']
assert all(v == 0 for v in result['hud_rect_rgb_difference_max'].values())
assert result['hud_foreground_samples_unchanged']
assert result['top_alpha'] > 0 and result['bottom_alpha'] > 0
assert result['start_end_same'] and result['full_state_has_motion']
assert result['healing_control_difference_max'] == result['damage_control_difference_max'] == 0
(root / 'pixel-verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
