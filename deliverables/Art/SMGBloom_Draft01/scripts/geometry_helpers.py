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


