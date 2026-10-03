from PIL import Image,ImageDraw,ImageFilter
import numpy as np,json
from pathlib import Path
base=Path(__file__).resolve().parent.parent
img=Image.open(base/'References/ThreeView.png').convert('RGB')
print('REFERENCE',img.size)
rgb=np.asarray(img,dtype=np.float32);lum=rgb.mean(2)
regions={
'blade':[(523,157),(650,147),(1120,134),(1658,117),(1726,128),(1763,149),(1782,180),(1790,223),(1781,260),(1759,295),(1723,316),(1677,328),(1040,316),(524,291)],
'case':[(36,314),(130,197),(269,113),(492,112),(523,128),(522,308),(423,393),(41,326)],
'top':[(203,479),(412,459),(497,482),(509,502),(509,614),(412,645),(203,630)]}
arrays={}
meta={}
for name,poly in regions.items():
 mask_im=Image.new('L',img.size);ImageDraw.Draw(mask_im).polygon(poly,fill=255)
 inside=np.asarray(mask_im.filter(ImageFilter.MinFilter(13)))>0
 if name=='blade':
  local=np.asarray(img.convert('L').filter(ImageFilter.GaussianBlur(7)),dtype=np.float32)
  raw=(lum<143)&(lum<local-12)&inside
  # Join the two thin outlines into the narrow connected band they bound.
  mi=Image.fromarray(raw.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(3))
 else:
  raw=(rgb[:,:,1]>92)&((rgb[:,:,0]-rgb[:,:,1])<18)&((rgb[:,:,1]-rgb[:,:,2])<21)&inside
  mi=Image.fromarray(raw.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
 m=(np.asarray(mi)>0)&inside
 # Sampling at 2 reference pixels keeps the drawn widths, without a million-vertex overlay.
 x0=min(p[0] for p in poly);y0=min(p[1] for p in poly);x1=max(p[0] for p in poly);y1=max(p[1] for p in poly)
 sub=m[y0:y1+1:2,x0:x1+1:2]
 # Remove isolated specks, retain the line network itself.
 pad=np.pad(sub,1);adj=sum(pad[1+dy:1+dy+sub.shape[0],1+dx:1+dx+sub.shape[1]] for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)])
 sub=sub&(adj>=1)
 arrays[name]=sub;meta[name]={'origin_px':[x0,y0],'step_px':2,'shape':list(sub.shape),'filled_samples':int(sub.sum()),'polygon_px':poly}
np.savez_compressed(base/'reference_masks.npz',**arrays)
(base/'reference_extraction.json').write_text(json.dumps({'reference_size':img.size,'method':'extract visible drawn line bands, reconstruct as raised meshes; no image projection shader','regions':meta},indent=2),encoding='utf8')
print(json.dumps(meta))
