import bpy,json
BASE=r'M:/UnityProject/Purgers/deliverables/Art/RifleVine_Draft01'
bpy.ops.wm.open_mainfile(filepath=BASE+'/RifleVine_Arms_Draft01.blend')
for a in bpy.data.actions:a.name=a.name.split('|')[-1]
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj.type in {'ARMATURE','MESH'}:obj.select_set(True)
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/RifleVine_Arms_Draft01.fbx',use_selection=True,
    object_types={'ARMATURE','MESH'},add_leaf_bones=False,mesh_smooth_type='FACE',
    bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0,bake_anim_force_startend_keying=True,
    path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
bpy.context.scene.frame_set(1)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/RifleVine_Arms_Draft01.blend')
with open(BASE+'/build_report.json',encoding='utf8') as f:report=json.load(f)
for a in report['actions']:a['name']=a['name'].split('|')[-1]
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
