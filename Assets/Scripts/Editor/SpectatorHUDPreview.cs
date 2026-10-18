#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>獨立 Preview Scene 離屏檢查，不聚焦 Editor、不保存 Scene。</summary>
public static class SpectatorHUDPreview
{
    public static string Capture()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Spectator preview"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var rt=new RenderTexture(800,240,24); rt.Create();
        Texture2D texture=null;
        try
        {
            var cameraRoot=new GameObject("Preview camera",typeof(Camera)); cameraRoot.transform.SetParent(root.transform,false);
            var camera=cameraRoot.GetComponent<Camera>(); camera.enabled=false;camera.scene=scene;
            camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=120;camera.aspect=800f/240;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.055f,.045f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var canvasRoot=new GameObject("Preview canvas",typeof(RectTransform),typeof(Canvas));canvasRoot.transform.SetParent(root.transform,false);
            var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            ((RectTransform)canvas.transform).sizeDelta=new Vector2(800,240);
            var hud=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpectatorHUDSetup.PrefabPath),canvas.transform);
            hud.GetComponent<LocalSpectatorHUD>().enabled=false;
            var content=hud.transform.Find("Content"); content.gameObject.SetActive(true);content.Find("Waiting").gameObject.SetActive(false);
            var players=content.Find("Players");var template=players.Find("CardTemplate");
            for(int i=0;i<3;i++)
            {
                var card=Object.Instantiate(template.gameObject,players);card.SetActive(true);
                ((RectTransform)card.transform).anchoredPosition=new Vector2((i-1)*144,0);
                card.transform.Find("Name").GetComponent<TMP_Text>().text=new[]{"Host 名稱","Teammate ID","玩家名稱預覽"}[i];
                card.transform.Find("Name").GetComponent<TMP_Text>().color=TeammateDisplayRules.IdentityColor(i);
                card.transform.Find("State").GetComponent<TMP_Text>().text=i==1?"倒地":"";
                card.transform.Find("Border").GetComponent<Image>().color=i==1?new Color32(213,196,94,255):new Color32(94,100,87,220);
            }
            ((RectTransform)content.Find("Previous")).anchoredPosition=new Vector2(-248,0);
            ((RectTransform)content.Find("Next")).anchoredPosition=new Vector2(248,0);
            Canvas.ForceUpdateCanvases(); foreach(var text in hud.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;
            try{RenderTexture.active=rt;texture=new Texture2D(800,240,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,800,240),0,0);texture.Apply();}
            finally{RenderTexture.active=previous;}
            string path="Temp/SpectatorSwitchVerification/hud-preview.png";Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,texture.EncodeToPNG());return path;
        }
        finally {if(texture!=null)Object.DestroyImmediate(texture);Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);rt.Release();Object.DestroyImmediate(rt);}
    }
}
#endif
