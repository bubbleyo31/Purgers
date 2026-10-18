"""Original geometric rank font, drawn as vector outlines; no source image pixels copied."""
import struct, pathlib
P=lambda fmt,*v:struct.pack('>'+fmt,*v)
def checksum(b):
 b+=b'\0'*((-len(b))%4)
 return sum(struct.unpack('>'+'I'*(len(b)//4),b))&0xffffffff
def glyph(pts):
 if not pts:return b''
 xs,ys=zip(*pts)
 b=P('hhhhh',1,min(xs),min(ys),max(xs),max(ys))+P('HH',len(pts)-1,0)+bytes([1])*len(pts)
 for seq in (xs,ys):
  prev=0
  for x in seq:b+=P('h',x-prev);prev=x
 return b+b'\0'*((-len(b))%4)
# I follows reference ratios: width 430, height 750, stem 180, horizontal bars 130.
i=[(40,0),(470,0),(470,130),(345,130),(345,620),(470,620),(470,750),(40,750),(40,620),(165,620),(165,130),(40,130)]
v=[(40,750),(240,750),(240,620),(205,620),(330,215),(455,620),(420,620),(420,750),(620,750),(620,620),(590,620),(390,0),(270,0),(70,620),(40,620)]
glyphs=[b'',b'',glyph(i),glyph(v),glyph([(40,-100),(470,-100),(470,-40),(40,-40)])]
glyf=b''.join(glyphs);offs=[0]
for g in glyphs:offs.append(offs[-1]+len(g))
t={}
t['glyf']=glyf;t['loca']=P('6I',*offs)
t['head']=P('IIIIHHQQhhhhHHhhh',0x10000,0x10000,0,0x5f0f3cf5,3,1000,0,0,0,-100,660,750,1,8,2,1,0)
t['hhea']=P('IhhhHhhhhhhhhhhhH',0x10000,800,-200,0,700,0,0,660,1,0,0,0,0,0,0,0,5)
t['hmtx']=b''.join(P('Hh',a,0 if k<2 else 40) for k,a in enumerate([500,250,510,660,510]))
t['maxp']=P('I14H',0x10000,5,16,1,0,0,2,0,0,0,0,0,0,0,0)
# cmap format 4: space, I, V, sentinel.
ends=[32,73,86,95,65535];starts=ends;deltas=[1-32,2-73,3-86,4-95,1]
sub=P('7H',4,56,0,10,8,2,2)+P('5H',*ends)+P('H',0)+P('5H',*starts)+P('5h',*deltas)+P('5H',0,0,0,0,0)
t['cmap']=P('HHHHI',0,1,3,1,12)+sub
names={1:'Purgers Rank Slab',2:'Bold',3:'PurgersRankSlab-1',4:'Purgers Rank Slab Bold',5:'Version 1.0',6:'PurgersRankSlab-Bold'}
records=b'';strings=b''
for n,s in names.items():
 b=s.encode('utf-16-be');records+=P('6H',3,1,0x409,n,len(b),len(strings));strings+=b
t['name']=P('3H',0,len(names),6+12*len(names))+records+strings
t['post']=P('IIhhIIIII',0x30000,0,-100,50,0,0,0,0,0)
# OS/2 version 0, Windows-compatible metrics.
t['OS/2']=P('HhHHH',0,510,700,5,0)+P('11h',650,600,0,75,650,600,0,350,50,300,0)+bytes([2,6,8,3,5,5,5,2,3,4])+P('4I',1,0,0,0)+b'PURG'+P('HHHhhhHH',32,32,86,800,-200,0,800,200)
tags=sorted(t);n=len(tags);offset=12+16*n;directory=b'';body=b''
for tag in tags:
 b=t[tag];directory+=tag.encode()+P('III',checksum(b),offset,len(b));padded=b+b'\0'*((-len(b))%4);body+=padded;offset+=len(padded)
font=P('IHHHH',0x10000,n,128,3,n*16-128)+directory+body
head_offset=12+16*n+sum((len(t[tag])+3)//4*4 for tag in tags[:tags.index('head')])
adjustment=(0xb1b0afba-checksum(font))&0xffffffff
font=font[:head_offset+8]+P('I',adjustment)+font[head_offset+12:]
path=pathlib.Path('Assets/_Project_Assets/UI/GrowthIcons/Fonts/PurgersRankSlab.ttf');path.write_bytes(font)
print(path,len(font),hex(checksum(font)))

