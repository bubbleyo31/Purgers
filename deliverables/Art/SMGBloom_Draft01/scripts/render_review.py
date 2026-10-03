# Actual mesh review renders, no AI concept image substituted for geometry.
scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.065;scene.eevee.gtao_factor=1.15
scene.eevee.use_ssr=True;scene.eevee.use_ssr_refraction=True;scene.eevee.taa_render_samples=64
scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.2
scene.world=bpy.data.worlds.new('SMG Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.067,.08,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Review_Camera';scene.camera=camera
for name,pos,power,size,color in [('Key',(-1.1,-.5,1.2),155,1.25,(1,.91,.8)),('Fill',(1,.4,.8),95,1.3,(.76,.86,1)),('Rim',(.2,.9,1.1),165,1,(1,.95,.84))]:
    bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.name='Studio_'+name;l.data.energy=power;l.data.size=size;l.data.color=color;l.rotation_euler=(Vector((0,0,-.1))-l.location).to_track_quat('-Z','Y').to_euler()
def render(name,position,target,scale=1,res=(1600,1100),perspective=False,lens=42):
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='PERSP' if perspective else 'ORTHO';camera.data.ortho_scale=scale;camera.data.lens=lens;camera.data.clip_start=.001
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
render('01_SMG_Side',(-2,0,-.145),(0,0,-.145),.94,(1800,1200))
render('02_SMG_ThreeQuarter',(-1.4,-.6,.36),(0,-.012,-.125),1.02,(1800,1200))
render('03_Reflex_Detail',(-.26,.26,.23),(0,.02,.077),.23,(1400,1100))
render('04_Flower_Detail',(-.5,.22,.19),(0,.205,-.025),.21,(1400,1100))
for o in asset_objects:o.hide_render=False
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
center,extent=pose_center();render('00_Hero',center+Vector((-1.2,-1.3,.55)),center,extent*1.33,(1800,1200))
T=arm.matrix_world@arm.pose.bones['Root'].matrix@arm.data.bones['Root'].matrix_local.inverted()@arm.matrix_world.inverted()
render('05_FirstPerson',T@Vector((-.07,.45,.17)),T@Vector((0,-.2,-.07)),1,(1600,1100),True,34)
arm.animation_data.action=bpy.data.actions['Opening the scope'];scene.frame_set(4)
T=arm.matrix_world@arm.pose.bones['Root'].matrix@arm.data.bones['Root'].matrix_local.inverted()@arm.matrix_world.inverted()
eye=T@Vector((0,.20,AXIS_Z));target=T@Vector((0,-.3,AXIS_Z))
# Grid is for aperture inspection only, never exported into the FBX.
guide=bpy.data.materials.new('Review_Grid');guide.diffuse_color=(.3,.37,.34,1)
dark=bpy.data.materials.new('Review_Line');dark.diffuse_color=(.035,.07,.06,1)
review=[]
def gridquad(x1,x2,z1,z2,y,mat):
    pts=[T@Vector((x,y,AXIS_Z+z)) for x,z in [(x1,z1),(x2,z1),(x2,z2),(x1,z2)]]
    review.append(mesh('ReviewOnly_Grid',pts,[(0,1,2,3)],mat,None,'review_only'))
gridquad(-.3,.3,-.3,.3,-1,guide)
for j in range(-7,8):
    a=j*.04;gridquad(a-.0007,a+.0007,-.3,.3,-.999,dark);gridquad(-.3,.3,a-.0007,a+.0007,-.998,dark)
render('06_Aperture_Axis',eye,target,.3,(1400,1000),True,42)
for o in review:bpy.data.objects.remove(o,do_unlink=True)
arm.animation_data.action=bpy.data.actions['Reload']
for f in [1,12,24,36]:
    scene.frame_set(f);center,extent=pose_center();render('Reload_%02d'%f,center+Vector((-1.1,-1.25,.4)),center,max(extent*1.4,1.25),(1100,800))
arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
center,extent=pose_center();camera.location=center+Vector((-1.2,-1.3,.55));camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=extent*1.33
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/SMGBloom_Arms_Draft01.blend')
