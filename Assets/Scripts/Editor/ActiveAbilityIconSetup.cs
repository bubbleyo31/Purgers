#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ActiveAbilityIconSetup
{
    public const string IconRoot = "Assets/_Project_Assets/UI/AbilityIcons/E_V4";
    private static readonly string[] Keys = { "Grenade", "PiercingCannon", "Shockwave", "Shield", "HealingPack",
        "Ricochet", "Experience", "Blink", "BulletTime", "PrecisionLock" };
    private static readonly string[] Files = { "01_ExplosiveGrenade.png", "02_PiercingCannon.png", "03_ImpactFanWave.png",
        "04_QuickShield.png", "05_HealingPack.png", "06_RicochetBullet.png", "07_ExperienceBoost.png",
        "08_Blink.png", "09_BulletTime.png", "10_PrecisionLock.png" };
    [MenuItem("Tools/Player Ability/套用指定 V4 技能圖示")]
    public static void ApplyIcons()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("請離開 Play Mode。");
        Directory.CreateDirectory(IconRoot);
        for (int i=0;i<Keys.Length;i++)
        {
            string path = IconRoot+"/"+Files[i];
            File.Copy("deliverables/AbilityIcons_E_20261003_v4/"+Files[i], path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var def = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(PlayerActiveAbilityAssetSetup.DataRoot+"/"+Keys[i]+".asset");
            if (def == null) throw new System.InvalidOperationException("缺少技能："+Keys[i]);
            var data = new SerializedObject(def);
            data.FindProperty("hudIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            data.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(def);
        }
        Directory.CreateDirectory("Assets/Resources/Presentation");
        const string settingsPath = "Assets/Resources/Presentation/ActiveAbilityColorGrading.asset";
        if (AssetDatabase.LoadAssetAtPath<AbilityColorGradingSettings>(settingsPath) == null)
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AbilityColorGradingSettings>(), settingsPath);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }
}
#endif
