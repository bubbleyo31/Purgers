import bpy,bmesh,math,os,json,random,hashlib
import numpy as np
from mathutils import Vector,Matrix,noise
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT=os.path.abspath(os.path.join(BASE,'../../..'))
SOURCE=ROOT+'/Assets/_Project_Assets/Models/Player/Profession/Tank/Materials/chainsaw/第一人稱__電鋸_uv.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.view.use_translate_new_dataname=False
bpy.ops.import_scene.fbx(filepath=SOURCE)
scene=bpy.context.scene;arm=next(o for o in scene.objects if o.type=='ARMATURE');arm.data.pose_position='REST'
hands=bpy.data.objects['立方体.004'];front_handle=bpy.data.objects['NURBS曲线']
original_hand_vertices=len(hands.data.vertices)
original_hand_positions=[list(v.co) for v in hands.data.vertices]
for a in bpy.data.actions:a.name=a.name.split('|')[-1];a.use_fake_user=True
def run_helper(name):
 path=BASE+'/scripts/'+name+'.py';exec(compile(open(path,encoding='utf-8-sig').read(),path,'exec'),globals())
run_helper('surface_helpers')
case=surface('Housing_Burgundy',(.090,.047,.042),'leather',.82,.03,size=2048)
blade=surface('Blade_PaleSteel',(.43,.445,.425),'metal',.70,.35,size=2048)
core=surface('Handle_Charcoal',(.020,.023,.022),'metal',.62,.20)
wood=surface('Thorn_Deadwood',(.13,.105,.040),'bark',.89,0,size=1024)
seam=surface('Blade_Mycelium_Ridge',(.23,.25,.23),'leather',.86,0,size=1024)
hypha=surface('Case_Mycelium_Ridge',(.40,.415,.385),'leather',.86,0,size=1024)
steel=surface('Clamp_WornSteel',(.065,.070,.067),'metal',.49,.65,size=1024)
rubber=core;opticmat=core;run_helper('geometry_helpers')
# Reset to original FBX: keep its untouched arms and fit the front contact handle to its exact source geometry.
for o in list(scene.objects):
 if o.type=='MESH' and o not in [hands,front_handle]:bpy.data.objects.remove(o,do_unlink=True)
front_handle.data.transform(front_handle.matrix_world);front_handle.matrix_world=Matrix.Identity(4)
front_handle.data.materials.clear();front_handle.data.materials.append(core);front_handle['region']='handles'
uv_project(front_handle);bevel(front_handle,.0025,2)
hands['region']='arms';run_helper('restore_source_material')
# Image side silhouette maps to original case root datum. Front grip stays in its original position.
S=.00118;XC=-.012
def yz(px,py):return (.108-(px-524)*S,.068-(py-112)*S)
def wp(px,py,x=XC):return (x,*yz(px,py))
def width(px):
 return float(np.interp(px,[36,120,203,410,490,524],[.036,.055,.102,.112,.090,.082]))
side_poly=[(36,314),(130,197),(269,113),(492,112),(523,128),(522,308),(423,393),(41,326)]
verts=[]
for sign in [-1,1]:
 for px,py in side_poly:verts.append(wp(px,py,XC+sign*width(px)))
n=len(side_poly);faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
housing=mesh('ThreeView_WedgeHousing',verts,faces,case,region='case');bevel(housing,.0045,2)
run_helper('carve_grip')
# Front support is part of the drawing's red-brown shell, under the original wrap handle.
box('Grip_Support',(-.03,.19,.077),(.065,.07,.055),case,region='case',radius=.004)
# True thin bar, widening slightly toward its rounded nose as shown in SIDE and TOP.
profile_px=[(524,158),(760,148),(1080,138),(1430,126),(1654,118)]
for j in range(1,26):
 a=math.pi*j/25;profile_px.append((1654+138*math.sin(a),223-105*math.cos(a)))
profile_px += [(1410,324),(1070,314),(780,302),(524,291)]
bar_half=.0118;n=len(profile_px)
verts=[wp(px,py,XC+sgn*bar_half) for sgn in [-1,1] for px,py in profile_px]
faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
bar=mesh('ThreeView_ThinGuideBar',verts,faces,blade,region='blade');bevel(bar,.0012,2)
# Two narrow perimeter rails, dark olive wood. One closes across the rear mount hidden in the clamp.
pts=[Vector(wp(px,py)) for px,py in profile_px];pts.append(pts[0])
for sign in [-1,1]:
 rail=[p+Vector((sign*.014,0,0)) for p in pts]
 tube('Deadwood_EdgeRail',rail,.0042,wood,region='thorns',sides=6)
# Short regularly spaced triangular wood teeth with occasional small opposite-side stubs.
def tooth(p,tangent,out,length,lean,sign):
 b=p+Vector((sign*.004,0,0));side=Vector((1,0,0))
 v=[b-tangent*.010-side*.005,b+tangent*.010-side*.005,b+tangent*.010+side*.005,b-tangent*.010+side*.005,
 b+out*length+tangent*lean]
 f=[(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)]
 o=mesh('Short_Angular_WoodTooth',v,f,wood,region='thorns')
 for poly in o.data.polygons:poly.use_smooth=False
lengths=[0.]
for a,b in zip(pts,pts[1:]):lengths.append(lengths[-1]+(b-a).length)
rng=random.Random(212);seg=0
for i in range(int(lengths[-1]/.095)):
 dist=.018+i*.095
 while seg+1<len(lengths)-1 and lengths[seg+1]<dist:seg+=1
 t=(dist-lengths[seg])/(lengths[seg+1]-lengths[seg]);p=pts[seg].lerp(pts[seg+1],t)
 tan=(pts[seg+1]-pts[seg]).normalized();out=Vector((0,-tan.z,tan.y))
 if out.dot(p-Vector((XC,-.58,-.07)))<0:out=-out
 if p.y>.098:continue
 tooth(p,tan,out,rng.uniform(.020,.037),rng.uniform(-.010,.009),(-1)**i)
# Small rectangular rails/links in TOP, subordinate to the major spikes.
for i in range(int(lengths[-1]/.028)):
 d=.005+i*.028;si=0
 while si+1<len(lengths)-1 and lengths[si+1]<d:si+=1
 t=(d-lengths[si])/(lengths[si+1]-lengths[si]);p=pts[si].lerp(pts[si+1],t)
 if p.y>.095:continue
 tangent=(pts[si+1]-pts[si]).normalized()
 # These low saddles show the thin edge construction without adding a thick chain.
 tube('Small_EdgeSaddle',[p-tangent*.007,p+tangent*.007],.0047,wood,region='thorns',sides=4)
# Source rear contact bar retained at the approved original grip location.
outer=[(.50,-.039),(.621,-.039),(.672,-.070),(.679,-.143),(.647,-.177),(.50,-.177)]
inner=[(.52,-.070),(.615,-.070),(.641,-.087),(.646,-.129),(.626,-.147),(.52,-.147)]
v=[]
for x in [-.068,.044]:
 for ring in [outer,inner]:v.extend((x,y,z) for y,z in ring)
n=len(outer);f=[]
for k in [0,12]:
 for i in range(n):j=(i+1)%n;f.append((k+i,k+j,k+n+j,k+n+i))
for k in [0,6]:
 for i in range(n):j=(i+1)%n;f.append((k+i,k+12+i,k+12+j,k+j))
rear=mesh('SourcePosition_RearHandle',v,f,core,region='handles');bevel(rear,.003,2)
# Narrow vertical clamp and heel plates from the supplied views.
y,z=yz(519,226)
box('Bar_RootClamp',(XC,y,z),(.072,.037,.165),core,region='handles',radius=.006)
for x in [XC-.05,XC+.05]:
 box('Clamp_SidePlate',(x,y+.010,z),(.016,.043,.160),steel,region='handles',radius=.003)
# Fasteners are limited to the mount, matching the reference's quiet large casing faces.
for x in [XC-.061,XC+.061]:
 for zz in [z-.059,z+.059]:cylinder('Clamp_Pin',(x,y+.01,zz),.007,.004,'X',steel,n=12,region='handles')
# Original wrap-handle's non-contact lower end is now tied back into the narrower new housing.
box('FrontHandle_LowerMount',(.126,.175,-.172),(.103,.060,.054),core,region='handles',radius=.004)
box('FrontHandle_UpperMount',(-.108,.175,.042),(.054,.067,.035),core,region='handles',radius=.003)
run_helper('build_reference_ridges')
# Source arms are unchanged, including local mesh positions and all weights.
assert len(hands.data.vertices)==original_hand_vertices
assert all(list(v.co)==old for v,old in zip(hands.data.vertices,original_hand_positions))
with open(BASE+'/construction_report.json','w',encoding='utf8') as f:
 json.dump({'reference':'References/ThreeView.png','scale_m_per_pixel':S,'side_silhouette':'reconstructed','thickness_source':'TOP','front_view':'stacking reference; conflicting handle projection resolved with source grips','grip_decision':'user approved original hand contact points; adapt new handles','source_hand_vertices':original_hand_vertices,'source_hand_geometry_unchanged':True,'red_mushrooms':0,'mycelium':'actual raised mesh from reference line bands'},f,indent=2)
run_helper('export_asset');run_helper('render_review')
print('THREEVIEW_REBUILD_COMPLETE')
