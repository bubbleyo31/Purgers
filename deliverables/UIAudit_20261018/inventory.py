from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,re,hashlib,subprocess,collections
R=Path('M:/UnityProject/Purgers');O=R/'deliverables/UIAudit_20261018'
records=[]
for p in (R/'Assets').rglob('*'):
 if p.suffix.lower() not in {'.png','.jpg','.jpeg','.tga','.psd','.bmp','.tif','.tiff','.exr','.hdr','.svg'}:continue
 rel=p.relative_to(R).as_posix();mp=Path(str(p)+'.meta');m=mp.read_text('utf-8-sig',errors='replace') if mp.exists() else ''
 def f(k):
  x=re.search(r'^\s*'+re.escape(k)+r':\s*(.*)$',m,re.M);return x.group(1).strip() if x else None
 d={'path':rel,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
 for k in ['textureType','maxTextureSize','enableMipMap','filterMode','isReadable','spriteMode','alphaIsTransparency','guid']:d[k]=f(k)
 d['group']='project_ui' if '/_Project_Assets/UI/' in rel else 'project_image' if '/_Project_Assets/Image/' in rel else 'project_vfx' if '/_Project_Assets/VFX/' in rel else 'other_sprite' if d['textureType']=='8' else 'other_texture'
 try:
  with Image.open(p) as im:
   d.update(width=im.width,height=im.height,mode=im.mode)
   if d['group'] in ['project_ui','project_image','other_sprite']:
    a=im.convert('RGBA').getchannel('A');bbox=a.point(lambda x:255 if x>16 else 0).getbbox();h=a.histogram();d['alpha_bbox16']=bbox
    d['nontransparent_fraction']=round(sum(h[17:])/(im.width*im.height),5);d['semitransparent_fraction']=round(sum(h[1:255])/(im.width*im.height),5)
    if bbox:d['bbox_area_fraction']=round((bbox[2]-bbox[0])*(bbox[3]-bbox[1])/(im.width*im.height),5)
 except Exception as e:d['decode_note']=str(e)
 records.append(d)
records.sort(key=lambda d:d['path'])
(O/'asset-inventory.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),'utf-8')
s={'total':len(records),'groups':dict(collections.Counter(d['group'] for d in records)),'source_bytes':sum(d['bytes'] for d in records),'note':'Source files only; not build inclusion or GPU memory.'}
(O/'inventory-summary.json').write_text(json.dumps(s,ensure_ascii=False,indent=2),'utf-8')
font=ImageFont.truetype('C:/Windows/Fonts/msjh.ttc',14);title=ImageFont.truetype('C:/Windows/Fonts/msjhbd.ttc',24)
for group in ['project_ui','project_image','other_sprite']:
 rows=[d for d in records if d['group']==group and 'width' in d]
 for page in range((len(rows)+39)//40):
  b=rows[page*40:(page+1)*40];sheet=Image.new('RGB',(1400,70+((len(b)+4)//5)*200),(24,29,27));dr=ImageDraw.Draw(sheet);dr.text((20,18),f'{group} | {page+1} | SOURCE THUMBNAILS',font=title,fill=(238,230,204))
  for i,d in enumerate(b):
   x=i%5*280;y=70+i//5*200;im=Image.open(R/d['path']).convert('RGBA');im.thumbnail((250,140),Image.Resampling.LANCZOS);sheet.paste(im,(x+(280-im.width)//2,y+(140-im.height)//2),im)
   dr.text((x+8,y+144),Path(d['path']).name[:30],font=font,fill=(230,232,220));dr.text((x+8,y+166),f"{d['width']}x{d['height']} max {d['maxTextureSize']} fill {d.get('bbox_area_fraction',1):.2f}",font=font,fill=(173,185,170))
  sheet.save(O/f'contact-{group}-{page+1:02}.jpg',quality=92)
(O/'git-status-before.txt').write_text(subprocess.run(['git','status','--short'],cwd=R,capture_output=True,text=True,encoding='utf-8').stdout,'utf-8')
protected={}
for base in ['Assets/Scripts','Assets/Scenes','Assets/Prefabs/UI','Assets/Resources/Presentation','Assets/_Project_Assets/UI','Assets/_Project_Assets/Image']:
 for p in (R/base).rglob('*'):
  if p.is_file():protected[p.relative_to(R).as_posix()]=hashlib.sha256(p.read_bytes()).hexdigest()
(O/'protected-hashes-before.json').write_text(json.dumps(protected,ensure_ascii=False,indent=2),'utf-8')
print(json.dumps(s));print('protected_files',len(protected))
