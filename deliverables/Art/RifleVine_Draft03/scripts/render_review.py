"""只渲染實際 Blender 模型；格線／燈光不輸出 FBX。"""
import bpy,os,math,json
from mathutils import Vector
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.open_mainfile(filepath=BASE+'/RifleVine_Arms_Draft03.blend')
scene=bpy.context.scene;arm=next(o for o in scene.objects if o.type=='ARMATURE')
assets=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
hands=bpy.data.objects['立方体.004']
scene.render.engine='BLENDER_EEVEE'
scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.065;scene.eevee.gtao_factor=1.1
scene.eevee.use_ssr=True;scene.eevee.use_ssr_refraction=True;scene.eevee.taa_render_samples=64
scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.3
scene.world=bpy.data.worlds.new('Concept02 Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.065,.078,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.42
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Review_Camera';scene.camera=camera
for name,pos,power,size,color in [('Key',(-1.1,-.5,1.2),160,1.25,(1,.9,.77)),('Fill',(1,.4,.8),100,1.3,(.73,.85,1)),('Rim',(.2,.9,1.1),175,1.0,(1,.96,.85))]:
    bpy.ops.object.light_add(type='AREA',location=pos);o=bpy.context.object;o.name='Studio_'+name
    o.data.energy=power;o.data.size=size;o.data.color=color
    o.rotation_euler=(Vector((0,0,-.1))-o.location).to_track_quat('-Z','Y').to_euler()
def render(name,pos,target,scale=1.2,res=(1600,1000),perspective=False,lens=40):
    camera.location=pos;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='PERSP' if perspective else 'ORTHO';camera.data.ortho_scale=scale;camera.data.lens=lens;camera.data.clip_start=.001
    scene.render.resolution_x=res[0];scene.render.resolution_y=res[1]
    scene.render.filepath=BASE+'/Previews/'+name+'.png';bpy.ops.render.render(write_still=True)
    print('RENDERED',name,flush=True)
arm.data.pose_position='REST'
for o in assets:
    if o==hands or o.get('region')=='arms':o.hide_render=True
render('01_Rifle_Side',(-2,-.015,-.08),(0,-.035,-.080),1.15)
render('02_Rifle_ThreeQuarter',(-1.6,-.65,.46),(0,-.035,-.080),1.23,(1800,1100))
render('03_Optic_Closeup',(-.36,-.30,.23),(0,.020,.060),.23,(1400,1000))
render('04_Stock_Magazine',(-.75,.54,.17),(0,.305,-.095),.49,(1400,1000))
render('05_Opposite',(1.6,-.65,.46),(0,-.035,-.08),1.23)
# 四角面線框：只顯示新建區塊的真實拓撲，不用三角化 wireframe shader。
wiremat=bpy.data.materials.new('Review_Wire');wiremat.diffuse_color=(.015,.55,.30,1)
wire_objects=[]
for o in assets:
    if o.type!='MESH' or not o.get('concept02_new'):continue
    c=o.copy();c.data=o.data.copy();scene.collection.objects.link(c);c.modifiers.clear()
    c.data.materials.clear();c.data.materials.append(wiremat)
    for p in c.data.polygons:p.material_index=0
    w=c.modifiers.new('Review quad edges','WIREFRAME');w.thickness=.00028;w.offset=1
    wire_objects.append(c)
render('06_NewParts_QuadWire',(-1.6,-.65,.46),(0,-.035,-.080),1.23,(1800,1100))
for o in wire_objects:bpy.data.objects.remove(o,do_unlink=True)
for o in assets:o.hide_render=False
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
render('00_Hero',(-1.25,-1.4,.45),(-.26,-.61,-.30),1.25,(1800,1200))
render('07_FirstPerson',(-.27,.15,-.02),(-.285,-.78,-.265),1.2,(1400,1000),True,35)
arm.animation_data.action=bpy.data.actions['Opening the scope'];scene.frame_set(4)
root=arm.matrix_world@arm.pose.bones['Root'].matrix@arm.data.bones['Root'].matrix_local.inverted()@arm.matrix_world.inverted()
axis=.0647295;eye=root@Vector((0,.19,axis));target=root@Vector((0,-.08,axis))
# 格線背景，讓透明鏡片、中央視線通道可直接檢視。
def flatmat(name,col):
    m=bpy.data.materials.new(name);m.diffuse_color=(*col,1);return m
bg=flatmat('Review_Background',(.40,.46,.44));line=flatmat('Review_Lines',(.065,.12,.10))
guides=[]
def quad(x1,x2,z1,z2,y,mat):
    verts=[root@Vector((x,y,axis+z)) for x,z in [(x1,z1),(x2,z1),(x2,z2),(x1,z2)]]
    d=bpy.data.meshes.new('Review_Grid');d.from_pydata(verts,[],[(0,1,2,3)]);d.materials.append(mat)
    o=bpy.data.objects.new('Review_Grid',d);scene.collection.objects.link(o);guides.append(o)
quad(-.28,.28,-.28,.28,-.94,bg)
for i in range(-7,8):
    a=i*.04;quad(a-.0008,a+.0008,-.28,.28,-.939,line);quad(-.28,.28,a-.0008,a+.0008,-.938,line)
render('08_ADS_Axis',eye,target,.3,(1400,1000),True,35)
for o in guides:bpy.data.objects.remove(o,do_unlink=True)
with open(BASE+'/optic_setup.json',encoding='utf8') as f:m=json.load(f)
m['ads_frame4_review_camera']={'position':list(eye),'target':list(target),'scope':'Optical axis reference, not the live Unity camera'}
with open(BASE+'/optic_setup.json','w',encoding='utf8') as f:json.dump(m,f,ensure_ascii=False,indent=2)
arm.animation_data.action=bpy.data.actions['Reload.001']
for frame in [1,20,40,63]:
    scene.frame_set(frame);render('Reload_%02d'%frame,(-1.5,-1.7,.45),(-.25,-.62,-.28),1.58,(1000,680))
arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
render('00_Hero',(-1.25,-1.4,.45),(-.26,-.61,-.30),1.25,(1800,1200))
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/RifleVine_Arms_Draft03.blend')
print('REVIEW_COMPLETE',flush=True)

