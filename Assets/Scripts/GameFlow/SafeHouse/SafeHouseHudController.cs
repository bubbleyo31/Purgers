using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Purgers.GameFlow.SafeHouse
{
    [DisallowMultipleComponent]
    public sealed class SafeHouseHudController : MonoBehaviour
    {
        [Tooltip("安全屋流程控制器。")]
        [SerializeField] private SafeHouseFlowController flowController;

        [Header("Stage HUD")]
        [Tooltip("持續顯示目前關卡等級的 TMP。")]
        [SerializeField] private TMP_Text stageLevelLabel;

        [Header("Ready Check")]
        [Tooltip("Ready Check 開啟後顯示的最上層面板。")]
        [SerializeField] private GameObject readyPanelRoot;

        [SerializeField] private TMP_Text readyStatusLabel;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonLabel;

        private void Awake()
        {
            if (readyButton)
                readyButton.onClick.AddListener(OnReadyPressed);

            if (readyPanelRoot)
                readyPanelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (readyButton)
                readyButton.onClick.RemoveListener(OnReadyPressed);
        }

        private void Update()
        {
            if (!flowController || !flowController.IsNetworkReady)
            {
                if (readyPanelRoot)
                    readyPanelRoot.SetActive(false);

                return;
            }

            if (stageLevelLabel)
            {
                stageLevelLabel.text =
                    $"關卡等級  {Mathf.Max(1, flowController.StageLevel)}";
            }

            SafeHousePhase phase = flowController.Phase;
            bool showReadyPanel =
                phase == SafeHousePhase.ReadyCheck ||
                phase == SafeHousePhase.LoadingStage;

            if (readyPanelRoot &&
                readyPanelRoot.activeSelf != showReadyPanel)
            {
                readyPanelRoot.SetActive(showReadyPanel);
            }

            if (!showReadyPanel)
                return;

            int readyCount = flowController.ReadyPlayerCount;
            int connectedCount = flowController.ConnectedPlayerCount;
            bool localReady = flowController.IsLocalPlayerReady;
            bool loading = phase == SafeHousePhase.LoadingStage;

            if (readyStatusLabel)
            {
                readyStatusLabel.text = loading
                    ? "全員準備完成，正在載入關卡…"
                    : $"準備人數  {readyCount} / {connectedCount}";
            }

            if (readyButton)
                readyButton.interactable = !loading && !localReady;

            if (readyButtonLabel)
            {
                readyButtonLabel.text = loading
                    ? "載入中"
                    : localReady
                        ? "已準備"
                        : "確認準備";
            }
        }

        private void OnReadyPressed()
        {
            if (flowController)
                flowController.RequestLocalReady();
        }
    }
}
