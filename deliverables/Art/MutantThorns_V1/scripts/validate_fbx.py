"""逐一重匯入 FBX，驗證單網格、UV、有限座標與 LOD 三角形預算。"""
import bpy, os, json, math
root='M:/UnityProject/Purgers'
folder=root+'/Assets/_Project_Assets/Models/Map/MutantThorns_V1'
result=[]
for filename in sorted(os.listdir(folder)):
    if not filename.endswith('.fbx'):continue
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=folder+'/'+filename)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert len(meshes)==1,(filename,len(meshes))
    o=meshes[0];m=o.data
    assert m.uv_layers.active is not None,filename
    assert all(math.isfinite(v) for vert in m.vertices for v in vert.co),filename
    tris=sum(len(p.vertices)-2 for p in m.polygons)
    assert tris>0 and tris<22000,(filename,tris)
    bounds=list(o.dimensions)
    assert min(bounds)>1 and max(bounds)<4,(filename,bounds)
    assert max(bounds)/min(bounds)<1.5,(filename,bounds)
    result.append(dict(file=filename,meshes=1,triangles=tris,dimensions_m=bounds,uv=True))
assert len(result)==9
with open(root+'/deliverables/Art/MutantThorns_V1/fbx_validation.json','w') as f:json.dump(result,f,indent=2)
print('FBX_VALIDATION_PASSED',len(result))
