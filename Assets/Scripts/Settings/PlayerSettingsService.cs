using System;
using System.IO;
using UnityEngine;
namespace Purgers.Settings
{
    // Local preferences only. Remote simulation consumes NetInput, never this singleton.
    public static class PlayerSettingsService
    {
        static PlayerSettingsData current;
        public static PlayerSettingsData Current => current ?? (current = new PlayerSettingsData());
        public static event Action Changed;
        public static bool IsInitialized { get; private set; }
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "player-settings-v1.json");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { current = null; Changed = null; IsInitialized = false; }
        public static void Initialize(PlayerSettingsData initial)
        {
            if (IsInitialized) return;
            current = initial.Copy();
            try { if (File.Exists(Path)) { var saved = JsonUtility.FromJson<PlayerSettingsData>(File.ReadAllText(Path)); if (saved != null && (saved.version == 1 || saved.version == 2)) current = saved; } }
            catch (Exception e) { Debug.LogWarning("[PlayerSettings] 偏好讀取失敗，使用初始值：" + e.Message); }
            current.Normalize(); IsInitialized = true;
        }
        public static void Commit(PlayerSettingsData data)
        {
            var value = data.Copy(); value.Normalize(); SaveTo(Path, value); current = value; Changed?.Invoke();
        }
        public static void SaveTo(string path, PlayerSettingsData data)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
        }
        public static void SetPrompt(TMPro.TMP_Text label, PlayerBinding action) => SetPrompt(label, Key(action));
        public static void SetPrompt(TMPro.TMP_Text label, KeyCode key)
        {
            if (!label) return;
            if (!label.enableAutoSizing) { label.fontSizeMax = label.fontSize; label.fontSizeMin = 10; label.enableAutoSizing = true; }
            label.text = PlayerSettingsData.KeyLabel(key);
        }
        public static KeyCode Key(PlayerBinding action) => Current.Key(action);
        public static string Label(PlayerBinding action) => PlayerSettingsData.KeyLabel(Key(action));
        public static bool Held(PlayerBinding action) => Key(action) != KeyCode.None && Input.GetKey(Key(action));
        public static bool Down(PlayerBinding action) => Key(action) != KeyCode.None && Input.GetKeyDown(Key(action));
    }
}
