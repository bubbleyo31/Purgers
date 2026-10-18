using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>真實 UI 元件與正式 Sprite 的離屏排版檢查；比例為示例，不是遊戲實戰截圖。</summary>
public static class StatusEffectVisualVerification
{
    public static string Capture()
    {
        if (Application.isPlaying) throw new InvalidOperationException("需 Edit Mode。");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Status preview"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var camObject = new GameObject("Offscreen camera", typeof(Camera)); camObject.transform.SetParent(root.transform);
        var camera = camObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.transform.position = new Vector3(0,0,-10); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f,.06f,.065f); camera.nearClipPlane=.01f; camera.farClipPlane=100;
        var light = new GameObject("Preview light", typeof(Light)); light.transform.SetParent(root.transform); light.GetComponent<Light>().type=LightType.Directional;
        var rt = new RenderTexture(1800,800,24); rt.Create(); camera.targetTexture=rt;
        var canvasObj = new GameObject("Preview Canvas",typeof(Canvas)); canvasObj.transform.SetParent(root.transform);
        var canvas=canvasObj.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var settings=AssetDatabase.LoadAssetAtPath<StatusEffectHudSettings>(StatusEffectHudSetup.SettingsPath);
        var healthSettings=AssetDatabase.LoadAssetAtPath<EnemyHealthBarSettings>(EnemyHealthBarSetup.SettingsPath);
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
        try
        {
            Column(canvas.transform,-570,"怪物",font,settings,healthSettings,false,new[]{StatusEffectKind.Stun,StatusEffectKind.Root,StatusEffectKind.BulletTime,StatusEffectKind.Vulnerability,StatusEffectKind.Mark},new[]{.8f,.45f,.9f,.6f,.35f});
            Column(canvas.transform,0,"自己準心下方",font,settings,healthSettings,true,new[]{StatusEffectKind.Captured,StatusEffectKind.BulletTime,StatusEffectKind.GrappleBlocked,StatusEffectKind.Slow},new[]{.7f,.45f,.9f,.55f});
            Column(canvas.transform,570,"隊友頭頂",font,settings,healthSettings,false,new[]{StatusEffectKind.Captured,StatusEffectKind.BulletTime,StatusEffectKind.GrappleBlocked,StatusEffectKind.Slow},new[]{.35f,.8f,.45f,.7f});
            Canvas.ForceUpdateCanvases();camera.Render();
            var old=RenderTexture.active;var image=new Texture2D(1800,800,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1800,800),0,0);image.Apply();
                Directory.CreateDirectory("Temp/StatusEffects");File.WriteAllBytes("Temp/StatusEffects/ui-preview.png",image.EncodeToPNG());
            }
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            return "Temp/StatusEffects/ui-preview.png";
        }
        finally {UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
    }
    private static void Column(Transform parent,float x,string title,TMP_FontAsset font,StatusEffectHudSettings settings,EnemyHealthBarSettings healthSettings,bool self,StatusEffectKind[] kinds,float[] ratios)
    {
        var text=new GameObject(title,typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();text.transform.SetParent(parent,false);
        text.rectTransform.anchorMin=text.rectTransform.anchorMax=Vector2.one*.5f;text.rectTransform.sizeDelta=new Vector2(450,70);text.rectTransform.anchoredPosition=new Vector2(x,270);
        text.text=title;text.font=font;text.fontSize=42;text.alignment=TextAlignmentOptions.Center;text.color=new Color32(241,237,217,255);text.raycastTarget=false;
        var health=new GameObject("Health",typeof(RectTransform),typeof(EnemyHealthBarGraphic)).GetComponent<EnemyHealthBarGraphic>();health.transform.SetParent(parent,false);
        health.rectTransform.anchorMin=health.rectTransform.anchorMax=Vector2.one*.5f;health.rectTransform.anchoredPosition=new Vector2(x,self?-270:-110);
        float scale=self?1.5f:3.2f;
        health.rectTransform.sizeDelta=new Vector2(self?280:118,self?18:9);health.rectTransform.localScale=Vector3.one*scale;health.SetSnapshot(.7f,healthSettings);
        RectTransform anchor=health.rectTransform;
        if(self)
        {
            anchor=new GameObject("Screen center",typeof(RectTransform)).GetComponent<RectTransform>();anchor.SetParent(parent,false);
            anchor.anchorMin=anchor.anchorMax=anchor.pivot=Vector2.one*.5f;anchor.sizeDelta=Vector2.zero;
            anchor.anchoredPosition=new Vector2(x,130);anchor.localScale=Vector3.one*scale;
            for(int axis=0;axis<2;axis++)
            {
                var line=new GameObject("Crosshair",typeof(RectTransform),typeof(Image)).GetComponent<Image>();line.transform.SetParent(anchor,false);
                line.rectTransform.sizeDelta=axis==0?new Vector2(16,2):new Vector2(2,16);line.color=Color.white;line.raycastTarget=false;
            }
        }
        var view=StatusEffectListView.Create(anchor);var snapshots=new StatusEffectSnapshot[kinds.Length];
        for(int i=0;i<kinds.Length;i++)snapshots[i]=StatusEffectSnapshot.FromProgress(kinds[i],ratios[i]);
        view.Present(snapshots,snapshots.Length,settings,self?280:118,self);
    }

}
