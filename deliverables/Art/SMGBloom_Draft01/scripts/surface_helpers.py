def select(obj):
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj

def apply(obj,modifier):
    select(obj);bpy.ops.object.modifier_apply(modifier=modifier.name)

def bind(obj,bone='Root'):
    g=obj.vertex_groups.new(name=bone);g.add(list(range(len(obj.data.vertices))),1,'REPLACE')
    m=obj.modifiers.new('Source rig deformation','ARMATURE');m.object=arm

def uv_project(obj,scale=5):
    uv=obj.data.uv_layers.active or obj.data.uv_layers.new(name='SurfaceUV')
    uv.name='SurfaceUV'
    obj.data.update()
    for p in obj.data.polygons:
        n=p.normal;axes=(1,2) if abs(n.x)>.5 else ((0,1) if abs(n.z)>.5 else (0,2))
        for li in p.loop_indices:
            v=obj.data.vertices[obj.data.loops[li].vertex_index].co
            uv.data[li].uv=(v[axes[0]]*scale,v[axes[1]]*scale)

def mesh(name,verts,faces,mat,bone='Root',region='weapon'):
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.update()
    bm=bmesh.new();bm.from_mesh(data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
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
    elif kind=='petal':
        fan=(x-.5)/(np.sin(np.pi*y)*.36+.13)
        veins=(np.clip(np.cos(fan*38+y*2),0,1)**12)*(.25+.75*y)
        variation=.79+fbm*.30+y*.20-veins*.12
        h=fbm*.012+veins*.01
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
    mat=bpy.data.materials.new(name+'_SMG01');mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Metallic'].default_value=metal
    for label,socket in [('BaseColor','Base Color'),('Roughness','Roughness')]:
        n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=images[label];mat.node_tree.links.new(n.outputs['Color'],bs.inputs[socket])
    n=mat.node_tree.nodes.new('ShaderNodeTexImage');n.image=images['Normal']
    normalnode=mat.node_tree.nodes.new('ShaderNodeNormalMap');normalnode.inputs['Strength'].default_value=.7
    mat.node_tree.links.new(n.outputs['Color'],normalnode.inputs['Color']);mat.node_tree.links.new(normalnode.outputs[0],bs.inputs['Normal'])
    mat.diffuse_color=(*base,1);palette[name]={'material':mat.name,'metallic':metal,'roughness':rough,'resolution':size}
    return mat


