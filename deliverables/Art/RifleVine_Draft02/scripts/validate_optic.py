"""檢查鏡軸不被不透明幾何擋住，並確認獨立鏡體的可替換輸出。"""
import bpy, json, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
BASE=r'M:/UnityProject/Purgers/deliverables/Art/RifleVine_Draft02'
bpy.ops.wm.open_mainfile(filepath=BASE+'/RifleVine_Arms_Draft02.blend')
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');arm.data.pose_position='REST'
with open(BASE+'/optic_setup.json',encoding='utf8') as f:setup=json.load(f)
z=setup['blender_rest_coordinates_m']['rear_lens_center'][2]
opaque=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('region')=='optic' and not any('Glass' in m.name for m in o.data.materials)]
checks=[]
for dx,dz in [(0,0)]+[(math.cos(i*math.pi/4)*.008,math.sin(i*math.pi/4)*.008) for i in range(8)]:
    origin=Vector((dx,.19,z+dz));hits=[]
    for obj in opaque:
        verts=[obj.matrix_world @ v.co for v in obj.data.vertices]
        tree=BVHTree.FromPolygons(verts,[list(p.vertices) for p in obj.data.polygons])
        hit,n,index,d=tree.ray_cast(origin,Vector((0,-1,0)),.3)
        if hit is not None:hits.append({'object':obj.name,'distance':d})
    checks.append({'offset_m':[dx,dz],'unobstructed':not hits,'hits':hits})
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BASE+'/ACOG_25_Module_Draft02.fbx')
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
errors=[]
if not all(c['unobstructed'] for c in checks):errors.append('Opaque geometry obstructs aperture test ray')
if len(objects)!=5:errors.append('Expected five independent scope material meshes')
if any(o.type=='ARMATURE' for o in bpy.context.scene.objects):errors.append('Standalone scope should not require an armature')
if bpy.data.actions:errors.append('Standalone scope unexpectedly contains animation')
for obj in objects:
    if not obj.data.uv_layers or not obj.data.materials:errors.append('Missing UV or material: '+obj.name)
report={'passed':not errors,'errors':errors,'aperture_rays':checks,'aperture_test_radius_m':.008,
        'standalone_mesh_count':len(objects),'standalone_has_armature':False,'zoom_implemented':False,
        'scope':'Tests static scope housing and standalone import. Does not prove alignment to the existing Unity camera or actual optical magnification.'}
with open(BASE+'/optic_validation.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print(json.dumps(report,ensure_ascii=False))
if errors:raise RuntimeError('; '.join(errors))
