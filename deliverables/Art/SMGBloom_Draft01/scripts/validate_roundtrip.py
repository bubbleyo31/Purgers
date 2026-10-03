"""驗證實際輸出 FBX 可重匯入，並比較五段動作的骨骼世界座標。"""
import bpy, json, math, os
from mathutils import Vector
BASE=r'M:/UnityProject/Purgers/deliverables/Art/SMGBloom_Draft01'
SOURCE=r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Support/Materials/mp7/第一人稱_衝鋒槍_uv.fbx'

def inspect(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    actions={a.name.split('|')[-1]:a for a in bpy.data.actions}
    result={'bones':{b.name:b.parent.name if b.parent else None for b in arm.data.bones},'poses':{},'bases':{},'frames':{},'meshes':[]}
    for name,a in actions.items():
        arm.animation_data.action=a
        start,end=a.frame_range
        result['frames'][name]=[start,end]
        # 每個來源整數幀都比對，包含 Reload 中途彈匣拆卸。
        for f in range(round(start),round(end)+1):
            bpy.context.scene.frame_set(f)
            result['poses'][name+':'+str(f)]={b.name:list(arm.matrix_world @ b.head) for b in arm.pose.bones}
            result['bases'][name+':'+str(f)]={b.name:[v for row in (arm.matrix_world @ b.matrix).to_3x3() for v in row] for b in arm.pose.bones}
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH':continue
        unweighted=0;nonfinite=0
        for v in obj.data.vertices:
            if sum(g.weight for g in v.groups)<.001:unweighted+=1
            if any(not math.isfinite(x) for x in v.co):nonfinite+=1
        result['meshes'].append({'name':obj.name,'vertices':len(obj.data.vertices),'uv_layers':len(obj.data.uv_layers),
                                 'unweighted':unweighted,'nonfinite':nonfinite,'materials':len(obj.data.materials),
                                 'armature_modifier':any(m.type=='ARMATURE' and m.object==arm for m in obj.modifiers)})
    result['texture_paths']=[{'name':i.name,'path':bpy.path.abspath(i.filepath),'exists':os.path.isfile(bpy.path.abspath(i.filepath))} for i in bpy.data.images if i.source=='FILE']
    return result

source=inspect(SOURCE)
draft=inspect(BASE+'/SMGBloom_Arms_Draft01.fbx')
errors=[];max_distance=0;max_basis_delta=0;compared=0;worst=None
if source['bones']!=draft['bones']:errors.append('Bone names or parent relationships changed')
if source['frames']!=draft['frames']:errors.append('Action names or frame ranges changed')
for key,pose in source['poses'].items():
    other=draft['poses'].get(key)
    if not other:errors.append('Missing pose '+key);continue
    for name,p in pose.items():
        if name not in other:continue
        d=(Vector(p)-Vector(other[name])).length;compared+=1
        max_basis_delta=max(max_basis_delta,max(abs(a-b) for a,b in zip(source['bases'][key][name],draft['bases'][key][name])))
        if d>max_distance:max_distance=d;worst=[key,name]
if max_distance>.0001:errors.append('Bone position deviation exceeds 0.1 mm')
if max_basis_delta>.0001:errors.append('Bone rotation/scale basis differs above tolerance')
for m in draft['meshes']:
    if m['unweighted'] or m['nonfinite'] or not m['uv_layers'] or not m['armature_modifier'] or not m['materials']:errors.append('Invalid mesh '+m['name'])
if any(not p['exists'] for p in draft['texture_paths']):errors.append('Missing external texture')
report={'passed':not errors,'errors':errors,'compared_bone_positions':compared,'max_bone_position_delta_m':max_distance,'worst_sample':worst,'max_basis_coefficient_delta':max_basis_delta,
        'source_bones':len(source['bones']),'output_bones':len(draft['bones']),'source_actions':source['frames'],'output_actions':draft['frames'],
        'meshes':draft['meshes'],'texture_paths':draft['texture_paths'],
        'scope':'Blender FBX round-trip only. Does not verify Unity import, runtime animation blending, material conversion or collision-free animation.'}
with open(BASE+'/roundtrip_validation.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print(json.dumps({k:v for k,v in report.items() if k not in ['meshes','texture_paths']},ensure_ascii=False))
if errors:raise RuntimeError('; '.join(errors))

