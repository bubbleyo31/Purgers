using TMPro;
using UnityEngine;
using Purgers.Settings;

public sealed partial class LocalPlayerBattleAbilityHUD
{
    [Header("ALT 技能管理")]
    [SerializeField, Tooltip("包含技能格與框架背景的共同容器。留空時使用槽位容器；由 Editor 遷移工具配置正式共同框架。")]
    private RectTransform focusManagementRoot;
    [SerializeField, Range(1f, 2f)] private float focusManagementScale = 1.18f;
    [SerializeField, Min(.01f)] private float focusManagementTransitionSeconds = .15f;
    [SerializeField] private Vector2 focusManagementHintOffset = new Vector2(-50, 103);
    [SerializeField] private Color focusManagementHintColor = new Color32(120, 238, 35, 255);
    private RectTransform scaledFocusRoot;
    private Vector3 authoredFocusScale;
    private TMP_Text focusDropHint;
    private HudKeyPromptRow focusDropPromptRow;
    private CanvasGroup focusDropHintGroup;
    private float focusScaleWeight;

    private void BindEmptyFocusView(SlotView view, int slot)
    {
        view.Root.SetActive(true); view.Pulse.Reset();
        if (view.Icon != null) view.Icon.enabled = false;
        if (view.Fallback != null) view.Fallback.gameObject.SetActive(false);
        if (view.CooldownText != null) view.CooldownText.gameObject.SetActive(false);
        if (view.StateLabel != null) view.StateLabel.gameObject.SetActive(false);
        view.StateGraphic.SetState(0, 0, 0, stateBorderWidth, cooldownShadeColor, recastBorderColor, readyFlashColor);
        if (view.Background != null) view.Background.color = slotUnavailableColor;
        KeyCode key = PlayerSettingsService.Key(slot == 1 ? PlayerBinding.FocusZ : slot == 2 ? PlayerBinding.FocusX : PlayerBinding.Interact);
        view.PromptLayout?.Refresh(key); view.PromptLayout?.SetState(false, true);
    }
    private void RefreshFocusManagementPresentation()
    {
        var target = focusManagementRoot != null ? focusManagementRoot : slotsRoot;
        if (target == null) return;
        if (scaledFocusRoot != target)
        {
            ResetFocusManagementPresentation();
            scaledFocusRoot = target; authoredFocusScale = target.localScale;
        }
        bool managing = !observing && inputManager != null && inputManager.IsFocusManagementHeld;
        float desired = managing ? 1f : 0f;
        focusScaleWeight = Mathf.MoveTowards(focusScaleWeight, desired, Time.unscaledDeltaTime / Mathf.Max(.01f, focusManagementTransitionSeconds));
        float eased = Mathf.SmoothStep(0f, 1f, focusScaleWeight);
        target.localScale = authoredFocusScale * Mathf.Lerp(1f, focusManagementScale, eased);
        if (focusDropHint == null && slotTemplate != null)
        {
            var original = slotTemplate.GetComponentInChildren<TMP_Text>(true);
            if (original != null)
            {
                focusDropHint = Instantiate(original, target);
                focusDropHint.name = "FocusDropHint";
                focusDropHint.fontSize = 14f; focusDropHint.enableAutoSizing = true;
                focusDropHint.fontSizeMin = 10f; focusDropHint.fontSizeMax = 14f;
                focusDropHint.alignment = TextAlignmentOptions.Center;
                focusDropHint.raycastTarget = false;
                focusDropHint.color = focusManagementHintColor;
                var rect = focusDropHint.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f); rect.pivot = new Vector2(.5f, 1f);
                rect.sizeDelta = new Vector2(320, 26); rect.anchoredPosition = focusManagementHintOffset;
                rect.localScale = Vector3.one;
                var layout = focusDropHint.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                layout.ignoreLayout = true;
                focusDropHintGroup = focusDropHint.gameObject.AddComponent<CanvasGroup>();
                focusDropHintGroup.blocksRaycasts = false;
                focusDropHintGroup.interactable = false;
                focusDropPromptRow = new HudKeyPromptRow(focusDropHint, true);
            }
        }
        if (focusDropHint != null)
        {
            bool replacing = inputManager != null && inputManager.FocusSlotSelectionPending;
            focusDropHint.gameObject.SetActive(focusScaleWeight > 0f && !replacing);
            if (focusDropHint.gameObject.activeSelf)
            {
                KeyCode modifier = inputManager != null ? inputManager.RewardHoldKey : PlayerSettingsService.Key(PlayerBinding.Rewards);
                focusDropPromptRow.RefreshWithPrefix(modifier, "按住", "+ 技能鍵 丟棄", false, 14f, true);
            }
            focusDropHintGroup.alpha = eased;
        }
    }
    private void ResetFocusManagementPresentation()
    {
        if (scaledFocusRoot != null) scaledFocusRoot.localScale = authoredFocusScale;
        focusScaleWeight = 0f;
        if (focusDropHint != null) focusDropHint.gameObject.SetActive(false);
    }
}
