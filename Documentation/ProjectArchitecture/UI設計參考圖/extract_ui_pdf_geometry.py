from pypdf import PdfReader
from pathlib import Path
import json, math
# Reproduce the vector outlines for this exact Canva export; this is not a general PDF importer.
# Requires pypdf. Run from any working directory; UI.pdf is read, never modified.
import argparse
parser=argparse.ArgumentParser()
parser.add_argument('--pdf',type=Path,default=Path(__file__).with_name('UI.pdf'))
parser.add_argument('--output',type=Path,default=Path(__file__).with_name('UI-PDF-geometry.json'))
args=parser.parse_args()
pdf=PdfReader(args.pdf)
assert len(pdf.pages)==5
assert abs(float(pdf.pages[0].mediabox.width)-1440)<.001
assert abs(float(pdf.pages[0].mediabox.height)-810)<.001
all_paths={}
def mul(a,b):
    return [a[0]*b[0]+a[1]*b[2],a[0]*b[1]+a[1]*b[3],a[2]*b[0]+a[3]*b[2],a[2]*b[1]+a[3]*b[3],a[4]*b[0]+a[5]*b[2]+b[4],a[4]*b[1]+a[5]*b[3]+b[5]]
for pn in [0,2]:
    m=[1,0,0,1,0,0]; clips=[]; stack=[]; path=[]; result=[]
    def point(x,y):
        return [(x*m[0]+y*m[2]+m[4])/0.75,(817.92-(x*m[1]+y*m[3]+m[5]))/0.75]
    for n,(v,op) in enumerate(pdf.pages[pn].get_contents().operations):
        v=[float(x) if isinstance(x,(int,float)) else x for x in v]
        if op==b'q': stack.append((m[:],clips[:]))
        elif op==b'Q': m,clips=stack.pop()
        elif op==b'cm': m=mul(v,m)
        elif op in [b'm',b'l',b'c']:
            path.append([op.decode()]+[point(v[i],v[i+1]) for i in range(0,len(v),2)])
        elif op==b're':
            x,y,w,h=v;path += [['m',point(x,y)],['l',point(x+w,y)],['l',point(x+w,y+h)],['l',point(x,y+h)],['h']]
        elif op==b'h':path.append(['h'])
        elif op in [b'W',b'W*']:clips.append(path[:])
        elif op in [b'n',b'f',b'f*',b'S',b's']:path=[]
        elif op==b'Do':
            p=clips[-1] if clips else []; pts=[xy for cmd in p for xy in cmd[1:]]
            bb=[min(x[0] for x in pts),min(x[1] for x in pts),max(x[0] for x in pts),max(x[1] for x in pts)] if pts else None
            result.append(dict(name=str(v[0]),op=n,bounds=bb,path=p))
    all_paths[pn+1]=result

mapping={1:{'Minimap':'/X10','Panel':'/X17','Momentum':'/X9','Experience':'/X26','SkillSlot':'/X11','Key':'/X22','HealthCross':'/X15','HealthCell':'/X35','SkillStar':'/X43','GrappleOuter':'/X41','GrappleInner':'/X42','Crosshair':'/X16','SkillKeyQ':'/X33','SkillKeyE':'/X31'},3:{'RewardDescription':'/X43','RewardDescriptionMiddle':'/X47','RewardDescriptionRight':'/X44','RewardHex':'/X45','RewardHexMiddle':'/X50','RewardHexRight':'/X46','BracketLeft':'/X48','BracketMiddle':'/X51','BracketRight':'/X49','MouseLeft':'/X52','MouseMiddle':'/X54','MouseRight':'/X56'}}
shapes=[]
for page,lookup in mapping.items():
    data={r['name']:r for r in all_paths[page]}
    for name,src in lookup.items():
        row=data[src]; points=[]
        for cmd in row['path']:
            if cmd[0] in ['m','l']:points.append(cmd[1])
            elif cmd[0]=='c':
                a=points[-1];b,c,d=cmd[1:]
                for i in range(1,25):
                    t=i/24;points.append([(1-t)**3*a[j]+3*(1-t)**2*t*b[j]+3*(1-t)*t*t*c[j]+t**3*d[j] for j in range(2)])
        if points[-1]==points[0]:points.pop()
        x=min(p[0] for p in points);y=min(p[1] for p in points);w=max(p[0] for p in points)-x;h=max(p[1] for p in points)-y
        shapes.append(dict(name=name,page=page,source=src,x=round(x,5),y=round(y,5),width=round(w,5),height=round(h,5),points=[dict(x=round((p[0]-x)/w,7),y=round((p[1]-y)/h,7)) for p in points]))
        print(name, *(round(n,4) for n in [x,y,w,h]))
out=args.output
out.write_text(json.dumps(dict(referenceWidth=1920,referenceHeight=1080,pdfPageWidthPt=1440,pdfPageHeightPt=810,shapes=shapes),ensure_ascii=False,indent=2),encoding='utf8')
