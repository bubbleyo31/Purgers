"""SMG art draft. Blender 4.0. Source rig remains authoritative."""
import bpy, bmesh, math, os, json, random, hashlib
import numpy as np
from mathutils import Vector, Matrix, noise
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT=os.path.abspath(os.path.join(BASE,'../../..'))
SOURCE=ROOT+'/Assets/_Project_Assets/Models/Player/Profession/Support/Materials/mp7/第一人稱_衝鋒槍_uv.fbx'
for d in ['Textures','Previews']:os.makedirs(BASE+'/'+d,exist_ok=True)
random.seed(1002)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SOURCE)
scene=bpy.context.scene
arm=next(o for o in scene.objects if o.type=='ARMATURE');arm.data.pose_position='REST'
hands=bpy.data.objects['立方体.004']
original_meshes=[o for o in scene.objects if o.type=='MESH']
for a in bpy.data.actions:a.name=a.name.split('|')[-1];a.use_fake_user=True
for o in original_meshes:o.data.materials.clear()
for m in list(bpy.data.materials):
    if m.users==0:bpy.data.materials.remove(m)
for i in list(bpy.data.images):
    if i.users==0:bpy.data.images.remove(i)
def run_helper(name):
    path=BASE+'/scripts/'+name+'.py'
    exec(compile(open(path,encoding='utf-8-sig').read(),path,'exec'),globals())
run_helper('surface_helpers')
wood=surface('Bark_Walnut',(.19,.099,.041),'bark',.84,size=2048)
vine=surface('Vine_Olive',(.076,.125,.034),'vine',.76,size=2048)
core=surface('Core_Charcoal',(.031,.036,.033),'metal',.64,.28)
steel=surface('Trim_BrushedSteel',(.22,.24,.23),'metal',.4,.85)
cloth=surface('Sleeve_Ochre',(.41,.285,.04),'cloth',.86)
glove=surface('Glove_Forest',(.04,.054,.025),'leather',.68)
bracer=surface('Bracer_Graphite',(.025,.031,.024),'metal',.54,.18)
rubber=surface('Rubber_Soot',(.016,.018,.014),'leather',.9)
opticmat=surface('Optic_Anodized',(.028,.034,.034),'metal',.45,.66)
petalmat=surface('Petal_DustyRose',(.48,.245,.225),'petal',.73)
pollen=surface('Pollen_Ochre',(.56,.39,.09),'vine',.81,size=512)
run_helper('geometry_helpers')
for o in original_meshes:
    if o==hands:continue
    if o.name in ['立方体.011','柱体']:
        bpy.data.objects.remove(o,do_unlink=True);continue
    o.data.transform(o.matrix_world);o.matrix_world=Matrix.Identity(4)
    mat=wood if o.name in ['立方体.006','立方体.007'] else core
    o.data.materials.append(mat)
    for p in o.data.polygons:p.material_index=0
    o['region']='magazine' if o.name=='立方体.008' else 'weapon'
    uv_project(o);bevel(o,.0016,3)
def catmull(points,steps=7):
    pts=[Vector(p) for p in points];out=[]
    for i in range(len(pts)-1):
        a,b,c,d=pts[max(i-1,0)],pts[i],pts[i+1],pts[min(i+2,len(pts)-1)]
        for j in range(steps):
            t=j/steps;out.append(.5*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
    return out+[pts[-1]]
def strand(name,points,width=.01,depth=.006,mat=None,bone='Root',taper=.7,region='weapon'):
    pts=catmull(points);verts=[];faces=[];n=12;last_a=None
    for i,p in enumerate(pts):
        t=(pts[min(i+1,len(pts)-1)]-pts[max(i-1,0)]).normalized()
        ref=last_a if last_a is not None else Vector((1,0,0))
        a=ref-t*ref.dot(t)
        if a.length<.01:a=Vector((0,1,0))-t*t.y
        a.normalize();last_a=a.copy();b=t.cross(a).normalized()
        s=i/(len(pts)-1);fac=(1-taper*s)*(.95+.07*math.sin(s*17))
        for k in range(n):
            th=k*2*math.pi/n;r=1+.05*noise.noise(p*170+Vector((k,0,0)))
            verts.append(p+fac*r*(a*math.cos(th)*depth+b*math.sin(th)*width))
    for i in range(len(pts)-1):
        for j in range(n):faces.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    faces += [tuple(range(n-1,-1,-1)),tuple((len(pts)-1)*n+j for j in range(n))]
    o=mesh(name,verts,faces,mat or vine,bone,region)
    uv=o.data.uv_layers.active
    lengths=[0.]
    for j in range(1,len(pts)):lengths.append(lengths[-1]+(pts[j]-pts[j-1]).length)
    for p in o.data.polygons:
        p.use_smooth=True
        for li in p.loop_indices:
            vi=o.data.loops[li].vertex_index;wrap=(n-1 in [v%n for v in p.vertices] and 0 in [v%n for v in p.vertices])
            vcoord=1.0 if wrap and vi%n==0 else (vi%n)/n
            uv.data[li].uv=(lengths[vi//n]*1.1,vcoord)
    return o
run_helper('weapon_design')
run_helper('optic_design')
trim=steel;mesh_obj=mesh;select_only=select
run_helper('arms_helpers');hands['region']='arms'
select(hands);mod=hands.modifiers.new('Garment refinement','SUBSURF');mod.levels=1
while hands.modifiers.find(mod.name)>0:bpy.ops.object.modifier_move_up(modifier=mod.name)
apply(hands,mod)
for v in hands.data.vertices:
    if v.co.z>-.25:v.co+=v.normal*(math.sin(v.co.z*130+v.co.x*30)*.0012+noise.noise(v.co*73)*.0007)
run_helper('export_asset')
run_helper('render_review')
print('SMG_BUILD_COMPLETE')
