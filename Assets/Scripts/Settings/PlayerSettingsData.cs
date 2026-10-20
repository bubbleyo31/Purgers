using System;
using UnityEngine;
namespace Purgers.Settings
{
    public enum PlayerBinding { Forward, Back, Left, Right, Jump, Sprint, Crouch, Grapple, Interact, Aim, Fire, Reload, QuickAction, Rewards, Map, Ready, FocusZ, FocusX }
    [Serializable]
    public sealed class PlayerSettingsData
    {
        public int version = 2;
        public float total = 1, horizontal = 1, vertical = 1;
        public int master = 100, effects = 100, music = 100;
        public bool grappleToggle = true, aimToggle, sprintToggle = true, crouchToggle;
        public bool teamInfoToggle; // 舊保存缺欄位時維持預設按住；共用 Ready 鍵，不改既有 keys 長度。
        public bool fps, ping, fullscreen, vsync;
        public int resolution, quality, frameLimit = -1, region, maxPlayers = 4;
        public string appVersion = "";
        public KeyCode[] keys = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Q, KeyCode.E, KeyCode.Mouse1, KeyCode.Mouse0, KeyCode.R, KeyCode.F, KeyCode.LeftAlt, KeyCode.M, KeyCode.Tab, KeyCode.Z, KeyCode.X };
        public PlayerSettingsData Copy() => JsonUtility.FromJson<PlayerSettingsData>(JsonUtility.ToJson(this));
        public bool Same(PlayerSettingsData other) => JsonUtility.ToJson(this) == JsonUtility.ToJson(other);
        public KeyCode Key(PlayerBinding action) => keys[(int)action];
        public void Bind(PlayerBinding action, KeyCode key)
        {
            if (key == KeyCode.Escape || !IsAllowedKey(key)) return;
            for (int i = 0; i < keys.Length; i++) if (i != (int)action && keys[i] == key) keys[i] = KeyCode.None;
            keys[(int)action] = key;
        }
        public static bool IsAllowedKey(KeyCode key) => Enum.IsDefined(typeof(KeyCode), key) && key >= KeyCode.None && key < KeyCode.JoystickButton0 && key != KeyCode.Escape;
        public void Normalize()
        {
            total = Clamp(total, .1f, 5); horizontal = Clamp(horizontal, .1f, 2); vertical = Clamp(vertical, .1f, 2);
            master = Mathf.Clamp(master, 0, 100); effects = Mathf.Clamp(effects, 0, 100); music = Mathf.Clamp(music, 0, 100);
            var defaults = new PlayerSettingsData().keys;
            if (keys == null) keys = defaults;
            else if (keys.Length != defaults.Length)
            {
                var migrated = new KeyCode[defaults.Length];
                Array.Copy(keys, migrated, Math.Min(keys.Length, migrated.Length));
                for (int i = keys.Length; i < migrated.Length; i++)
                {
                    // Preserve old custom bindings, even if Z/X was already assigned.
                    KeyCode candidate = defaults[i];
                    migrated[i] = Array.IndexOf(migrated, candidate, 0, i) >= 0 ? KeyCode.None : candidate;
                }
                keys = migrated;
            }
            version = 2;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!IsAllowedKey(keys[i])) keys[i] = KeyCode.None;
                for (int j = 0; j < i; j++) if (keys[i] != KeyCode.None && keys[j] == keys[i]) keys[i] = KeyCode.None;
            }
        }
        static float Clamp(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp(value, min, max);
        public static string KeyLabel(KeyCode key)
        {
            switch (key) { case KeyCode.None: return "—"; case KeyCode.Escape: return "ESC"; case KeyCode.Space: return "SPACE"; case KeyCode.LeftShift: return "L SHIFT"; case KeyCode.LeftControl: return "L CTRL"; case KeyCode.LeftAlt: return "L ALT"; case KeyCode.Mouse0: return "滑鼠左"; case KeyCode.Mouse1: return "滑鼠右"; case KeyCode.Mouse2: return "滑鼠中"; case KeyCode.Mouse3: return "側鍵 1"; case KeyCode.Mouse4: return "側鍵 2"; default: return key.ToString().ToUpperInvariant(); }
        }
    }
}
