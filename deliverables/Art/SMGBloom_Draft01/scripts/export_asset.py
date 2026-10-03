for region in ['weapon','magazine','optic','arms']:
    for mat in list(bpy.data.materials):
        group=[o for o in scene.objects if o.type=='MESH' and o.get('region')==region and o!=hands and len(o.data.materials)==1 and o.data.materials[0]==mat]
        if not group:continue
        select(group[0])
        for o in group:o.select_set(True)
        if len(group)>1:bpy.ops.object.join()
        group[0].name='SMG_'+region+'_'+mat.name
optic_objects=[o for o in scene.objects if o.type=='MESH' and o.get('region')=='optic']
collection=bpy.data.collections.new('Reflex_Branch_Replaceable');scene.collection.children.link(collection)
for o in optic_objects:
    for c in list(o.users_collection):c.objects.unlink(o)
    collection.objects.link(o)
asset_objects=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/SMGBloom_Arms_Draft01.fbx',use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
copies=[]
for o in optic_objects:
    c=o.copy();c.data=o.data.copy();scene.collection.objects.link(c);c.name=o.name+'_Standalone';c.modifiers.clear();c.vertex_groups.clear();c.parent=None;c.matrix_world=Matrix.Identity(4);c.data.transform(Matrix.Translation(-Vector(MOUNT)));copies.append(c)
bpy.ops.object.select_all(action='DESELECT')
for c in copies:c.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.export_scene.fbx(filepath=BASE+'/Reflex_Branch_Module_Draft01.fbx',use_selection=True,object_types={'MESH'},bake_anim=False,mesh_smooth_type='FACE',path_mode='COPY',axis_forward='-Z',axis_up='Y')
for c in copies:bpy.data.objects.remove(c,do_unlink=True)
manifest={'module':'Branch reflex original art draft','inspiration':'https://falke-germany.com/en/falke-le/','replica':False,'attachment_bone':'Root','collection':collection.name,'runtime_reticle_implemented':False,'blender_rest_m':{'mount_origin':MOUNT,'axis_direction':[0,-1,0],'axis_point':[0,-.004,AXIS_Z],'review_eye':[0,.20,AXIS_Z]},'materials':palette,'lens_note':'Blender transparent coating is a review material. Rebuild as transparent URP material in Unity.'}
with open(BASE+'/optic_setup.json','w',encoding='utf8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)
report={'source_sha256':hashlib.sha256(open(SOURCE,'rb').read()).hexdigest(),'bones':len(arm.data.bones),'actions':{a.name:list(a.frame_range) for a in bpy.data.actions},'meshes':len(asset_objects)-1,'vertices':sum(len(o.data.vertices) for o in asset_objects if o.type=='MESH'),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in asset_objects if o.type=='MESH'),'unity_output_validation':'not executed'}
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
for img in bpy.data.images:
    if img.source=='FILE' and os.path.isfile(bpy.path.abspath(img.filepath)):img.pack()
scene.frame_start=1;scene.frame_end=61;bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/SMGBloom_Arms_Draft01.blend')
