using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>匯入已確認圖示與獨立設定；不保存場景、不改動技能原圖或玩家／敵人 Prefab。</summary>
public static class StatusEffectHudSetup
{
    public const string SettingsPath = "Assets/Resources/Presentation/StatusEffectHud.asset";
    public const string ArtRoot = "Assets/_Project_Assets/UI/StatusEffects/V1/";
    [MenuItem("Tools/Purgers/UI/Create Approved Status Effect Assets")]
    public static void CreateMissingAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先退出 Play Mode。");
        var current = AssetDatabase.LoadAssetAtPath<StatusEffectHudSettings>(SettingsPath);
        if (current != null) { Debug.Log("異常狀態設定已存在，保留所有人工設定。"); return; }
        Import(ArtRoot + "BulletTime.png", false);
        Import(ArtRoot + "GrappleBlocked.png", false);
        Import(ArtRoot + "StatusSymbols.png", true);
        var symbols = AssetDatabase.LoadAllAssetsAtPath(ArtRoot + "StatusSymbols.png").OfType<Sprite>().ToArray();
        Sprite Symbol(string name) => symbols.Single(s => s.name == name);
        var settings = ScriptableObject.CreateInstance<StatusEffectHudSettings>();
        settings.styles = new[] {
            Style(StatusEffectKind.Stun, Symbol("Stun"), new Color32(255,204,49,255)),
            Style(StatusEffectKind.Root, Symbol("Root"), new Color32(255,189,43,255)),
            Style(StatusEffectKind.BulletTime, AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot+"BulletTime.png"), new Color32(181,72,255,255)),
            Style(StatusEffectKind.Vulnerability, Symbol("Vulnerability"), new Color32(255,100,111,255)),
            Style(StatusEffectKind.Mark, Symbol("Mark"), new Color32(255,155,63,255)),
            Style(StatusEffectKind.Captured, Symbol("Captured"), new Color32(145,248,55,255)),
            Style(StatusEffectKind.GrappleBlocked, AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot+"GrappleBlocked.png"), new Color32(255,92,100,255)),
            Style(StatusEffectKind.Slow, Symbol("Slow"), new Color32(70,194,247,255))
        };
        if (settings.styles.Any(s => s.icon == null)) throw new InvalidOperationException("圖示匯入不完整。");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        AssetDatabase.Refresh(); AssetDatabase.CreateAsset(settings, SettingsPath); AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("異常狀態圖示與設定已建立；三個既有 HUD 讀取共用 Resources 設定。");
    }
    private static StatusEffectHudSettings.Style Style(StatusEffectKind kind, Sprite icon, Color color) =>
        new StatusEffectHudSettings.Style { kind = kind, icon = icon, barColor = color };

    [MenuItem("Tools/Purgers/UI/Restore Health Position For Center Status HUD")]
    public static void RestoreSelfHealthPosition()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/_Menu.unity" || scene.isDirty)
            throw new InvalidOperationException("需非播放且已儲存的 _Menu，避免連帶保存人工修改。");
        var controllers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LocalPlayerHealthSlider>(true)).ToArray();
        if (controllers.Length != 1) throw new InvalidOperationException("預期唯一常駐生命 HUD。");
        var health = new SerializedObject(controllers[0]).FindProperty("healthSegmentView").objectReferenceValue as LocalHealthSegmentView;
        if (health == null) throw new InvalidOperationException("生命分格引用未就緒。");
        var group = health.transform.parent as RectTransform;
        // 使用加入異常 HUD 前已存在的場景座標；只接受本次遷移前後的位置，避免覆蓋人工調整。
        const float originalY = -967.7671f, reservedY = -912.06366f;
        if (Mathf.Abs(group.anchoredPosition.y - originalY) < .01f) return;
        if (Mathf.Abs(group.anchoredPosition.y - reservedY) > .01f)
            throw new InvalidOperationException("生命群組位置另有修改，請先核對。");
        Undo.RecordObject(group, "Restore original health HUD position");
        group.anchoredPosition = new Vector2(group.anchoredPosition.x, originalY);
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
    }
    private static void Import(string path, bool sheet)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = sheet ? 1024 : 512; importer.spritePixelsPerUnit = 100;
        importer.spriteImportMode = sheet ? SpriteImportMode.Multiple : SpriteImportMode.Single;
        if (sheet)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            var names = new[] { "Stun", "Root", "Vulnerability", "Mark", "Captured", "Slow" };
            var sprites = new SpriteMetaData[6];
            for (int i = 0; i < 6; i++) sprites[i] = new SpriteMetaData {
                name = names[i], rect = new Rect(i % 3 * width / 3f, (1 - i / 3) * height / 2f, width / 3f, height / 2f),
                alignment = (int)SpriteAlignment.Center, pivot = Vector2.one * .5f };
            // 專案未啟用 2D Sprite Editor 套件；沿用 Unity 2022.3 內建匯入序列化入口，不新增套件依賴。
#pragma warning disable 0618
            importer.spritesheet = sprites;
#pragma warning restore 0618
        }
        importer.SaveAndReimport();
    }
}
