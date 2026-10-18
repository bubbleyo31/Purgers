#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>明確建立獨立觀戰 HUD 並接入既有 ReferenceFrame；不重排其他 HUD。</summary>
public static class SpectatorHUDSetup
{
    public const string PrefabPath = "Assets/Prefabs/UI/SpectatorHUD.prefab";
    public const string PortraitPath = "Assets/_Project_Assets/UI/BattleHUD/SpectatorPortrait.asset";
    public const string ProviderPath = "Assets/_Project_Assets/UI/BattleHUD/SharedPlayerPortrait.asset";
    public static void CreateAssets()
    {
        Sprite portrait = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPath);
        if (portrait == null)
        {
            // 共用占位圖：簡單人像剪影，未改動角色美術；可在 Provider Inspector 換成核准圖。
            var texture = new Texture2D(128,128,TextureFormat.RGBA32,false) { name="Shared portrait", filterMode=FilterMode.Bilinear };
            var pixels = new Color32[128*128];
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float dx=x-63.5f;
                bool head=dx*dx+(y-83f)*(y-83f)<22f*22f;
                bool shoulders=dx*dx/(45f*45f)+(y-21f)*(y-21f)/(37f*37f)<1f;
                pixels[y*128+x] = head||shoulders ? new Color32(224,220,200,255) : new Color32(36,44,36,255);
            }
            texture.SetPixels32(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture,PortraitPath);
            portrait=Sprite.Create(texture,new Rect(0,0,128,128),Vector2.one*.5f,100); portrait.name="Shared player portrait";
            AssetDatabase.AddObjectToAsset(portrait,texture);
        }
        var provider=AssetDatabase.LoadAssetAtPath<SharedPlayerPortraitProvider>(ProviderPath);
        if(provider==null)
        {
            provider=ScriptableObject.CreateInstance<SharedPlayerPortraitProvider>();
            var data=new SerializedObject(provider); data.FindProperty("portrait").objectReferenceValue=portrait; data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(provider,ProviderPath);
        }
        if(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)!=null) { AssetDatabase.SaveAssets(); return; }
        var stage=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
        if (font == null) font=stage.GetComponentInChildren<TMP_Text>(true).font;
        var root=new GameObject("SpectatorHUD",typeof(RectTransform),typeof(LocalSpectatorHUD));
        try
        {
            var rect=(RectTransform)root.transform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,0); rect.pivot=Vector2.one*.5f;
            rect.sizeDelta=new Vector2(1640,150); rect.anchoredPosition=new Vector2(0,100);
            var content=Rect(root.transform,"Content",Vector2.zero,new Vector2(1640,150));
            var cards=Rect(content,"Players",Vector2.zero,new Vector2(1500,140));
            var template=Rect(cards,"CardTemplate",Vector2.zero,new Vector2(108,140));
            ImageAt(template,"Border",Vector2.zero,new Vector2(76,76),new Color32(213,196,94,255));
            var icon=ImageAt(template,"Portrait",Vector2.zero,new Vector2(68,68),Color.white); icon.sprite=portrait; icon.preserveAspect=true;
            var name=TextAt(template,"Name",new Vector2(0,-54),new Vector2(136,28),font,18); name.text="玩家名稱"; name.overflowMode=TextOverflowModes.Ellipsis;
            var status=TextAt(template,"State",new Vector2(0,53),new Vector2(106,24),font,16); status.text="觀戰中";
            var prev=TextAt(content,"Previous",new Vector2(-92,0),new Vector2(52,36),font,22); prev.text="‹ Q";
            var next=TextAt(content,"Next",new Vector2(92,0),new Vector2(52,36),font,22); next.text="E ›";
            var waiting=TextAt(content,"Waiting",Vector2.zero,new Vector2(480,40),font,22); waiting.text="等待可觀戰玩家";
            var so=new SerializedObject(root.GetComponent<LocalSpectatorHUD>());
            so.FindProperty("content").objectReferenceValue=content.gameObject;
            so.FindProperty("cardsRoot").objectReferenceValue=cards;
            so.FindProperty("cardTemplate").objectReferenceValue=template;
            so.FindProperty("previousKey").objectReferenceValue=prev.rectTransform;
            so.FindProperty("nextKey").objectReferenceValue=next.rectTransform;
            so.FindProperty("waitingText").objectReferenceValue=waiting;
            so.FindProperty("portraitProvider").objectReferenceValue=provider;
            so.FindProperty("fallbackPortrait").objectReferenceValue=portrait;
            so.ApplyModifiedPropertiesWithoutUndo(); template.gameObject.SetActive(false); content.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath); AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static string WireCurrentMenu()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("需在 Edit Mode 接線");
        var frame=GameObject.Find("GameplayHUD Canvas/PlayerHUDCanvas/ReferenceFrame");
        if(frame==null || frame.scene.path!="Assets/Scenes/_Menu.unity") throw new InvalidOperationException("正式 _Menu ReferenceFrame 未開啟");
        if(frame.scene.isDirty) throw new InvalidOperationException("場景有未儲存工作，不自動保存");
        if(frame.GetComponentInChildren<LocalSpectatorHUD>(true)!=null) return "Already wired";
        CreateAssets();
        PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),frame.transform);
        EditorSceneManager.MarkSceneDirty(frame.scene); EditorSceneManager.SaveScene(frame.scene);
        return "SpectatorHUD connected to existing ReferenceFrame";
    }
    static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
    }
    static Image ImageAt(Transform parent,string name,Vector2 pos,Vector2 size,Color color)
    {var image=Rect(parent,name,pos,size).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;}
    static TMP_Text TextAt(Transform parent,string name,Vector2 pos,Vector2 size,TMP_FontAsset font,float fontSize)
    {var text=Rect(parent,name,pos,size).gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=fontSize;text.alignment=TextAlignmentOptions.Center;text.color=new Color32(241,237,217,255);text.raycastTarget=false;text.enableWordWrapping=false;return text;}
}
#endif
