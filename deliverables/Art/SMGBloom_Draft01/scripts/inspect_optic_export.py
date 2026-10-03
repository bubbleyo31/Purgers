import bpy,os,json
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BASE+'/Reflex_Branch_Module_Draft01.fbx')
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
report={'meshes':len(meshes),'armatures':len([o for o in bpy.context.scene.objects if o.type=='ARMATURE']),'uv_layers':{o.name:[u.name for u in o.data.uv_layers] for o in meshes},'materials':[]}
for mat in bpy.data.materials:
 bs=next((n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED'),None) if mat.use_nodes else None
 report['materials'].append({'name':mat.name,'diffuse':list(mat.diffuse_color),'alpha':bs.inputs['Alpha'].default_value if bs else None})
with open(BASE+'/optic_roundtrip.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print(json.dumps(report,ensure_ascii=False))
