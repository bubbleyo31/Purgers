from pathlib import Path
base=Path(r'M:/UnityProject/Purgers/deliverables/Art/Chainsaw_ThreeView_Rebuild01/scripts')
p=base/'export_asset.py';s=p.read_text(encoding='utf-8-sig')
s="""# Every rebuilt rigid part must have complete Root weights after boolean/relief operations.
for o in list(scene.objects):
 if o.type=='MESH' and o!=hands:
  o.vertex_groups.clear();g=o.vertex_groups.new(name='Root');g.add(list(range(len(o.data.vertices))),1.0,'REPLACE')
  mods=[m for m in o.modifiers if m.type=='ARMATURE']
  if not mods:
   m=o.modifiers.new('Original rig deformation','ARMATURE');m.object=arm
  else:mods[0].object=arm
""" + s
p.write_text(s,encoding='utf8')
p=base/'build_reference_ridges.py';s=p.read_text(encoding='utf-8-sig').replace("uv_project(o);bind(o);o['reference_derived_geometry']=True","""if plane!='blade':
  for v in o.data.vertices:
   hit,n,index,dist=case_tree.find_nearest(v.co)
   if hit is not None and dist>.003:v.co=hit+n*.0014
 uv_project(o);bind(o);o['reference_derived_geometry']=True""");p.write_text(s,encoding='utf8')
p=base/'render_review.py';s=p.read_text(encoding='utf-8-sig').replace("camera.rotation_euler.z+=math.pi/2","camera.rotation_euler.z-=math.pi/2");p.write_text(s,encoding='utf8')
