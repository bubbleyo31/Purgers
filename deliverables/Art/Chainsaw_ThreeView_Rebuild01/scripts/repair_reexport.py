import bpy,os,json,math,hashlib
from mathutils import Vector
from mathutils.bvhtree import BVHTree
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE=r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Tank/Materials/chainsaw/第一人稱__電鋸_uv.fbx'
bpy.ops.wm.open_mainfile(filepath=BASE+'/Chainsaw_ThreeView_Animated.blend');bpy.context.preferences.view.use_translate_new_dataname=False
scene=bpy.context.scene;arm=next(o for o in scene.objects if o.type=='ARMATURE');arm.data.pose_position='REST';hands=bpy.data.objects['立方体.004']
palette=json.load(open(BASE+'/build_report.json',encoding='utf8'))['materials']
def select(o):
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
vv=[];ff=[]
for o in list(scene.objects):
 if o.type in {'CAMERA','LIGHT'}:bpy.data.objects.remove(o,do_unlink=True)
 elif o.type=='MESH' and o.get('region')=='case':
  k=len(vv);vv.extend(v.co.copy() for v in o.data.vertices);ff.extend(tuple(i+k for i in p.vertices) for p in o.data.polygons)
tree=BVHTree.FromPolygons(vv,ff)
snapped=0;maxdist=0.
for o in scene.objects:
 if o.type=='MESH' and o.get('region')=='mycelium' and any('Case_Mycelium' in m.name for m in o.data.materials):
  for v in o.data.vertices:
   hit,n,index,dist=tree.find_nearest(v.co);maxdist=max(maxdist,dist)
   if dist>.003:v.co=hit+n*.0014;snapped+=1
print('SURFACE_REPAIR',snapped,maxdist)
for name in ['export_asset','render_review']:
 f=BASE+'/scripts/'+name+'.py';exec(compile(open(f,encoding='utf-8-sig').read(),f,'exec'),globals())
print('FINAL_REEXPORT_COMPLETE')
