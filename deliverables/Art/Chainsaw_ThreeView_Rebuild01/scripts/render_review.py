scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.08;scene.eevee.gtao_factor=1.12;scene.eevee.use_ssr=True;scene.eevee.taa_render_samples=64
scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.1
scene.world=bpy.data.worlds.new('Chainsaw Studio');scene.world.use_nodes=True;next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.052,.061,.07,1);next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.45
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Review_Camera';scene.camera=camera
for name,pos,power,size,color in [('Key',(-1.3,-.7,1.4),190,1.5,(1,.92,.82)),('Fill',(1,.7,.8),120,1.8,(.76,.85,1)),('Rim',(.2,.8,1.4),160,1.2,(1,.95,.85))]:
 bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.name='Studio_'+name;l.data.energy=power;l.data.size=size;l.data.color=color;l.rotation_euler=(Vector((0,0,-.1))-l.location).to_track_quat('-Z','Y').to_euler()
def render(name,position,target,scale=1.6,res=(1800,1100),perspective=False,lens=40):
 camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='PERSP' if perspective else 'ORTHO';camera.data.ortho_scale=scale;camera.data.lens=lens;camera.data.clip_start=.001
 if name=='07_Top':camera.rotation_euler.z-=math.pi/2
 scene.render.resolution_x=res[0];scene.render.resolution_y=res[1];scene.render.filepath=BASE+'/Previews/'+name+'.png';bpy.ops.render.render(write_still=True)
def pose_center():
 dg=bpy.context.evaluated_depsgraph_get();pts=[]
 for o in asset_objects:
  if o.type=='MESH':
   e=o.evaluated_get(dg);pts.extend(e.matrix_world@Vector(p) for p in e.bound_box)
 lo=Vector([min(p[i] for p in pts) for i in range(3)]);hi=Vector([max(p[i] for p in pts) for i in range(3)])
 return (lo+hi)/2,max(hi-lo)
arm.data.pose_position='REST'
for o in asset_objects:
 if o==hands or o.get('region')=='arms':o.hide_render=True
render('01_Chainsaw_Side',(-3,-.28,-.09),(0,-.28,-.09),2.37,(2100,850))
render('07_Top',(0,-.28,3),(0,-.28,-.05),2.37,(2100,850))
render('08_Front',(0,-3,-.025),(0,0,-.025),.72,(1000,1100))
render('02_Chainsaw_ThreeQuarter',(-2.4,-1.30,.65),(0,-.28,-.05),2.5)
render('03_Blade_Detail',(-1.4,-.7,.25),(-.012,-.66,-.07),1.18,(1500,1100))
render('04_Case_Detail',(-1,.9,.53),(0,.36,-.035),.90,(1500,1100))
render('05_Reverse',(1.8,-1.1,.65),(0,-.28,-.05),2.5)
clay=bpy.data.materials.new('Geometry_Review_Clay');clay.diffuse_color=(.33,.33,.33,1);clay.use_nodes=True
next(n for n in clay.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Base Color'].default_value=(.33,.33,.33,1)
next(n for n in clay.node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Roughness'].default_value=.82
saved_materials={o:list(o.data.materials) for o in asset_objects if o.type=='MESH'}
for o,mats in saved_materials.items():
 for i in range(len(mats)):o.data.materials[i]=clay
render('06_Geometry_Only',(-2.4,-1.30,.65),(0,-.28,-.05),2.5)
for o,mats in saved_materials.items():
 for i,m in enumerate(mats):o.data.materials[i]=m
for o in asset_objects:o.hide_render=False
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
c,e=pose_center();render('00_Hero',c+Vector((-1.2,-1.3,.55)),c,e*1.3,(1800,1200))
for action,frames in [('attack1',[1,13,26]),('attack2',[13]),('attack3.001',[13]),('attack4',[11]),('melee',[12]),('partition',[7])]:
 arm.animation_data.action=bpy.data.actions[action]
 for frame in frames:
  scene.frame_set(frame);c,e=pose_center();render(action.replace('.','_')+'_%02d'%frame,c+Vector((-1.3,-1.4,.55)),c,e*1.36,(1100,800))
arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
c,e=pose_center();camera.location=c+Vector((-1.2,-1.3,.55));camera.rotation_euler=(c-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=e*1.3
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/Chainsaw_ThreeView_Animated.blend')




