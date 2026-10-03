# Extracted bands become actual low relief geometry. No reference image is used as a material.
from mathutils.bvhtree import BVHTree
masks=np.load(BASE+'/reference_masks.npz')
extraction=json.load(open(BASE+'/reference_extraction.json',encoding='utf8'))
def build_tree(obj):
 return BVHTree.FromPolygons([v.co.copy() for v in obj.data.vertices],[tuple(p.vertices) for p in obj.data.polygons])
case_tree=build_tree(housing)
ridge_stats={}
def build_band(name,mask,origin,plane,sign=1):
 verts=[];faces=[];lookup={};valid={}
 def coord(ix,iy):
  px=origin[0]+ix*2;py=origin[1]+iy*2
  if plane=='blade':
   yy,zz=yz(px,py);return Vector((XC+sign*(bar_half+.00055),yy,zz))
  if plane=='case':
   yy,zz=yz(px,py);hit,n,_,_=case_tree.ray_cast(Vector((sign*.8,yy,zz)),Vector((-sign,0,0)),2)
   if hit is None:return None
   return hit+Vector((sign*.0015,0,0))
  # TOP region is the upper view's casing; match its longitudinal origin to SIDE.
  yy=.108-(px-509)*S
  xx=XC+(py-552)*S
  hit,n,_,_=case_tree.ray_cast(Vector((xx,yy,.7)),Vector((0,0,-1)),2)
  if hit is None:return None
  return hit+Vector((0,0,.0015))
 def vertex(ix,iy):
  key=(ix,iy)
  if key in lookup:return lookup[key]
  co=coord(ix,iy)
  if co is None:return None
  lookup[key]=len(verts);verts.append(co);return lookup[key]
 for y,x in zip(*np.where(mask)):
  ids=[vertex(x,y),vertex(x+1,y),vertex(x+1,y+1),vertex(x,y+1)]
  if None not in ids:
   pts=[verts[i] for i in ids]
   if max((p-pts[0]).length for p in pts)<.02:faces.append(tuple(ids))
 if not faces:return
 # Smoothing the band's boundary removes pixel steps while keeping the actual drawn islands.
 o=mesh(name,verts,faces,seam if plane=='blade' else hypha,bone=None,region='mycelium')
 bm=bmesh.new();bm.from_mesh(o.data)
 loose=[v for v in bm.verts if not v.link_faces]
 if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
 boundary=[v for v in bm.verts if v.is_boundary]
 for i in range(4):bmesh.ops.smooth_vert(bm,verts=boundary,factor=.5,use_axis_x=True,use_axis_y=True,use_axis_z=True)
 bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
 bm.to_mesh(o.data);bm.free()
 # Low relief is raised outward, width is preserved from the reference image.
 for p in o.data.polygons:
  target=Vector((0,0,1)) if plane=='top' else Vector((sign,0,0))
  if p.normal.dot(target)<0:
   bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.reverse_faces(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
  break
 select(o);solid=o.modifiers.new('Physical relief thickness','SOLIDIFY');solid.thickness=.0009 if plane=='blade' else .0017;solid.offset=0;apply(o,solid)
 dec=o.modifiers.new('Simplify flat band interiors','DECIMATE');dec.decimate_type='DISSOLVE';dec.angle_limit=.055;apply(o,dec)
 # A restrained chamfer softens band edges without turning the network into round rope.
 bevel(o,.00025 if plane=='blade' else .00045,2)
 if plane!='blade':
  for v in o.data.vertices:
   if plane=='case':hit,n,_,_=case_tree.ray_cast(Vector((sign*.8,v.co.y,v.co.z)),Vector((-sign,0,0)),2)
   else:hit,n,_,_=case_tree.ray_cast(Vector((v.co.x,v.co.y,.7)),Vector((0,0,-1)),2)
   if hit is not None:
    axis=0 if plane=='case' else 2;sgn=sign if plane=='case' else 1
    delta=(v.co[axis]-hit[axis])*sgn
    if delta>.004:v.co[axis]=hit[axis]+sgn*.0015
 if plane!='blade':
  for v in o.data.vertices:
   hit,n,index,dist=case_tree.find_nearest(v.co)
   if hit is not None and dist>.003:v.co=hit+n*.0014
 uv_project(o);bind(o);o['reference_derived_geometry']=True
 ridge_stats[name]={'vertices':len(o.data.vertices),'faces':len(o.data.polygons),'height_m':.0009 if plane=='blade' else .0017,'source_plane':plane}
for name in ['blade','case','top']:
 origin=extraction['regions'][name]['origin_px'];mask=masks[name]
 for sign in ([1] if name=='top' else [-1,1]):
  build_band('ReferenceRelief_%s_%s'%(name,sign),mask,origin,name,sign)
with open(BASE+'/relief_report.json','w',encoding='utf8') as f:json.dump(ridge_stats,f,indent=2)
