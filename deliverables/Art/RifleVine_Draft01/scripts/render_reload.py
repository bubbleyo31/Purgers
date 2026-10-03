import bpy
from mathutils import Vector
BASE=r'M:/UnityProject/Purgers/deliverables/Art/RifleVine_Draft01'
bpy.ops.wm.open_mainfile(filepath=BASE+'/RifleVine_Arms_Draft01.blend')
scene=bpy.context.scene
arm=next(o for o in scene.objects if o.type=='ARMATURE')
arm.animation_data.action=bpy.data.actions['Reload.001']
scene.frame_start=1;scene.frame_end=63;scene.render.fps=24
scene.camera.location=(-1.5,-1.7,.45)
scene.camera.rotation_euler=(Vector((-.25,-.62,-.28))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
scene.camera.data.type='ORTHO';scene.camera.data.ortho_scale=1.58
scene.eevee.taa_render_samples=24
scene.render.resolution_x=1000;scene.render.resolution_y=680;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='FFMPEG'
scene.render.ffmpeg.format='MPEG4';scene.render.ffmpeg.codec='H264';scene.render.ffmpeg.constant_rate_factor='HIGH'
scene.render.filepath=BASE+'/Previews/06_Reload.mp4'
bpy.ops.render.render(animation=True)
