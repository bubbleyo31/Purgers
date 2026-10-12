"""FBX 往返、完整骨骼變換、手部網格／權重與資源成本檢查。"""
import bpy,os,json,math,hashlib
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PREV=os.path.join(os.path.dirname(BASE),'RifleVine_Draft02','RifleVine_Arms_Draft02.blend')
ORIGINAL='M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Attack/Materials/rifle/第一人稱_步槍_uv.fbx'
errors=[]
def hand_signature():
    o=bpy.data.objects['立方体.004']
    values={'vertices':[list(v.co) for v in o.data.vertices],'faces':[list(p.vertices) for p in o.data.polygons],
    'weights': [[(o.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in o.data.vertices],
    'uv':[[list(d.uv) for d in uv.data] for uv in o.data.uv_layers]}
    return hashlib.sha256(json.dumps(values,sort_keys=True).encode()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=PREV);previous_hand=hand_signature()
bpy.ops.wm.open_mainfile(filepath=BASE+'/RifleVine_Arms_Draft03.blend');current_hand=hand_signature()
if previous_hand!=current_hand:errors.append('Inherited hands geometry, UV or weights changed')
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE');arm.data.pose_position='REST'
axis=.0647295;blocked=[]
# 不透明鏡體與槍身共檢：中心及 8 mm 半徑的視線，不把格線與手臂當成鏡體。
opaque=[o for o in bpy.context.scene.objects if o.type=='MESH' and o.get('region')!='arms' and o.name!='立方体.004' and 'Glass' not in o.name]
trees=[]
for o in opaque:
    verts=[o.matrix_world@v.co for v in o.data.vertices]
    trees.append((o.name,BVHTree.FromPolygons(verts,[list(p.vertices) for p in o.data.polygons])))
for r in [0,.008]:
    for i in range(1 if r==0 else 8):
        a=i*math.pi/4;origin=Vector((math.cos(a)*r,.19,axis+math.sin(a)*r))
        for name,tree in trees:
            hit,n,idx,dist=tree.ray_cast(origin,Vector((0,-1,0)),.32)
            if hit is not None:blocked.append({'sample':[r,i],'object':name,'distance':dist})
if blocked:errors.append('Opaque mesh obstructs tested optic sightline')
def inspect(path):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=path)
    arm=next((o for o in bpy.context.scene.objects if o.type=='ARMATURE'),None)
    result={'bones':{} if not arm else {b.name:b.parent.name if b.parent else None for b in arm.data.bones},'poses':{},'frames':{},'meshes':[]}
    if arm:
        for a in bpy.data.actions:
            name=a.name.split('|')[-1];arm.animation_data.action=a
            lo,hi=a.frame_range;result['frames'][name]=[lo,hi]
            for frame in range(round(lo),round(hi)+1):
                bpy.context.scene.frame_set(frame)
                result['poses'][name+':'+str(frame)]={b.name:[list(row) for row in arm.matrix_world@b.matrix] for b in arm.pose.bones}
    for o in bpy.context.scene.objects:
        if o.type!='MESH':continue
        o.data.calc_loop_triangles()
        degenerate=sum(t.area<1e-12 for t in o.data.loop_triangles)
        result['meshes'].append({'name':o.name,'vertices':len(o.data.vertices),'quads':sum(len(p.vertices)==4 for p in o.data.polygons),
        'triangle_faces':sum(len(p.vertices)==3 for p in o.data.polygons),'ngons':sum(len(p.vertices)>4 for p in o.data.polygons),
        'triangulated_triangles':len(o.data.loop_triangles),'uv_layers':len(o.data.uv_layers),'materials':len(o.data.materials),
        'nonfinite':sum(not all(math.isfinite(x) for x in v.co) for v in o.data.vertices),
        'unweighted':sum(sum(g.weight for g in v.groups)<.001 for v in o.data.vertices) if arm else 0,
        'armature_modifier':any(m.type=='ARMATURE' and m.object==arm for m in o.modifiers) if arm else False,
        'degenerate_triangles':degenerate})
    result['textures']=[{'name':im.name,'path':bpy.path.abspath(im.filepath),'exists':os.path.isfile(bpy.path.abspath(im.filepath))} for im in bpy.data.images if im.source=='FILE']
    return result
original=inspect(ORIGINAL)
previous_export=inspect(os.path.join(os.path.dirname(BASE),'RifleVine_Draft02','RifleVine_Arms_Draft02.fbx'))
output=inspect(BASE+'/RifleVine_Arms_Draft03.fbx')
if original['bones']!=output['bones']:errors.append('Bone names or parent hierarchy changed')
if original['frames']!=output['frames']:errors.append('Action names or frame ranges changed')
max_pos=0;max_basis=0;count=0
for key,pose in original['poses'].items():
    for name,rows in pose.items():
        if key not in output['poses'] or name not in output['poses'][key]:errors.append('Missing pose '+key+' '+name);continue
        src=Matrix(rows);dst=Matrix(output['poses'][key][name]);count+=1
        max_pos=max(max_pos,(src.translation-dst.translation).length)
        max_basis=max(max_basis,max(abs(src[i][j]-dst[i][j]) for i in range(3) for j in range(3)))
if max_pos>.0001 or max_basis>.0001:errors.append('Animation transform deviation exceeds tolerance')
for m in output['meshes']:
    if m['name'].startswith(('Magazine_','Body_','Prism_')) and m['degenerate_triangles']:
        errors.append('Degenerate triangles in new part '+m['name'])
    if m['nonfinite'] or m['unweighted'] or not m['uv_layers'] or not m['materials'] or not m['armature_modifier']:
        errors.append('Invalid mesh '+m['name'])
if any(not t['exists'] for t in output['textures']):errors.append('Missing texture')
module=inspect(BASE+'/Prism_Concept02_Module.fbx')
if any(not t['exists'] for t in module['textures']):errors.append('Missing standalone optic texture')
if module['bones'] or module['frames']:errors.append('Standalone optic contains rig or animation')
if any(m['triangle_faces'] or m['ngons'] or m['degenerate_triangles'] for m in module['meshes']):
    errors.append('Standalone optic is not nondegenerate all-quad topology')
report={'passed':not errors,'errors':errors,'hand_geometry_uv_weights_exact':previous_hand==current_hand,
        'hand_sha256':current_hand,'compared_bone_samples':count,'max_bone_position_delta_m':max_pos,
        'max_rotation_scale_basis_delta':max_basis,'bones':len(output['bones']),'actions':output['frames'],
        'inherited_degenerate_meshes': [{'name':m['name'],'count':m['degenerate_triangles']} for m in previous_export['meshes'] if m['degenerate_triangles']],
        'sightline_9_rays_blocked':blocked,'output_meshes':output['meshes'],'texture_paths':output['textures'],
        'standalone_optic_meshes':module['meshes'],
        'scope':'Blender FBX round-trip. Unity import, shader conversion, actual ADS, runtime collisions and performance are separate.'}
with open(BASE+'/roundtrip_validation.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print(json.dumps({k:v for k,v in report.items() if k not in ['output_meshes','texture_paths','standalone_optic_meshes']},ensure_ascii=False))
if errors:raise RuntimeError('; '.join(errors))

