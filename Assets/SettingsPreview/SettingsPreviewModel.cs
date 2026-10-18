#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace Purgers.SettingsPreview
{
    // Editor-only interaction model. Never reads production input, preferences or world saves.
    [Serializable]
    public sealed class PreviewSettings
    {
        public float total = 1, horizontal = 1, vertical = 1;
        public int master = 100, effects = 100, music = 100;
        public bool grappleToggle = true, aimToggle, sprintToggle = true, crouchToggle;
        public bool fps, ping, fullscreen = true, vsync;
        public int resolution = 1, quality = 2, frameLimit = 2, region, maxPlayers = 4;
        public KeyCode[] keys = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.Space,
            KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Q, KeyCode.E, KeyCode.Mouse1,
            KeyCode.Mouse0, KeyCode.R, KeyCode.F, KeyCode.LeftAlt, KeyCode.M, KeyCode.Tab };
        public PreviewSettings Copy() { return JsonUtility.FromJson<PreviewSettings>(JsonUtility.ToJson(this)); }
        public bool Same(PreviewSettings other) { return JsonUtility.ToJson(this) == JsonUtility.ToJson(other); }
    }

    public enum PreviewScreen { Game, Pause, Settings, Main }
    public enum PreviewPopup { None, Save, Unsaved, Reset, Leave, Display }

    public sealed class SettingsPreviewModel
    {
        public static readonly string[] Actions = { "向前移動", "向後移動", "向左移動", "向右移動", "跳躍", "跑步", "蹲下／滑鏟", "鈎索／Q 能力", "技能／互動／救援", "瞄準", "主武器攻擊", "換彈", "武器特殊動作", "獎勵選擇", "展開小地圖", "準備切換" };
        public PreviewSettings Saved = new PreviewSettings();
        public PreviewSettings Draft;
        public PreviewScreen Screen = PreviewScreen.Game;
        public PreviewScreen ReturnTo = PreviewScreen.Pause;
        public PreviewPopup Popup;
        public bool Host = true, Vote, FailSave, FailLeave;
        public int Capture = -1, Selected;
        public string Notice = "";
        public float DisplaySeconds;
        public Action<PreviewSettings> Persist;
        PreviewSettings pending;
        bool pendingExit;
        public SettingsPreviewModel() { Draft = Saved.Copy(); }
        public bool Dirty { get { return !Draft.Same(Saved); } }
        public int[] Unbound { get { return Enumerable.Range(0, Draft.keys.Length).Where(i => Draft.keys[i] == KeyCode.None).ToArray(); } }
        public void OpenSettings(bool main = false) { ReturnTo = main ? PreviewScreen.Main : PreviewScreen.Pause; Draft = Saved.Copy(); Screen = PreviewScreen.Settings; Notice = ""; }
        public void OpenPause() { Screen = PreviewScreen.Pause; Selected = 0; }
        public void Bind(KeyCode key)
        {
            if (Capture < 0 || key == KeyCode.None) return;
            if (key == KeyCode.Escape) { Capture = -1; return; }
            int target = Capture;
            for (int i = 0; i < Draft.keys.Length; i++)
                if (i != target && Draft.keys[i] == key) Draft.keys[i] = KeyCode.None;
            Draft.keys[target] = key; Capture = -1;
            Notice = Unbound.Length > 0 ? "按鍵已移轉；請注意紅色未配置項目" : "按鍵提示已同步，確認後保存";
        }
        public void Escape()
        {
            if (Capture >= 0) { Capture = -1; Notice = "已取消改鍵"; return; }
            if (Vote) { Vote = false; Notice = "已取消投票（預覽）"; return; }
            if (Popup == PreviewPopup.Display) { RevertDisplay(); return; }
            if (Popup != PreviewPopup.None) { Popup = PreviewPopup.None; return; }
            if (Screen == PreviewScreen.Settings) Back();
            else if (Screen == PreviewScreen.Pause) Screen = PreviewScreen.Game;
            else if (Screen == PreviewScreen.Game) OpenPause();
        }
        public void Back() { if (Dirty) Popup = PreviewPopup.Unsaved; else Screen = ReturnTo; }
        public void RequestSave() { if (Dirty && Capture < 0) Popup = PreviewPopup.Save; }
        public void No()
        {
            if (Popup == PreviewPopup.Display) { RevertDisplay(); return; }
            if (Popup == PreviewPopup.Unsaved) { Draft = Saved.Copy(); Screen = ReturnTo; }
            Popup = PreviewPopup.None;
        }
        public void Yes()
        {
            if (Popup == PreviewPopup.Reset) { Draft = new PreviewSettings(); Popup = PreviewPopup.None; Notice = "已重設草稿，請確認更動後保存"; return; }
            if (Popup == PreviewPopup.Leave)
            {
                if (Host && FailLeave) { Notice = "模擬存檔失敗，保留房間，可重試"; return; }
                Screen = PreviewScreen.Main; Popup = PreviewPopup.None; Notice = Host ? "模擬已保存並關閉房間" : "模擬已離開房間"; return;
            }
            if (Popup == PreviewPopup.Display) { Commit(pending, pendingExit); return; }
            if (Popup != PreviewPopup.Save && Popup != PreviewPopup.Unsaved) return;
            pendingExit = Popup == PreviewPopup.Unsaved;
            pending = Draft.Copy();
            if (pending.resolution != Saved.resolution || pending.fullscreen != Saved.fullscreen)
            { Popup = PreviewPopup.Display; DisplaySeconds = 15; Notice = "預覽顯示模式；不改動實際螢幕"; return; }
            Commit(pending, pendingExit);
        }
        void Commit(PreviewSettings value, bool exit)
        {
            try
            {
                if (FailSave) throw new InvalidOperationException("模擬儲存失敗");
                if (Persist != null) Persist(value.Copy());
                Saved = value.Copy(); Draft = Saved.Copy(); Popup = PreviewPopup.None;
                Notice = "預覽設定已保存"; if (exit) Screen = ReturnTo;
            }
            catch (Exception e) { Popup = exit ? PreviewPopup.Unsaved : PreviewPopup.Save; Notice = "保存失敗，草稿保留：" + e.Message; }
        }
        public void Tick(float seconds) { if (Popup == PreviewPopup.Display && (DisplaySeconds -= seconds) <= 0) RevertDisplay(); }
        void RevertDisplay() { Draft.resolution = Saved.resolution; Draft.fullscreen = Saved.fullscreen; Popup = PreviewPopup.None; Notice = "顯示模式已還原；其他草稿仍保留，請重新確認"; }
        public static string KeyLabel(KeyCode key)
        {
            switch (key) { case KeyCode.None: return "—"; case KeyCode.Space: return "Space"; case KeyCode.LeftShift: return "L Shift"; case KeyCode.LeftControl: return "L Ctrl"; case KeyCode.LeftAlt: return "L Alt"; case KeyCode.Mouse0: return "滑鼠左"; case KeyCode.Mouse1: return "滑鼠右"; case KeyCode.Mouse2: return "滑鼠中"; case KeyCode.Mouse3: return "側鍵 1"; case KeyCode.Mouse4: return "側鍵 2"; default: return key.ToString(); }
        }
    }
}
#endif
