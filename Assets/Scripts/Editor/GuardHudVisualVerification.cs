using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正式元件／圖示與施法條 Prefab 的離屏示例，不開視窗、不移動 Scene 物件。</summary>
public static class GuardHudVisualVerification
{
    public static string Capture(bool broken=false, bool full=false)
    {
        if(Application.isPlaying)throw new InvalidOperationException("需 Edit Mode。");
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Guard preview");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var previous=RenderTexture.active;
        RenderTexture target=null;Texture2D image=null;
        try
        {
            var cameraObject=new GameObject("Offscreen camera",typeof(Camera));cameraObject.transform.SetParent(root.transform);
            var camera=cameraObject.GetComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.04f,.06f,.065f);camera.nearClipPlane=.01f;camera.farClipPlane=100;
            target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var canvasObject=new GameObject("Canvas",typeof(Canvas));canvasObject.transform.SetParent(root.transform);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var anchor=(RectTransform)canvas.transform;
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
            var fontSource=new GameObject("Preview label",typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();fontSource.transform.SetParent(anchor,false);
            fontSource.font=font;fontSource.fontSize=16;fontSource.text="格檔／四項異常／技能施法條　正式尺寸示例";
            fontSource.rectTransform.sizeDelta=new Vector2(620,24);fontSource.rectTransform.anchoredPosition=new Vector2(0,110);
            fontSource.alignment=TextAlignmentOptions.Center;
            for(int axis=0;axis<2;axis++)
            {
                var line=new GameObject("Crosshair",typeof(Image)).GetComponent<Image>();line.transform.SetParent(anchor,false);
                line.rectTransform.sizeDelta=axis==0?new Vector2(16,2):new Vector2(2,16);line.raycastTarget=false;
            }
            var settings=AssetDatabase.LoadAssetAtPath<StatusEffectHudSettings>(StatusEffectHudSetup.SettingsPath);
            var view=LocalPlayerGuardHUD.Create(anchor);
            float leading=view.Present(new GuardHudSnapshot(true,!broken&&!full,broken,broken?0:full?400:70,400,0,broken?3.2f:0),settings,settings.selfWidth);
            var kinds=new[]{StatusEffectKind.Captured,StatusEffectKind.BulletTime,StatusEffectKind.GrappleBlocked,StatusEffectKind.Slow};
            var snapshots=new StatusEffectSnapshot[kinds.Length];
            for(int i=0;i<kinds.Length;i++)snapshots[i]=StatusEffectSnapshot.FromProgress(kinds[i],.8f-i*.15f);
            var statuses=StatusEffectListView.Create(anchor);statuses.Present(snapshots,snapshots.Length,settings,settings.selfWidth,true,leading);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
            var cast=UnityEngine.Object.Instantiate(prefab.transform.Find("ReferenceFrame/BattleAbilityHUD/TimedAbilityBar").gameObject,anchor);
            cast.SetActive(true);
            foreach(var text in cast.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text=text.name.Contains("Name")?"震盪波":text.name.Contains("Seconds")?"1.4s":"施法中";
            }
            Canvas.ForceUpdateCanvases();
            float castTop=float.NegativeInfinity;var corners=new Vector3[4];
            foreach(var graphic in cast.GetComponentsInChildren<Graphic>(true))
            {
                graphic.rectTransform.GetWorldCorners(corners);
                foreach(var corner in corners)castTop=Mathf.Max(castTop,anchor.InverseTransformPoint(corner).y);
            }
            float offset=LocalPlayerGuardHUD.ResolveCrosshairOffset(settings,leading,statuses.VisibleCount,castTop);
            view.SetCrosshairOffset(offset);statuses.SetSelfCrosshairOffset(offset,leading);
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();camera.Render();
            RenderTexture.active=target;image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();
            Directory.CreateDirectory("Temp/GuardGrowthVerification");
            string path="Temp/GuardGrowthVerification/hud-"+(broken?"broken":full?"hidden":"guard")+".png";
            File.WriteAllBytes(path,image.EncodeToPNG());return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active=previous;
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
    }
}
