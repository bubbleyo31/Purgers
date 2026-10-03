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


