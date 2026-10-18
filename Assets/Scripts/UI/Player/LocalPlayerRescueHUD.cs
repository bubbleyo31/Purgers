using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>純本機救援 HUD。讀同步倒地／救援狀態，不送救援 RPC、不修改計時。</summary>
public sealed class LocalPlayerRescueHUD : MonoBehaviour
{
    [SerializeField, Tooltip("指定此 HUD 的 Runner；動態建立且只有一個觀看端時可留空。")]
    private NetworkRunner runner;
    [SerializeField, Tooltip("內容根物件；沒有倒地或可救援目標時隱藏。不可指定本元件所在根物件。")]
    private GameObject displayRoot;
    [SerializeField, Tooltip("狀態及按鍵提示文字。")]
    private TMP_Text statusText;
    [SerializeField, Tooltip("倒地剩餘秒數或救援完成秒數。")]
    private TMP_Text timeText;
    [SerializeField, Tooltip("空心圓環；使用 Filled / Radial360，0 至 1，純顯示。") ]
    private Image progressRing;
    [SerializeField, Tooltip("E 按鍵與方形背景根物件；僅救援者顯示，倒地者不顯示。")]
    private GameObject keyPrompt;

    private void Update()
    {
        if (runner == null || !runner.IsRunning)
        {
            runner = null;
            foreach (var candidate in NetworkRunner.Instances)
                if (candidate.IsRunning && candidate.ProvideInput && candidate.LocalPlayer.IsRealPlayer)
                { runner = candidate; break; }
        }
        if (runner == null || runner.IsSceneManagerBusy ||
            !runner.TryGetPlayerObject(runner.LocalPlayer, out var obj) || obj == null || !obj.IsValid ||
            !obj.TryGetComponent<PlayerHealth>(out var own) || !own.IsRescueReady || own.IsDead)
        { Show(false); return; }
        PlayerHealth target = own.IsDowned ? own : own.FindRescueCandidate();
        if (target == null) { Show(false); return; }
        Show(true);
        string interactionKey = Purgers.Settings.PlayerSettingsService.Label(Purgers.Settings.PlayerBinding.Interact);
        if (keyPrompt) { var label = keyPrompt.GetComponentInChildren<TMP_Text>(true); Purgers.Settings.PlayerSettingsService.SetPrompt(label, Purgers.Settings.PlayerBinding.Interact); }
        bool rescuing = target.Rescuer != null && target.Rescuer.IsValid;
        if (keyPrompt != null) keyPrompt.SetActive(!own.IsDowned || (own.CanSelfRescue && !rescuing));
        if (statusText != null) statusText.text = own.IsDowned
            ? (rescuing ? "正在被救援" : own.CanSelfRescue ? $"按住 {interactionKey} 自救" : "已倒地 · 等待隊友救援")
            : (rescuing ? $"救援中 · 持續按住 {interactionKey}" : $"按住 {interactionKey} 救援隊友");
        if (timeText != null) timeText.text = own.IsSelfRescuing ? $"{Mathf.Max(0f, own.SelfRescueDuration - own.SelfRescueElapsed):0.0} 秒" : rescuing
            ? $"{Mathf.Max(0f, target.RescueDurationSeconds - target.RescueElapsedSeconds):0.0} 秒"
            : $"倒地剩餘 {target.DisplayDownedSeconds:0.0} 秒";
        if (progressRing != null) progressRing.fillAmount = own.IsSelfRescuing ? own.SelfRescueProgress : target.RescueProgress;
    }

    private void Show(bool visible)
    {
        if (displayRoot != null && displayRoot != gameObject && displayRoot.activeSelf != visible)
            displayRoot.SetActive(visible);
    }
    private void OnDisable() => Show(false);
}
