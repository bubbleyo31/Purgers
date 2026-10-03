from pathlib import Path
base=Path(r'M:/UnityProject/Purgers/deliverables/Art/Chainsaw_ThreeView_Rebuild01/scripts')
p=base/'build_chainsaw.py';s=p.read_text(encoding='utf-8-sig')
s=s.replace("hands['region']='arms'","hands['region']='arms';run_helper('restore_source_material')")
s=s.replace("housing=mesh('ThreeView_WedgeHousing',verts,faces,case,region='case');bevel(housing,.0045,2)","housing=mesh('ThreeView_WedgeHousing',verts,faces,case,region='case');bevel(housing,.0045,2)\nrun_helper('carve_grip')")
s=s.replace("run_helper('build_reference_ridges')","""# Original wrap-handle's non-contact lower end is now tied back into the narrower new housing.
box('FrontHandle_LowerMount',(.126,.175,-.172),(.103,.060,.054),core,region='handles',radius=.004)
box('FrontHandle_UpperMount',(-.108,.175,.042),(.054,.067,.035),core,region='handles',radius=.003)
run_helper('build_reference_ridges')""")
p.write_text(s,encoding='utf8')
p=base/'render_review.py';s=p.read_text(encoding='utf-8-sig').replace("scene.render.resolution_x=res[0]", "if name=='07_Top':camera.rotation_euler.z+=math.pi/2\n scene.render.resolution_x=res[0]");p.write_text(s,encoding='utf8')
# Explicit post-smoothing projection keeps irregular casing ridge bands flush with their actual base.
p=base/'build_reference_ridges.py';s=p.read_text(encoding='utf-8-sig').replace("uv_project(o);bind(o);o['reference_derived_geometry']=True","""if plane!='blade':
  for v in o.data.vertices:
   if plane=='case':hit,n,_,_=case_tree.ray_cast(Vector((sign*.8,v.co.y,v.co.z)),Vector((-sign,0,0)),2)
   else:hit,n,_,_=case_tree.ray_cast(Vector((v.co.x,v.co.y,.7)),Vector((0,0,-1)),2)
   if hit is not None:
    axis=0 if plane=='case' else 2;sgn=sign if plane=='case' else 1
    delta=(v.co[axis]-hit[axis])*sgn
    if delta>.004:v.co[axis]=hit[axis]+sgn*.0015
 uv_project(o);bind(o);o['reference_derived_geometry']=True""")
p.write_text(s,encoding='utf8')
