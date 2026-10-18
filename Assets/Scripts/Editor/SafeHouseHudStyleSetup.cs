using System;
using System.Collections.Generic;
using System.Linq;
using Purgers.GameFlow.SafeHouse;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Explicit, SafeHouse-only migration of the approved first preview. Never styles Game or player HUD.</summary>
public static class SafeHouseHudStyleSetup
{
    const string ScenePath = "Assets/Scenes/SafeHouse.unity";
    const string PlatePath = "Assets/_Project_Assets/UI/MenuBrushV1/UtilityPlate.png";
    const string FontPath = "Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset";
    static readonly Color Ink = new Color32(28, 33, 25, 255);
    static readonly Color Ivory = new Color32(241, 237, 217, 255);

    [MenuItem("Tools/Purgers/UI/Apply Approved SafeHouse Map and Vote Style")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請先結束 Play Mode。");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.isDirty) throw new InvalidOperationException("SafeHouse 有未保存修改，請先確認，不能連帶保存。");
            var root = scene.GetRootGameObjects().Single(x => x.name == "SafeHouseHUD");
            var owner = root.GetComponent<SafeHouseHudController>();
            var so = new SerializedObject(owner);
            if (so.FindProperty("readyTimerLabel").objectReferenceValue)
                throw new InvalidOperationException("新版已配置；請直接調整指定 Inspector，避免覆蓋人工值。");
            var column = (RectTransform)(root.transform.Find("TopLeftColumn") ?? root.transform.Find("ReferenceFrame/TopLeftColumn"));
            var frame = (RectTransform)root.transform.Find("ReferenceFrame");
            if (!frame || !frame.GetComponent<BattleHudReferenceFrame>())
                throw new InvalidOperationException("缺少既有 ReferenceFrame；不重建其他 HUD。");
            var plate = AssetDatabase.LoadAssetAtPath<Sprite>(PlatePath);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (!plate || !font) throw new InvalidOperationException("缺少既有底板或 HUD 字型。");

            // All unrelated components, including flow, minimap settings, ability/reward trees,
            // must retain their exact serialized values. Only parent child lists may change.
            var untouched = new Dictionary<Component, string>();
            foreach (var go in scene.GetRootGameObjects())
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (!c || c == owner || c.transform.IsChildOf(column) ||
                    c == root.transform || c == frame) continue;
                untouched[c] = EditorJsonUtility.ToJson(c);
            }

            Undo.RegisterFullObjectHierarchyUndo(root, "Approved SafeHouse HUD style");
            column.SetParent(frame, false);
            Box(column, 45.47382f, 40.60165f, 522.1359f, 575);
            var map = R(column, "MinimapSlot");
            Box(map, 0, 0, 247.66895f, 252.54112f);
            Panel(map, plate);
            var mask = map.GetComponent<Mask>() ?? map.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            Stretch(R(map, "MinimapContent"), 20);
            var placeholder = R(map, "Placeholder");
            Stretch(placeholder, 20);
            var placeholderImage = placeholder.GetComponent<Image>();
            if (placeholderImage) { placeholderImage.sprite = null; placeholderImage.color = new Color32(12,12,17,255); }
            var placeholderLabel = placeholder.Find("Label");
            if (placeholderLabel) placeholderLabel.gameObject.SetActive(false);

            var info = R(column, "StageLevelPanel");
            Box(info, 0, 264.97757f, 522.1359f, 99.87904f); Panel(info, plate);
            Label(R(info,"StageLevelText"),font,16,8,490,38,24.7f);
            Label(R(info,"Objective"),font,16,49,490,41,23);

            var slot = R(column,"ReadyCheckSlot"); Box(slot,0,376.8566f,522.1359f,190);
            var vote = R(slot,"ReadyCheckPanel"); Box(vote,0,0,522.1359f,190); Panel(vote,plate);
            var leds = Label(R(vote,"Title"),font,22,18,322,38,25);
            leds.enableAutoSizing=true; leds.fontSizeMin=12; leds.fontSizeMax=25;
            Label(R(vote,"Status"),font,22,69,478,44,25);
            var timer = Label(New(vote,"VoteTimer"),font,350,18,150,38,25);
            timer.alignment=TextAlignmentOptions.MidlineRight;
            var divider=New(vote,"Divider"); Box(divider,22,121,478,1);
            var line=divider.GetComponent<Image>() ?? divider.gameObject.AddComponent<Image>();
            line.color=new Color32(117,125,85,110); line.raycastTarget=false;

            // Existing ready Button keeps its original click listener and authoritative request.
            var ready=R(vote,"ReadyButton"); Box(ready,22,136,188,40);
            var buttonImage=ready.GetComponent<Image>(); buttonImage.sprite=null; buttonImage.color=Color.clear;
            var button=ready.GetComponent<Button>(); button.transition=Selectable.Transition.None;
            var nav=button.navigation; nav.mode=Navigation.Mode.None; button.navigation=nav;
            Key(New(ready,"Keycap"),font,"TAB",0,0,76,40);
            Label(R(ready,"Label"),font,94,0,94,40,23);
            Key(New(vote,"CancelKeycap"),font,"ESC",216,136,76,40);
            var cancel=Label(New(vote,"CancelHint"),font,310,136,190,40,23);
            cancel.text="取消投票";
            so.FindProperty("readyTimerLabel").objectReferenceValue=timer;
            so.ApplyModifiedPropertiesWithoutUndo();
            foreach(var pair in untouched)
                if (!pair.Key || EditorJsonUtility.ToJson(pair.Key)!=pair.Value)
                    throw new InvalidOperationException("範圍外元件改變，未儲存："+pair.Key);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SafeHouse 儲存失敗。");
            Debug.Log("[SafeHouseStyle] Applied first-preview map/vote style. Unrelated components unchanged: "+untouched.Count);
        }
        finally { if (opened && scene.IsValid()) EditorSceneManager.CloseScene(scene,true); }
    }

    /// <summary>Isolated Unity UI render of authored assets; no Runner or gameplay changes.</summary>
    public static string RenderPreview(string outputPath, int width = 1920, int height = 1080)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Editor preview only.");
        var originalActive=SceneManager.GetActiveScene();
        var source=SceneManager.GetSceneByPath(ScenePath);
        bool opened=!source.IsValid() || !source.isLoaded;
        if(opened) source=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
        var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null; Texture2D image=null; Purgers.Map.MinimapTileCache tile=null;
        var previous=RenderTexture.active;
        try
        {
            var cameraGo=new GameObject("SafeHouse UI proof camera",typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraGo,preview);
            var cam=cameraGo.GetComponent<Camera>(); cam.orthographic=true; cam.orthographicSize=height*.5f;
            cam.transform.position=new Vector3(0,0,-10); cam.nearClipPlane=.1f; cam.farClipPlane=100;
            cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color32(71,75,72,255); cam.cullingMask=1<<31;
            var canvasGo=new GameObject("Authored SafeHouse UI proof",typeof(RectTransform),typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasGo,preview);
            var canvas=canvasGo.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=cam;
            ((RectTransform)canvas.transform).sizeDelta=new Vector2(width,height);
            var frame=New(canvas.transform,"ReferenceFrame"); frame.gameObject.AddComponent<BattleHudReferenceFrame>().Refresh();
            var sourceRoot=source.GetRootGameObjects().Single(x=>x.name=="SafeHouseHUD");
            var sourceColumn=sourceRoot.transform.Find("ReferenceFrame/TopLeftColumn");
            var column=UnityEngine.Object.Instantiate(sourceColumn.gameObject,frame).transform;
            column.gameObject.SetActive(true);
            column.Find("StageLevelPanel/StageLevelText").GetComponent<TMP_Text>().text="關卡等級 1　循環 1/4";
            column.Find("StageLevelPanel/Objective").GetComponent<TMP_Text>().text="確認準備，前往下一關";
            var vote=column.Find("ReadyCheckSlot/ReadyCheckPanel"); vote.gameObject.SetActive(true);
            vote.Find("Title").GetComponent<TMP_Text>().text="玩家  <color=#83CC72>●  ●</color>  <color=#B2B6AE>○  ○</color>";
            vote.Find("Status").GetComponent<TMP_Text>().text="已準備 2　未準備 2";
            vote.Find("VoteTimer").GetComponent<TMP_Text>().text="投票 <color=#E5C45E>5</color> 秒";
            vote.Find("ReadyButton/Label").GetComponent<TMP_Text>().text="準備";
            column.Find("MinimapSlot/Placeholder").gameObject.SetActive(false);
            var content=R(column,"MinimapSlot/MinimapContent");
            var bg=content.gameObject.AddComponent<Image>(); bg.color=new Color32(12,12,17,255); bg.raycastTarget=false;
            content.gameObject.AddComponent<RectMask2D>();
            var data=AssetDatabase.LoadAssetAtPath<Purgers.Map.MapCartographyData>("Assets/Prefabs/Map/SafeHouse_Cartography.asset");
            tile=new Purgers.Map.MinimapTileCache(data); tile.Build(data.CellCount,Matrix4x4.identity,null);
            var terrain=New(content,"ProofTerrain"); terrain.anchorMin=terrain.anchorMax=terrain.pivot=Vector2.one*.5f;
            float scale=(247.66895f-40)/35;
            terrain.sizeDelta=new Vector2(data.Width,data.Height)*data.CellSize*scale;
            terrain.gameObject.AddComponent<RawImage>().texture=tile.Terrain;
            var dot=New(content,"ProofLocalPlayer"); dot.anchorMin=dot.anchorMax=dot.pivot=Vector2.one*.5f;
            dot.sizeDelta=new Vector2(7,7); dot.gameObject.AddComponent<Image>().color=new Color32(255,238,160,255);
            var heading=New(dot,"Heading"); heading.anchorMin=heading.anchorMax=heading.pivot=Vector2.one*.5f;
            heading.sizeDelta=new Vector2(3,12); heading.anchoredPosition=new Vector2(0,5);
            heading.gameObject.AddComponent<Image>().color=new Color32(255,238,160,255);
            foreach(var t in canvasGo.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            Canvas.ForceUpdateCanvases();
            foreach(var text in canvasGo.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();
            rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); rt.Create(); cam.targetTexture=rt; cam.Render();
            RenderTexture.active=rt; image=new Texture2D(width,height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outputPath));
            System.IO.File.WriteAllBytes(outputPath,image.EncodeToPNG()); cam.targetTexture=null;
            return outputPath;
        }
        finally
        {
            RenderTexture.active=previous;
            EditorSceneManager.CloseScene(preview,true);
            tile?.Dispose(); if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);} if(image)UnityEngine.Object.DestroyImmediate(image);
            if(opened)EditorSceneManager.CloseScene(source,true);
            if(originalActive.IsValid() && originalActive.isLoaded)SceneManager.SetActiveScene(originalActive);
        }
    }

    static RectTransform R(Transform root,string path) => (RectTransform)root.Find(path);
    static RectTransform New(Transform parent,string name)
    {
        var found=parent.Find(name) as RectTransform;
        if(found) return found;
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); return rect;
    }
    static void Box(RectTransform rt,float x,float y,float w,float h)
    {
        rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);
        rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(w,h);
        rt.localScale=Vector3.one; rt.localRotation=Quaternion.identity;
    }
    static void Stretch(RectTransform rt,float inset)
    {
        rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.pivot=Vector2.one*.5f;
        rt.offsetMin=Vector2.one*inset; rt.offsetMax=-Vector2.one*inset;
        rt.localScale=Vector3.one; rt.localRotation=Quaternion.identity;
    }
    static void Panel(RectTransform rt,Sprite plate)
    {
        var im=rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        im.sprite=plate; im.type=Image.Type.Sliced; im.color=Color.white; im.raycastTarget=false;
    }
    static TMP_Text Label(RectTransform rt,TMP_FontAsset font,float x,float y,float w,float h,float size)
    {
        Box(rt,x,y,w,h);
        var t=rt.GetComponent<TMP_Text>() ?? rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font=font; t.fontSize=size; t.fontStyle=FontStyles.Normal; t.color=Ivory;
        t.enableAutoSizing=false; t.enableWordWrapping=false; t.alignment=TextAlignmentOptions.MidlineLeft;
        t.margin=Vector4.zero; t.raycastTarget=false; t.overflowMode=TextOverflowModes.Ellipsis;
        return t;
    }
    static void Key(RectTransform rt,TMP_FontAsset font,string text,float x,float y,float w,float h)
    {
        Box(rt,x,y,w,h);
        var im=rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        im.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/BattleHUD/ApprovedV2/SkillSquare.png");
        im.color=Ivory; im.type=im.sprite ? Image.Type.Sliced : Image.Type.Simple; im.raycastTarget=false;
        var t=Label(New(rt,"Label"),font,0,0,w,h,23);
        t.color=Ink; t.alignment=TextAlignmentOptions.Center; t.text=text;
    }
}
