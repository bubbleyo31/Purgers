using System;
using System.Linq;
using Fusion;
using Fusion.Menu;
using MultiClimb.Menu;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.SafeHouse;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace Purgers.Settings
{
    public sealed partial class PlayerSettingsMenu
    {
        [SerializeField] private FusionMenuUIMain mainScreen;
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private Vector3 audioBaselineDb;
        [SerializeField] private AudioClip sampleClip;
        [SerializeField] private AudioMixerGroup effectsGroup, musicGroup;
        public static PlayerSettingsMenu Instance { get; private set; }
        public static bool IsGameplayMenuOpen => Instance && Instance.menuLease != null;
        public static bool IsOpen => Instance && Instance.Model != null && (Instance.Model.Screen == SettingsScreen.Settings || Instance.Model.Screen == SettingsScreen.Pause);
        IDisposable menuLease;
        PlayerSettingsData initial;
        bool wasConnected;
        AudioSource audition;
        float smoothedDelta = .016f;
        int escapeFrame = -1;
        public static readonly int[] FrameLimits = { 30, 60, 75, 90, 120, 144, 165, 240, 360, -1 };
        public static string[] FrameLabels => FrameLimits.Select(v => v < 0 ? "不限" : v.ToString()).ToArray();
        public static int FrameIndex(int value) { int i = Array.IndexOf(FrameLimits, value); return i < 0 ? FrameLimits.Length - 1 : i; }
        MenuConnection Connection
        {
            get
            {
                if (!mainScreen) return null;
                var source = mainScreen.Connection;
                if (source is FusionMenuConnectionBehaviour wrapper) source = wrapper.Connection;
                return source as MenuConnection;
            }
        }
        NetworkRunner LocalRunner => Connection != null ? Connection.Runner : null;
        string RoomCode => Connection != null && Connection.IsConnected ? Connection.SessionName : "";
        public bool HasVote
        {
            get { var flow = SafeHouseFlowController.GetForRunner(LocalRunner); return flow && (flow.Phase == SafeHousePhase.ReadyCheck || flow.Phase == SafeHousePhase.Countdown); }
        }
        string[] ResolutionLabels => Screen.resolutions.Length == 0 ? new[] { Screen.width + " × " + Screen.height } : Screen.resolutions.Select(r => r.width + " × " + r.height + " @ " + Math.Round(r.refreshRateRatio.value) + " Hz").ToArray();
        string[] RegionLabels => mainScreen.Config.AvailableRegions.Select(r => string.IsNullOrEmpty(r) ? "自動（Best）" : r).ToArray();
        int MaxPlayers => mainScreen.Config.MaxPlayerCount;
        RectTransform statusPanel, fpsRow, pingRow;
        TMP_Text fpsValue, pingValue;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => Instance = null;
        void Awake() { Instance = this; }
        void Start()
        {
            if (!mainScreen || mainScreen.Config == null) { enabled = false; Debug.LogError("[PlayerSettings] 缺少正式選單設定來源。", this); return; }
            var graphics = new FusionMenuGraphicsSettings();
            initial = new PlayerSettingsData { fullscreen = graphics.Fullscreen, vsync = graphics.VSync, resolution = graphics.Resolution, quality = graphics.QualityLevel, frameLimit = graphics.Framerate,
                region = Mathf.Max(0, mainScreen.Config.AvailableRegions.IndexOf(mainScreen.ConnectionArgs.PreferredRegion)), maxPlayers = mainScreen.ConnectionArgs.MaxPlayerCount, appVersion = mainScreen.ConnectionArgs.AppVersion };
            PlayerSettingsService.Initialize(initial); ClampChoices(PlayerSettingsService.Current);
            Model = new PlayerSettingsSession { Saved = PlayerSettingsService.Current.Copy(), Screen = SettingsScreen.Main };
            Model.Draft = Model.Saved.Copy(); Model.Defaults = () => initial.Copy(); Model.CancelVote = CancelVote;
            Model.Persist = CommitSettings; Model.PreviewDisplay = PreviewDisplay; Model.RollbackDisplay = RollbackDisplay; Model.Leave = LeaveSession;
            new GraphicsAdapter(Model.Saved).Apply(); ApplyConnection(Model.Saved); ApplyAudio(Model.Saved); Build();
        }
        public void OpenFromMain()
        {
            if (Model == null || LocalPlayerControl.AllInputBlocked || Model.Busy) return;
            Model.Saved = PlayerSettingsService.Current.Copy(); Model.OpenSettings(true); Tab = 0; rebuild = true; SynchronizeOpenState();
        }
        // Called by both Update and Fusion BeforeUpdate. One physical ESC is consumed once per frame.
        public void ProcessEscape()
        {
            if (Model == null || escapeFrame == Time.frameCount || !Input.GetKeyDown(KeyCode.Escape) || Model.Busy || LocalPlayerControl.AllInputBlocked) return;
            escapeFrame = Time.frameCount;
            if (Model.Screen == SettingsScreen.Main) return;
            if (!HasVote && LocalTeamInfoPanel.CloseForEscape(LocalRunner)) return;
            var dropdown = GetComponentsInChildren<TMP_Dropdown>().FirstOrDefault(d => d.IsExpanded);
            if (Model.Capture < 0 && !HasVote && dropdown) { dropdown.Hide(); return; }
            Model.Escape(); if (Model.Popup != SettingsPopup.None) Model.Selected = 1; rebuild = true; SynchronizeOpenState();
        }
        void RefreshSession()
        {
            bool connected = Connection != null && Connection.IsConnected;
            smoothedDelta = Mathf.Lerp(smoothedDelta, Time.unscaledDeltaTime, .05f);
            if (connected != wasConnected)
            {
                wasConnected = connected; RollbackDisplay(); Model.Draft = Model.Saved.Copy(); Model.Popup = SettingsPopup.None; Model.Capture = -1; Model.Notice = "";
                Model.Screen = connected ? SettingsScreen.Game : SettingsScreen.Main; rebuild = true;
            }
            Model.Host = LocalRunner && LocalRunner.IsServer;
            ApplyAudio(Model.Screen == SettingsScreen.Settings ? Model.Draft : Model.Saved);
            if (audition && Model.Screen != SettingsScreen.Settings) audition.Stop();
            ProcessEscape(); RefreshLabels();
        }
        void SynchronizeOpenState()
        {
            if (Model == null) return;
            bool gameplayOpen = Connection != null && Connection.IsConnected && IsOpen;
            if (gameplayOpen && menuLease == null)
            {
                menuLease = LocalPlayerControl.Acquire(PlayerControlMask.Gameplay | PlayerControlMask.Movement | PlayerControlMask.Look, "Player settings menu");
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            else if (!gameplayOpen && menuLease != null)
            {
                menuLease.Dispose(); menuLease = null;
                if (Connection != null && Connection.IsConnected && !LocalPlayerControl.AllInputBlocked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }
        bool CancelVote()
        {
            if (!HasVote) return false;
            SafeHouseFlowController.GetForRunner(LocalRunner).RequestCancelLocalReadyCheck(); return true;
        }
        void ClampChoices(PlayerSettingsData data)
        {
            data.Normalize(); data.resolution = Mathf.Clamp(data.resolution, 0, Mathf.Max(0, Screen.resolutions.Length - 1));
            data.quality = Mathf.Clamp(data.quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            data.region = Mathf.Clamp(data.region, 0, Mathf.Max(0, mainScreen.Config.AvailableRegions.Count - 1));
            if (data.appVersion != mainScreen.Config.MachineId && !mainScreen.Config.AvailableAppVersions.Contains(data.appVersion)) data.appVersion = mainScreen.Config.MachineId;
            data.maxPlayers = Mathf.Clamp(data.maxPlayers, 1, MaxPlayers); data.frameLimit = FrameLimits[FrameIndex(data.frameLimit)];
        }
        void CommitSettings(PlayerSettingsData value)
        {
            ClampChoices(value);
            if (Model.Popup == SettingsPopup.Display && !Application.isEditor && Screen.resolutions.Length > 0)
            {
                var requested = Screen.resolutions[value.resolution];
                if (Screen.width != requested.width || Screen.height != requested.height || Screen.fullScreen != value.fullscreen)
                    throw new InvalidOperationException("目前顯示模式未成功套用，已還原");
            }
            PlayerSettingsService.Commit(value);
            new GraphicsAdapter(value).Apply(); ApplyConnection(value); ApplyAudio(value);
            // Retain the existing graphics owner and its startup preferences for the Fusion main menu.
            var graphics = new FusionMenuGraphicsSettings { Fullscreen = value.fullscreen, Resolution = value.resolution, Framerate = value.frameLimit, QualityLevel = value.quality, VSync = value.vsync };
            PlayerPrefs.Save();
        }
        void ApplyConnection(PlayerSettingsData value)
        {
            if (mainScreen.ConnectionArgs == null || (Connection != null && Connection.IsConnected)) return;
            if (mainScreen.Config.AvailableRegions.Count > 0) mainScreen.ConnectionArgs.PreferredRegion = mainScreen.Config.AvailableRegions[value.region];
            mainScreen.ConnectionArgs.MaxPlayerCount = value.maxPlayers;
            if (!string.IsNullOrEmpty(value.appVersion)) mainScreen.ConnectionArgs.AppVersion = value.appVersion;
        }
        void PreviewDisplay(PlayerSettingsData pending)
        {
            var display = Model.Saved.Copy(); display.resolution = pending.resolution; display.fullscreen = pending.fullscreen;
            new GraphicsAdapter(display).Apply();
        }
        void RollbackDisplay() { if (Model != null) new GraphicsAdapter(Model.Saved).Apply(); }
        void ApplyAudio(PlayerSettingsData value)
        {
            if (!audioMixer) return;
            GameplayAudioTransitionGain.ApplyUserMaster(audioMixer, VolumeDb(value.master, audioBaselineDb.x));
            audioMixer.SetFloat("PlayerEffectsVolume", VolumeDb(value.effects, audioBaselineDb.y));
            audioMixer.SetFloat("PlayerMusicVolume", VolumeDb(value.music, audioBaselineDb.z));
        }
        static float VolumeDb(int value, float baseline) => value <= 0 ? -80 : baseline + 20 * Mathf.Log10(value / 100f);
        void PlaySample(bool music)
        {
            if (!sampleClip) { Model.Notice = "尚未配置試聽音效"; return; }
            if (!audition) { audition = gameObject.AddComponent<AudioSource>(); audition.playOnAwake = false; audition.spatialBlend = 0; }
            audition.Stop(); audition.outputAudioMixerGroup = music ? musicGroup : effectsGroup; audition.PlayOneShot(sampleClip);
        }
        void BuildStatusHud()
        {
            statusPanel = Image(ui.parent, "Performance status", 0, 0, 240, 90, new Color(23 / 255f, 29 / 255f, 22 / 255f, .9f)).rectTransform;
            statusPanel.anchorMin = statusPanel.anchorMax = statusPanel.pivot = Vector2.one;
            statusPanel.anchoredPosition = new Vector2(-28, -24);
            var accent = Image(statusPanel, "Gold accent", 0, 0, 3, 90, Gold).rectTransform;
            accent.anchorMin = Vector2.zero; accent.anchorMax = new Vector2(0, 1);
            accent.offsetMin = Vector2.zero; accent.offsetMax = new Vector2(3, 0);
            fpsRow = Rect(statusPanel, "FPS row", 0, 8, 240, 36);
            pingRow = Rect(statusPanel, "Latency row", 0, 46, 240, 36);
            Text(fpsRow, "FPS", 18, 0, 72, 36, 17, Muted);
            Text(pingRow, "延遲", 18, 0, 72, 36, 17, Muted);
            fpsValue = Text(fpsRow, "", 92, 0, 130, 36, 22, Cream, TextAlignmentOptions.MidlineRight);
            pingValue = Text(pingRow, "", 92, 0, 130, 36, 22, Cream, TextAlignmentOptions.MidlineRight);
            foreach (var graphic in statusPanel.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
            RefreshStatusHud();
        }
        void RefreshStatusHud()
        {
            if (!statusPanel || Model == null) return;
            bool connected = Connection != null && Connection.IsConnected;
            bool fps = Model.Saved.fps, ping = Model.Saved.ping;
            statusPanel.gameObject.SetActive(connected && (fps || ping));
            fpsRow.gameObject.SetActive(fps); pingRow.gameObject.SetActive(ping);
            statusPanel.sizeDelta = new Vector2(240, fps && ping ? 90 : 52);
            pingRow.anchoredPosition = new Vector2(0, fps ? -46 : -8);
            if (!connected) return;
            fpsValue.text = Mathf.RoundToInt(1 / Mathf.Max(.001f, smoothedDelta)).ToString();
            pingValue.text = Connection.Ping + " ms";
        }        void BuildAppVersion(RectTransform parent, bool locked)
        {
            var versions = new[] { mainScreen.Config.MachineId }.Concat(mainScreen.Config.AvailableAppVersions).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToArray();
            if (versions.Length == 0) { Text(parent, "版本：" + mainScreen.ConnectionArgs.AppVersion, 16, 274, 960, 48, 20, Muted); return; }
            int index = Mathf.Max(0, Array.IndexOf(versions, Model.Draft.appVersion));
            var dropdown = Choice(parent, "連線版本", 260, versions, index, v => Model.Draft.appVersion = versions[v]); dropdown.interactable = !locked;
        }
        async void LeaveSession()
        {
            if (Model.Busy || Connection == null) return;
            Model.Busy = true; Model.Notice = Model.Host ? "正在保存並離開…" : "正在離開…"; rebuild = true;
            try
            {
                await Connection.LeaveAndSaveAsync();
                if (!this) return; Model.Notice = ""; Model.Popup = SettingsPopup.None; Model.Screen = SettingsScreen.Main; Model.Draft = Model.Saved.Copy();
                mainScreen.Controller.Show<FusionMenuUIMain>(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            }
            catch (Exception e) { if (this) { Model.Notice = "離開失敗：" + e.Message; Model.Popup = SettingsPopup.Leave; } }
            finally { if (this) { Model.Busy = false; rebuild = true; SynchronizeOpenState(); } }
        }
        void OnDisable() { if (!Application.isPlaying) return; menuLease?.Dispose(); menuLease = null; if (Model != null) { RollbackDisplay(); ApplyAudio(Model.Saved); } }
        void OnDestroy() { if (Instance == this) Instance = null; }
        sealed class GraphicsAdapter : FusionMenuGraphicsSettings
        {
            public override void Apply()
            {
                if (!Application.isEditor) { base.Apply(); return; }
                if (QualitySettings.GetQualityLevel() != data.quality) QualitySettings.SetQualityLevel(data.quality);
                QualitySettings.vSyncCount = data.vsync ? 1 : 0;
                Application.targetFrameRate = data.frameLimit;
            }
            readonly PlayerSettingsData data;
            public GraphicsAdapter(PlayerSettingsData data) { this.data = data; }
            public override int Framerate { get => data.frameLimit; set { } }
            public override bool Fullscreen { get => Application.isEditor ? Screen.fullScreen : data.fullscreen; set { } }
            public override int Resolution { get => data.resolution; set { } }
            public override bool VSync { get => data.vsync; set { } }
            public override int QualityLevel { get => data.quality; set { } }
        }
    }
}

