using System;
using UnityEditor;
using UnityEngine;
using TMPro;
using Purgers.Progression;

public static class TeamInfoPanelSetup
{
    static void ConfigureWeaponContent(TeamInfoPanelSettings settings)
    {
        settings.weaponContent = new[] {
            Weapon("AttackWeapon",76,465,1196,943), Weapon("SupportWeapon",215,463,948,972), Weapon("TankWeapon",86,490,1198,881)
        };
    }
    static TeamInfoPanelSettings.WeaponContent Weapon(string file,float left,float top,float right,float bottom) =>
        new TeamInfoPanelSettings.WeaponContent {
            sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/BattleHUD/WeaponIcon/"+file+".png"),
            contentUV=new Rect(left/1254,(1254-bottom)/1254,(right-left)/1254,(bottom-top)/1254)
        };
    public const string SettingsPath = "Assets/Resources/Presentation/TeamInfoPanel.asset";
    [MenuItem("Tools/Purgers/UI/Create TAB Team Info Settings")]
    public static void CreateMissingSettings()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TeamInfoPanelSettings>(SettingsPath);
        if (existing != null)
        {
            if (existing.weaponContent.Length == 0) { ConfigureWeaponContent(existing); EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); }
            return;
        }
        var settings = ScriptableObject.CreateInstance<TeamInfoPanelSettings>();
        settings.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/PlayerSettings/PlayerSettingsFont.asset");
        settings.rankFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/GrowthIcons/Fonts/PurgersRankSlab SDF.asset");
        settings.portraits = AssetDatabase.LoadAssetAtPath<PlayerPortraitProvider>("Assets/_Project_Assets/UI/BattleHUD/SharedPlayerPortrait.asset");
        settings.catalog = Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
        settings.skillBackground = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/BattleHUD/ApprovedV2/SkillSquare.png");
        settings.growthBackground = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/BattleHUD/Layout/RewardHex.png");
        if (!settings.font || !settings.rankFont || !settings.portraits || !settings.catalog || !settings.skillBackground)
            throw new InvalidOperationException("缺少既有字體、頭像、獎勵清單或技能背景引用。");
        ConfigureWeaponContent(settings);
        AssetDatabase.CreateAsset(settings,SettingsPath); AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("TAB 隊伍資訊設定已建立；沿 InputManager 建立本機呈現，不改 Scene／Prefab。");
    }
}
