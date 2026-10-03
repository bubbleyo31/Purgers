"""原創球塊荊棘：固定種子、單材質、三層 LOD；不讀入參考圖或修改既有資產。"""
import bpy, math, random, os, json
import numpy as np
from mathutils import Vector

ROOT = 'M:/UnityProject/Purgers'
OUT = ROOT + '/deliverables/Art/MutantThorns_V1'
ASSET = ROOT + '/Assets/_Project_Assets/Models/Map/MutantThorns_V1'
for p in [OUT+'/Previews', ASSET+'/Textures']: os.makedirs(p, exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)

# 可攜帶的 UV 色彩／粗糙度圖集：樹皮、角質尖刺、裂核，避免依賴 Blender 節點。
n=1024
y,x=np.mgrid[0:n,0:n]/n
rng=np.random.default_rng(9125)
grain=rng.random((n,n))
ridge=np.sin(x*math.tau*48+2*np.sin(y*math.tau*3)+.45*np.sin(y*math.tau*31))
fine=np.sin(x*math.tau*177+np.sin(y*math.tau*17))
shade=np.clip(.72+.20*ridge+.055*fine+.10*grain,.35,1.05)
colors=np.zeros((n,n,4),dtype=np.float32); colors[:,:,3]=1
base=np.zeros((n,n,3)); base[:int(n*.50)]=[.235,.116,.057];base[int(n*.50):int(n*.80)]=[.37,.211,.103];base[int(n*.80):]=[.16,.053,.036]
colors[:,:,:3]=base*shade[:,:,None]*.38
def save_image(name,pixels):
    im=bpy.data.images.new(name,width=n,height=n,alpha=True)
    im.pixels.foreach_set(pixels.ravel()); im.filepath_raw=ASSET+'/Textures/'+name+'.png'; im.file_format='PNG'; im.save();return im
tex=save_image('Thorn_BaseColor',colors)
rough=np.ones((n,n,4),dtype=np.float32); rough[:,:,:3]=(.77+.17*(1-shade))[:,:,None]
roughtex=save_image('Thorn_Roughness',rough)
mat=bpy.data.materials.new('Thorn_DarkWalnut');mat.use_nodes=True
nodes=mat.node_tree.nodes; bs=nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.87
bs.inputs['Specular IOR Level'].default_value=.18
im=nodes.new('ShaderNodeTexImage'); im.image=tex;mat.node_tree.links.new(im.outputs['Color'],bs.inputs['Base Color'])

class Geometry:
    def __init__(self):self.v=[];self.f=[];self.uv=[]
    def tube(self,points,radii,sides=8,band=0,offset=0):
        start=len(self.v);count=len(points)
        lo,hi=[(.015,.485),(.515,.785),(.815,.985)][band]
        prev=None
        for i,p in enumerate(points):
            tangent=(points[min(i+1,count-1)]-points[max(0,i-1)]).normalized()
            ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((1,0,0))
            a=tangent.cross(ref).normalized() if prev is None else (prev-tangent*prev.dot(tangent)).normalized()
            b=tangent.cross(a).normalized();prev=a
            for j in range(sides+1):
                theta=math.tau*j/sides
                bark=1+.07*math.sin(j*5+i*.65)
                self.v.append(tuple(p+radii[i]*bark*(a*math.cos(theta)+b*math.sin(theta))))
                self.uv.append(((j/sides+offset)%1,lo+(hi-lo)*i/(count-1)))
        for i in range(count-1):
            for j in range(sides):
                a=start+i*(sides+1)+j;b=a+sides+1
                self.f.append((a,a+1,b+1,b))
        self.f.append(tuple(start+j for j in reversed(range(sides))))
        self.f.append(tuple(start+(count-1)*(sides+1)+j for j in range(sides)))
    def blob(self,center,scale,seed):
        rr=random.Random(seed);start=len(self.v);rings=10;sides=16
        for i in range(rings+1):
            t=.005+(math.pi-.01)*i/rings
            for j in range(sides+1):
                a=math.tau*j/sides
                wob=1+.12*math.sin(a*3+t*5)+.05*math.sin(a*7-t*3)
                self.v.append(tuple(center+Vector((math.sin(t)*math.cos(a)*scale[0],math.sin(t)*math.sin(a)*scale[1],math.cos(t)*scale[2]))*wob))
                self.uv.append((j/sides,.82+.16*i/rings))
        for i in range(rings):
            for j in range(sides):
                a=start+i*(sides+1)+j;b=a+sides+1;self.f.append((a,b,b+1,a+1))
    def object(self,name):
        mesh=bpy.data.meshes.new(name);mesh.from_pydata(self.v,[],self.f);mesh.materials.append(mat);mesh.update()
        uv=mesh.uv_layers.new(name='ThornAtlasUV')
        for poly in mesh.polygons:
            poly.use_smooth=True
            for li in poly.loop_indices:uv.data[li].uv=self.uv[mesh.loops[li].vertex_index]
        obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
        return obj

names=['A_BriarHeart','B_GnarledKnot','C_SplitCrown']; all_lods=[]; stats=[]
for variant,name in enumerate(names):
    r=random.Random(91025+variant);g=Geometry()
    axes=[Vector((1,1,.95)),Vector((1.1,.87,.91)),Vector((.99,.94,1.04))][variant]
    if variant==0:g.blob(Vector((0,0,0)),(.72,.70,.68),11)
    elif variant==1:
        for c,s in [((-.32,0,.12),(.56,.60,.61)),((.35,.06,-.13),(.55,.58,.59))]:g.blob(Vector(c),s,12)
    else:
        for c in [(-.36,0,.03),(.36,0,-.07)]:g.blob(Vector(c),(.40,.66,.69),14)
    # 閉合纏繞木質藤：多方向纏成實心團塊，局部露出暗紅褐裂核。
    paths=[]
    for k in range(8 if variant!=1 else 10):
        normal=Vector((r.uniform(-1,1),r.uniform(-1,1),r.uniform(-1,1))).normalized()
        a=normal.cross(Vector((0,0,1))).normalized(); b=normal.cross(a)
        phase=r.random()*math.tau;radius=r.uniform(.67,.87);thick=r.uniform(.10,.165)
        pts=[];rads=[]
        for j in range(65):
            t=math.tau*j/64
            p=(a*math.cos(t)+b*math.sin(t))*(radius+.075*math.sin(3*t+phase))+normal*(.12*math.sin(2*t+phase))
            pts.append(Vector((p.x*axes.x,p.y*axes.y,p.z*axes.z)));rads.append(thick*(1+.22*math.sin(5*t+phase)))
        g.tube(pts,rads,8,0,r.random()); paths.append((pts,rads))
    # 從藤表面長出寬根彎鉤刺，強調輪廓而不是細針毛。
    thorn_count=0
    for k,(pts,rads) in enumerate(paths):
        for j in range(2+(k%3),63,4 if variant==0 else 5):
            p=pts[j];out=p.normalized();tangent=(pts[j+1]-pts[j-1]).normalized()
            length=r.uniform(.25,.53)*(1.15 if variant==2 else 1)
            if (j+k)%6==0:length*=1.4
            bend=tangent*r.uniform(.12,.29)
            base=p+out*rads[j]*.48
            curve=[base+out*(length*t)+bend*(t*t) for t in [0,.2,.48,.76,1]]
            width=r.uniform(.065,.105)
            g.tube(curve,[width,width*.8,width*.48,width*.19,.0015],7,1,r.random());thorn_count+=1
            if (j+k)%4==0:
                bud=base+out*.09
                g.tube([bud,bud-tangent*.13+out*.12,bud-tangent*.24+out*.18],[width*.55,width*.28,.0015],6,1)
    obj=g.object('Thorn_'+name+'_LOD0')
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    tri=obj.modifiers.new('StableTriangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
    # 重新計算外側法線，讓各封閉藤管與尖刺一致。
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    lods=[obj]
    for level,ratio in [(1,.42),(2,.15)]:
        low=obj.copy();low.data=obj.data.copy();bpy.context.collection.objects.link(low);low.name='Thorn_'+name+'_LOD'+str(level)
        bpy.context.view_layer.objects.active=low
        d=low.modifiers.new('SilhouetteReduction','DECIMATE');d.ratio=ratio;bpy.ops.object.modifier_apply(modifier=d.name)
        lods.append(low)
    info={'variant':name,'thorns':thorn_count,'lod_triangles':[]}
    for level,o in enumerate(lods):
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
        export_name=o.name; o.name='ThornSurface'
        bpy.ops.export_scene.fbx(filepath=ASSET+'/'+export_name.replace('_LOD','_Detail')+'.fbx',use_selection=True,object_types={'MESH'},add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,path_mode='STRIP')
        o.name=export_name
        info['lod_triangles'].append(sum(len(p.vertices)-2 for p in o.data.polygons))
        o.hide_render=level!=0;o.hide_set(level!=0)
    all_lods.append(lods);stats.append(info)

# 真實幾何的審閱拍攝，不使用概念圖替代模型。
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32
scene.cycles.use_denoising=True;scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.14,.14,.14)
scene.view_settings.view_transform='AgX'
def aim(obj,p):obj.rotation_euler=(Vector(p)-obj.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size,col in [('Key',(-6,-8,10),3000,8,(1,.88,.74)),('Fill',(6,-4,5),1800,8,(.86,.90,1)),('Rim',(0,6,5),2200,6,(1,.70,.45))]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=col
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=loc;aim(o,(0,0,0))
bpy.ops.object.camera_add(location=(3,-5,2.5));cam=bpy.context.object;cam.name='ReviewCamera';aim(cam,(0,0,0));cam.data.type='ORTHO';cam.data.ortho_scale=3.75;scene.camera=cam
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
for i,lods in enumerate(all_lods):
    for j,other in enumerate(all_lods):other[0].hide_render=i!=j
    scene.render.filepath=OUT+'/Previews/'+names[i]+'.png';bpy.ops.render.render(write_still=True)
for i,lods in enumerate(all_lods):
    for o in lods:o.location.x=(i-1)*3.4
    lods[0].hide_render=False
cam.location=(2,-10,5);aim(cam,(0,0,0));cam.data.ortho_scale=10.8
scene.render.resolution_x=1800;scene.render.resolution_y=800;scene.render.filepath=OUT+'/Previews/Variants.png';bpy.ops.render.render(write_still=True)
for image in bpy.data.images:
    if image.source=='FILE':image.pack()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/MutantThorns_V1.blend')
with open(OUT+'/mesh_report.json','w',encoding='utf8') as f:json.dump(stats,f,indent=2)
print('THORNS_COMPLETE',stats)
