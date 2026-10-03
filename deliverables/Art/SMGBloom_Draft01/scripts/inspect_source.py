import bpy, json, os
from mathutils import Vector

BASE = r'M:/UnityProject/Purgers/deliverables/Art/SMGBloom_Draft01'
SOURCE = r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Support/Materials/mp7/第一人稱_衝鋒槍_uv.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SOURCE)
report = {'objects': [], 'actions': []}
for obj in bpy.context.scene.objects:
    item = {'name': obj.name, 'type': obj.type, 'matrix': [list(row) for row in obj.matrix_world]}
    if obj.type == 'MESH':
        points = [obj.matrix_world @ v.co for v in obj.data.vertices]
        item.update(vertices=len(points), faces=len(obj.data.polygons),
                    min=[min(v[i] for v in points) for i in range(3)],
                    max=[max(v[i] for v in points) for i in range(3)])
        weights = {}
        for v in obj.data.vertices:
            for g in v.groups:
                if g.weight > 0.001:
                    name = obj.vertex_groups[g.group].name
                    weights[name] = weights.get(name, 0) + 1
        item['weights'] = weights
        item['modifiers'] = [(m.name, m.type) for m in obj.modifiers]
    if obj.type == 'ARMATURE':
        item['bones'] = [{'name': b.name, 'parent': b.parent.name if b.parent else None,
                          'head': list(obj.matrix_world @ b.head_local),
                          'tail': list(obj.matrix_world @ b.tail_local)} for b in obj.data.bones]
    report['objects'].append(item)
for a in bpy.data.actions:
    report['actions'].append({'name': a.name, 'range': list(a.frame_range), 'curves': len(a.fcurves)})
os.makedirs(BASE, exist_ok=True)
with open(BASE + '/source_inspection.json', 'w', encoding='utf8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
bpy.ops.wm.save_as_mainfile(filepath=BASE + '/source_inspection.blend')
print(json.dumps(report, ensure_ascii=False))
