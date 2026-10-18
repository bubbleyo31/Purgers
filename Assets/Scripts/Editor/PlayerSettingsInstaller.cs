using System;
using System.Linq;
using System.Reflection;
using Purgers.Settings;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PlayerSettingsInstaller
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("請先停止播放。");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity", OpenSceneMode.Additive);
        if (scene.isDirty) throw new InvalidOperationException("主選單有未保存變更，未自動保存或覆寫。");
        var main = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Fusion.Menu.FusionMenuUIMain>(true)).Single();
        var existing = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerSettingsMenu>(true)).FirstOrDefault();
        if (existing) throw new InvalidOperationException("正式設定已配置，不重複安裝。");
        const string folder = "Assets/_Project_Assets/UI/PlayerSettings";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project_Assets/UI", "PlayerSettings");
        const string fontPath = folder + "/PlayerSettingsFont.asset";
        if (!AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath))
            if (!AssetDatabase.CopyAsset("Assets/SettingsPreview/SettingsPreviewFont.asset", fontPath)) throw new InvalidOperationException("無法建立正式字型。");
        var root = new GameObject("Player Settings", typeof(PlayerSettingsMenu));
        root.transform.SetParent(main.transform.root, false);
        var menu = root.GetComponent<PlayerSettingsMenu>();
        menu.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        menu.brush = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/MenuBrushV1/Brush.png");
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/MainAudioMixer.mixer");
        var master = mixer.FindMatchingGroups("").Single(g => g.name == "Master");
        var effects = mixer.FindMatchingGroups("").Single(g => g.name == "SFX");
        var music = mixer.FindMatchingGroups("").FirstOrDefault(g => g.name == "Music");
        if (!music)
        {
            music = (AudioMixerGroup)mixer.GetType().GetMethod("CreateNewGroup", Flags).Invoke(mixer, new object[] { "Music", true });
            var children = master.GetType().GetProperty("children", Flags);
            var previous = (Array)children.GetValue(master);
            var next = Array.CreateInstance(previous.GetType().GetElementType(), previous.Length + 1);
            Array.Copy(previous, next, previous.Length); next.SetValue(music, previous.Length); children.SetValue(master, next);
        }
        var baseline = new Vector3(Expose(mixer, master, "PlayerMasterVolume"), Expose(mixer, effects, "PlayerEffectsVolume"), Expose(mixer, music, "PlayerMusicVolume"));
        EditorUtility.SetDirty(mixer); AssetDatabase.SaveAssetIfDirty(mixer);
        var data = new SerializedObject(menu);
        data.FindProperty("mainScreen").objectReferenceValue = main;
        data.FindProperty("audioMixer").objectReferenceValue = mixer;
        data.FindProperty("audioBaselineDb").vector3Value = baseline;
        data.FindProperty("effectsGroup").objectReferenceValue = effects;
        data.FindProperty("musicGroup").objectReferenceValue = music;
        data.FindProperty("sampleClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/AudioClips/Combat/Hit/Hit 1.mp3");
        data.ApplyModifiedPropertiesWithoutUndo();
        var button = main.GetComponentsInChildren<Button>(true).Single(b => b.name == "SettingsButton");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, menu.OpenFromMain);
        EditorUtility.SetDirty(button);
        var gameplay = main.transform.root.GetComponentInChildren<Fusion.Menu.FusionMenuUIGameplay>(true);
        var gameplayData = new SerializedObject(gameplay);
        gameplayData.FindProperty("_useUnifiedGameplayMenu").boolValue = true;
        foreach (var name in new[] { "_sessionGameObject", "_playersGameObject" })
            ((GameObject)gameplayData.FindProperty(name).objectReferenceValue).SetActive(false);
        var disconnect = (Button)gameplayData.FindProperty("_disconnectButton").objectReferenceValue;
        disconnect.gameObject.SetActive(false); gameplayData.ApplyModifiedPropertiesWithoutUndo();
        foreach (var c in main.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
            if (c && (c.GetType().Name == "FusionMenuScreenPluginPing" || c.GetType().Name == "FusionMenuFpsAvgCounter"))
            { c.enabled = false; c.gameObject.SetActive(false); EditorUtility.SetDirty(c); }
        // Hide the containing status widgets too, including their static labels and backgrounds.
        foreach (var t in gameplay.GetComponentsInChildren<Transform>(true))
            if (t.name == "Fps" || t.name == "Ping" || t.name == "FPS") t.gameObject.SetActive(false);
        gameplay.transform.Find("SessionCodeOverlay")?.gameObject.SetActive(false);
        EditorUtility.SetDirty(menu); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("主選單保存失敗。");
        if (opened) EditorSceneManager.CloseScene(scene, true);
        return "Installed production settings, main menu button, audio parameters and gameplay HUD ownership.";
    }
    static float Expose(AudioMixer mixer, AudioMixerGroup group, string name)
    {
        var groupType = group.GetType();
        var snapshot = mixer.GetType().GetProperty("TargetSnapshot", Flags).GetValue(mixer);
        float baseline = (float)groupType.GetMethod("GetValueForVolume", Flags).Invoke(group, new[] { (object)mixer, snapshot });
        var property = mixer.GetType().GetProperty("exposedParameters", Flags);
        var array = (Array)property.GetValue(mixer); var type = array.GetType().GetElementType();
        if (array.Cast<object>().Any(p => (string)type.GetField("name", Flags).GetValue(p) == name)) return baseline;
        var parameter = Activator.CreateInstance(type);
        type.GetField("guid", Flags).SetValue(parameter, groupType.GetMethod("GetGUIDForVolume", Flags).Invoke(group, null));
        type.GetField("name", Flags).SetValue(parameter, name);
        var result = Array.CreateInstance(type, array.Length + 1); Array.Copy(array, result, array.Length); result.SetValue(parameter, array.Length);
        property.SetValue(mixer, result); return baseline;
    }
}
