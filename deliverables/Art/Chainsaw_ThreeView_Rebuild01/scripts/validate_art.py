import bpy,os,json,hashlib,numpy as np
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE=r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Tank/Materials/chainsaw/第一人稱__電鋸_uv.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=SOURCE)
h=bpy.data.objects['立方体.004']
src_positions=np.array([list(v.co) for v in h.data.vertices])
src_weights=[{h.vertex_groups[g.group].name:round(g.weight,7) for g in v.groups} for v in h.data.vertices]
bpy.ops.wm.open_mainfile(filepath=BASE+'/Chainsaw_ThreeView_Animated.blend')
h=bpy.data.objects['立方体.004'];positions=np.array([list(v.co) for v in h.data.vertices])
weights=[{h.vertex_groups[g.group].name:round(g.weight,7) for g in v.groups} for v in h.data.vertices]
objects=[o for o in bpy.context.scene.objects if o.type=='MESH']
errors=[];hands_equal=src_positions.shape==positions.shape and bool(np.array_equal(src_positions,positions));weights_equal=weights==src_weights
if not hands_equal or not weights_equal:errors.append('Source hand geometry or weights changed')
relief=[o for o in objects if o.get('region')=='mycelium']
if not relief:errors.append('Missing physical reference relief')
mushrooms=[o.name for o in objects if 'Fungus' in o.name or 'Crimson' in o.name]
if mushrooms:errors.append('Unexpected mushrooms carried from prior versions')
bad=[o.name for o in objects if not o.data.uv_layers]
if bad:errors.append('Missing UVs')
report=json.load(open(BASE+'/build_report.json',encoding='utf8'))
source_unchanged=hashlib.sha256(open(SOURCE,'rb').read()).hexdigest()==report['source_sha256']
if not source_unchanged:errors.append('Source hash mismatch')
result={'passed':not errors,'errors':errors,'source_unchanged':source_unchanged,'source_hand_geometry_exact':hands_equal,'source_hand_weights_exact':weights_equal,'source_hand_vertices':len(h.data.vertices),'real_relief_vertices':sum(len(o.data.vertices) for o in relief),'mushrooms':mushrooms,'meshes_with_missing_uv':bad,'fbx_sha256':hashlib.sha256(open(BASE+'/Chainsaw_ThreeView_Animated.fbx','rb').read()).hexdigest(),'unity_output_imported':False,'scope':'geometry/weights, reference relief and file integrity; does not claim Unity or collision-free animation'}
with open(BASE+'/delivery_validation.json','w',encoding='utf8') as f:json.dump(result,f,indent=2)
print(json.dumps(result));assert not errors
