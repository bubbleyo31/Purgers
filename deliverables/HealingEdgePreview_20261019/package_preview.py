"""Package unmodified Unity-rendered frames as a looping GIF; check exported pixels."""
from pathlib import Path
import json
from PIL import Image, ImageChops

root = Path(__file__).resolve().parent
paths = sorted((root / "frames").glob("*.png"))
assert len(paths) == 88
frames = [Image.open(p).convert("RGB") for p in paths]
# One palette for every frame prevents temporal palette flicker.
palette_source = Image.new("RGB", (960, 1080))
palette_source.paste(frames[0], (0, 0))
palette_source.paste(frames[26], (0, 540))
palette = palette_source.quantize(colors=256)
encoded = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
encoded[0].save(root / "healing-preview.gif", save_all=True, append_images=encoded[1:],
                duration=50, loop=0, disposal=1, optimize=False)

before = Image.open(root / "before.png").convert("RGB")
peak = Image.open(root / "healing-peak.png").convert("RGB")
alpha = Image.open(root / "overlay.png").getchannel("A")
center = (462, 280, 1458, 800)
center_diff = ImageChops.difference(before.crop(center), peak.crop(center))
result = {
    "source": "Unity Graphics.Blit; historical static gameplay background",
    "frame_count": len(paths), "fps": 20, "duration_seconds": 4.4,
    "center_alpha_max": alpha.crop(center).getextrema()[1],
    "center_rgb_difference_max": max(v[1] for v in center_diff.getextrema()),
    "edge_alpha_max": alpha.crop((0, 280, 50, 800)).getextrema()[1],
    "start_end_same": ImageChops.difference(frames[0], frames[-1]).getbbox() is None,
    "animation_changes": ImageChops.difference(frames[10], frames[40]).getbbox() is not None,
    "ultrawide_size": Image.open(root / "ultrawide.png").size,
    "gif_bytes": (root / "healing-preview.gif").stat().st_size,
}
assert result["center_alpha_max"] == 0
assert result["center_rgb_difference_max"] == 0
assert result["edge_alpha_max"] > 0
assert result["start_end_same"] and result["animation_changes"]
hud_regions = {
    "minimap": (20, 20, 244, 245), "quest": (20, 258, 520, 357),
    "ability_q": (1336, 909, 1419, 991), "ability_e": (1448, 909, 1531, 991),
}
result["hud_rgb_difference_max"] = {
    name: max(v[1] for v in ImageChops.difference(before.crop(rect), peak.crop(rect)).getextrema())
    for name, rect in hud_regions.items()
}
assert all(v == 0 for v in result["hud_rgb_difference_max"].values())
samples = [(200, 920), (400, 910), (155, 1000), (205, 1000), (120, 1000),
           (960, 900), (920, 940), (1780, 905), (1710, 980), (1370, 1020), (1490, 1020)]
result["hud_foreground_samples_unchanged"] = all(before.getpixel(p) == peak.getpixel(p) for p in samples)
assert result["hud_foreground_samples_unchanged"]
result["top_alpha"] = alpha.getpixel((960, 10))
result["bottom_alpha"] = alpha.getpixel((1150, 1070))
assert result["top_alpha"] > 0 and result["bottom_alpha"] > 0
(root / "pixel-verification.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result, indent=2))
