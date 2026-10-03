# Distinct branching roots expose the original receiver between junctions.
root_paths=[[(.341,-.157),(.32,-.093),(.26,-.069),(.18,-.083),(.10,-.074),(.025,-.089),(-.085,-.073),(-.18,-.081),(-.288,-.073),(-.33,-.055)],
[(.33,-.047),(.288,.006),(.213,.018),(.149,.01),(.09,.029),(.036,.019)],
[(.286,-.093),(.236,-.038),(.178,-.008),(.15,.018)],
[(.205,-.078),(.147,-.045),(.081,-.053),(.035,-.041),(-.022,-.06),(-.082,-.047)],
[(-.27,-.085),(-.225,-.042),(-.191,-.042),(-.155,-.012)],
[(.109,-.203),(.092,-.161),(.066,-.131),(.037,-.10),(.024,-.077)],
[(.064,-.085),(.117,-.116),(.16,-.104),(.182,-.126)],
[(-.104,-.08),(-.16,-.099),(-.228,-.087),(-.293,-.095)]]
for side in [-1,1]:
    for j,path in enumerate(root_paths):
        pts=[(side*(.036+.003*math.sin(k*2.3+j)),y,z) for k,(y,z) in enumerate(path)]
        pts.insert(0,(side*.016,path[0][0]+.004,path[0][1]))
        strand('Bark_Root_%s_%s'%(side,j),pts,.013 if j<2 else .009,.008,wood,taper=.82)
        if j<5:
            y,z=path[1];strand('Bark_Offshoot',[(side*.042,y,z),(side*.045,y-.022,z+.023),(side*.041,y-.033,z+.04)],.005,.004,wood,taper=.98)
    trails=[[(.322,-.05),(.269,-.021),(.213,-.046),(.136,-.049),(.087,-.02),(.025,-.042),(-.047,-.061),(-.135,-.055),(-.218,-.042),(-.301,-.056),(-.349,-.035)],
            [(.251,.025),(.269,.001),(.226,-.039),(.218,-.086),(.168,-.101),(.119,-.083),(.09,-.043),(.061,-.014),(.038,-.023),(.046,-.044),(.061,-.038)],
            [(.168,.036),(.13,.011),(.078,.008),(.046,.031),(.006,.037),(-.008,.004),(-.005,-.019),(-.026,-.03),(-.037,-.012),(-.025,-.005)],
            [(-.25,.03),(-.241,.003),(-.211,-.029),(-.157,-.05),(-.117,-.079),(-.151,-.101),(-.194,-.105),(-.206,-.135),(-.19,-.148),(-.187,-.137)],
            [(.067,-.135),(.099,-.147),(.099,-.178),(.077,-.20),(.057,-.212),(.039,-.201)]]
    for j,path in enumerate(trails):
        pts=[(side*(.052+.0025*math.sin(k*1.9+j)),y,z) for k,(y,z) in enumerate(path)]
        y0,z0=path[0]
        if z0>0:pts=[(0,y0+.018,.047),(side*.029,y0+.012,.053)]+pts
        else:pts=[(side*.022,y0+.005,z0)]+pts
        strand('Vine_Sweep_%s_%s'%(side,j),pts,.010 if j<3 else .007,.0048,vine,taper=.6 if j==0 else .91)
    for y,z in [(.291,-.063),(.169,-.062),(-.065,-.035),(-.254,-.055)]:
        cylinder('Receiver_Fastener',(side*.036,y,z),.003,.002,'X',steel,n=12,region='weapon')
        box('Fastener_Recess',(side*.0372,y,z),(.0004,.0035,.0007),rubber,region='weapon',radius=.0001)
    for t in [.22,.5,.78]:
        pts=[(side*.0289-.00036,lo+(hi-lo)*t,z) for lo,hi,z in [(.038,.113,-.267),(.03025,.10653,-.30462),(.01682,.09393,-.36217),(.013,.086,-.38)]]
        strand('Magazine_Rib',pts,.0013,.0008,core,'magazine',taper=.1,region='magazine')
floor=box('Magazine_Floorplate',(0,.04016,-.40332),(.062,.082,.008),rubber,'magazine','magazine',.001)
c=Vector((0,.04016,-.40332));floor.data.transform(Matrix.Translation(c)@Matrix.Rotation(-.228,4,'X')@Matrix.Translation(-c))
lathe('Muzzle_Sleeve',[(-.283,.014),(-.353,.015),(-.375,.014),(-.375,.008),(-.343,.008)],z=-.046,mat=steel,region='weapon',n=32)
cylinder('Muzzle_Interior',(0,-.341,-.046),.008,.001,'Y',rubber,region='weapon')
for y in [-.354,-.361,-.368]:lathe('Muzzle_GripRing',[(y,.0149),(y-.0012,.0152),(y-.0025,.0145)],z=-.046,mat=core,region='weapon',n=32)
for i in range(21):box('Rail_Tooth',(0,-.235+i*.022,.043),(.041,.011,.004),core,region='weapon',radius=.0007)

def flower(name,side,y,z,radius,angle=0):
    center=Vector((side*.064,y,z))
    for j in range(5):
        a=angle+j*2*math.pi/5;d=Vector((0,math.cos(a),math.sin(a)));cross=Vector((0,-math.sin(a),math.cos(a)))
        verts=[];faces=[];rows=14;cols=10
        for row in range(rows+1):
            t=row/rows;length=radius*(.94+random.random()*.015);w=math.sin(math.pi*t)*t**.4*radius*.42+.0002
            for col in range(cols+1):
                u=col/cols*2-1;p=center+d*(.002+t*length)+cross*(u*w)
                p.x+=side*(.001+radius*(.18*math.sin(t*math.pi)-.08*t+.11*u*u*t)+.00045*math.sin(t*35+u*5));verts.append(p)
        for row in range(rows):
            for col in range(cols):
                n=row*(cols+1)+col;faces.append((n,n+1,n+cols+2,n+cols+1))
        o=mesh(name+'_Petal',verts,faces,petalmat)
        uv=o.data.uv_layers.active
        for p in o.data.polygons:
            p.use_smooth=True
            for li in p.loop_indices:
                vi=o.data.loops[li].vertex_index;uv.data[li].uv=(vi%(cols+1)/cols,vi//(cols+1)/rows)
        mod=o.modifiers.new('Petal thickness','SOLIDIFY');mod.thickness=.00045
        select(o);bpy.ops.object.modifier_move_up(modifier=mod.name);apply(o,mod)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=1,location=center+Vector((side*.005,0,0)))
    o=bpy.context.object;o.name=name+'_Pollen';o.scale=(.0035,radius*.16,radius*.16)
    select(o);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o.data.materials.append(pollen);o['region']='weapon';uv_project(o);bind(o)
    for p in o.data.polygons:p.use_smooth=True
    for k in range(12):
        a=k*2*math.pi/12;p=center+Vector((side*.007,math.cos(a)*radius*.16,math.sin(a)*radius*.16))
        tube(name+'_Stamen',[p,p+Vector((side*.0025,0,0))],.0007,pollen,sides=6)
for side in [-1,1]:
    flower('Rear_Bloom',side,.212,-.027,.044,.35)
    flower('Fore_Bloom',side,-.166,-.052,.029,.8)
    flower('Grip_Bloom',side,.085,-.19,.023,1.4)
