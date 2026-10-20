using System;
using System.Linq;
using UnityEngine;
namespace Purgers.Settings
{
    public enum SettingsScreen { Game, Pause, Settings, Main }
    public enum SettingsPopup { None, Save, Unsaved, Reset, Leave, Display }

    public sealed class PlayerSettingsSession
    {
        public static readonly string[] Actions = { "向前移動", "向後移動", "向左移動", "向右移動", "跳躍", "跑步", "蹲下／滑鏟", "鈎索／Q 能力", "技能／互動／救援", "瞄準", "主武器攻擊", "換彈", "武器特殊動作", "獎勵選擇", "展開小地圖", "隊伍資訊／準備切換", "專注技能 Z 槽", "專注技能 X 槽" };
        public PlayerSettingsData Saved = new PlayerSettingsData();
        public PlayerSettingsData Draft;
        public SettingsScreen Screen = SettingsScreen.Game;
        public SettingsScreen ReturnTo = SettingsScreen.Pause;
        public SettingsPopup Popup;
        public bool Host = true, Busy; public Func<bool> CancelVote; public Action Leave; public Action<PlayerSettingsData> PreviewDisplay; public Action RollbackDisplay; public Func<PlayerSettingsData> Defaults;
        public int Capture = -1, Selected;
        public string Notice = "";
        public float DisplaySeconds;
        public Action<PlayerSettingsData> Persist;
        PlayerSettingsData pending;
        bool pendingExit;
        public PlayerSettingsSession() { Draft = Saved.Copy(); }
        public bool Dirty { get { return !Draft.Same(Saved); } }
        public int[] Unbound { get { return Enumerable.Range(0, Draft.keys.Length).Where(i => Draft.keys[i] == KeyCode.None).ToArray(); } }
        public void OpenSettings(bool main = false) { ReturnTo = main ? SettingsScreen.Main : SettingsScreen.Pause; Draft = Saved.Copy(); Screen = SettingsScreen.Settings; Notice = ""; }
        public void OpenPause() { Screen = SettingsScreen.Pause; Selected = 0; Notice = ""; }
        public void Bind(KeyCode key)
        {
            if (Capture < 0 || key == KeyCode.None) return;
            if (key == KeyCode.Escape) { Capture = -1; return; }
            int target = Capture;
            for (int i = 0; i < Draft.keys.Length; i++)
                if (i != target && Draft.keys[i] == key) Draft.keys[i] = KeyCode.None;
            Draft.Bind((PlayerBinding)target, key); Capture = -1;
            Notice = Unbound.Length > 0 ? "按鍵已移轉；請注意紅色未配置項目" : "按鍵提示已同步，確認後保存";
        }
        public void Escape()
        {
            if (Capture >= 0) { Capture = -1; Notice = "已取消改鍵"; return; }
            if (CancelVote != null && CancelVote()) { Notice = "已取消投票"; return; }
            if (Popup == SettingsPopup.Display) { RevertDisplay(); return; }
            if (Popup != SettingsPopup.None) { Popup = SettingsPopup.None; return; }
            if (Screen == SettingsScreen.Settings) Back();
            else if (Screen == SettingsScreen.Pause) Screen = SettingsScreen.Game;
            else if (Screen == SettingsScreen.Game) OpenPause();
        }
        public void Back() { if (Dirty) Popup = SettingsPopup.Unsaved; else Screen = ReturnTo; }
        public void RequestSave() { if (Dirty && Capture < 0) Popup = SettingsPopup.Save; }
        public void No()
        {
            if (Popup == SettingsPopup.Display) { RevertDisplay(); return; }
            if (Popup == SettingsPopup.Unsaved) { Draft = Saved.Copy(); Screen = ReturnTo; }
            Popup = SettingsPopup.None;
        }
        public void Yes()
        {
            if (Popup == SettingsPopup.Reset) { Draft = Defaults != null ? Defaults() : new PlayerSettingsData(); Popup = SettingsPopup.None; Notice = "已重設草稿，請確認更動後保存"; return; }
            if (Popup == SettingsPopup.Leave)
            {
                Leave?.Invoke(); return;
            }
            if (Popup == SettingsPopup.Display) { Commit(pending, pendingExit); return; }
            if (Popup != SettingsPopup.Save && Popup != SettingsPopup.Unsaved) return;
            pendingExit = Popup == SettingsPopup.Unsaved;
            pending = Draft.Copy();
            if (pending.resolution != Saved.resolution || pending.fullscreen != Saved.fullscreen)
            { try { PreviewDisplay?.Invoke(pending); Popup = SettingsPopup.Display; DisplaySeconds = 15; Notice = "請確認畫面正常後選擇保留"; } catch (Exception e) { RollbackDisplay?.Invoke(); Notice = "顯示套用失敗：" + e.Message; } return; }
            Commit(pending, pendingExit);
        }
        void Commit(PlayerSettingsData value, bool exit)
        {
            try
            {
                
                if (Persist != null) Persist(value.Copy());
                Saved = value.Copy(); Draft = Saved.Copy(); Popup = SettingsPopup.None;
                Notice = "設定已保存"; if (exit) Screen = ReturnTo;
            }
            catch (Exception e) { RollbackDisplay?.Invoke(); Popup = exit ? SettingsPopup.Unsaved : SettingsPopup.Save; Notice = "保存失敗，草稿保留：" + e.Message; }
        }
        public void Tick(float seconds) { if (Popup == SettingsPopup.Display && (DisplaySeconds -= seconds) <= 0) RevertDisplay(); }
        void RevertDisplay() { RollbackDisplay?.Invoke(); Draft.resolution = Saved.resolution; Draft.fullscreen = Saved.fullscreen; Popup = SettingsPopup.None; Notice = "顯示模式已還原；其他草稿仍保留，請重新確認"; }
        public static string KeyLabel(KeyCode key)
        {
            switch (key) { case KeyCode.None: return "—"; case KeyCode.Space: return "Space"; case KeyCode.LeftShift: return "L Shift"; case KeyCode.LeftControl: return "L Ctrl"; case KeyCode.LeftAlt: return "L Alt"; case KeyCode.Mouse0: return "滑鼠左"; case KeyCode.Mouse1: return "滑鼠右"; case KeyCode.Mouse2: return "滑鼠中"; case KeyCode.Mouse3: return "側鍵 1"; case KeyCode.Mouse4: return "側鍵 2"; default: return key.ToString(); }
        }
    }
}
