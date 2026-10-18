from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json
R=Path('M:/UnityProject/Purgers');O=R/'deliverables/UIAudit_20261018';f=ImageFont.truetype('C:/Windows/Fonts/msjh.ttc',18)
files=['Assets/_Project_Assets/UI/GrowthIcons/V5/S05_HardChoice.png','Assets/_Project_Assets/UI/GrowthIcons/V5/S04_CarnivorousTree.png','Assets/_Project_Assets/UI/BattleHUD/WeaponIcon/AttackWeapon.png','Assets/_Project_Assets/UI/BattleHUD/WeaponIcon/SupportWeapon.png','Assets/_Project_Assets/UI/BattleHUD/WeaponIcon/TankWeapon.png','Assets/_Project_Assets/UI/StatusEffects/V1/BulletTime.png','Assets/_Project_Assets/UI/StatusEffects/V1/GrappleBlocked.png']
S=Image.new('RGB',(1410,85+len(files)*160),(23,29,22));d=ImageDraw.Draw(S);d.text((20,15),'原圖合成檢查：保留原色/alpha，未清理；每格 16 / 24 / 48 / 96 px',font=f,fill='white');d.text((20,43),'這是來源圖縮圖測試，並非 Unity 世界 UI／色覺或自然遊玩驗收。',font=f,fill='white')
for j,p in enumerate(files):
 im=Image.open(R/p).convert('RGBA');y=85+j*160;d.text((10,y+5),Path(p).stem,font=f,fill='white')
 for k,bg in enumerate([(23,29,22),(242,238,219),(0,0,0)]):
  x=300+k*370;d.rectangle((x,y,x+360,y+150),fill=bg)
  for z,size in enumerate([16,24,48,96]):
   t=im.resize((size,size),Image.Resampling.LANCZOS);S.paste(t,(x+10+z*85+(80-size)//2,y+16+(100-size)//2),t);d.text((x+25+z*85,y+122),str(size),font=f,fill=(180,170,130) if k!=1 else (35,40,25))
S.save(O/'alpha-native-size-review.png')
# Full 32:9 images are retained; side-by-side plate uses a centered 1800px viewport crop for inspection.
S=Image.new('RGB',(1800,680),(16,21,15));d=ImageDraw.Draw(S)
for j,(name,title) in enumerate([('tab-current-3840.png','目前設定：32:9 高度超出'),('tab-proposed-3840.png','提案：依可用高度縮放＋溢出提示')]):
 im=Image.open(O/name).crop((1020,0,2820,1080));im=im.resize((900,540),Image.Resampling.LANCZOS);S.paste(im,(900*j,65));d.text((900*j+20,18),title,font=f,fill=(242,238,219))
d.text((20,625),'正式元件＋示例資料，Unity 離屏渲染；中央區域對照，完整 3840×1080 原圖另附。',font=f,fill=(242,238,219));d.text((20,650),'僅暫存預覽調整，未修改專案。此圖縮小展示；文字可讀性須看原始尺寸。',font=f,fill=(180,190,160));S.save(O/'tab-ultrawide-comparison.png')
print('generated analysis plates')
