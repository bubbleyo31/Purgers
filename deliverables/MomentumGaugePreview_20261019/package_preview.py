from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

folder = Path(r'M:\UnityProject\Purgers\deliverables\MomentumGaugePreview_20261019')
font = ImageFont.truetype(r'C:\Windows\Fonts\msjh.ttc', 20)
small = ImageFont.truetype(r'C:\Windows\Fonts\msjh.ttc', 16)
frames = []
for path in sorted((folder / 'frames').glob('*.png')):
    canvas = Image.new('RGB', (947, 310), (25, 32, 30))
    draw = ImageDraw.Draw(canvas)
    draw.text((52, 18), 'Unity 量條預覽 · 固定尺寸 · #25FFD9', font=font, fill=(224, 233, 226))
    draw.text((52, 61), '一般 65%｜藍色・慢速流動', font=small, fill=(171, 187, 178))
    normal = Image.open(folder / 'normal-frames' / path.name).convert('RGB').resize((843, 75), Image.Resampling.LANCZOS)
    canvas.paste(normal, (52, 88))
    draw.text((52, 184), '滿值 100%｜#25FFD9・兩倍流速', font=small, fill=(171, 187, 178))
    full = Image.open(path).convert('RGB').resize((843, 75), Image.Resampling.LANCZOS)
    canvas.paste(full, (52, 215))
    frames.append(canvas)
if len(frames) != 96:
    raise RuntimeError(f'Expected 96 Unity frames; found {len(frames)}')
frames[0].save(folder / 'preview.png')
durations = [50 if i % 6 == 2 else 40 for i in range(len(frames))]
frames[0].save(folder / 'preview.gif', save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True)
print(f'Packaged {len(frames)} Unity-rendered frames; GIF bytes={(folder / "preview.gif").stat().st_size}')
