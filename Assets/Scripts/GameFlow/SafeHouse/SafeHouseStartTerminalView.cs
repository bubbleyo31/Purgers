using Purgers.Settings;
using TMPro;
using UnityEngine;

namespace Purgers.GameFlow.SafeHouse
{
    [DisallowMultipleComponent]
    public sealed class SafeHouseStartTerminalView : MonoBehaviour
    {
        [Tooltip("安全屋的 Host 權威流程控制器。")]
        [SerializeField] private SafeHouseFlowController flowController;

        [Tooltip("靠近裝置且本機是 Host 時才顯示的 World Space 提示根物件。")]
        [SerializeField] private GameObject promptRoot;

        [Tooltip("提示內容；留空時不會自動改字。")]
        [SerializeField] private TMP_Text promptLabel;

        private void Awake()
        {
            if (promptLabel)
                promptLabel.text = $"按 {PlayerSettingsService.Label(PlayerBinding.Interact)} 開始遊戲";

            SetPromptVisible(false);
        }

        private void Update()
        {
            bool canInteract =
                !PlayerSettingsMenu.IsOpen && !Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked &&
                flowController != null &&
                flowController.CanLocalHostOpenReadyCheck();

            if (promptLabel) promptLabel.text = $"按 {PlayerSettingsService.Label(PlayerBinding.Interact)} 開始遊戲";
            SetPromptVisible(canInteract);

            if (canInteract && PlayerSettingsService.Down(PlayerBinding.Interact))
                flowController.RequestOpenReadyCheck();
        }

        private void OnDisable()
        {
            SetPromptVisible(false);
        }

        private void SetPromptVisible(bool visible)
        {
            if (promptRoot && promptRoot.activeSelf != visible)
                promptRoot.SetActive(visible);
        }
    }
}
