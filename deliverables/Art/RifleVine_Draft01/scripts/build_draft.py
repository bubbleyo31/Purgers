"""依原始 FBX 骨架建立獨立美術草稿；不修改 Unity 資產與動畫驅動。"""
import bpy, math, os, json, random, hashlib
import numpy as np
from mathutils import Vector, Matrix

BASE = r'M:/UnityProject/Purgers/deliverables/Art/RifleVine_Draft01'
SOURCE = r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Attack/Materials/rifle/第一人稱_步槍_uv.fbx'
os.makedirs(BASE + '/Textures', exist_ok=True)
os.makedirs(BASE + '/Previews', exist_ok=True)
random.seed(42)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SOURCE)
scene=bpy.context.scene
arm=next(o for o in scene.objects if o.type=='ARMATURE')
arm.data.pose_position='REST'
original_meshes=[o for o in scene.objects if o.type=='MESH']
hands=bpy.data.objects['立方体.004']
original_actions=list(bpy.data.actions)
for action in original_actions:
    action.use_fake_user=True
    action.name=action.name.split('|')[-1]
# 來源 FBX 的舊貼圖 URL 指向資料夾。草稿使用獨立新貼圖，移除未使用的匯入材質。
for obj in original_meshes:obj.data.materials.clear()
for mat in list(bpy.data.materials):
    if mat.users==0:bpy.data.materials.remove(mat)
for img in list(bpy.data.images):
    if img.users==0:bpy.data.images.remove(img)

def save_texture(name, data, color=True):
    h,w=data.shape[:2]
    image=bpy.data.images.new(name,width=w,height=h,alpha=True)
    image.colorspace_settings.name='sRGB' if color else 'Non-Color'
    rgba=np.ones((h,w,4),dtype=np.float32)
    rgba[:,:,:3]=data if data.ndim==3 else data[:,:,None]
    image.pixels.foreach_set(rgba.reshape(-1))
    image.filepath_raw=BASE+'/Textures/'+name+'.png'
    image.file_format='PNG';image.save()
    return image

def material(name, base, style, rough=.75, metal=0):
    rng=np.random.default_rng(134+len(bpy.data.materials))
    size=512
    y,x=np.mgrid[0:size,0:size]/size
    noise=rng.random((size,size))-.5
    if style=='wood':
        bend=y+.02*np.sin(x*13)+.035*np.sin(x*5+y*8)
        wave=np.sin(bend*230+np.sin(bend*67)*2)
        grain=np.clip((wave-.6)*3,0,1)
        pattern=.97-.13*grain+.055*np.sin(bend*43)+noise*.10
        height=.5+wave*.05+noise*.025
    elif style=='vine':
        line=np.sin(y*110+np.sin(x*19)*.9)
        pattern=.95+line*.035+noise*.09+.065*np.sin(x*18+y*10)
        height=.5+line*.025+noise*.02
    elif style=='cloth':
        weave=np.sin(x*math.pi*256)*np.sin(y*math.pi*256)
        pattern=.98+weave*.065+noise*.04+.035*np.sin(x*31)*np.sin(y*23)
        height=.5+weave*.045
    else:
        scratch=(rng.random((size,size))>.997)*.12
        pattern=.97+noise*.065+scratch
        height=.5+noise*.015
    rgb=np.clip(np.array(base)[None,None,:]*pattern[:,:,None],0,1)
    col=save_texture(name+'_BaseColor',rgb)
    roughmap=save_texture(name+'_Roughness',np.clip(rough+noise*.07,0,1),False)
    gy,gx=np.gradient(height)
    normal=np.stack([-gx*2,-gy*2,np.ones_like(x)],axis=2)
    normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    norm=save_texture(name+'_Normal',normal*.5+.5,False)
    m=bpy.data.materials.new(name);m.use_nodes=True
    bsdf=m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Metallic'].default_value=metal
    bsdf.inputs['Roughness'].default_value=rough
    for img,socket in [(col,'Base Color'),(roughmap,'Roughness')]:
        n=m.node_tree.nodes.new('ShaderNodeTexImage');n.image=img
        m.node_tree.links.new(n.outputs['Color'],bsdf.inputs[socket])
    n=m.node_tree.nodes.new('ShaderNodeTexImage');n.image=norm
    normalnode=m.node_tree.nodes.new('ShaderNodeNormalMap');normalnode.inputs['Strength'].default_value=.35
    m.node_tree.links.new(n.outputs['Color'],normalnode.inputs['Color'])
    m.node_tree.links.new(normalnode.outputs[0],bsdf.inputs['Normal'])
    m.diffuse_color=(*base,1)
    return m

wood=material('Bark_Walnut',(.27,.145,.075),'wood',.84)
vine=material('Vine_Olive',(.13,.225,.062),'vine',.86)
core=material('Core_Charcoal',(.045,.055,.047),'metal',.67,.35)
trim=material('Trim_BrushedSteel',(.29,.32,.29),'metal',.45,.7)
cloth=material('Sleeve_Ochre',(.56,.46,.075),'cloth',.91)
glove=material('Glove_Forest',(.055,.085,.027),'cloth',.92)
bracer=material('Bracer_Graphite',(.035,.045,.031),'metal',.64,.2)
rubber=material('Rubber_Soot',(.018,.023,.018),'cloth',.95)

def select_only(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj

def rigid_bind(obj,bone='Root'):
    group=obj.vertex_groups.new(name=bone);group.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    mod=obj.modifiers.new('Source rig deformation','ARMATURE');mod.object=arm
    obj['attachment_bone']=bone

def uv_project(obj):
    uv=obj.data.uv_layers.new(name='DraftUV') if not obj.data.uv_layers else obj.data.uv_layers.active
    for poly in obj.data.polygons:
        normal=poly.normal
        axes=(1,2) if abs(normal.x)>.5 else ((0,1) if abs(normal.z)>.5 else (0,2))
        for li in poly.loop_indices:
            pos=obj.matrix_world @ obj.data.vertices[obj.data.loops[li].vertex_index].co
            uv.data[li].uv=(pos[axes[0]]*3,pos[axes[1]]*3)

def mesh_obj(name,verts,faces,mat,bone='Root',uv=True):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj);mesh.materials.append(mat)
    if uv: uv_project(obj)
    if bone: rigid_bind(obj,bone)
    return obj

def bevel(obj,width=.002,segments=2):
    select_only(obj)
    mod=obj.modifiers.new('Soft carved edges','BEVEL');mod.width=width;mod.segments=segments
    # 放在蒙皮之前，避免依目前姿勢改變建模基礎。
    if obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.update()

# 原網格與名稱保留；各部位套用材質並把硬邊倒角。
for obj in original_meshes:
    if obj==hands: continue
    mat=wood if obj.name in ['立方体.022','立方体.024'] else core
    if obj.name in ['柱体.001','立方体.025','立方体.026']:mat=trim
    obj.data.materials.clear();obj.data.materials.append(mat)
    for face in obj.data.polygons: face.material_index=0
    uv_project(obj)
    # 原物件帶有 FBX 縮放，先只套用網格空間，避免倒角單位失真。
    old_world=obj.matrix_world.copy()
    obj.data.transform(old_world);obj.matrix_world=Matrix.Identity(4)
    bevel(obj,.0015,2)

def plate(name,outline,side=1,depth=.009,mat=wood,bone='Root'):
    # 輪廓座標為 (槍長方向 Y, 高度 Z)，厚度沿 X。
    n=len(outline);x0=side*.034;x1=side*(.034+depth)
    verts=[(x,y,z) for x in (x0,x1) for y,z in outline]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    obj=mesh_obj(name,verts,faces,mat,bone);bevel(obj,.002,2);return obj

# 側面的木質長片保留不規則枝節，讓輪廓接近概念圖。
upper=[(.426,-.018),(.34,-.008),(.292,.014),(.225,.025),(.18,.013),(.13,.032),(.09,.017),(.016,.028),(-.045,.024),(-.10,.039),(-.119,.023),(-.255,.012),(-.302,.002),(-.279,-.019),(-.21,-.026),(-.15,-.02),(-.10,-.039),(-.046,-.025),(.032,-.043),(.108,-.024),(.19,-.044),(.254,-.028),(.342,-.037),(.425,-.056)]
lower=[(.426,-.147),(.34,-.14),(.293,-.151),(.23,-.13),(.19,-.152),(.13,-.138),(.091,-.165),(.071,-.141),(.003,-.15),(-.054,-.164),(-.062,-.142),(-.17,-.151),(-.218,-.135),(-.285,-.142),(-.30,-.121),(-.24,-.105),(-.194,-.12),(-.103,-.106),(-.03,-.12),(.052,-.105),(.112,-.123),(.181,-.11),(.25,-.116),(.335,-.109),(.426,-.112)]
for side in [-1,1]:
    plate('Bark_Upper_'+str(side),upper,side,.013)
    plate('Bark_Lower_'+str(side),lower,side,.011)
    plate('Bark_RearRoot_'+str(side),[(.41,-.04),(.38,-.039),(.314,-.10),(.264,-.14),(.251,-.155),(.28,-.143),(.359,-.119),(.42,-.084)],side,.016)
    plate('Bark_ForeRoot_'+str(side),[(-.16,.011),(-.20,-.028),(-.225,-.087),(-.279,-.13),(-.287,-.16),(-.264,-.143),(-.218,-.12),(-.195,-.065),(-.17,-.035),(-.14,-.015)],side,.017)

def catmull(points,steps=8):
    pts=[Vector(p) for p in points];out=[]
    for i in range(len(pts)-1):
        p0=pts[max(i-1,0)];p1=pts[i];p2=pts[i+1];p3=pts[min(i+2,len(pts)-1)]
        for j in range(steps):
            t=j/steps
            out.append(.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))
    out.append(pts[-1]);return out

def strand(name,points,width=.015,depth=.007,mat=vine,bone='Root',taper=False,steps=7):
    pts=catmull(points,steps);verts=[];faces=[];sides=8
    for i,p in enumerate(pts):
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        radial=Vector((p.x,0,p.z+.065))
        if radial.length<.001:radial=Vector((1,0,0))
        radial=(radial-tangent*radial.dot(tangent)).normalized()
        broad=tangent.cross(radial).normalized()
        t=i/(len(pts)-1)
        fac=(.12+.88*math.sin(math.pi*min(1,t*1.05))**.4) if taper else .9+.1*math.sin(t*14)
        for k in range(sides):
            ang=2*math.pi*k/sides
            verts.append(p+fac*(math.cos(ang)*depth*radial+math.sin(ang)*width*broad))
    for i in range(len(pts)-1):
        for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    faces += [tuple(range(sides-1,-1,-1)),tuple((len(pts)-1)*sides+j for j in range(sides))]
    obj=mesh_obj(name,verts,faces,mat,bone,False)
    uv=obj.data.uv_layers.new(name='StrandUV')
    for face in obj.data.polygons:
        for li in face.loop_indices:
            vi=obj.data.loops[li].vertex_index;uv.data[li].uv=(vi//sides/20,(vi%sides)/sides)
        face.use_smooth=True
    return obj

# 長藤蔓沿側面穿插；跨到背面的路徑另外建造，避免把側面設計鏡像貼平。
side_paths=[
[(.43,-.041),(.35,-.063),(.24,-.012),(.12,-.015),(.02,-.079),(-.10,-.078),(-.22,-.039),(-.35,-.060),(-.50,-.020)],
[(.41,-.117),(.32,-.083),(.21,-.085),(.07,-.038),(-.04,-.020),(-.12,-.042),(-.19,-.098),(-.31,-.108),(-.46,-.055)],
[(.39,-.145),(.27,-.119),(.20,-.056),(.11,-.076),(.01,-.112),(-.14,-.116),(-.26,-.069),(-.35,-.029),(-.49,-.053)],
[(.36,-.009),(.28,-.051),(.23,-.105),(.12,-.131),(.02,-.098),(-.09,-.085),(-.18,-.019),(-.29,-.039),(-.39,-.083)]
]
for side in [-1,1]:
    for i,path in enumerate(side_paths):
        points=[(side*(.053+.008*math.sin(j*1.8+i)),y,z+(0 if side<0 else .006*math.sin(j))) for j,(y,z) in enumerate(path)]
        points[0]=(side*.033,points[0][1],points[0][2])
        points[-1]=(side*.025,points[-1][1],points[-1][2])
        strand('Vine_Ribbon_%s_%s'%(side,i),points,.016+(i%2)*.003,.005,taper=True,steps=8)
    for i,(cy,cz,rad) in enumerate([(.205,-.056,.04),(-.07,-.062,.04),(-.31,-.085,.036)]):
        points=[]
        for j in range(15):
            t=j/14;angle=t*math.pi*2.05;r=rad*(1-t*.86)
            points.append((side*(.062+.004*math.sin(t*math.pi)),cy+r*math.sin(angle),cz+r*math.cos(angle)))
        strand('Vine_Curl_%s_%s'%(side,i),points,.007,.004,taper=True,steps=4)
# 槍身兩個環繞藤圈與槍口包覆，避開彈匣、扳機及握把活動範圍。
for idx,(y0,rx,rz) in enumerate([(.355,.046,.081),(-.235,.048,.076),(-.43,.042,.043)]):
    points=[]
    for j in range(25):
        a=j/24*math.pi*2.6
        points.append((math.cos(a)*rx,y0-.047*(j/24-.5),-.068+math.sin(a)*rz))
    strand('Vine_Wrap_'+str(idx),points,.017,.005,taper=True,steps=3)

# 瞄具保留原有孔徑，薄藤圈僅包覆外緣。
points=[]
for j in range(25):
    a=j/24*math.pi*2
    points.append((math.cos(a)*.038,.033+.008*math.sin(a*2),.0647+math.sin(a)*.038))
strand('Vine_SightRim',points,.005,.004,taper=True,steps=3)

# 彈匣裝飾只綁 magazine；保留拆卸空隙。
for side in [-1,1]:
    for i in range(3):
        points=[(side*.0325,.251+i*.026,-.183),(side*.033,.239+i*.027,-.241),(side*.033,.220+i*.028,-.310)]
        strand('Magazine_Flute_%s_%s'%(side,i),points,.003,.0025,trim,'magazine',steps=5)
    plate('Magazine_Floor_'+str(side),[(.218,-.315),(.3,-.326),(.31,-.344),(.224,-.348),(.206,-.334)],side,.003,core,'magazine')

# 槍口內襯：開口可見，避免灰盒封閉圓柱直接作為正式槍口。
def muzzle_ring():
    verts=[];faces=[];n=20
    for y,r in [(-.546,.027),(-.557,.027),(-.557,.017),(-.540,.017)]:
        for i in range(n):
            a=i/n*2*math.pi;verts.append((math.cos(a)*r,y,-.046+math.sin(a)*r))
    for ring in range(3):
        for i in range(n):faces.append((ring*n+i,ring*n+(i+1)%n,(ring+1)*n+(i+1)%n,(ring+1)*n+i))
    obj=mesh_obj('Muzzle_SteelLip',verts,faces,trim);bevel(obj,.0008,1)
    # 深色內片在原封口之前，預覽時提供口徑深度。
    mesh_obj('Muzzle_DarkBore',[(0,-.549,-.046)]+[(math.cos(i/n*2*math.pi)*.017,-.549,-.046+math.sin(i/n*2*math.pi)*.017) for i in range(n)],[(0,i+1,(i+1)%n+1) for i in range(n)],rubber)
muzzle_ring()

# 手臂沿用全部骨骼權重；依手腕到前臂區域區分服裝材質。
hand_world=hands.matrix_world.copy();hands.data.transform(hand_world);hands.matrix_world=Matrix.Identity(4)
hands.data.materials.clear()
for m in [cloth,bracer,glove,rubber,trim]:hands.data.materials.append(m)
for vertex in hands.data.vertices:
    p=vertex.co
    if p.z>-.27:
        side='l' if p.x>0 else 'r'
        b=arm.data.bones['骨骼.002.'+side]
        head=arm.matrix_world @ b.head_local;tail=arm.matrix_world @ b.tail_local
        t=(p.z-head.z)/(tail.z-head.z)
        axis=head+(tail-head)*t
        fac=1.055+.025*math.sin(p.z*110)
        p.x=axis.x+(p.x-axis.x)*fac;p.y=axis.y+(p.y-axis.y)*fac
for face in hands.data.polygons:
    z=sum(hands.data.vertices[i].co.z for i in face.vertices)/len(face.vertices)
    face.material_index=0 if z>-.265 else (1 if z>-.424 else (2 if z>-.642 else 3))
    face.use_smooth=True
uv_project(hands)

def section_band(side,z,width,name,mat):
    # 從既有皮膚邊緣切取截面；新增環的權重以兩端插值，避免套用陌生骨架。
    intersections=[]
    for e in hands.data.edges:
        va,vb=[hands.data.vertices[i] for i in e.vertices];a,b=va.co,vb.co
        if a.x*side<=0 or b.x*side<=0 or (a.z-z)*(b.z-z)>=0:continue
        t=(z-a.z)/(b.z-a.z);p=a.lerp(b,t)
        weights={}
        for vertex,f in [(va,1-t),(vb,t)]:
            for g in vertex.groups:weights[g.group]=weights.get(g.group,0)+g.weight*f
        intersections.append((p,weights))
    if len(intersections)<3:return
    center=sum((p for p,w in intersections),Vector())/len(intersections)
    intersections.sort(key=lambda a:math.atan2(a[0].y-center.y,a[0].x-center.x))
    n=len(intersections);verts=[];faces=[]
    for dz,scale in [(-width/2,1.04),(width/2,1.04)]:
        for p,w in intersections:
            q=center+(p-center)*scale;q.z+=dz;verts.append(q)
    for i in range(n):faces.append((i,(i+1)%n,(i+1)%n+n,i+n))
    obj=mesh_obj(name,verts,faces,mat,None)
    obj['region']='arms'
    for g in hands.vertex_groups:obj.vertex_groups.new(name=g.name)
    for i,(p,weights) in enumerate(intersections):
        for index,weight in weights.items():obj.vertex_groups[index].add([i,i+n],weight,'REPLACE')
    select_only(obj)
    solid=obj.modifiers.new('Cuff thickness','SOLIDIFY');solid.thickness=.003
    bpy.ops.object.modifier_apply(modifier=solid.name)
    bevel(obj,.0008,2)
    mod=obj.modifiers.new('Source rig deformation','ARMATURE');mod.object=arm
    for face in obj.data.polygons:face.use_smooth=True
for side in [-1,1]:
    section_band(side,-.269,.012,'Cuff_Upper_'+str(side),trim)
    section_band(side,-.422,.009,'Cuff_Wrist_'+str(side),trim)
    section_band(side,-.295,.009,'Bracer_Strap_'+str(side),rubber)

# 各前臂外側加一片薄硬質護板，直接複製原權重。
for side in [-1,1]:
    chosen=[]
    for face in hands.data.polygons:
        center=sum((hands.data.vertices[i].co for i in face.vertices),Vector())/len(face.vertices)
        if center.x*side>0 and -.397<center.z<-.298 and center.y>.134:chosen.append(face)
    ids=sorted({i for f in chosen for i in f.vertices});mapping={v:i for i,v in enumerate(ids)}
    verts=[hands.data.vertices[i].co+Vector((0,.002,0)) for i in ids]
    faces=[tuple(mapping[i] for i in f.vertices) for f in chosen]
    if faces:
        obj=mesh_obj('Bracer_Plate_'+str(side),verts,faces,bracer,None)
        obj['region']='arms'
        for group in hands.vertex_groups:obj.vertex_groups.new(name=group.name)
        for i,old in enumerate(ids):
            for g in hands.data.vertices[old].groups:obj.vertex_groups[g.group].add([i],g.weight,'REPLACE')
        select_only(obj);mod=obj.modifiers.new('Guard depth','SOLIDIFY');mod.thickness=.003
        bpy.ops.object.modifier_apply(modifier=mod.name);bevel(obj,.001,2)
        mod=obj.modifiers.new('Source rig deformation','ARMATURE');mod.object=arm

# 以材質＋骨架合併新增零件，減少 Unity 的 SkinnedMeshRenderer 數量。
for region in ['weapon','arms']:
    for mat in [wood,vine,core,trim,rubber,bracer]:
        new_meshes=[o for o in scene.objects if o.type=='MESH' and o not in original_meshes]
        group=[o for o in new_meshes if o.data.materials and o.data.materials[0]==mat and o.get('region','weapon')==region]
        if not group:continue
        bpy.ops.object.select_all(action='DESELECT')
        for obj in group:obj.select_set(True)
        bpy.context.view_layer.objects.active=group[0];bpy.ops.object.join()
        group[0].name='Detail_'+region+'_'+mat.name
        group[0]['region']=region

asset_objects=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
for o in asset_objects:o['draft_status']='Review draft 01; Unity import and clipping acceptance pending'

# 輸出全部六段來源動作，保留原始名稱與骨骼。
arm.data.pose_position='POSE'
arm.animation_data.action=next(a for a in original_actions if a.name=='idle')
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/RifleVine_Arms_Draft01.fbx',use_selection=True,
    object_types={'ARMATURE','MESH'},add_leaf_bones=False,mesh_smooth_type='FACE',
    bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
    bake_anim_simplify_factor=0.0,bake_anim_force_startend_keying=True,
    path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')

# 模型拍攝：實際網格的側面、斜角與持槍畫面。
scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.1;scene.eevee.gtao_factor=1.15
scene.eevee.taa_render_samples=96
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
scene.world=bpy.data.worlds.new('Review Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.043,.05,.055,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.5
studio=[]
bpy.ops.object.camera_add();camera=bpy.context.object;studio.append(camera);scene.camera=camera
for name,pos,power,size in [('Key',(-1,-.4,1.5),110,1.4),('Fill',(1,.4,.6),75,1.2),('Rim',(0,1,.8),95,.8)]:
    bpy.ops.object.light_add(type='AREA',location=pos);light=bpy.context.object;studio.append(light)
    light.name='Studio_'+name;light.data.energy=power;light.data.shape='DISK';light.data.size=size
    light.rotation_euler=(Vector((0,0,-.1))-light.location).to_track_quat('-Z','Y').to_euler()

def render(name,position,target,scale=1.22,res=(1600,900),perspective=False):
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='PERSP' if perspective else 'ORTHO';camera.data.ortho_scale=scale;camera.data.lens=35
    scene.render.resolution_x=res[0];scene.render.resolution_y=res[1]
    scene.render.filepath=BASE+'/Previews/'+name+'.png';bpy.ops.render.render(write_still=True)

arm.data.pose_position='REST'
hands.hide_render=True
for o in asset_objects:
    if o.get('region')=='arms':o.hide_render=True
render('01_Rifle_Side',(-2,-.015,-.10),(0,-.045,-.105),1.19)
render('02_Rifle_ThreeQuarter',(-1.6,-.60,.45),(0,-.045,-.10),1.25)
render('03_Rifle_Opposite',(1.8,.22,.24),(0,-.045,-.10),1.22)
hands.hide_render=False;arm.data.pose_position='POSE';scene.frame_set(1)
for o in asset_objects:
    if o.get('region')=='arms':o.hide_render=False
# 實際攝影機尚需 Unity 確認；此鏡頭僅為持槍檢視。
render('04_Idle_Arms',(-1.32,-1.6,.48),(-.26,-.57,-.32),1.28)
render('05_FirstPerson',(-.27,.15,-.02),(-.285,-.78,-.265),1.2,(1600,1000),True)
arm.animation_data.action=next(a for a in original_actions if a.name=='Reload.001')
for frame in [1,20,40,63]:
    scene.frame_set(frame)
    render('Reload_%02d'%frame,(-1.5,-1.7,.45),(-.25,-.62,-.28),1.58,(1000,680))
arm.animation_data.action=next(a for a in original_actions if a.name=='idle');scene.frame_set(1)
render('00_Hero',(-1.25,-1.4,.45),(-.26,-.61,-.31),1.2,(1800,1200))
for image in bpy.data.images:
    if image.source=='FILE' and os.path.isfile(bpy.path.abspath(image.filepath)):image.pack()
scene.frame_start=1;scene.frame_end=61
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/RifleVine_Arms_Draft01.blend')
report={'source_sha256':hashlib.sha256(open(SOURCE,'rb').read()).hexdigest(),
    'meshes':len([o for o in asset_objects if o.type=='MESH']),
    'vertices':sum(len(o.data.vertices) for o in asset_objects if o.type=='MESH'),
    'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in asset_objects if o.type=='MESH'),
    'bones':len(arm.data.bones),
    'actions':[{'name':a.name,'frames':list(a.frame_range),'curves':len(a.fcurves)} for a in original_actions],
    'notes':['Procedural draft textures, not final hand-painted art','Unity MCP unavailable; no Unity runtime acceptance','Original Unity assets unchanged']}
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print('DRAFT_COMPLETE',json.dumps(report,ensure_ascii=False))
