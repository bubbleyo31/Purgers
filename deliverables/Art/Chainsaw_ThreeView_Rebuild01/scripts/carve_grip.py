# Free the hand volume in the new wedge while preserving the original animation.
right_indices=[]
for v in hands.data.vertices:
 if not v.groups:continue
 g=max(v.groups,key=lambda x:x.weight);name=hands.vertex_groups[g.group].name
 if '.r' in name and name not in ['骨骼.r','骨骼.001.r','骨骼.002.r']:right_indices.append(v.index)
root_rest=arm.matrix_world@arm.data.bones['Root'].matrix_local
points=[];old_action=arm.animation_data.action
arm.data.pose_position='POSE'
for action in list(bpy.data.actions):
 arm.animation_data.action=action
 for fr in range(int(action.frame_range[0]),int(action.frame_range[1])+1):
  scene.frame_set(fr);dg=bpy.context.evaluated_depsgraph_get()
  ev=hands.evaluated_get(dg);em=ev.to_mesh()
  to_rest=root_rest@(arm.matrix_world@arm.pose.bones['Root'].matrix).inverted()
  for i in right_indices:
   p=to_rest@(ev.matrix_world@em.vertices[i].co)
   if .44<p.y<.70 and -.24<p.z<.08:points.append(p.copy())
  ev.to_mesh_clear()
arm.animation_data.action=old_action;arm.data.pose_position='REST';scene.frame_set(1)
# Quantized union plus 5 mm clearance; convex envelope is appropriate for this hand opening.
unique={tuple(round(c/.003) for c in p):p for p in points}
bm=bmesh.new()
for p in unique.values():
 for d in [Vector((.005,0,0)),Vector((-.005,0,0)),Vector((0,.005,0)),Vector((0,-.005,0)),Vector((0,0,.005)),Vector((0,0,-.005))]:bm.verts.new(p+d)
bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
inside=[v for v in bm.verts if not v.link_faces]
if inside:bmesh.ops.delete(bm,geom=inside,context='VERTS')
cutdata=bpy.data.meshes.new('AnimatedHandClearance');bm.to_mesh(cutdata);bm.free()
cut=bpy.data.objects.new('AnimatedHandClearance',cutdata);scene.collection.objects.link(cut)
select(housing);mod=housing.modifiers.new('Original hand sweep clearance','BOOLEAN');mod.operation='DIFFERENCE';mod.solver='EXACT';mod.object=cut;apply(housing,mod)
bpy.data.objects.remove(cut,do_unlink=True)
# Explicit triangles make surface raycasts and the renderer use identical panels.
select(housing);tri=housing.modifiers.new('Consistent projection triangles','TRIANGULATE');apply(housing,tri)
with open(BASE+'/grip_clearance.json','w',encoding='utf8') as f:
 json.dump({'right_hand_vertices':len(right_indices),'animation_samples':len(points),'clearance_m':.005,'scope':'7 clips, all integer frames, right hand volume clipped to rear grip region','adaptation':'hand sweep subtracted from new housing; original hand animation untouched'},f,indent=2)
