using Purgers.Settings;
using System.Collections.Generic;
using System.Text;
using Fusion;
using Purgers.GameFlow.Stage;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Purgers.GameFlow.SafeHouse
{
    [DisallowMultipleComponent]
    public sealed class SafeHouseHudController : MonoBehaviour
    {
        [Tooltip("安全屋流程控制器。")]
        [SerializeField] private SafeHouseFlowController flowController;

        [Header("關卡抬頭顯示")]
        [Tooltip("持續顯示目前關卡等級的 TMP。")]
        [SerializeField] private TMP_Text stageLevelLabel;

        [Tooltip("第二列的目前任務；安全屋沒有關卡計時，不顯示假倒數。")]
        [SerializeField] private TMP_Text objectiveLabel;

        [Header("準備確認")]
        [Tooltip("Ready Check 開啟後顯示的最上層面板。")]
        [SerializeField] private GameObject readyPanelRoot;

        [Tooltip("依連線玩家順序顯示一顆 LED；亮燈代表已準備。")]
        [SerializeField] private TMP_Text readyLedLabel;
        [SerializeField] private TMP_Text readyStatusLabel;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;

        [Header("新版投票呈現（可選）")]
        [Tooltip("獨立顯示投票／出發秒數。指定後，狀態列只顯示人數，準備按鈕文字不再重複 TAB／ESC；鍵帽由場景配置。留空沿用舊版合併文字。")]
        [SerializeField] private TMP_Text readyTimerLabel;

        private readonly List<SafeHousePlayerReadyState> readyStates =
            new List<SafeHousePlayerReadyState>(12);
        private readonly StringBuilder ledText = new StringBuilder(256);
        private NetworkRunner hudRunner;
        private int hudStageLevel;
        private int hudSceneHandle;
        private bool hasHudClaim;
        private HudKeyPromptLayout readyPrompt, cancelPrompt;

        /// <summary>子層玩家 HUD 共用安全屋的 Runner 與生命週期，不另搶 HUD ownership。</summary>
        public NetworkRunner HudRunner => hasHudClaim ? hudRunner : null;

        private void Awake()
        {
            hudSceneHandle = gameObject.scene.handle;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            if (readyButton)
                readyButton.onClick.AddListener(OnReadyPressed);

            if (readyPanelRoot)
                readyPanelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            StageHudLifetimeRegistry.Release(hudRunner, this);
            ClearPresentation();

            if (readyButton)
                readyButton.onClick.RemoveListener(OnReadyPressed);
        }

        private void Update()
        {
            if (!TryMaintainStageOwnership())
            {
                return;
            }

            if (stageLevelLabel)
            {
                stageLevelLabel.text =
                    Purgers.GameFlow.Stage.StageHudText.BuildStageHeader(
                        flowController.StageLevel,
                        flowController.CycleStage,
                        flowController.CycleLength,
                        flowController.IsBossStage);
            }

            SafeHousePhase phase = flowController.Phase;
            bool showReadyPanel = ShouldShowReadyPanel(phase);

            if (objectiveLabel)
                SetTextIfChanged(objectiveLabel, showReadyPanel
                    ? "確認準備，前往下一關"
                    : phase == SafeHousePhase.LoadingStage
                        ? "正在前往關卡…"
                        : "由房主前往裝置開始任務");

            SetReadyPanelVisible(showReadyPanel);

            if (!showReadyPanel)
                return;

            if (!Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked &&
                phase != SafeHousePhase.LoadingStage &&
                PlayerSettingsMenu.Instance == null && Input.GetKeyDown(KeyCode.Escape))
            {
                flowController.RequestCancelLocalReadyCheck();
            }
            else if (!Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked &&
                     phase != SafeHousePhase.LoadingStage &&
                     !PlayerSettingsMenu.IsOpen && !Input.GetKeyDown(KeyCode.Escape) && LocalTeamInfoPanel.ReadyPressed(flowController.Runner))
            {
                flowController.RequestToggleLocalReady();
            }

            int readyCount = flowController.ReadyPlayerCount;
            int connectedCount = flowController.ConnectedPlayerCount;
            UpdateReadyLeds();
            RenderReadyPresentation(phase, readyCount, connectedCount,
                flowController.IsLocalPlayerReady,
                phase == SafeHousePhase.Countdown
                    ? flowController.RemainingCountdownSeconds
                    : flowController.RemainingReadyCheckSeconds);
        }

        // Presentation only: the caller supplies the existing authoritative timer snapshot.
        private void RenderReadyPresentation(SafeHousePhase phase, int readyCount,
            int connectedCount, bool localReady, float remainingSeconds)
        {
            int unreadyCount = Mathf.Max(0, connectedCount - readyCount);
            bool loading = phase == SafeHousePhase.LoadingStage;
            bool countingDown = phase == SafeHousePhase.Countdown;
            int seconds = Mathf.CeilToInt(remainingSeconds);
            string timerTitle = countingDown ? "出發" : "投票";

            if (readyTimerLabel)
                SetTextIfChanged(readyTimerLabel, loading ? "載入中" :
                    $"{timerTitle} <color=#E5C45E>{seconds}</color> 秒");

            if (readyStatusLabel)
            {
                string status = loading
                    ? "正在載入關卡…"
                    : $"已準備 {readyCount}　未準備 {unreadyCount}" +
                      (readyTimerLabel ? string.Empty : $"　{timerTitle} {seconds}");
                SetTextIfChanged(readyStatusLabel, status);
            }

            if (readyButton)
                readyButton.interactable = !loading;

            if (readyButton)
            {
                RefreshKeyPrompts();
            }

            if (readyButtonLabel)
            {
                string prompt = loading
                    ? "載入中"
                    : readyTimerLabel
                        ? (localReady ? "取消準備" : "準備")
                        : localReady
                        ? $"{PlayerSettingsService.Label(PlayerBinding.Ready)}  取消準備　ESC  取消投票"
                        : $"{PlayerSettingsService.Label(PlayerBinding.Ready)}  準備　ESC  取消投票";
                SetTextIfChanged(readyButtonLabel, prompt);
            }
        }

        private bool TryMaintainStageOwnership()
        {
            if (!flowController || !flowController.IsNetworkReady || !flowController.Runner)
            {
                if (hasHudClaim)
                    ClearAndDestroy();
                else
                    ClearPresentation();

                return false;
            }

            NetworkRunner currentRunner = flowController.Runner;
            int currentStageLevel = flowController.StageLevel;
            if (!hasHudClaim)
            {
                hudRunner = currentRunner;
                hudStageLevel = currentStageLevel;
                hasHudClaim = true;
                StageHudLifetimeRegistry.Claim(hudRunner, hudStageLevel, this);
            }
            else if (hudRunner != currentRunner || hudStageLevel != currentStageLevel)
            {
                ClearAndDestroy();
                return false;
            }

            return StageHudLifetimeRegistry.IsOwner(
                hudRunner,
                hudStageLevel,
                this);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            // Fusion can migrate this HUD before unloading its original scene.
            if (scene.handle == hudSceneHandle && gameObject.scene.handle == scene.handle)
                ClearAndDestroy();
        }

        private void OnDisable()
        {
            ClearPresentation();
        }

        private void ClearAndDestroy()
        {
            ClearPresentation();
            StageHudLifetimeRegistry.Release(hudRunner, this);
            hasHudClaim = false;

            if (gameObject.activeSelf)
                gameObject.SetActive(false);

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        private void ClearPresentation()
        {
            if (stageLevelLabel)
                stageLevelLabel.text = string.Empty;
            if (objectiveLabel)
                objectiveLabel.text = string.Empty;
            if (readyLedLabel)
                readyLedLabel.text = string.Empty;
            if (readyStatusLabel)
                readyStatusLabel.text = string.Empty;
            if (readyTimerLabel)
                readyTimerLabel.text = string.Empty;
            if (readyButton)
            {
                RefreshKeyPrompts();
            }

            if (readyButtonLabel)
                readyButtonLabel.text = string.Empty;
            if (readyButton)
                readyButton.interactable = false;

            SetReadyPanelVisible(false);
        }

        private void RefreshKeyPrompts()
        {
            if (readyPrompt == null && readyButton != null)
            {
                var cap = readyButton.transform.Find("Keycap");
                if (cap != null) readyPrompt = new HudKeyPromptLayout(cap.GetComponentInChildren<TMP_Text>(true), cap.GetComponent<Image>(), null);
            }
            if (cancelPrompt == null && readyPanelRoot != null)
            {
                var cap = readyPanelRoot.transform.Find("CancelKeycap");
                if (cap != null) cancelPrompt = new HudKeyPromptLayout(cap.GetComponentInChildren<TMP_Text>(true), cap.GetComponent<Image>(), null);
            }
            var key = PlayerSettingsService.Key(PlayerBinding.Ready);
            readyPrompt?.Refresh(key);
            readyPrompt?.SetState(Input.GetKey(key), key == KeyCode.None);
            cancelPrompt?.Refresh(KeyCode.Escape);
            cancelPrompt?.SetState(Input.GetKey(KeyCode.Escape));
            if (readyButton != null && readyPrompt != null)
                HudKeyPromptLayout.PlaceDescriptionAfter(readyButtonLabel, readyButton.transform.Find("Keycap").GetComponent<Image>());
            if (readyPanelRoot != null && cancelPrompt != null)
            {
                var hint = readyPanelRoot.transform.Find("CancelHint");
                if (hint != null) HudKeyPromptLayout.PlaceDescriptionAfter(hint.GetComponent<TMP_Text>(), readyPanelRoot.transform.Find("CancelKeycap").GetComponent<Image>());
            }
        }

        private void OnReadyPressed()
        {
            if (flowController)
                flowController.RequestToggleLocalReady();
        }

        private static bool ShouldShowReadyPanel(SafeHousePhase phase)
        {
            return phase == SafeHousePhase.ReadyCheck ||
                   phase == SafeHousePhase.Countdown;
        }

        private void SetReadyPanelVisible(bool visible)
        {
            if (readyPanelRoot && readyPanelRoot.activeSelf != visible)
                readyPanelRoot.SetActive(visible);
        }

        private void UpdateReadyLeds()
        {
            if (!readyLedLabel)
                return;

            flowController.CopyConnectedReadyStates(readyStates);
            ledText.Clear();
            ledText.Append("玩家  ");

            for (int i = 0; i < readyStates.Count; i++)
            {
                ledText.Append(
                    readyStates[i].IsReady
                        ? (readyTimerLabel ? "<color=#83CC72>●</color>" : "<color=#53F28B>●</color>")
                        : (readyTimerLabel ? "<color=#B2B6AE>○</color>" : "<color=#425269>●</color>"));

                if (i + 1 < readyStates.Count)
                    ledText.Append("  ");
            }

            SetTextIfChanged(readyLedLabel, ledText.ToString());
        }

        private static void SetTextIfChanged(TMP_Text label, string value)
        {
            if (label.text != value)
                label.text = value;
        }
    }
}
