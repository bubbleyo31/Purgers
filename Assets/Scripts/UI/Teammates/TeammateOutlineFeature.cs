using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>URP 世界相機的隊友輪廓；只畫 HUD 提供的同 Runner 第三人稱 Renderer。</summary>
public sealed class TeammateOutlineFeature : ScriptableRendererFeature
{
    [SerializeField,Tooltip("指定 Purgers/TeammateOutline Shader，避免 Build 裁除。只加入世界相機使用的 Forward Renderer。")] private Shader outlineShader;
    private OutlinePass pass;
    public override void Create(){pass?.Dispose();pass=new OutlinePass(outlineShader);}
    public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
    {
        if(pass==null || data.cameraData.cameraType!=CameraType.Game || data.cameraData.renderType!=CameraRenderType.Base)return;
        if(LocalTeammateHUD.ForCamera(data.cameraData.camera)!=null)renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing){pass?.Dispose();pass=null;}
    private sealed class OutlinePass : ScriptableRenderPass
    {
        private readonly Material[] materials=new Material[TeammateDisplayRules.Capacity];
        public OutlinePass(Shader shader)
        {
            renderPassEvent=RenderPassEvent.AfterRenderingTransparents;
            if(shader==null)shader=Shader.Find("Purgers/TeammateOutline");
            if(shader!=null)for(int i=0;i<materials.Length;i++)materials[i]=CoreUtils.CreateEngineMaterial(shader);
        }
        public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data)
        {ConfigureTarget(data.cameraData.renderer.cameraColorTargetHandle,data.cameraData.renderer.cameraDepthTargetHandle);}
        public override void Execute(ScriptableRenderContext context,ref RenderingData data)
        {
            var hud=LocalTeammateHUD.ForCamera(data.cameraData.camera);
            if(hud==null || hud.Settings==null || materials[0]==null)return;
            var settings=hud.Settings;
            var cmd=CommandBufferPool.Get("Purgers.TeammateOutline");
            // Reserve/clear only stencil bit 128; leave other stencil consumers intact.
            cmd.DrawProcedural(Matrix4x4.identity,materials[0],0,MeshTopology.Triangles,3);
            foreach(var target in hud.OutlineTargets)Draw(cmd,target.Anchor,materials[0],1);
            foreach(var target in hud.OutlineTargets)
            {
                var material=materials[Mathf.Clamp(target.Slot,0,materials.Length-1)];
                Color color=TeammateDisplayRules.IdentityColor(target.Slot)*Mathf.Max(0,settings.outlineBrightness);color.a=1;
                material.SetColor("_OutlineColor",color);material.SetFloat("_OutlinePixels",settings.outlinePixels);
                material.SetFloat("_VisibleAlpha",settings.visibleOutline);material.SetFloat("_HiddenAlpha",settings.occludedOutline);
                Draw(cmd,target.Anchor,material,2);Draw(cmd,target.Anchor,material,3);
            }
            cmd.DrawProcedural(Matrix4x4.identity,materials[0],0,MeshTopology.Triangles,3);
            context.ExecuteCommandBuffer(cmd);CommandBufferPool.Release(cmd);
        }
        private static void Draw(CommandBuffer cmd,TeammateVisualAnchor anchor,Material material,int passIndex)
        {
            if(anchor==null || anchor.bodyRenderers==null)return;
            foreach(var renderer in anchor.bodyRenderers)
            {
                if(renderer==null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
                Mesh mesh=null;
                if(renderer is SkinnedMeshRenderer skinned)mesh=skinned.sharedMesh;
                else if(renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                if(mesh==null)continue;
                for(int sub=0;sub<mesh.subMeshCount;sub++)cmd.DrawRenderer(renderer,material,sub,passIndex);
            }
        }
        public void Dispose(){foreach(var material in materials)CoreUtils.Destroy(material);}
    }
}
