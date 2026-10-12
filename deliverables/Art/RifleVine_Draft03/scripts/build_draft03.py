"""已確認 Concept02：局部替換，四角面優先；保留手臂、骨架、權重與六段動作。"""
import bpy, bmesh, math, os, json, shutil, hashlib, sys
from mathutils import Vector, Matrix
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PREV=os.path.join(os.path.dirname(BASE),'RifleVine_Draft02')
SOURCE=PREV+'/RifleVine_Arms_Draft02.blend'
for d in ['Textures','Previews']:os.makedirs(BASE+'/'+d,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=SOURCE)
scene=bpy.context.scene
arm=next(o for o in scene.objects if o.type=='ARMATURE')
hands=bpy.data.objects['立方体.004']
arm.data.pose_position='REST'
for o in list(scene.objects):
    if o.type in {'CAMERA','LIGHT'} or o.get('region')=='review_only' or o.get('region')=='optic':
        bpy.data.objects.remove(o,do_unlink=True)
for a in bpy.data.actions:a.use_fake_user=True
for o in scene.objects:o.hide_render=False;o.hide_set(False)
baseline={'triangles':67756,'optic_triangles':16040}
# 外部貼圖副本；不覆寫舊版或變更原材質色彩。
for filename in os.listdir(PREV+'/Textures'):
    shutil.copy2(PREV+'/Textures/'+filename,BASE+'/Textures/'+filename)
for image in bpy.data.images:
    if image.source=='FILE':
        filename=os.path.basename(bpy.path.abspath(image.filepath))
        if os.path.isfile(BASE+'/Textures/'+filename):
            image.filepath=BASE+'/Textures/'+filename
wood=bpy.data.materials['Bark_Walnut_R2']
vine=bpy.data.materials['Vine_Olive_R2']
core=bpy.data.materials['Core_Charcoal_R2']
steel=bpy.data.materials['Trim_BrushedSteel_R2']
rubber=bpy.data.materials['Rubber_Soot_R2']
optic=bpy.data.materials['Optic_Anodized_R2']

def select(o):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o

def uv_project(o,scale=5):
    uv=o.data.uv_layers.active or o.data.uv_layers.new(name='SurfaceUV')
    o.data.update()
    for p in o.data.polygons:
        n=p.normal;axes=(1,2) if abs(n.x)>.5 else ((0,1) if abs(n.z)>.5 else (0,2))
        for li in p.loop_indices:
            v=o.data.vertices[o.data.loops[li].vertex_index].co
            uv.data[li].uv=(v[axes[0]]*scale,v[axes[1]]*scale)

def mesh(name,verts,faces,mat,bone='Root',region='weapon_new',smooth=True):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);data.materials.append(mat)
    o['region']=region;o['concept02_new']=True
    for p in data.polygons:p.use_smooth=smooth
    data.use_auto_smooth=True;data.auto_smooth_angle=math.radians(35)
    uv_project(o)
    if bone:
        g=o.vertex_groups.new(name=bone);g.add(list(range(len(data.vertices))),1,'REPLACE')
        m=o.modifiers.new('Original rig','ARMATURE');m.object=arm
    return o

def quad_cap(verts,faces,ring,reverse=False):
    # 平面端蓋以中心扇形四角面分割；偶數邊界不生成 N-gon 或三角扇。
    center=sum((Vector(verts[i]) for i in ring),Vector())/len(ring)
    idx=len(verts);verts.append(tuple(center))
    for j in range(0,len(ring),2):
        f=(idx,ring[j],ring[(j+1)%len(ring)],ring[(j+2)%len(ring)])
        faces.append(tuple(reversed(f)) if reverse else f)

def loft(name,rings,mat,bone='Root',region='weapon_new',cap=True,smooth=True):
    n=len(rings[0]);verts=[tuple(p) for r in rings for p in r];faces=[]
    for k in range(len(rings)-1):
        for j in range(n):faces.append((k*n+j,k*n+(j+1)%n,(k+1)*n+(j+1)%n,(k+1)*n+j))
    if cap:
        quad_cap(verts,faces,list(range(n)),True)
        quad_cap(verts,faces,list(range((len(rings)-1)*n,len(rings)*n)))
    return mesh(name,verts,faces,mat,bone,region,smooth)

def rounded_rect(a,b,r,steps=3):
    r=min(r,a*.85,b*.85)
    pts=[]
    for cx,cy,start in [(a-r,b-r,0),(-a+r,b-r,90),(-a+r,-b+r,180),(a-r,-b+r,270)]:
        for j in range(steps):
            ang=math.radians(start+j*90/(steps-1))
            pts.append((cx+r*math.cos(ang),cy+r*math.sin(ang)))
    return pts

def rounded_box(name,center,size,mat,bone='Root',region='weapon_new',radius=.002):
    cx,cy,cz=center;sx,sy,sz=size
    r=min(radius,sx*.35,sy*.35,sz*.35)
    rings=[]
    for z,inset in [(-sz/2,r),(-sz/2+r,0),(sz/2-r,0),(sz/2,r)]:
        rings.append([(cx+x,cy+y,cz+z) for x,y in rounded_rect(sx/2-inset,sy/2-inset,min(r,sx/2-inset,sy/2-inset),3)])
    return loft(name,rings,mat,bone,region)

def lathe(name,profile,mat=optic,axis='Y',center=(0,0,0),n=32,cap=False,region='optic'):
    cx,cy,cz=center;rings=[]
    for pos,r in profile:
        if axis=='Y':rings.append([(cx+math.cos(i*2*math.pi/n)*r,cy+pos,cz+math.sin(i*2*math.pi/n)*r) for i in range(n)])
        elif axis=='Z':rings.append([(cx+math.cos(i*2*math.pi/n)*r,cy+math.sin(i*2*math.pi/n)*r,cz+pos) for i in range(n)])
        else:rings.append([(cx+pos,cy+math.cos(i*2*math.pi/n)*r,cz+math.sin(i*2*math.pi/n)*r) for i in range(n)])
    return loft(name,rings,mat,'Root',region,cap)

def remove_parts(o,predicate):
    bm=bmesh.new();bm.from_mesh(o.data);remain=set(bm.verts);deleted=[]
    while remain:
        stack=[remain.pop()];part=[]
        while stack:
            v=stack.pop();part.append(v)
            for e in v.link_edges:
                n=e.other_vert(v)
                if n in remain:remain.remove(n);stack.append(n)
        if predicate(part):deleted.extend(part)
    bmesh.ops.delete(bm,geom=deleted,context='VERTS');bm.to_mesh(o.data);bm.free()

# 移除舊後托、彈匣及其金屬裝飾；其餘實體保持原稿。
for name in ['立方体.022','立方体.023','Detail_weapon_Core_Charcoal']:
    o=bpy.data.objects.get(name)
    if o:bpy.data.objects.remove(o,do_unlink=True)
remove_parts(bpy.data.objects['Detail_weapon_Trim_BrushedSteel_R2'],
             lambda part:max(v.co.z for v in part)<-.17)
# 木殼後段改平順；前護木的全部頂點保持原狀。
bark=bpy.data.objects['Detail_weapon_Bark_Walnut']
remove_parts(bark,lambda part:min(v.co.y for v in part)>.24)
bm=bmesh.new();bm.from_mesh(bark.data)
bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,
    plane_co=(0,.22,0),plane_no=(0,1,0),clear_outer=True,clear_inner=False)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(bark.data);bm.free()
for v in bark.data.vertices:
    if v.co.y>.16:
        t=min(1,max(0,(v.co.y-.16)/.12))
        if v.co.z>-.01:v.co.z=(1-t)*v.co.z+t*(.018+.007*math.sin((v.co.y-.16)*8))
        if v.co.y>.395:v.co.x*=max(.3,1-(v.co.y-.395)*15)
uv_project(bark)
# 原框架尾端位於圓木托內，不改变原握把／扳機区域。
stock_sections=[
(.16,.043,-.145,.018),(.20,.045,-.146,.024),(.25,.046,-.147,.027),
(.30,.044,-.149,.026),(.35,.045,-.150,.029),(.385,.046,-.158,.038),
(.414,.047,-.183,.043),(.439,.046,-.199,.041),(.459,.041,-.202,.034),
(.473,.030,-.192,.022),(.48,.012,-.172,.003)]
rings=[]
for y,w,lo,hi in stock_sections:
    rings.append([(x,y,(hi+lo)/2+z) for x,z in rounded_rect(w,(hi-lo)/2,min(.022,w*.75),4)])
stock=loft('Walnut_Rounded_Stock',rings,wood)
# 深色薄肩墊沿圓滑末端包邊，保留四角面環線。
rings=[]
for y,w,lo,hi in [(.451,.042,-.2,.033),(.458,.043,-.202,.034),(.467,.038,-.200,.031),(.480,.027,-.186,.015),(.485,.008,-.162,-.010)]:
    rings.append([(x,y,(hi+lo)/2+z) for x,z in rounded_rect(w,(hi-lo)/2,min(.022,w*.75),4)])
loft('Rounded_Recoil_Pad',rings,rubber)

# 短聚合物彈匣，沿原 magazine 骨骼剛性蒙皮。
poly=core.copy();poly.name='Magazine_Polymer_R3'
poly.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value=0
# 共用既有皮革細節貼圖以避免額外高解析資產；色彩使用 charcoal，粗糙度使用 rubber。
for node in poly.node_tree.nodes:
    if node.type=='TEX_IMAGE' and node.image and 'Roughness' in node.image.name:
        node.image=next(n.image for n in rubber.node_tree.nodes if n.type=='TEX_IMAGE' and n.image and 'Roughness' in n.image.name)
    if node.type=='TEX_IMAGE' and node.image and 'Normal' in node.image.name:
        node.image=next(n.image for n in rubber.node_tree.nodes if n.type=='TEX_IMAGE' and n.image and 'Normal' in n.image.name)
rings=[]
for z,w,l,y in [(-.148,.030,.063,.294),(-.158,.032,.064,.294),(-.18,.033,.064,.292),(-.22,.033,.062,.287),(-.26,.032,.060,.280),(-.298,.031,.057,.274),(-.306,.028,.054,.274)]:
    rings.append([(x,y+yy,z) for x,yy in rounded_rect(w,l,.009,4)])
loft('Magazine_Polymer_Body',rings,poly,'magazine')
rounded_box('Magazine_Rounded_Floorplate',(0,.274,-.305),(.070,.122,.012),poly,'magazine',radius=.004)
# 凹面板外圈和內面；薄環四角面直接嵌入側殼，不堆疊高面倒角。
for side in [-1,1]:
    for row,(z,y) in enumerate([(-.186,.292),(-.232,.285),(-.275,.277)]):
        for col in [-1,1]:
            yy=y+col*.028;ww=.023;hh=.017
            rings=[]
            for xx,shrink in [(side*.0338,0),(side*.034,.001),(side*.0331,.003)]:
                rings.append([(xx,yy+a,z+b) for a,b in rounded_rect(ww-shrink,hh-shrink,.004,3)])
            loft('Magazine_Recess_%s_%s_%s'%(side,row,col),rings,rubber,'magazine',cap=True)
    # 背脊橫向紋路是低面數槽條，較細顆粒來自貼圖。
    for i in range(10):
        z=-.181-i*.011;y=.353-(.181+z)*-.15
        rounded_box('Magazine_Spine_Rib',(side*.018,y,z),(.034,.0025,.003),poly,'magazine',radius=.0007)

# 原創短筒鏡體，保留原 ADS 軸高，所有外殼環帶為四角面。
AXIS=.0647295
MOUNT=(0,.027,.0305)
rounded_box('Prism_Low_Saddle',(0,.027,.034),(.047,.092,.008),optic,region='optic',radius=.002)
for y in [-.006,.060]:
    rounded_box('Prism_Clamp',(0,y,.0318),(.057,.012,.007),optic,region='optic',radius=.0015)
    lathe('Prism_MountBolt',[(-.031,.0045),(-.027,.0045)],steel,'X',(0,y,.033),n=8,cap=True)
# 閉合厚殼沿軸線留出貫通內腔。
lathe('Prism_Enclosed_Housing',[
(-.025,.026),(-.018,.028),(-.008,.0265),(.022,.026),(.048,.0245),(.066,.0225),(.075,.022),
(.075,.0175),(.061,.0175),(.021,.020),(-.008,.0215),(-.025,.022),(-.025,.026)],center=(0,0,AXIS),n=32)
# 外罩斜口：每圈的前端 Y 隨 Z 變化，使上緣延長；同圈連接保持四角面。
rings=[]
for y,r in [(-.030,.027),(-.023,.028),(-.014,.0275),(-.014,.024),(-.029,.023),(-.030,.027)]:
    rings.append([(math.cos(i*2*math.pi/32)*r,y-math.sin(i*2*math.pi/32)*.005,AXIS+math.sin(i*2*math.pi/32)*r) for i in range(32)])
loft('Prism_Swept_Hood',rings,optic,region='optic',cap=False)
lathe('Prism_Front_Edge',[(-.031,.0269),(-.0302,.0272),(-.0294,.0268)],steel,center=(0,0,AXIS),n=32)
lathe('Prism_Rear_Collar',[(.065,.022),(.073,.024),(.081,.024),(.084,.022),(.084,.0177),(.080,.017),(.065,.0175),(.065,.022)],rubber,center=(0,0,AXIS),n=32)
# 一個頂旋鈕，滾花由低面輪廓交替半徑，非數十個獨立圓柱。
lathe('Prism_Top_Dial_Base',[(.086,.009),(.09,.011),(.092,.011)],optic,'Z',(0,.025,0),n=24,cap=True)
rings=[]
for z,rad in [(.090,.012),(.092,.013),(.099,.013),(.100,.0115)]:
    rings.append([(math.cos(i*2*math.pi/32)*(rad+(.0005 if i%2==0 else 0)),.025+math.sin(i*2*math.pi/32)*(rad+(.0005 if i%2==0 else 0)),z) for i in range(32)])
loft('Prism_Single_Dial',rings,optic,region='optic')
rounded_box('Prism_Dial_Index',(0,.025,.1004),(.009,.0013,.0005),steel,region='optic',radius=.0001)
# 側控制鍵與木質低側飾，配合確認圖而非複製商品外殼。
for side in [-1,1]:
    rings=[]
    for x,scale in [(side*.024,.9),(side*.027,1),(side*.028,.84)]:
        rings.append([(x,.033+y,AXIS+.001+z) for y,z in rounded_rect(.012*scale,.007*scale,.005*scale,4)])
    loft('Prism_Oval_Control',rings,rubber,region='optic')
    profile=[(-.013,.054),(-.005,.060),(.019,.061),(.041,.054),(.05,.045),(.045,.040),(.014,.041),(-.008,.043)]
    rings=[]
    for x,scale in [(side*.024,.94),(side*.027,1),(side*.028,.9)]:
        rings.append([(x,.018+(y-.018)*scale,.050+(z-.050)*scale) for y,z in profile])
    loft('Prism_Walnut_Inlay',rings,wood,region='optic')
# 透明青綠鍍膜；前後分離，給 Unity 手動指定專用透明材質。
glass=bpy.data.materials.new('Prism_Teal_CoatedGlass_R3');glass.use_nodes=True
bs=glass.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value=(.055,.255,.215,1)
bs.inputs['Metallic'].default_value=.55;bs.inputs['Roughness'].default_value=.13
bs.inputs['Alpha'].default_value=.13;bs.inputs['IOR'].default_value=1.46
glass.diffuse_color=(.10,.31,.28,.13);glass.blend_method='BLEND'
glass.use_screen_refraction=True;glass.show_transparent_back=False
for name,y,r in [('Prism_Objective_Glass',-.021,.022),('Prism_Ocular_Glass',.081,.0173)]:
    verts=[(math.cos(i*2*math.pi/32)*r,y,AXIS+math.sin(i*2*math.pi/32)*r) for i in range(32)]
    faces=[];quad_cap(verts,faces,list(range(32)))
    mesh(name,verts,faces,glass,region='optic',smooth=False)

# 按材質／變形骨分組，只合併本輪建立的小件，保持透明鏡片及模組邊界。
for region in ['weapon_new','optic']:
    mats={o.data.materials[0] for o in scene.objects if o.type=='MESH' and o.get('concept02_new') and o.get('region')==region}
    for mat in mats:
        for bone in ['Root','magazine']:
            group=[o for o in scene.objects if o.type=='MESH' and o.get('concept02_new') and o.get('region')==region and o.data.materials[0]==mat and o.vertex_groups.get(bone)]
            if not group:continue
            select(group[0])
            for o in group:o.select_set(True)
            if len(group)>1:bpy.ops.object.join()
            group[0].name=('Prism_' if region=='optic' else ('Magazine_' if bone=='magazine' else 'Body_'))+mat.name
            group[0]['region']=region;group[0]['concept02_new']=True
optic_objects=[o for o in scene.objects if o.type=='MESH' and o.get('region')=='optic']
collection=bpy.data.collections.new('Prism_Concept02_Replaceable');scene.collection.children.link(collection)
for o in optic_objects:
    for c in list(o.users_collection):c.objects.unlink(o)
    collection.objects.link(o)
    o['optical_zoom_implemented']=False
asset_objects=[o for o in scene.objects if o.type in {'MESH','ARMATURE'}]
def stats(objects):
    meshes=[o for o in objects if o.type=='MESH'];faces=[p for o in meshes for p in o.data.polygons]
    return {'meshes':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),
            'quads':sum(len(p.vertices)==4 for p in faces),'triangles_faces':sum(len(p.vertices)==3 for p in faces),
            'ngons':sum(len(p.vertices)>4 for p in faces),'triangulated_triangles':sum(len(p.vertices)-2 for p in faces),
            'materials':len({m.name for o in meshes for m in o.data.materials if m})}
report={'baseline':baseline,'full':stats(asset_objects),'new_parts':stats([o for o in asset_objects if o.get('concept02_new')]),
        'optic':stats(optic_objects),'bones':len(arm.data.bones),'actions':{a.name:list(a.frame_range) for a in bpy.data.actions},
        'source_blend_sha256':hashlib.sha256(open(SOURCE,'rb').read()).hexdigest(),
        'approved_concept_sha256':hashlib.sha256(open(BASE+'/References/Approved_Concept02.png','rb').read()).hexdigest(),
        'hands_unchanged':True,'unity_import_verified':False}
assert report['full']['triangulated_triangles']<baseline['triangles'],report
assert report['new_parts']['triangles_faces']==0 and report['new_parts']['ngons']==0,report
with open(BASE+'/build_report.json','w',encoding='utf8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
# 輸出只有美術物件，不含審閱攝影機或背景。
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions['idle'];scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in asset_objects:o.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=BASE+'/RifleVine_Arms_Draft03.fbx',use_selection=True,object_types={'ARMATURE','MESH'},
add_leaf_bones=False,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
bake_anim_simplify_factor=0,path_mode='COPY',embed_textures=False,axis_forward='-Z',axis_up='Y')
copies=[]
for o in optic_objects:
    c=o.copy();c.data=o.data.copy();scene.collection.objects.link(c);c.name=o.name+'_Standalone'
    c.modifiers.clear();c.vertex_groups.clear();c.parent=None;c.matrix_world=Matrix.Identity(4)
    c.data.transform(Matrix.Translation(-Vector(MOUNT)));copies.append(c)
bpy.ops.object.select_all(action='DESELECT')
for o in copies:o.select_set(True)
bpy.context.view_layer.objects.active=copies[0]
bpy.ops.export_scene.fbx(filepath=BASE+'/Prism_Concept02_Module.fbx',use_selection=True,object_types={'MESH'},
bake_anim=False,mesh_smooth_type='FACE',path_mode='COPY',axis_forward='-Z',axis_up='Y')
for o in copies:bpy.data.objects.remove(o,do_unlink=True)
manifest={'replaceable_collection':collection.name,'attachment_bone':'Root','zoom_implemented':False,
'blender_rest_coordinates_m':{'mount_origin':MOUNT,'axis_direction':[0,-1,0],'rear_lens_center':[0,.081,AXIS],
'front_lens_center':[0,-.021,AXIS],'review_eye':[0,.19,AXIS]},
'lens_material':{'name':glass.name,'base_color_linear':[.055,.255,.215],'alpha':.13,'metallic':.55,'roughness':.13},
'note':'Original fictional housing; approved Concept02. Coordinates are Blender rest space, not Unity Transform values.'}
with open(BASE+'/optic_setup.json','w',encoding='utf8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)

# 保存乾淨的建模狀態，審閱視圖由另一支脚本建立，可反覆檢查而不重建資產。
for image in bpy.data.images:
    if image.source=='FILE' and image.filepath.startswith(BASE) and os.path.isfile(image.filepath):image.pack()
scene.frame_start=1;scene.frame_end=61
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=BASE+'/RifleVine_Arms_Draft03.blend')
print('BUILD_COMPLETE',json.dumps(report,ensure_ascii=False))

