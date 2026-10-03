import bpy,json
bpy.ops.wm.open_mainfile(filepath=r'M:/UnityProject/Purgers/deliverables/Art/Chainsaw_ThreeView_Rebuild01/Chainsaw_ThreeView_Animated.blend')
h=bpy.data.objects['立方体.004']
print('HAND_MATERIALS',[(m.name,[(n.image.name,n.image.filepath) for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]) for m in h.data.materials if m])
