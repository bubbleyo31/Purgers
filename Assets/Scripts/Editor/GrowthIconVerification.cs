#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using Purgers.Progression;

public static class GrowthIconVerification
{
    public static string Render()
    {
        const int width=1000, height=1440;
        GameObject root=null, cameraObject=null;
        RenderTexture rt=null;
        Texture2D image=null;
        var old=RenderTexture.active;
        try
        {
            root=new GameObject("GrowthIconReview",typeof(RectTransform),typeof(Canvas));
            root.layer=31;
            var canvas=root.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.WorldSpace;
            var canvasRect=(RectTransform)root.transform;
            canvasRect.sizeDelta=new Vector2(width,height);
            cameraObject=new GameObject("GrowthIconReviewCamera",typeof(Camera));
            cameraObject.layer=31;
            var camera=cameraObject.GetComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10);
            camera.orthographic=true; camera.orthographicSize=height*.5f;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color32(24,29,35,255);
            camera.cullingMask=1<<31;
            camera.nearClipPlane=.1f;camera.farClipPlane=20;
            canvas.worldCamera=camera;
            rt=new RenderTexture(width,height,24);
            camera.targetTexture=rt;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GrowthIconSetup.StagePath);
            var source=prefab.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow/LeftCard");
            var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
            int count=0;
            for(int kind=1;kind<=9;kind++)
            {
                var reward=AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                    "Assets/_Project_Assets/Data/Progression/Growth/Reward_"+(PlayerGrowthKind)kind+".asset");
                for(int rank=1;rank<=PlayerGrowthRules.MaximumRank((PlayerGrowthKind)kind);rank++)
                {
                    var card=UnityEngine.Object.Instantiate(source.gameObject,root.transform,false);
                    card.SetActive(true);
                    var cardRect=(RectTransform)card.transform;
                    cardRect.anchorMin=cardRect.anchorMax=Vector2.one*.5f;
                    cardRect.pivot=Vector2.one*.5f;
                    cardRect.localScale=Vector3.one;
                    cardRect.localRotation=Quaternion.identity;
                    cardRect.anchoredPosition=new Vector2(-400+(rank-1)*200,630-(kind-1)*140);
                    foreach(Transform child in card.transform)
                        child.gameObject.SetActive(child.name=="Icon"||child.name=="IconPlate");
                    var plate=(RectTransform)card.transform.Find("IconPlate");
                    plate.anchorMin=plate.anchorMax=Vector2.one*.5f;plate.pivot=Vector2.one*.5f;
                    plate.anchoredPosition=Vector2.zero;plate.localScale=Vector3.one;plate.localRotation=Quaternion.identity;
                    plate.sizeDelta=new Vector2(86,100);
                    var icon=card.transform.Find("Icon").GetComponent<Image>();
                    icon.rectTransform.localScale=Vector3.one;
                    icon.rectTransform.localRotation=Quaternion.identity;
                    icon.rectTransform.anchorMin=icon.rectTransform.anchorMax=Vector2.one*.5f;
                    icon.rectTransform.pivot=Vector2.one*.5f;
                    icon.rectTransform.anchoredPosition=Vector2.zero;
                    icon.rectTransform.sizeDelta=new Vector2(86,100);
                    icon.sprite=reward.HudIcon;icon.enabled=true;
                    var view=icon.GetComponent<GrowthRewardRankView>();
                    view.Present(reward,rank);
                    var label=icon.transform.Find("GrowthRank").GetComponent<TMP_Text>();
                    label.ForceMeshUpdate(true);
                    if(label.text!=GrowthRewardRankView.RankLabel((PlayerGrowthKind)kind,rank)||label.textInfo.characterCount!=label.text.Length)
                        throw new InvalidOperationException("Rank render mismatch: "+kind+"/"+rank);
                    var title=new GameObject("ReviewTitle",typeof(RectTransform),typeof(TextMeshProUGUI));
                    title.transform.SetParent(card.transform,false);
                    var t=title.GetComponent<TextMeshProUGUI>();t.font=font;t.fontSize=12;t.alignment=TextAlignmentOptions.Center;
                    t.text=reward.DisplayName+" "+label.text;t.raycastTarget=false;
                    t.rectTransform.sizeDelta=new Vector2(195,30);t.rectTransform.anchoredPosition=new Vector2(0,-65);
                    foreach(var child in card.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                    count++;
                }
            }
            for(int k=10;k<=15;k++)
            {
                var reward=AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                    "Assets/_Project_Assets/Data/Progression/Growth/Reward_"+(PlayerGrowthKind)k+".asset");
                var go=new GameObject("Passive",typeof(RectTransform),typeof(Image));
                go.transform.SetParent(root.transform,false);go.layer=31;
                var im=go.GetComponent<Image>();im.sprite=reward.HudIcon;im.preserveAspect=true;
                im.rectTransform.sizeDelta=new Vector2(110,110);im.rectTransform.anchoredPosition=new Vector2(-415+(k-10)*166,-650);
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active=rt;
            image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            const string path="Documentation/ProjectArchitecture/Validation/GrowthIcons/AllRanks.png";
            File.WriteAllBytes(path,image.EncodeToPNG());
            return count+" attribute rank renders; "+path;
        }
        finally
        {
            RenderTexture.active=old;
            if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            if(rt!=null)UnityEngine.Object.DestroyImmediate(rt);
            if(image!=null)UnityEngine.Object.DestroyImmediate(image);
        }
    }

    public static string RenderGrapple()
    {
        var root=new GameObject("GrappleReview",typeof(RectTransform),typeof(Canvas));
        root.layer=31;
        var cameraObject=new GameObject("GrappleReviewCamera",typeof(Camera));
        var rt=new RenderTexture(660,220,24);
        Texture2D image=null;
        var old=RenderTexture.active;
        try
        {
            var canvas=root.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta=new Vector2(660,220);
            var camera=cameraObject.GetComponent<Camera>();
            camera.transform.position=new Vector3(0,0,-10);
            camera.orthographic=true;camera.orthographicSize=110;camera.aspect=3;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color32(24,29,35,255);
            camera.cullingMask=1<<31;camera.nearClipPlane=.1f;camera.farClipPlane=20;
            canvas.worldCamera=camera;camera.targetTexture=rt;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GrowthIconSetup.StagePath);
            var source=prefab.transform.Find("ReferenceFrame/BattleAbilityHUD");
            for(int i=0;i<3;i++)
            {
                var panel=UnityEngine.Object.Instantiate(source.gameObject,root.transform,false);
                panel.SetActive(true);
                panel.GetComponent<LocalPlayerBattleAbilityHUD>().enabled=false;
                foreach(var group in panel.GetComponentsInChildren<CanvasGroup>(true)) group.alpha=1;
                foreach(var graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.enabled=true;
                foreach(Transform child in panel.transform)
                    child.gameObject.SetActive(child.name=="GrappleBackground"||child.name=="GrappleFill"||
                        child.name=="GrappleBodyIcon"||child.name=="GrappleKey");
                var fill=panel.transform.Find("GrappleFill").GetComponent<Image>();
                fill.fillAmount=i*.5f;
                var rect=(RectTransform)panel.transform;
                rect.anchorMin=rect.anchorMax=Vector2.one*.5f;
                rect.pivot=Vector2.one*.5f;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
                rect.localPosition=Vector3.zero;
                Canvas.ForceUpdateCanvases();
                Vector3 center=rect.InverseTransformPoint(fill.rectTransform.TransformPoint(fill.rectTransform.rect.center));
                rect.localPosition=new Vector3(-220+i*220,20,0)-center;
                foreach(var t in panel.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=31;
            }
            Canvas.ForceUpdateCanvases();camera.Render();
            RenderTexture.active=rt;image=new Texture2D(660,220,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,660,220),0,0);image.Apply();
            const string path="Documentation/ProjectArchitecture/Validation/GrowthIcons/GrappleEnergy.png";
            File.WriteAllBytes(path,image.EncodeToPNG());
            return path;
        }
        finally
        {
            RenderTexture.active=old;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(rt);
            if(image!=null) UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
#endif
