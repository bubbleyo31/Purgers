from pathlib import Path
import json, math
from PIL import Image, ImageDraw, ImageFont
P=Path(__file__).parent
data=json.loads((P/'timelines.json').read_text(encoding='utf-8'))
W,H=960,360
fontpath='C:/Windows/Fonts/msjh.ttc'
boldpath='C:/Windows/Fonts/msjhbd.ttc'
def font(n,b=False):return ImageFont.truetype(boldpath if b else fontpath,n)
C={'bg':(23,33,35),'cream':(241,237,217),'gold':(213,196,94),'red':(228,122,104),'green':(184,216,173),'empty':(43,57,52),'muted':(174,183,169)}
def mix(a,b,t):return tuple(round(x*(1-t)+y*t) for x,y in zip(a,b))
def draw_state(s,old,age,name):
 im=Image.new('RGB',(W,H),C['bg']);d=ImageDraw.Draw(im)
 d.text((32,20),'PURGERS / '+name,font=font(18,True),fill=C['cream'])
 d.text((W-177,24),'UI PREVIEW',font=font(13),fill=C['gold'])
 d.text((32,63),'HEALTH',font=font(13),fill=C['muted'])
 d.text((30,77),str(s['hp']),font=font(58,True),fill=C['red'] if s['hp']/s['max']<=.25 else C['cream'])
 nw=d.textlength(str(s['hp']),font=font(58,True))
 d.text((42+nw,116),'/ '+str(s['max']),font=font(21),fill=C['muted'])
 if s['shield']>0:d.text((33,151),'+ '+str(s['shield'])+' 護盾',font=font(20,True),fill=C['gold'])
 cap=math.ceil(s['max']/20)+math.ceil(s['shieldCap']/20)
 step=min(26,890/max(1,cap));gap=min(3,step*.18);cw=step-gap;slant=min(4,cw*.28);y=192;bh=32
 def cell(i,color,alpha=1):
  x=32+i*step
  d.polygon([(x+slant,y),(x+cw,y),(x+cw-slant,y+bh),(x,y+bh)],fill=mix(C['bg'],color,max(0,min(1,alpha))))
 for i in range(cap):cell(i,C['empty'])
 t=min(1,age/.3)
 hpcol=C['red'] if s['hp']>0 and s['hp']/s['max']<=.25 else C['cream']
 for i in range(s['hc']):
  new=old is not None and i>=old['hc']
  cell(i,mix(C['green'],hpcol,t) if new else hpcol,.55+.45*t if new else 1)
 for i in range(s['sc']):
  new=old is not None and i>=old['sc']
  cell(s['hc']+i,mix(C['cream'],C['gold'],t) if new else C['gold'],.55+.45*t if new else 1)
 if old is not None and t<1:
  alpha=1-max(0,(t-.26)/.74)
  for i in range(s['hc'],old['hc']):
   if i>=s['hc']+s['sc']:cell(i,C['red'],alpha)
  for i in range(s['sc'],old['sc']):
   pos=old['hc']+i
   if pos>=s['hc']+s['sc']:cell(pos,mix(C['gold'],C['red'],.55),alpha)
 d.text((32,239),'生命 '+str(s['hc'])+' 格',font=font(16),fill=C['cream'])
 d.text((173,239),'護盾 '+str(s['sc'])+' 格',font=font(16),fill=C['gold'])
 d.text((315,239),'每格 20 HP',font=font(16),fill=C['muted'])
 bank=s['hb'] or s['sb']
 d.text((500,239),'未滿格變化 '+str(bank)+' HP',font=font(16),fill=C['muted'])
 d.line((32,278,W-32,278),fill=(60,72,63),width=1)
 event=s['event'].replace(' · ','  /  ')
 d.text((32,297),event,font=font(19,True),fill=C['cream'])
 return im
# One shared global palette prevents frame-to-frame color flicker.
palette=Image.new('P',(1,1));colors=[]
for c in C.values():
 for i in range(16):colors.extend(mix(C['bg'],c,i/15))
for i in range(16):colors.extend(mix(C['green'],C['cream'],i/15))
colors=(colors+[0]*768)[:768];palette.putpalette(colors)
for key,g in data.items():
 frames=[];dur=[]
 def add(s,old,age,ms=70):
  frames.append(draw_state(s,old,age,g['name']).quantize(palette=palette,dither=Image.Dither.NONE));dur.append(ms)
 add(g['initial'],None,1,1000)
 for st in g['steps']:
  for j in range(6):add(st['after'],st['before'],j*.07)
  add(st['after'],st['before'],1,830)
 add(g['steps'][-1]['after'],None,1,1400)
 out=P/('health-'+key+'.gif')
 frames[0].save(out,save_all=True,append_images=frames[1:],duration=dur,loop=0,disposal=2,optimize=False)
 draw_state(g['initial'],None,1,g['name']).save(P/('health-'+key+'-still.png'))
 print(out.name, len(frames), out.stat().st_size)
