#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Purgers.Progression;

/// <summary>只接上已確認成長圖示、階級文字與能量圈中央圖示；不重設既有 HUD 版面或獎勵規則。</summary>
public static class GrowthIconSetup
{
    public const string Root = "Assets/_Project_Assets/UI/GrowthIcons/V5";
    public const string StagePath = "Assets/Prefabs/UI/StageHUD.prefab";
    public static readonly string[] Files = {
        "", "A01_MaxHealth_I", "A02_ReloadSpeed_I", "A03_MeleeSpeed_I", "A04_MagazineCapacity_I",
        "A05_GrappleEnergy_I", "A06_FocusCooldown_I", "A07_GrappleCooldown_I", "A08_OutOfCombatRegen_I",
        "A09_RegenDelay_I", "S01_NotToday", "S02_SelfRevive", "S03_Adrenaline", "S04_CarnivorousTree",
        "S05_HardChoice", "S06_GunMaster" };
    private static readonly Vector2[] Centers = { Vector2.zero,
        new Vector2(.5f,.5f),new Vector2(.46f,.43f),new Vector2(.58f,.43f),
        new Vector2(.49f,.4f),new Vector2(.395f,.43f),new Vector2(.5f,.43f),
        new Vector2(.395f,.43f),new Vector2(.5f,.48f),new Vector2(.5f,.47f) };
    private static readonly Vector2[] Sizes = { Vector2.zero,
        new Vector2(.42f,.34f),new Vector2(.27f,.34f),new Vector2(.40f,.40f),
        new Vector2(.25f,.33f),new Vector2(.20f,.18f),new Vector2(.42f,.32f),
        new Vector2(.20f,.18f),new Vector2(.38f,.30f),new Vector2(.38f,.30f) };

    [MenuItem("Tools/Purgers/UI/Apply approved growth icons V5")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先離開 Play Mode。");
        for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("開啟中的場景有未保存變更，請先處理，避免一併寫入。");
        foreach (var path in Directory.GetFiles(Root,"*.png",SearchOption.AllDirectories))
        {
            string normalized=path.Replace('\\','/');
            AssetDatabase.ImportAsset(normalized,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(normalized);
            importer.textureType=TextureImporterType.Sprite;
            importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;
            importer.mipmapEnabled=false;
            importer.maxTextureSize=256;
            var settings=new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType=SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        for(int i=1;i<=15;i++)
        {
            var kind=(PlayerGrowthKind)i;
            var reward=AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                "Assets/_Project_Assets/Data/Progression/Growth/Reward_"+kind+".asset");
            if(reward==null) throw new InvalidOperationException("缺少獎勵："+kind);
            var so=new SerializedObject(reward);
            so.FindProperty("growthIcon").objectReferenceValue=Sprite(Files[i]+".png");
            if(i<=9)
            {
                so.FindProperty("growthRankBackground").objectReferenceValue=Sprite("RankBackgrounds/"+Files[i].Replace("_I","_Base")+".png");
                so.FindProperty("growthRankCenter").vector2Value=Centers[i];
                so.FindProperty("growthRankSize").vector2Value=Sizes[i];
            }
            if(kind==PlayerGrowthKind.JohnWick) so.FindProperty("growthDisplayName").stringValue="槍械高手";
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(reward);
        }
        var prefab=PrefabUtility.LoadPrefabContents(StagePath);
        try { Configure(prefab); PrefabUtility.SaveAsPrefabAsset(prefab,StagePath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        const string safePath="Assets/Scenes/SafeHouse.unity";
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(safePath);
        bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(safePath,OpenSceneMode.Additive);
        try
        {
            foreach(var root in scene.GetRootGameObjects()) Configure(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
        ApplyApprovedReadability();
        Debug.Log("[GrowthIcons] Applied 15 definitions, StageHUD and SafeHouse icon presentation.");
    }


    // 核准預覽的九張完整底圖邊長；對應正式 71.28525×82.95008、Inset 2。
    private static readonly float[] ApprovedIconSides = {
        0, 68.857994f, 66.60168f, 69.61367f, 64.429306f, 62.554512f,
        71.51949f, 62.867744f, 64.48724f, 63.84962f
    };

    [MenuItem("Tools/Purgers/UI/Apply approved attribute icon readability")]
    public static void ApplyApprovedReadability()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("請先離開 Play Mode。");
        float referenceSide = 1f / (1f / (82.95008f - 4f) + .5f / (71.28525f - 4f));
        for (int i = 1; i <= 9; i++)
        {
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                "Assets/_Project_Assets/Data/Progression/Growth/Reward_" + (PlayerGrowthKind)i + ".asset");
            if (reward == null || reward.GrowthRankBackground == null)
                throw new InvalidOperationException("缺少已確認的屬性底圖：" + i);
            var so = new SerializedObject(reward);
            so.FindProperty("growthIconScale").floatValue = ApprovedIconSides[i] / referenceSide;
            // 保留各項人工 Y／字框；只校正核准的兩項彈匣水平中心。
            if (i == 2 || i == 4)
            {
                Vector2 center = so.FindProperty("growthRankCenter").vector2Value;
                center.x = i == 2 ? 618.5f / 1254f : 654.5f / 1254f;
                so.FindProperty("growthRankCenter").vector2Value = center;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(reward);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(reward.GrowthRankBackground));
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.mipMapBias = -.5f;
            importer.SaveAndReimport();
        }
        Debug.Log("[GrowthIcons] Applied approved size, magazine centres and sampling to nine attribute definitions. HUD scene/prefab layout unchanged.");
    }

    private static Sprite Sprite(string file)
    {
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Root+"/"+file);
        if(sprite==null) throw new InvalidOperationException("缺少 Sprite："+file);
        return sprite;
    }

    public static void Configure(GameObject root)
    {
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/_Project_Assets/UI/GrowthIcons/Fonts/PurgersRankSlab SDF.asset");
        foreach(var hud in root.GetComponentsInChildren<LocalPlayerRewardHUD>(true))
        {
            var so=new SerializedObject(hud);
            foreach(var key in new[]{"leftCard","middleCard","rightCard"})
            {
                var card=so.FindProperty(key);
                var icon=card.FindPropertyRelative("icon").objectReferenceValue as Image;
                if(icon==null) continue;
                var view=icon.GetComponent<GrowthRewardRankView>();
                if(view==null) view=icon.gameObject.AddComponent<GrowthRewardRankView>();
                var child=icon.transform.Find("GrowthRank");
                if(child==null) child=new GameObject("GrowthRank",typeof(RectTransform)).transform;
                child.SetParent(icon.transform,false);
                child.gameObject.layer=icon.gameObject.layer;
                var label=child.GetComponent<TextMeshProUGUI>();
                if(label==null) label=child.gameObject.AddComponent<TextMeshProUGUI>();
                label.font=font;
                label.fontStyle=FontStyles.Bold;
                label.alignment=TextAlignmentOptions.Center;
                label.enableAutoSizing=true;
                label.fontSizeMin=1;
                label.fontSizeMax=60;
                label.enableWordWrapping=false;
                label.overflowMode=TextOverflowModes.Truncate;
                label.raycastTarget=false;
                label.margin=Vector4.zero;
                label.text="";
                var rect=label.rectTransform;
                rect.anchorMin=rect.anchorMax=Vector2.one*.5f;
                rect.pivot=Vector2.one*.5f;
                rect.localScale=Vector3.one;
                var viewSo=new SerializedObject(view);
                viewSo.FindProperty("rankLabel").objectReferenceValue=label;
                viewSo.ApplyModifiedPropertiesWithoutUndo();
                card.FindPropertyRelative("rankView").objectReferenceValue=view;
                child.gameObject.SetActive(false);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach(var hud in root.GetComponentsInChildren<LocalPlayerBattleAbilityHUD>(true))
        {
            var so=new SerializedObject(hud);
            var fill=so.FindProperty("grappleFill").objectReferenceValue as Image;
            if(fill==null) continue;
            var child=fill.transform.parent.Find("GrappleBodyIcon");
            if(child==null) child=new GameObject("GrappleBodyIcon",typeof(RectTransform),typeof(Image)).transform;
            child.SetParent(fill.transform.parent,false);
            child.gameObject.layer=fill.gameObject.layer;
            var image=child.GetComponent<Image>();
            var rt=image.rectTransform;
            var f=fill.rectTransform;
            rt.anchorMin=f.anchorMin; rt.anchorMax=f.anchorMax; rt.pivot=Vector2.one*.5f;
            rt.anchoredPosition=f.anchoredPosition+Vector2.Scale(f.rect.size,Vector2.one*.5f-f.pivot);
            rt.sizeDelta=new Vector2(64,64); rt.localScale=Vector3.one; rt.localRotation=Quaternion.identity;
            image.sprite=Sprite("B01_GrappleBase.png"); image.preserveAspect=true; image.raycastTarget=false;
            image.color=new Color32(241,237,217,255);
            child.SetSiblingIndex(fill.transform.GetSiblingIndex()+1);
        }
    }
}
#endif
