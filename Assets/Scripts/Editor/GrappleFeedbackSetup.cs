using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>明確執行才配置使用者指定三個音檔；不開啟、不保存 Scene，不重設已存在 Cue 的人工值。</summary>
public static class GrappleFeedbackSetup
{
    public const string CueFolder = "Assets/Audio/Cues/Network/Grapple";
    public const string CatalogPath = "Assets/Audio/Catalogs/GameplayAudioCatalog.asset";
    public const string PlayerPath = "Assets/Prefabs/KCC_Player.prefab";

    [MenuItem("Purgers/Audio/Configure Grapple Feedback")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請在 Edit Mode 配置。");
        var catalog = AssetDatabase.LoadAssetAtPath<GameplayAudioCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException("找不到既有 GameplayAudioCatalog。");
        string[] names = { "GrappleShoot", "Hooking", "HookOnThing" };
        var clips = names.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/AudioClips/Grapple/" + n + ".mp3")).ToArray();
        if (clips.Any(c => c == null)) throw new InvalidOperationException("三個指定音檔必須先完成匯入。");
        var all = AssetDatabase.FindAssets("t:GameplayAudioCue")
            .Select(g => AssetDatabase.LoadAssetAtPath<GameplayAudioCue>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            string path = CueFolder + "/" + names[i] + ".asset";
            if (AssetDatabase.LoadAssetAtPath<GameplayAudioCue>(path) == null && all.Any(c => c.NetworkId == 20 + i))
                throw new InvalidOperationException("預留音效 ID " + (20 + i) + " 已被其他 Cue 使用，未覆寫。");
        }
        if (!AssetDatabase.IsValidFolder(CueFolder)) AssetDatabase.CreateFolder("Assets/Audio/Cues/Network", "Grapple");
        var cues = new GameplayAudioCue[3];
        for (int i = 0; i < cues.Length; i++)
        {
            string path = CueFolder + "/" + names[i] + ".asset";
            cues[i] = AssetDatabase.LoadAssetAtPath<GameplayAudioCue>(path);
            if (cues[i] != null) continue;
            var cue = ScriptableObject.CreateInstance<GameplayAudioCue>();
            var data = new SerializedObject(cue);
            data.FindProperty("networkId").intValue = 20 + i;
            data.FindProperty("clips").arraySize = 1;
            data.FindProperty("clips").GetArrayElementAtIndex(0).objectReferenceValue = clips[i];
            data.FindProperty("worldSpatialBlend").floatValue = 1f;
            // 線性衰減確保最大距離外為靜音，適用這次指定的可聽範圍。
            data.FindProperty("worldRolloffMode").enumValueIndex = (int)AudioRolloffMode.Linear;
            data.FindProperty("worldMinimumDistance").floatValue = 4f;
            data.FindProperty("worldMaximumDistance").floatValue = 60f;
            data.FindProperty("worldDopplerLevel").floatValue = 0f;
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(cue, path); cues[i] = cue;
        }
        foreach (var clip in clips)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            if (!importer.forceToMono) { importer.forceToMono = true; importer.SaveAndReimport(); }
        }
        var catData = new SerializedObject(catalog);
        var list = catData.FindProperty("networkCues");
        foreach (var cue in cues)
        {
            bool exists = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == cue) exists = true;
            if (!exists) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = cue; }
        }
        catData.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(catalog);
        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var grapple = root.GetComponent<PlayerGrapple>();
            var emitter = root.GetComponent<NetworkPlayerAudioEmitter>();
            if (grapple == null || emitter == null) throw new InvalidOperationException("玩家 Prefab 缺少既有鈎索或音效元件。");
            var emitterData = new SerializedObject(emitter);
            if (emitterData.FindProperty("audioCatalog").objectReferenceValue != catalog)
                throw new InvalidOperationException("玩家使用不同 Catalog，未覆寫。");
            var data = new SerializedObject(grapple);
            data.FindProperty("grappleShootCue").objectReferenceValue = cues[0];
            data.FindProperty("grapplePullLoopCue").objectReferenceValue = cues[1];
            data.FindProperty("grappleImpactCue").objectReferenceValue = cues[2];
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach (var cue in cues) AssetDatabase.SaveAssetIfDirty(cue);
        Debug.Log("[Grapple Feedback] 三個 3D Cue 已接入既有 Catalog 與 KCC_Player；Scene 未保存，既有 Cue 調整值保留。");
    }
}
