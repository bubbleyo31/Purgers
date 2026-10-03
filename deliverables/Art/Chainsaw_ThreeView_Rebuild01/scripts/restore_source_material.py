import shutil
# Restore the project's existing source arm atlas, whose FBX link points to a directory.
source_dir=os.path.dirname(SOURCE)
mat=hands.data.materials[0];mat.use_nodes=True;nodes=mat.node_tree.nodes
for node in list(nodes):nodes.remove(node)
bs=nodes.new('ShaderNodeBsdfPrincipled');bs.inputs['Roughness'].default_value=.82
out=nodes.new('ShaderNodeOutputMaterial');mat.node_tree.links.new(bs.outputs[0],out.inputs['Surface'])
for label,suffix in [('BaseColor','AlbedoTransparency'),('Normal','Normal'),('MetallicSmoothness','MetallicSmoothness')]:
 src=source_dir+'/電鋸_uv_UV_'+suffix+'.png';dst=BASE+'/Textures/SourceArms_'+label+'.png';shutil.copy2(src,dst)
 if label=='MetallicSmoothness':continue
 img=bpy.data.images.load(dst,check_existing=True)
 if label=='Normal':img.colorspace_settings.name='Non-Color'
 tex=nodes.new('ShaderNodeTexImage');tex.image=img
 if label=='BaseColor':mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
 else:
  nm=nodes.new('ShaderNodeNormalMap');mat.node_tree.links.new(tex.outputs['Color'],nm.inputs['Color']);mat.node_tree.links.new(nm.outputs[0],bs.inputs['Normal'])
# Discard unused invalid source image references so the deliverable contains no directory-as-texture link.
for img in list(bpy.data.images):
 if img.users==0:bpy.data.images.remove(img)
