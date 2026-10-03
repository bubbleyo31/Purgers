import bpy,os
from mathutils import Vector
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.open_mainfile(filepath=BASE+'/Chainsaw_ThreeView_Animated.blend')
scene=bpy.context.scene;arm=next(o for o in scene.objects if o.type=='ARMATURE')
arm.animation_data.action=bpy.data.actions['attack1'];arm.data.pose_position='POSE'
pts=[]
for f in range(1,27):
 scene.frame_set(f);dg=bpy.context.evaluated_depsgraph_get()
 for o in scene.objects:
  if o.type=='MESH' and not o.hide_render:
   e=o.evaluated_get(dg);pts.extend(e.matrix_world@Vector(p) for p in e.bound_box)
lo=Vector([min(p[i] for p in pts) for i in range(3)]);hi=Vector([max(p[i] for p in pts) for i in range(3)]);c=(lo+hi)/2
camera=scene.camera;camera.location=c+Vector((-1.1,-1.25,.4));camera.rotation_euler=(c-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=max(hi-lo)*1.5
scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
scene.render.fps=24;scene.frame_start=1;scene.frame_end=26
scene.render.image_settings.file_format='FFMPEG';scene.render.ffmpeg.format='MPEG4';scene.render.ffmpeg.codec='H264';scene.render.ffmpeg.constant_rate_factor='MEDIUM';scene.render.filepath=BASE+'/Previews/Attack1_Animation.mp4'
bpy.ops.render.render(animation=True)





