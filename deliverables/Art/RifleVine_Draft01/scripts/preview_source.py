import bpy, os, json
from mathutils import Vector
BASE = r'M:/UnityProject/Purgers/deliverables/Art/RifleVine_Draft01'
bpy.ops.wm.open_mainfile(filepath=BASE + '/source_inspection.blend')
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == 'ARMATURE')
arm.data.pose_position = 'REST'
bpy.data.objects['立方体.004'].hide_render = True
for m in bpy.data.materials:
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    for l in list(m.node_tree.links):
        m.node_tree.links.remove(l)
    m.node_tree.links.new(b.outputs['BSDF'], m.node_tree.nodes.get('Material Output').inputs['Surface'])
    b.inputs['Base Color'].default_value=(.32,.36,.37,1)
    b.inputs['Roughness'].default_value=.6
bpy.ops.object.camera_add(location=(-1.8,-.35,.2))
cam=bpy.context.object
cam.rotation_euler=(Vector((0,-.04,-.12))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO'; cam.data.ortho_scale=1.22; scene.camera=cam
for pos,power,size in [((-1,-.5,2),160,2),((1,.2,.5),100,1),((0,1,1),110,1)]:
    bpy.ops.object.light_add(type='AREA',location=pos)
    light=bpy.context.object; light.data.energy=power; light.data.shape='DISK';light.data.size=size
    light.rotation_euler=(Vector((0,0,-.1))-light.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('Studio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.07,.08,.09,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1200;scene.render.resolution_y=650;scene.render.resolution_percentage=100
scene.view_settings.view_transform='Standard';scene.render.filepath=BASE+'/source_weapon.png'
bpy.ops.render.render(write_still=True)
arm.data.pose_position='POSE';arm.animation_data.action=bpy.data.actions.get('第一人稱手|idle');scene.frame_set(1)
bpy.data.objects['立方体.004'].hide_render=False
dg=bpy.context.evaluated_depsgraph_get()
for obj in [arm,bpy.data.objects['立方体.004'],bpy.data.objects['立方体.020']]:
    ev=obj.evaluated_get(dg)
    if obj.type=='MESH':
        pts=[ev.matrix_world @ v.co for v in ev.data.vertices]
        print('POSEBOUND',obj.name,[min(v[i] for v in pts) for i in range(3)],[max(v[i] for v in pts) for i in range(3)])
for name in ['Root','骨骼.002.l','骨骼.003.l','骨骼.002.r','骨骼.003.r']:
    bone=arm.pose.bones[name]
    print('POSEBONE',name,list(arm.matrix_world @ bone.head),list(arm.matrix_world @ bone.tail))
