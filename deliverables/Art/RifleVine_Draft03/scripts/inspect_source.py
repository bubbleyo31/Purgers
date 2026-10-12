"""唯讀盤點上一版，輸出網格／骨架／材質與連通部件。"""
import bpy, json, os, bmesh
BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(os.path.dirname(BASE), 'RifleVine_Draft02', 'RifleVine_Arms_Draft02.blend')
bpy.ops.wm.open_mainfile(filepath=SOURCE)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
arm.data.pose_position = 'REST'
out = {'armature': arm.name, 'bones': list(arm.data.bones.keys()), 'meshes': []}
def bounds(points):
    return [[round(fn(p[i] for p in points), 6) for i in range(3)] for fn in (min, max)]
for o in bpy.context.scene.objects:
    if o.type != 'MESH' or o.get('region') == 'review_only': continue
    item = {'name': o.name, 'region': o.get('region'), 'vertices': len(o.data.vertices),
            'matrix': [list(row) for row in o.matrix_world],
            'bounds': bounds([o.matrix_world @ v.co for v in o.data.vertices]),
            'materials': [m.name for m in o.data.materials],
            'groups': [g.name for g in o.vertex_groups],
            'topology': {str(n):sum(len(p.vertices)==n for p in o.data.polygons) for n in (3,4)},
            'triangles': sum(len(p.vertices)-2 for p in o.data.polygons)}
    if o.name.startswith('Detail_weapon_Bark') or o.name.startswith('Detail_weapon_Trim'):
        bm = bmesh.new(); bm.from_mesh(o.data); remaining = set(bm.verts); parts=[]
        while remaining:
            stack=[remaining.pop()]; verts=[]
            while stack:
                v=stack.pop(); verts.append(v)
                for edge in v.link_edges:
                    n=edge.other_vert(v)
                    if n in remaining: remaining.remove(n); stack.append(n)
            parts.append({'count':len(verts), 'bounds':bounds([o.matrix_world @ v.co for v in verts])})
        item['parts']=parts; bm.free()
    out['meshes'].append(item)
os.makedirs(BASE, exist_ok=True)
with open(BASE+'/source_inspection.json','w',encoding='utf8') as f: json.dump(out,f,ensure_ascii=False,indent=2)
print(json.dumps(out,ensure_ascii=False))
