from pathlib import Path
from collections import Counter
from PIL import Image, ImageChops
import json

root=Path(__file__).resolve().parents[2]
folder=root/'Documentation/ProjectArchitecture/Validation/ScreenEdgeEffects'
def rgb(path): return Image.open(path).convert('RGB')
def delta(a,b): return max(x[1] for x in ImageChops.difference(a,b).getextrema())
before=rgb(folder/'before.png')
# Exact solid HUD ink colors, excluding the flat world background and antialias edges.
colors=[c for c,n in Counter(before.getdata()).most_common(20) if max(c)-min(c)>50 or min(c)>200]
mask=[i for i,c in enumerate(before.getdata()) if c in colors]
result={'source':'Actual UGUI shader plus cloned real HUD via offscreen camera; no screenshot mask',
        'solid_hud_samples':len(mask),'modes':{},'shared_shader_parity':{}}
assert len(mask)>10000
for name in ['healing','damage','momentum','combined']:
    im=rgb(folder/(name+'.png'));pixels=list(im.getdata());baseline=list(before.getdata())
    data={'center_rgb_difference_max':delta(before.crop((500,300,1400,800)),im.crop((500,300,1400,800))),
          'solid_hud_difference_max':max(max(abs(a-b) for a,b in zip(baseline[i],pixels[i])) for i in mask),
          'has_effect':delta(before,im)>0}
    # Near-opaque sprite ink can retain <= 1/255 background contribution after quantization.
    # Preserve the center exactly and report the measured HUD difference.
    assert data['center_rgb_difference_max']==0
    assert data['solid_hud_difference_max']<=1
    assert data['has_effect']
    result['modes'][name]=data
refs={'healing':'HealingEdgePreview_20261019/v1.2/healing-peak.png','damage':'DamageEdgePreview_20261019/single-peak.png','momentum':'MomentumEdgePreview_20261019/peak.png'}
for name,reference in refs.items():
    result['shared_shader_parity'][name]=delta(rgb(folder/(name+'-parity.png')),rgb(root/'deliverables'/reference))
    assert result['shared_shader_parity'][name]==0
(folder/'pixel-verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result,indent=2))
