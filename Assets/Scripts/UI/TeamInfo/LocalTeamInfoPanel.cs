using System;
using System.Collections.Generic;
using Fusion;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.SafeHouse;
using Purgers.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>每個本機 Runner 的唯讀資訊窗；只擁有 UI／游標與輸入鎖，不擁有玩法結果。</summary>
[DefaultExecutionOrder(-30000), DisallowMultipleComponent]
public sealed partial class LocalTeamInfoPanel : MonoBehaviour
{
    static readonly Dictionary<NetworkRunner, LocalTeamInfoPanel> panels = new Dictionary<NetworkRunner, LocalTeamInfoPanel>();
    public const uint CombatBindings = (1u << (int)PlayerBinding.FocusZ) | (1u << (int)PlayerBinding.FocusX) | (1u << (int)PlayerBinding.Grapple) | (1u << (int)PlayerBinding.Interact) |
        (1u << (int)PlayerBinding.Fire) | (1u << (int)PlayerBinding.Aim) | (1u << (int)PlayerBinding.Reload) |
        (1u << (int)PlayerBinding.QuickAction) | (1u << (int)PlayerBinding.Rewards);
    NetworkRunner runner;
    TeamInfoPanelSettings settings;
    readonly TeamInfoInputState input = new TeamInfoInputState();
    IDisposable lease;
    int inputFrame = -1;
    CursorLockMode previousCursor;
    bool previousCursorVisible;
    readonly List<PlayerRef> roster = new List<PlayerRef>();
    public bool IsOpen => input.IsOpen;
    public NetworkRunner Runner => runner;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => panels.Clear();
    public static void EnsureForRunner(NetworkRunner owner)
    {
        if (owner == null || panels.ContainsKey(owner)) return;
        var component = owner.gameObject.AddComponent<LocalTeamInfoPanel>();
        component.runner = owner; panels[owner] = component;
    }
    public static bool IsOpenFor(NetworkRunner owner) => owner != null && panels.TryGetValue(owner, out var panel) && panel != null && panel.IsOpen;
    public static void ProcessInput(NetworkRunner owner)
    { if (owner != null && panels.TryGetValue(owner, out var panel) && panel != null) panel.PollInput(); }
    public static bool ReadyPressed(NetworkRunner owner)
    {
        if (owner == null || !panels.TryGetValue(owner, out var panel) || panel == null)
            return PlayerSettingsService.Down(PlayerBinding.Ready);
        panel.PollInput(); return panel.input.ReadyPressed;
    }
    public static bool CloseForEscape(NetworkRunner owner)
    {
        if (owner == null || !panels.TryGetValue(owner, out var panel) || panel == null) return false;
        // PollInput and the settings menu can run in either order this frame; consume ESC once.
        if (panel.inputFrame == Time.frameCount && panel.input.EscapeClosed) return true;
        if (!panel.IsOpen) return false;
        panel.input.Close(PlayerSettingsService.Held(PlayerBinding.Ready)); panel.SynchronizeOpen();
        return true;
    }
    void Update() => PollInput();
    void PollInput()
    {
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        if (settings == null) settings = TeamInfoPanelSettings.Load();
        bool connected = runner != null && runner.IsRunning && runner.LocalPlayer.IsRealPlayer && !runner.IsSceneManagerBusy;
        var flow = connected ? SafeHouseFlowController.GetForRunner(runner) : null;
        bool vote = flow != null && (flow.Phase == SafeHousePhase.ReadyCheck || flow.Phase == SafeHousePhase.Countdown);
        bool editing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        bool eligible = connected && (vote || settings != null) && !editing && !PlayerSettingsMenu.IsOpen &&
            !PlayerControlLocks.BlocksGameplay(LocalPlayerControl.Locks.Mask);
        input.Step(PlayerSettingsService.Held(PlayerBinding.Ready), PlayerSettingsService.Down(PlayerBinding.Ready),
            PlayerSettingsService.Current.teamInfoToggle, eligible, vote, Input.GetKeyDown(KeyCode.Escape));
        SynchronizeOpen();
    }
    void SynchronizeOpen()
    {
        if (input.IsOpen && lease == null)
        {
            Build();
            previousCursor = Cursor.lockState; previousCursorVisible = Cursor.visible;
            lease = LocalPlayerControl.Acquire(PlayerControlMask.Combat | PlayerControlMask.Look, "Team information");
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        else if (!input.IsOpen && lease != null)
        {
            lease.Dispose(); lease = null;
            // 新選單／轉場擁有游標時，不用舊資訊窗狀態覆蓋它。
            if (!PlayerSettingsMenu.IsOpen && !LocalPlayerControl.AllInputBlocked && runner != null && runner.IsRunning)
            { Cursor.lockState = previousCursor; Cursor.visible = previousCursorVisible; }
        }
        if (canvasRoot != null && canvasRoot.gameObject.activeSelf != input.IsOpen) canvasRoot.gameObject.SetActive(input.IsOpen);
        if (!input.IsOpen) HideTooltip();
    }
    void LateUpdate()
    {
        if (!IsOpen || canvasRoot == null) return;
        var logic = GameLogic.GetPrimaryForRunner(runner);
        if (logic == null || !logic.IsTeammateRosterReady) { ClearRows(); return; }
        roster.Clear();
        foreach (var player in runner.ActivePlayers) roster.Add(player);
        roster.Sort((a,b) => a == runner.LocalPlayer ? (b == a ? 0 : -1) : b == runner.LocalPlayer ? 1 : a.RawEncoded.CompareTo(b.RawEncoded));
        countLabel.text = Math.Min(4, roster.Count) + " / 4 人";
        for (int i=0; i<4; i++) PresentRow(rows[i], i < roster.Count ? roster[i] : PlayerRef.None, logic);
        UpdateTooltip();
    }
    void OnDisable() { input.Close(true); SynchronizeOpen(); }
    void OnDestroy()
    {
        if (runner != null) panels.Remove(runner);
        lease?.Dispose(); lease = null;
    }
}
