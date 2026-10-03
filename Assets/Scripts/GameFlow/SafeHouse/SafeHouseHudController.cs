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

        private readonly List<SafeHousePlayerReadyState> readyStates =
            new List<SafeHousePlayerReadyState>(12);
        private readonly StringBuilder ledText = new StringBuilder(256);
        private NetworkRunner hudRunner;
        private int hudStageLevel;
        private int hudSceneHandle;
        private bool hasHudClaim;

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
                Input.GetKeyDown(KeyCode.Escape))
            {
                flowController.RequestCancelLocalReadyCheck();
            }
            else if (!Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked &&
                     phase != SafeHousePhase.LoadingStage &&
                     Input.GetKeyDown(KeyCode.Tab))
            {
                flowController.RequestToggleLocalReady();
            }

            int readyCount = flowController.ReadyPlayerCount;
            int connectedCount = flowController.ConnectedPlayerCount;
            int unreadyCount = Mathf.Max(0, connectedCount - readyCount);
            bool localReady = flowController.IsLocalPlayerReady;
            bool loading = phase == SafeHousePhase.LoadingStage;
            bool countingDown = phase == SafeHousePhase.Countdown;

            UpdateReadyLeds();

            if (readyStatusLabel)
            {
                string status = loading
                    ? "正在載入關卡…"
                    : countingDown
                        ? $"已準備 {readyCount}　未準備 {unreadyCount}　" +
                          $"出發 {Mathf.CeilToInt(flowController.RemainingCountdownSeconds)}　" +
                          $"投票 {Mathf.CeilToInt(flowController.RemainingReadyCheckSeconds)}"
                        : $"已準備 {readyCount}　未準備 {unreadyCount}　" +
                          $"投票 {Mathf.CeilToInt(flowController.RemainingReadyCheckSeconds)}";
                SetTextIfChanged(readyStatusLabel, status);
            }

            if (readyButton)
                readyButton.interactable = !loading;

            if (readyButtonLabel)
            {
                string prompt = loading
                    ? "載入中"
                    : localReady
                        ? "TAB  取消準備　ESC  取消投票"
                        : "TAB  準備　ESC  取消投票";
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
            if (readyButtonLabel)
                readyButtonLabel.text = string.Empty;
            if (readyButton)
                readyButton.interactable = false;

            SetReadyPanelVisible(false);
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
                        ? "<color=#53F28B>●</color>"
                        : "<color=#425269>●</color>");

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
