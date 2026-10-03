# Original optic: staggered chamfered arches, split mount, offset circular controls.
MOUNT=(0,.021,.045);AXIS_Z=.084
box('Reflex_Base',(0,.02,.048),(.045,.098,.009),radius=.0015)
box('Reflex_Rear_Electronics',(0,.049,.057),(.041,.037,.012),radius=.002)
for y in [-.012,.049]:
    box('Reflex_SplitFoot',(0,y,.052),(.057,.012,.009),radius=.0015)
    cylinder('Reflex_MountBolt',(-.030,y,.052),.004,.003,'X',steel,n=8)
def arch(name,yfront,yback,outer,inner,mat):
    verts=[(x,y,z) for y in [yfront,yback] for loop in [outer,inner] for x,z in loop];n=len(outer);faces=[]
    for j in range(n):
        k=(j+1)%n;faces.extend([(j,k,n+k,n+j),(2*n+j,3*n+j,3*n+k,2*n+k),(j,2*n+j,2*n+k,k),(n+j,n+k,3*n+k,3*n+j)])
    o=mesh(name,verts,faces,mat,region='optic');bevel(o,.001,3);return o
outer=[(-.027,.061),(-.027,.094),(-.017,.108),(.017,.108),(.027,.094),(.027,.061)]
inner=[(-.021,.065),(-.021,.092),(-.014,.101),(.014,.101),(.021,.092),(.021,.065)]
arch('Reflex_ForwardShield',-.022,-.006,outer,inner,opticmat)
arch('Reflex_RearRim',.015,.022,[(x*.94,z-.002) for x,z in outer],[(x*.94,z-.002) for x,z in inner],core)
for side in [-1,1]:
    box('Reflex_ShieldInset',(side*.028,-.014,.080),(.003,.016,.03),wood,region='optic',radius=.0015)
    strand('Reflex_BarkCrest',[(side*.027,-.026,.06),(side*.028,-.02,.094),(side*.017,-.018,.112),(side*.014,-.024,.116)],.003,.0028,wood,taper=.9,region='optic')
    box('Reflex_SideBrace',(side*.025,.004,.061),(.005,.051,.007),opticmat,region='optic',radius=.001)
for y in [.032,.046]:cylinder('Reflex_ControlButton',(.025,y,.063),.004,.003,'X',rubber,n=20)
glass=bpy.data.materials.new('Reflex_Lens_Coating');glass.use_nodes=True;glass.diffuse_color=(.10,.31,.28,.15)
nodes=glass.node_tree.nodes;pr=nodes.get('Principled BSDF')
pr.inputs['Base Color'].default_value=(.07,.25,.21,1)
pr.inputs['Metallic'].default_value=.55;pr.inputs['Roughness'].default_value=.13;pr.inputs['Alpha'].default_value=.13
glass.blend_method='BLEND';glass.use_screen_refraction=True
mesh('Reflex_OpticalWindow',[(x,-.004+(z-.083)*.12,z) for x,z in inner],[tuple(range(6))],glass,region='optic')
