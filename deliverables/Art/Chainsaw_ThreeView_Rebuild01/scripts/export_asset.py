# Every rebuilt rigid part must have complete Root weights after boolean/relief operations.
for o in list(scene.objects):
 if o.type=='MESH' and o!=hands:
  o.vertex_groups.clear();g=o.vertex_groups.new(name='Root');g.add(list(range(len(o.data.vertices))),1.0,'REPLACE')
  mods=[m for m in o.modifiers if m.type=='ARMATURE']
  if not mods:
   m=o.modifiers.new('Original rig deformation','ARMATURE');m.object=arm
  else:mods[0].object=arm
# Separate by function and material. Arms stay exactly as imported.
for region in ['case','blade','thorns','handles','mycelium']:
 for mat in list(bpy.data.materials):
  group=[o for o in scene.objects if o.type=='MESH' and o.get('region')==region and o!=hands and len(o.data.materials)==1 and o.data.materials[0]==mat]
  if not group:continue
  select(group[0])
  for o in group:o.select_set(True)
  if len(group)>1:bpy.ops.object.join()
  group[0].name='TV_'+region+'_'+mat.name
asset_objects=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/Chainsaw_ThreeView_Animated.fbx',use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
report={'source_sha256':hashlib.sha256(open(SOURCE,'rb').read()).hexdigest(),'bones':len(arm.data.bones),'actions':{a.name:list(a.frame_range) for a in bpy.data.actions},'fps':scene.render.fps,'meshes':len(asset_objects)-1,'vertices':sum(len(o.data.vertices) for o in asset_objects if o.type=='MESH'),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in asset_objects if o.type=='MESH'),'materials':palette,'reference':'new three-view image','chain_motion':'all weapon parts follow original Root; no new independent chain animation','unity_output_validation':'not executed'}
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
for img in bpy.data.images:
 if img.source=='FILE' and os.path.isfile(bpy.path.abspath(img.filepath)):img.pack()
scene.frame_start=1;scene.frame_end=26;bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/Chainsaw_ThreeView_Animated.blend')
