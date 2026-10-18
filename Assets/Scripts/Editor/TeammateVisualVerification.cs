#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>以複製的 URP Renderer 在 Preview Scene 驗證輪廓；finally 還原品質管線，不保存資產。</summary>
public static class TeammateVisualVerification
{
    public static string Status{get;private set;}="尚未執行";
    public static async void Capture()
    {
        if(Application.isPlaying || Status=="執行中")throw new InvalidOperationException("需在 Edit Mode 執行且沒有進行中的擷取。");
        Status="執行中";
        var original=QualitySettings.renderPipeline;
        var pipeline=UnityEngine.Object.Instantiate((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline);
        var field=typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",BindingFlags.NonPublic|BindingFlags.Instance);
        var data=UnityEngine.Object.Instantiate(((ScriptableRendererData[])field.GetValue(pipeline))[0]);
        data.rendererFeatures.Clear();
        var feature=ScriptableObject.CreateInstance<TeammateOutlineFeature>();data.rendererFeatures.Add(feature);feature.Create();
        field.SetValue(pipeline,new[]{data});
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Teammate GPU preview");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var cameraRoot=new GameObject("Preview world camera",typeof(Camera));cameraRoot.transform.SetParent(root.transform);
        var camera=cameraRoot.GetComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.transform.position=new Vector3(0,0,-8);camera.transform.LookAt(Vector3.zero);
        camera.fieldOfView=45;camera.aspect=2;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.06f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(.16f,.19f,.21f));
        var a=GameObject.CreatePrimitive(PrimitiveType.Sphere);a.transform.SetParent(root.transform);a.transform.position=new Vector3(-1.5f,0,0);a.transform.localScale=Vector3.one*2;a.GetComponent<Renderer>().sharedMaterial=material;
        var b=GameObject.CreatePrimitive(PrimitiveType.Sphere);b.transform.SetParent(root.transform);b.transform.position=new Vector3(1.5f,0,0);b.transform.localScale=Vector3.one*2;b.GetComponent<Renderer>().sharedMaterial=material;
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(root.transform);wall.transform.position=new Vector3(1.5f,0,-1.3f);wall.transform.localScale=new Vector3(2.5f,3,.2f);wall.GetComponent<Renderer>().sharedMaterial=material;
        var canvasRoot=new GameObject("Preview UI",typeof(Canvas));canvasRoot.transform.SetParent(root.transform);
        var hudRoot=new GameObject("Preview HUD",typeof(RectTransform),typeof(LocalTeammateHUD));hudRoot.transform.SetParent(canvasRoot.transform);
        var hud=hudRoot.GetComponent<LocalTeammateHUD>();
        var settings=ScriptableObject.CreateInstance<TeammateDisplaySettings>();settings.outlinePixels=5;
        hud.Configure(settings,null,camera);
        typeof(LocalTeammateHUD).GetMethod("OnEnable",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(hud,null);
        var list=(IList)typeof(LocalTeammateHUD).GetField("outlineTargets",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(hud);
        var anchorA=a.AddComponent<TeammateVisualAnchor>();anchorA.bodyRenderers=new[]{a.GetComponent<Renderer>()};list.Add(new LocalTeammateHUD.OutlineTarget(anchorA,1));
        var anchorB=b.AddComponent<TeammateVisualAnchor>();anchorB.bodyRenderers=new[]{b.GetComponent<Renderer>()};list.Add(new LocalTeammateHUD.OutlineTarget(anchorB,2));
        var rt=new RenderTexture(1024,512,24);rt.Create();
        try
        {
            QualitySettings.renderPipeline=pipeline;
            await Task.Delay(500);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;
            var tex=new Texture2D(1024,512,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1024,512),0,0);tex.Apply();RenderTexture.active=previous;
            Directory.CreateDirectory("Temp/Teammates");File.WriteAllBytes("Temp/Teammates/outline-urp.png",tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            Status="通過";
        }
        catch(Exception ex){Status="失敗";Debug.LogException(ex);}
        finally
        {
            QualitySettings.renderPipeline=original;
            UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
            rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(pipeline);UnityEngine.Object.DestroyImmediate(data);UnityEngine.Object.DestroyImmediate(feature);
        }
    }
}
#endif
