from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json
R=Path('M:/UnityProject/Purgers');O=R/'deliverables/UIAudit_20261018';a=json.loads((O/'asset-inventory.json').read_text('utf-8'));f=ImageFont.truetype('C:/Windows/Fonts/msjh.ttc',17)
rows=[x for x in a if x['group']=='project_vfx' or '/Art/VFX/' in x['path']]
S=Image.new('RGB',(1400,70+220*((len(rows)+4)//5)),(23,29,22));d=ImageDraw.Draw(S);d.text((20,15),'自有 VFX 原圖總覽（不代表播放驗收）',font=f,fill='white')
for i,x in enumerate(rows):
 try:
  im=Image.open(R/x['path']).convert('RGBA');im.thumbnail((265,160));xx=i%5*280;yy=70+i//5*220;S.paste(im,(xx+(280-im.width)//2,yy),im);d.text((xx+4,yy+165),Path(x['path']).name[:29],font=f,fill='white');d.text((xx+4,yy+190),str((x.get('width'),x.get('height'))),font=f,fill='white')
 except:pass
S.save(O/'contact-vfx.jpg',quality=93)
sel=['TeamInfo/ui-preview.png','AbilitySlotStates/ui-preview.png','PlayerSettings/flat-controls-1600.png','SafeHouseStyle/unity-preview-1920.png','MenuBrushV2/main-isolated.png','MenuWindowsV2/party-final.png','LootPresentation/loot-bold-20261017.png','PlayerRescue/Recipient/being-rescued.png','SpectatorSwitch/hud-preview.png','StatusEffects/ui-preview.png','GrowthIcons/SlabAdaptive/Cards_2_1280x720.png','FirstPersonGoggles/goggles-2560x1080-t1.00-a0.94.png']
S=Image.new('RGB',(1800,320*6),(23,29,22));d=ImageDraw.Draw(S)
for i,p in enumerate(sel):
 im=Image.open(R/'Documentation/ProjectArchitecture/Validation'/p).convert('RGB');im.thumbnail((880,280));x=i%2*900;y=i//2*320;S.paste(im,(x+(900-im.width)//2,y+25));d.text((x+8,y+3),p,font=f,fill='white')
S.save(O/'reviewed-ui-evidence.jpg',quality=94)
print('vfx',len(rows));print([(x['path'],x['width'],x['height']) for x in a if any(s in x['path'] for s in ['S05_','S04_','WeaponIcons','BulletTime','GrappleBlocked'])])
