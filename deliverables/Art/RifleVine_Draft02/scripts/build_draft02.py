"""第二版：在第一版上迭代寫實表面與獨立 ACOG 造型模組，保留來源動作。"""
import bpy, bmesh, math, os, json, random, hashlib
import numpy as np
from mathutils import Vector, Matrix, noise
from mathutils.kdtree import KDTree
from mathutils.bvhtree import BVHTree

ROOT=r'M:/UnityProject/Purgers'
BASE=ROOT+'/deliverables/Art/RifleVine_Draft02'
PREVIOUS=ROOT+'/deliverables/Art/RifleVine_Draft01/RifleVine_Arms_Draft01.blend'
SOURCE=ROOT+'/Assets/_Project_Assets/Models/Player/Profession/Attack/Materials/rifle/第一人稱_步槍_uv.fbx'
for d in ['Textures','Previews']:os.makedirs(BASE+'/'+d,exist_ok=True)
random.seed(2502)
bpy.ops.wm.open_mainfile(filepath=PREVIOUS)
scene=bpy.context.scene
arm=next(o for o in scene.objects if o.type=='ARMATURE')
arm.data.pose_position='REST'
hands=bpy.data.objects['立方体.004']
old_materials={m.name:m for m in bpy.data.materials}
for a in bpy.data.actions:a.name=a.name.split('|')[-1];a.use_fake_user=True
for o in list(scene.objects):
    if o.type in {'CAMERA','LIGHT'}:bpy.data.objects.remove(o,do_unlink=True)

def select(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj

def apply(obj,modifier):
    select(obj);bpy.ops.object.modifier_apply(modifier=modifier.name)

def bind(obj,bone='Root'):
    g=obj.vertex_groups.new(name=bone);g.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    m=obj.modifiers.new('Source rig deformation','ARMATURE');m.object=arm

def uv_project(obj,scale=5):
    uv=obj.data.uv_layers.active or obj.data.uv_layers.new(name='SurfaceUV')
    obj.data.update()
    for p in obj.data.polygons:
        n=p.normal;axes=(1,2) if abs(n.x)>.5 else ((0,1) if abs(n.z)>.5 else (0,2))
        for li in p.loop_indices:
            v=obj.data.vertices[obj.data.loops[li].vertex_index].co
            uv.data[li].uv=(v[axes[0]]*scale,v[axes[1]]*scale)

def mesh(name,verts,faces,mat,bone='Root',region='weapon'):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);data.materials.append(mat)
    obj['region']=region;uv_project(obj)
    if bone:bind(obj,bone)
    return obj

def bevel(obj,width=.001,segments=3):
    mod=obj.modifiers.new('Edge radii','BEVEL');mod.width=width;mod.segments=segments
    select(obj)
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    apply(obj,mod)
    # 保留大面的平整法線，減少金屬側面的不自然凹痕。
    obj.data.use_auto_smooth=True
    mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL');mod.keep_sharp=True;mod.weight=50
    while obj.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
    apply(obj,mod)

def smooth_noise(size,grid,rng):
    raw=rng.random((grid+1,grid+1)).astype(np.float32)
    coords=np.arange(size,dtype=np.float32)/size*grid
    lo=coords.astype(int);t=coords-lo;t=t*t*(3-2*t)
    a=raw[lo[:,None],lo[None,:]];b=raw[lo[:,None]+1,lo[None,:]]
    c=raw[lo[:,None],lo[None,:]+1];d=raw[lo[:,None]+1,lo[None,:]+1]
    return (a*(1-t[:,None])+b*t[:,None])*(1-t[None,:])+(c*(1-t[:,None])+d*t[:,None])*t[None,:]

def save_image(name,data,color=True,alpha=None):
    size=data.shape[0];img=bpy.data.images.new(name,width=size,height=size,alpha=True)
    img.colorspace_settings.name='sRGB' if color else 'Non-Color'
    rgba=np.ones((size,size,4),np.float32)
    rgba[:,:,:3]=data if data.ndim==3 else data[:,:,None]
    if alpha is not None:rgba[:,:,3]=alpha
    img.pixels.foreach_set(rgba.ravel());img.filepath_raw=BASE+'/Textures/'+name+'.png';img.file_format='PNG';img.save()
    return img

palette={}
def surface(name,base,kind,rough=.7,metal=0,size=1024):
    rng=np.random.default_rng(101+len(palette));y,x=np.mgrid[0:size,0:size].astype(np.float32)/size
    low=smooth_noise(size,7,rng);med=smooth_noise(size,39,rng);fine=smooth_noise(size,180,rng);grain=rng.random((size,size)).astype(np.float32)
    fbm=(low*.48+med*.3+fine*.15+grain*.07)
    variation=.75+fbm*.45;h=fbm*.035
    if kind=='bark':
        phase=y*115+low*11+med*2
        fissure=np.clip((np.sin(phase)-.61)*2.5,0,1)
        woodgrain=np.sin(y*480+low*29+med*5)
        variation=.59+fbm*.7-fissure*.24+woodgrain*.045
        h=fbm*.2-fissure*.12+woodgrain*.008
    elif kind=='vine':
        striation=np.sin(y*155+low*9)
        pores=np.clip((grain-.94)*16,0,1)*(med>.52)
        variation=.63+fbm*.7+striation*.065-pores*.12
        h=fbm*.06+striation*.022-pores*.025
    elif kind in {'cloth','leather'}:
        if kind=='cloth':
            weave=np.sin(x*2*math.pi*190)*np.sin(y*2*math.pi*190)
            cross=(np.sin((x-y)*math.pi*320)>.7).astype(np.float32)
            variation=.86+fbm*.22+weave*.045-cross*.045;h=weave*.055+cross*.025+fbm*.04
        else:
            cells=np.abs(np.sin((x*240+med*3))*np.sin((y*235+fine*2)))
            variation=.75+fbm*.36+cells*.065;h=cells*.08+fbm*.03
    elif kind=='metal':
        sc=np.zeros_like(x)
        for i in range(45):
            sx,sy=rng.random(2);length=rng.uniform(.004,.05);angle=rng.uniform(-.45,.45)
            dx=x-sx;dy=y-sy-angle*dx
            sc=np.maximum(sc,np.exp(-(dy/.00055)**2)*(np.abs(dx)<length))
        variation=.86+fbm*.22+sc*.24;h=grain*.025-sc*.025
    rgb=np.clip(np.array(base)[None,None,:]*variation[:,:,None],0,1)
    if kind=='bark':rgb[:,:,1]+=np.clip((low-.58)*.09,0,.03)
    if kind=='vine':rgb[:,:,0]+=np.clip((low-.56)*.15,0,.06)
    roughmap=np.clip(rough+(fbm-.5)*.20,0,1)
    gy,gx=np.gradient(h);strength=12 if kind=='bark' else (7 if kind=='vine' else 3)
    normal=np.stack([-gx*strength,-gy*strength,np.ones_like(x)],2);normal/=np.linalg.norm(normal,axis=2)[:,:,None]
    images={'BaseColor':save_image(name+'_BaseColor',rgb),
            'Normal':save_image(name+'_Normal',normal*.5+.5,False),
            'Roughness':save_image(name+'_Roughness',roughmap,False)}
    packed=np.zeros((size,size,3),np.float32);packed[:,:,0]=metal
    save_image(name+'_MetallicSmoothness',packed,False,1-roughmap)
    mat=bpy.data.materials.new(name+'_R2');mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Metallic'].default_value=metal
    for label,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:
        n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=images[label];mat.node_tree.links.new(n.outputs['Color'],bs.inputs[socket])
    n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=images['Normal']
    normalnode=mat.node_tree.nodes.new('ShaderNodeNormalMap');normalnode.inputs['Strength'].default_value=.7
    mat.node_tree.links.new(n.outputs['Color'],normalnode.inputs['Color']);mat.node_tree.links.new(normalnode.outputs[0],bs.inputs['Normal'])
    mat.diffuse_color=(*base,1);palette[name]={'material':mat.name,'metallic':metal,'roughness':rough,'resolution':size}
    return mat

wood=surface('Bark_Walnut',(.14,.072,.028),'bark',.84,size=2048)
vine=surface('Vine_Olive',(.058,.088,.024),'vine',.79,size=2048)
core=surface('Core_Charcoal',(.03,.039,.033),'metal',.56,.55)
steel=surface('Trim_BrushedSteel',(.24,.255,.24),'metal',.38,.88)
cloth=surface('Sleeve_Ochre',(.41,.285,.04),'cloth',.86)
glove=surface('Glove_Forest',(.04,.054,.025),'leather',.66)
bracer=surface('Bracer_Graphite',(.025,.031,.024),'metal',.54,.18)
rubber=surface('Rubber_Soot',(.016,.018,.014),'leather',.91)
opticmat=surface('Optic_Anodized',(.027,.031,.031),'metal',.49,.65)
thread=surface('Seam_Thread',(.06,.07,.037),'cloth',.9,size=512)
new_mats=[wood,vine,core,steel,cloth,glove,bracer,rubber]
for oldname,newmat in zip(['Bark_Walnut','Vine_Olive','Core_Charcoal','Trim_BrushedSteel','Sleeve_Ochre','Glove_Forest','Bracer_Graphite','Rubber_Soot'],new_mats):
    old=old_materials.get(oldname)
    if old:
        for obj in scene.objects:
            if obj.type=='MESH':
                for slot in obj.material_slots:
                    if slot.material==old:slot.material=newmat

# 舊準星由可獨立替換的鏡體取代，槍身幾何與骨架不更動。
old_sight=bpy.data.objects.get('柱体.002')
if old_sight:bpy.data.objects.remove(old_sight,do_unlink=True)
vines=bpy.data.objects['Detail_weapon_Vine_Olive']
bm=bmesh.new();bm.from_mesh(vines.data);remaining=set(bm.verts)
remove=[]
while remaining:
    first=remaining.pop();comp=[first];stack=[first]
    while stack:
        v=stack.pop()
        for e in v.link_edges:
            n=e.other_vert(v)
            if n in remaining:remaining.remove(n);comp.append(n);stack.append(n)
    center=sum((v.co for v in comp),Vector())/len(comp)
    if max(v.co.z for v in comp)>.09 and .01<center.y<.06:remove.extend(comp)
    else:
        # 打散帶狀藤蔓的均勻表面，加上細微自然凹凸。
        for v in comp:
            p=v.co;value=noise.noise_vector(p*110)[0]
            v.co+=v.normal*(value*.0007)
bmesh.ops.delete(bm,geom=remove,context='VERTS');bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(vines.data);bm.free()

# 木殼切割面與樹皮在幾何上稍有高低，不再是完全平整的板片。
bark=bpy.data.objects['Detail_weapon_Bark_Walnut']
for v in bark.data.vertices:
    if abs(v.co.x)>.038:
        amount=noise.noise(v.co*47)*.0013+noise.noise(v.co*130)*.0004
        v.co.x+=math.copysign(amount,v.co.x)
uv_project(bark,5)

# 手臂平滑一次，保留蒙皮插值；小幅袖褶只作用在布料範圍。
select(hands);sub=hands.modifiers.new('Garment and glove refinement','SUBSURF');sub.levels=1
while hands.modifiers.find(sub.name)>0:bpy.ops.object.modifier_move_up(modifier=sub.name)
apply(hands,sub)
for v in hands.data.vertices:
    p=v.co
    if p.z>-.25:
        wrinkle=(math.sin(p.z*132+p.x*25)*.0014+noise.noise(p*72)*.001)*max(0,1-abs(p.z+.17)/.4)
        v.co+=v.normal*wrinkle
for p in hands.data.polygons:p.use_smooth=True

def box(name,center,size,mat=opticmat,bone='Root',region='optic',radius=.001):
    bpy.ops.mesh.primitive_cube_add(size=1,location=center);obj=bpy.context.object;obj.name=name
    obj.scale=size;select(obj);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.data.materials.append(mat);obj['region']=region;uv_project(obj)
    if radius:bevel(obj,radius,3)
    if bone:bind(obj,bone)
    return obj

def lathe(name,rings,z=.0647295,mat=opticmat,n=48,bone='Root',region='optic',cap=False):
    verts=[(math.cos(2*math.pi*i/n)*r,y,z+math.sin(2*math.pi*i/n)*r) for y,r in rings for i in range(n)]
    faces=[]
    for k in range(len(rings)-1):
        for i in range(n):faces.append((k*n+i,k*n+(i+1)%n,(k+1)*n+(i+1)%n,(k+1)*n+i))
    if cap:faces += [tuple(range(n-1,-1,-1)),tuple((len(rings)-1)*n+i for i in range(n))]
    obj=mesh(name,verts,faces,mat,bone,region)
    for p in obj.data.polygons:p.use_smooth=True
    return obj

def cylinder(name,center,radius,depth,axis='Z',mat=opticmat,n=32,region='optic'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=radius,depth=depth,location=center)
    obj=bpy.context.object;obj.name=name
    obj.rotation_euler=(Vector((1,0,0)) if axis=='X' else Vector((0,1,0)) if axis=='Y' else Vector((0,0,1))).to_track_quat('Z','Y').to_euler()
    select(obj);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.data.materials.append(mat);obj['region']=region;uv_project(obj);bevel(obj,.0004,2);bind(obj)
    return obj

def tube(name,points,radius,mat,bone='Root',region='weapon',sides=8):
    pts=[Vector(p) for p in points];verts=[];faces=[]
    for j,p in enumerate(pts):
        t=(pts[min(j+1,len(pts)-1)]-pts[max(j-1,0)]).normalized()
        ref=Vector((0,0,1)) if abs(t.z)<.95 else Vector((1,0,0))
        a=t.cross(ref).normalized();b=t.cross(a).normalized()
        for k in range(sides):
            angle=2*math.pi*k/sides;verts.append(p+radius*(a*math.cos(angle)+b*math.sin(angle)))
    for j in range(len(pts)-1):
        for k in range(sides):faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
    faces += [tuple(range(sides-1,-1,-1)),tuple((len(pts)-1)*sides+k for k in range(sides))]
    obj=mesh(name,verts,faces,mat,bone,region)
    for p in obj.data.polygons:p.use_smooth=True
    return obj

# 以目前原準星軸心為基準，外形為 ACOG 風格遊戲用設計，非特定型號精密複製。
AXIS_Z=.0647295
MOUNT=(0,.028,.0305)
box('ACOG_Rail_Base',(0,.028,.0335),(.044,.115,.006))
box('ACOG_Prism_Foot',(0,.025,.043),(.036,.093,.017),radius=.003)
for y in [-.009,.068]:
    box('ACOG_Clamp',(0,y,.034),(.058,.016,.008),radius=.001)
    cylinder('ACOG_Mount_Bolt',(-.030,y,.034),.005,.006,'X',steel,n=6)
    box('ACOG_Bolt_Slot',(-.0332,y,.034),(.0005,.006,.0012),rubber,radius=.0001)
# 稜鏡鑄造機身：環形截面沿鏡軸縮放，內腔留通。
lathe('ACOG_Prism_Housing',[(-.047,.025),(-.036,.027),(-.017,.023),(.034,.024),(.069,.019),(.094,.020),(.098,.019),(.098,.0158),(.067,.0158),(-.017,.017),(-.047,.0198),(-.047,.025)])
# 下部非圓形鑄件提供 ACOG 輪廓。
for side in [-1,1]:
    profile=[(-.035,.047),(-.02,.035),(.055,.035),(.079,.051),(.064,.064),(-.018,.058)]
    n=len(profile);verts=[(side*x,y,z) for x in [.014,.024] for y,z in profile]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    obj=mesh('ACOG_Casting_'+str(side),verts,faces,opticmat,region='optic');bevel(obj,.0022,3)
lathe('ACOG_Objective_Rim',[(-.054,.025),(-.048,.026),(-.042,.026),(-.042,.022),(-.053,.020),(-.054,.025)])
lathe('ACOG_Eyepiece_Rubber',[(.083,.021),(.100,.022),(.109,.021),(.109,.0163),(.100,.0163),(.083,.017),(.083,.021)],mat=rubber)
for i in range(5):
    yy=.086+i*.004
    lathe('ACOG_Eyepiece_Rib_%02d'%i,[(yy,.0218),(yy+.0011,.0223),(yy+.002,.0218)],mat=rubber,n=40)
# 高低與左右調整旋鈕，加上可見滾花。
for axis,center in [('Z',(0,.017,.092)),('X',(-.029,.017,.066))]:
    cylinder('ACOG_Turret_'+axis,center,.010,.009,axis)
    for i in range(24):
        a=i/24*2*math.pi
        if axis=='Z':pos=(.0098*math.cos(a),.017+.0098*math.sin(a),.092)
        else:pos=(-.029,.017+.0098*math.cos(a),.066+.0098*math.sin(a))
        cylinder('ACOG_Knurl_'+axis+str(i),pos,.00065,.006,axis,n=6)
    if axis=='Z':box('ACOG_Dial_Index',(0,.017,.097),(.010,.001,.0005),steel,radius=.0001)
    else:box('ACOG_Dial_Index',(-.034,.017,.066),(.0005,.010,.001),steel,radius=.0001)
# 導光條獨立材質，留出中央調整鈕。
fiber=bpy.data.materials.new('Optic_Fiber_Red_R2');fiber.use_nodes=True
bs=fiber.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(.32,.018,.007,1)
bs.inputs['Roughness'].default_value=.2;bs.inputs['Emission Color'].default_value=(.35,.012,.002,1);bs.inputs['Emission Strength'].default_value=.22
points=[(.013,-.036+i*.112/31,.093+.003*math.sin(i/31*math.pi)) for i in range(32)]
tube('ACOG_Fiber_Channel',points,.0042,rubber,region='optic',sides=12)
tube('ACOG_Fiber_Red',[(x,y,z+.0033) for x,y,z in points],.0025,fiber,region='optic',sides=12)
for y in [-.028,.064]:box('ACOG_Fiber_Bridge',(.013,y,.092),(.014,.009,.004),radius=.0008)
glass=bpy.data.materials.new('Optic_CoatedGlass_R2');glass.use_nodes=True
bs=glass.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(.05,.16,.18,1)
bs.inputs['Roughness'].default_value=.085;bs.inputs['Metallic'].default_value=.15
bs.inputs['Alpha'].default_value=.09;bs.inputs['Transmission Weight'].default_value=0;bs.inputs['IOR'].default_value=1.46
glass.blend_method='BLEND';glass.use_screen_refraction=True;glass.show_transparent_back=False
lathe('ACOG_Objective_Glass',[(-.051,.0196),(-.0515,.0)],mat=glass,n=64)
lathe('ACOG_Ocular_Glass',[(.104,.0161),(.1045,.0)],mat=glass,n=64)

# 副枝與生長節點，讓藤蔓表面更接近自然植物。
for side in [-1,1]:
    for j in range(5):
        y=.31-j*.135;z=-.055-.020*math.sin(j*1.9)
        pts=[]
        for k in range(24):
            t=k/23;pts.append((side*(.055+.003*math.sin(t*3)),y-t*.055,z-.016*math.sin(t*math.pi*1.6)))
        tube('Fine_Root_%s_%s'%(side,j),pts,.0019,vine,sides=7)
    for y in [.30,.165,-.03,-.215]:
        # 沉頭螺絲明確區分槍械結構與自然覆蓋物。
        cylinder('Receiver_Screw', (side*.036,y,-.094),.0038,.002,'X',steel,n=12,region='weapon')

# 以最近表面權重轉移方式建立手套縫線與護腕縫線。
kd=KDTree(len(hands.data.vertices))
for v in hands.data.vertices:kd.insert(v.co,v.index)
kd.balance()
hand_bvh=BVHTree.FromPolygons([v.co.copy() for v in hands.data.vertices],[list(p.vertices) for p in hands.data.polygons])
def transfer_weights(obj):
    for g in hands.vertex_groups:obj.vertex_groups.new(name=g.name)
    for v in obj.data.vertices:
        nearest=kd.find_n(v.co,3);weights={};total=sum(1/max(d,.0001) for co,i,d in nearest)
        for co,i,d in nearest:
            fac=(1/max(d,.0001))/total
            for g in hands.data.vertices[i].groups:weights[g.group]=weights.get(g.group,0)+g.weight*fac
        for group,w in weights.items():
            if w>.0001:obj.vertex_groups[group].add([v.index],w,'REPLACE')
    mod=obj.modifiers.new('Source rig deformation','ARMATURE');mod.object=arm

for side in [-1,1]:
    # 沿既有網格每個高度搜尋手背的表面，縫線貼合皮膚，不創建新手部動畫。
    for offset in [-.013,.013]:
        pts=[]
        bone=arm.data.bones['骨骼.003.'+('l' if side>0 else 'r')]
        head=arm.matrix_world @ bone.head_local;tail=arm.matrix_world @ bone.tail_local
        for j in range(42):
            z=-.46-j*.0025;t=(z-head.z)/(tail.z-head.z)
            wanted=head.x+(tail.x-head.x)*t+offset
            hit,n,index,d=hand_bvh.ray_cast(Vector((wanted,.6,z)),Vector((0,-1,0)),1)
            if hit is not None:pts.append(hit+n*.0008)
        for j in range(0,len(pts)-2,3):
            obj=tube('Glove_Back_Stitch',pts[j:j+2],.0004,thread,None,'arms',6);transfer_weights(obj)

# 合併新增小零件，同時讓整顆鏡體與鏡片保持獨立。
original_names={'立方体.004','立方体.020','立方体.021','立方体.022','立方体.023','立方体.024','立方体.025','立方体.026','柱体.001','柱体.003'}
for region in ['optic','weapon','arms']:
    for mat in [opticmat,rubber,steel,fiber,glass,vine,thread]:
        group=[o for o in scene.objects if o.type=='MESH' and o.get('region')==region and o.name not in original_names and len(o.data.materials)==1 and o.data.materials[0]==mat]
        if not group:continue
        select(group[0])
        for o in group:o.select_set(True)
        if len(group)>1:bpy.ops.object.join()
        group[0].name=('ACOG_' if region=='optic' else 'Detail_'+region+'_')+mat.name
        group[0]['region']=region

optic_objects=[o for o in scene.objects if o.type=='MESH' and o.get('region')=='optic']
optic_collection=bpy.data.collections.new('ACOG_25_Replaceable');scene.collection.children.link(optic_collection)
for o in optic_objects:
    for c in list(o.users_collection):c.objects.unlink(o)
    optic_collection.objects.link(o)
    o['design_magnification']=2.5;o['optical_zoom_implemented']=False

# 全模型與獨立鏡體各自輸出。獨立鏡體原點為安裝座中心，無動畫骨架。
asset_objects=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/RifleVine_Arms_Draft02.fbx',use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
    mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
    path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
copies=[]
for o in optic_objects:
    c=o.copy();c.data=o.data.copy();scene.collection.objects.link(c);c.name=o.name+'_Standalone'
    c.modifiers.clear();c.vertex_groups.clear();c.parent=None;c.matrix_world=Matrix.Identity(4)
    c.data.transform(Matrix.Translation(-Vector(MOUNT)));copies.append(c)
bpy.ops.object.select_all(action='DESELECT')
for c in copies:c.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.export_scene.fbx(filepath=BASE+'/ACOG_25_Module_Draft02.fbx',use_selection=True,object_types={'MESH'},bake_anim=False,
    mesh_smooth_type='FACE',path_mode='COPY',axis_forward='-Z',axis_up='Y')
for c in copies:bpy.data.objects.remove(c,do_unlink=True)

# 光學軸與安裝座寫到側車資料；不插入來源骨架，避免影響既有動畫路徑。
manifest={'module':'ACOG-style 2.5x design draft','exact_product_replica':False,'zoom_implemented':False,
          'blender_rest_coordinates_m':{'mount_origin':MOUNT,'axis_direction':[0,-1,0],'rear_lens_center':[0,.104,AXIS_Z],
                                        'front_lens_center':[0,-.051,AXIS_Z],'suggested_review_eye':[0,.19,AXIS_Z]},
          'attachment_bone':'Root','replaceable_collection':optic_collection.name,'materials':palette,
          'reference':'Trijicon ACOG Family Specification Sheet, TA31 silhouette reference; 2.5x is the requested game design value, not a verified TA31 product specification.'}
with open(BASE+'/optic_setup.json','w',encoding='utf8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)

# 審閱攝影：比上一版降低過亮的環境反射，呈現粗糙度、鏡片及天然表面。
scene.render.engine='BLENDER_EEVEE';scene.eevee.use_gtao=True;scene.eevee.gtao_distance=.07;scene.eevee.gtao_factor=1.2
scene.eevee.use_ssr=True;scene.eevee.use_ssr_refraction=True;scene.eevee.taa_render_samples=96
scene.render.resolution_percentage=100;scene.render.image_settings.file_format='PNG';scene.view_settings.view_transform='AgX'
scene.view_settings.look='AgX - Medium High Contrast';scene.view_settings.exposure=.15
scene.world=bpy.data.worlds.new('Draft02 Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.065,.078,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.42
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Review_Camera';scene.camera=camera
for name,pos,power,size,color in [('Key',(-1.1,-.5,1.2),160,1.25,(1,.9,.77)),('Fill',(1,.4,.8),100,1.3,(.73,.85,1)),('Rim',(.2,.9,1.1),175,1.0,(1,.96,.85))]:
    bpy.ops.object.light_add(type='AREA',location=pos);light=bpy.context.object;light.name='Studio_'+name
    light.data.energy=power;light.data.size=size;light.data.color=color
    light.rotation_euler=(Vector((0,0,-.1))-light.location).to_track_quat('-Z','Y').to_euler()

def render(name,position,target,scale=1.22,res=(1800,1100),perspective=False,lens=40):
    camera.location=position;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.type='PERSP' if perspective else 'ORTHO';camera.data.ortho_scale=scale;camera.data.lens=lens;camera.data.clip_start=.001
    scene.render.resolution_x=res[0];scene.render.resolution_y=res[1]
    scene.render.filepath=BASE+'/Previews/'+name+'.png';bpy.ops.render.render(write_still=True)

arm.data.pose_position='REST'
for o in asset_objects:
    if o==hands or o.get('region')=='arms':o.hide_render=True
render('01_Rifle_Side',(-2,-.015,-.08),(0,-.045,-.085),1.2)
render('02_Rifle_ThreeQuarter',(-1.6,-.65,.46),(0,-.045,-.085),1.27)
render('03_Optic_Closeup',(-.40,-.32,.27),(0,.021,.057),.29,(1600,1200))
render('04_Optic_Rear',(-.22,.39,.23),(0,.025,.058),.31,(1600,1200))
for o in asset_objects:o.hide_render=False
arm.data.pose_position='POSE';scene.frame_set(1)
render('00_Hero',(-1.25,-1.4,.45),(-.26,-.61,-.30),1.25,(1800,1200))
render('05_FirstPerson',(-.27,.15,-.02),(-.285,-.78,-.265),1.2,(1600,1000),True,35)
arm.animation_data.action=bpy.data.actions['Opening the scope'];scene.frame_set(4)
root_transform=arm.matrix_world @ arm.pose.bones['Root'].matrix @ arm.data.bones['Root'].matrix_local.inverted() @ arm.matrix_world.inverted()
eye=root_transform @ Vector((0,.19,AXIS_Z));target=root_transform @ Vector((0,-.08,AXIS_Z))
# 對軸背景僅存在審閱場景；輸出的兩個 FBX 已在前面完成，沒有這些輔助物件。
guide_material=bpy.data.materials.new('Review_Grid_Only');guide_material.diffuse_color=(.4,.45,.42,1)
guide_dark=bpy.data.materials.new('Review_Grid_Lines');guide_dark.diffuse_color=(.06,.10,.08,1)
guide_center=bpy.data.materials.new('Review_Axis_Center');guide_center.diffuse_color=(.55,.075,.025,1)
guide_objects=[]
def guide_quad(name,x1,x2,z1,z2,y,mat):
    pts=[root_transform @ Vector((x,y,AXIS_Z+z)) for x,z in [(x1,z1),(x2,z1),(x2,z2),(x1,z2)]]
    obj=mesh(name,pts,[(0,1,2,3)],mat,None,'review_only');guide_objects.append(obj)
guide_quad('Review_Background',-.24,.24,-.24,.24,-.94,guide_material)
for i in range(-6,7):
    a=i*.04
    guide_quad('Review_Grid',a-.0009,a+.0009,-.24,.24,-.939,guide_dark)
    guide_quad('Review_Grid',-.24,.24,a-.0009,a+.0009,-.938,guide_dark)
guide_quad('Review_Axis',-.012,.012,-.002,.002,-.935,guide_center)
guide_quad('Review_Axis',-.002,.002,-.012,.012,-.934,guide_center)
render('06_ADS_Axis',eye,target,.3,(1400,1000),True,35)
for obj in guide_objects:obj.hide_render=True;obj.hide_set(True)
with open(BASE+'/optic_setup.json',encoding='utf8') as f:manifest=json.load(f)
manifest['ads_frame4_review_camera']={'position':list(eye),'target':list(target),'note':'Blender optical-axis review only. Not the current Unity camera.'}
with open(BASE+'/optic_setup.json','w',encoding='utf8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)
arm.animation_data.action=bpy.data.actions['Reload.001']
for frame in [1,20,40,63]:
    scene.frame_set(frame);render('Reload_%02d'%frame,(-1.5,-1.7,.45),(-.25,-.62,-.28),1.58,(1000,680))
arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
render('07_Arms_Closeup',(-1.0,-1.14,.10),(-.38,-.58,-.4),.79,(1600,1100))
render('00_Hero',(-1.25,-1.4,.45),(-.26,-.61,-.30),1.25,(1800,1200))
for img in bpy.data.images:
    if img.source=='FILE' and img.filepath.startswith(BASE) and os.path.isfile(img.filepath):img.pack()
scene.frame_start=1;scene.frame_end=61
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/RifleVine_Arms_Draft02.blend')
report={'source_sha256':hashlib.sha256(open(SOURCE,'rb').read()).hexdigest(),
        'previous_blend_sha256':hashlib.sha256(open(PREVIOUS,'rb').read()).hexdigest(),
        'meshes':len([o for o in asset_objects if o.type=='MESH']),
        'vertices':sum(len(o.data.vertices) for o in asset_objects if o.type=='MESH'),
        'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in asset_objects if o.type=='MESH'),
        'bones':len(arm.data.bones),'actions':{a.name:list(a.frame_range) for a in bpy.data.actions},
        'optic_meshes':[o.name for o in optic_objects],'unity_validation':'not executed','magnification_implemented':False}
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
print('DRAFT02_COMPLETE',json.dumps(report,ensure_ascii=False))
