using Fusion;
using Purgers.GameFlow.Control;
using TMPro;
using UnityEngine;

public sealed partial class LocalPlayerBattleAbilityHUD
{
    [Header("技能格冷卻與二次操作提示")]
    [SerializeField, Tooltip("整格半透明冷卻遮罩。從十二點順時針退去；不影響技能時間。")]
    private Color cooldownShadeColor = new Color(0f, 0f, 0f, .65f);
    [SerializeField, Tooltip("可再次施放的期限外框，預設淡金色。")]
    private Color recastBorderColor = new Color32(229, 206, 115, 255);
    [SerializeField, Tooltip("效果持續外框，預設青色；再次結束只用在原本支援的技能。")]
    private Color activeBorderColor = new Color(.25f, 1f, .9f, 1f);
    [SerializeField, Tooltip("冷卻完成時的短促亮光顏色。")]
    private Color readyFlashColor = new Color(1f, .96f, .75f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("冷卻完成亮光秒數，預設 0.32；0 停用。不因開窗、換裝或切換觀戰補播。")]
    private float readyFlashSeconds = .32f;
    [SerializeField, Range(1f, 8f), Tooltip("期限外框寬度，以技能格參考像素計算。")]
    private float stateBorderWidth = 3f;
    private NetworkObject viewedPlayer;

    private bool IsLocalSlotInputBlocked(PlayerAbilityRuntime runtime)
    {
        if (observing) return false;
        PlayerControlMask mask = LocalPlayerControl.Locks.Mask;
        if (PlayerControlLocks.BlocksCombat(mask)) return true;
        // Movement locks separately prevent the second Blink press. UI never adds a new gameplay lock.
        return PlayerControlLocks.BlocksMovement(mask) && runtime.TryGetModule<PlayerBlinkAbility>(out var blink) && blink.IsAbilityActive;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * .5f; rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void PrepareSlotState(SlotView view)
    {
        if (view.Icon != null) view.IconColor = view.Icon.color;
        if (view.CooldownShade != null) view.CooldownShade.gameObject.SetActive(false);
        var overlay = new GameObject("AbilityStateOverlay", typeof(RectTransform), typeof(UnityEngine.CanvasRenderer), typeof(AbilitySlotStateGraphic));
        overlay.transform.SetParent(view.Root.transform, false);
        Stretch((RectTransform)overlay.transform);
        view.StateGraphic = overlay.GetComponent<AbilitySlotStateGraphic>();
        view.StateGraphic.raycastTarget = false;
        if (view.CooldownText == null) return;
        Stretch(view.CooldownText.rectTransform);
        view.CooldownText.alignment = TextAlignmentOptions.Center;
        view.CooldownText.color = Color.white;
        view.CooldownText.raycastTarget = false;
        view.CooldownText.transform.SetAsLastSibling();
        var labelRoot = new GameObject("AbilityStateLabel", typeof(RectTransform));
        labelRoot.transform.SetParent(view.Root.transform, false);
        var label = labelRoot.AddComponent<TextMeshProUGUI>();
        label.font = view.CooldownText.font;
        label.fontSharedMaterial = view.CooldownText.fontSharedMaterial;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = 14f; label.fontSizeMin = 10f; label.fontSizeMax = 14f; label.enableAutoSizing = true;
        label.enableWordWrapping = false; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        label.richText = false;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = new Vector2(-.2f, 1f); rect.anchorMax = new Vector2(1.2f, 1f);
        rect.pivot = new Vector2(.5f, 0f); rect.sizeDelta = new Vector2(0f, 20f); rect.anchoredPosition = new Vector2(0f, 4f);
        view.StateLabel = label;
    }

    private void PresentSlotState(SlotView view, AbilitySlotPresentationState state, object identity)
    {
        bool cooling = state.Phase == AbilitySlotVisualPhase.Cooling;
        bool ready = state.Phase == AbilitySlotVisualPhase.Ready && state.CanPress;
        float flash = view.Pulse.Sample(identity, state.Cooling, ready, Time.unscaledTime, readyFlashSeconds);
        float border = state.Phase == AbilitySlotVisualPhase.Recast || state.Phase == AbilitySlotVisualPhase.Active ||
            state.Phase == AbilitySlotVisualPhase.Casting ? state.PhaseRatio : 0f;
        Color tint = state.Phase == AbilitySlotVisualPhase.Recast ? recastBorderColor :
            state.Phase == AbilitySlotVisualPhase.Casting ? castingBarColor : activeBorderColor;
        if (state.Phase == AbilitySlotVisualPhase.Recast && !state.CanPress) tint = slotUnavailableColor;
        view.StateGraphic.SetState(cooling ? state.CooldownRatio : 0f, border, flash, stateBorderWidth, cooldownShadeColor, tint, readyFlashColor);
        if (view.CooldownText != null)
        {
            view.CooldownText.gameObject.SetActive(cooling);
            if (cooling) view.CooldownText.SetText("{0}", Mathf.CeilToInt(state.CooldownRemaining));
        }
        if (view.StateLabel != null)
        {
            view.StateLabel.gameObject.SetActive(!string.IsNullOrEmpty(state.Label));
            view.StateLabel.text = state.Label; view.StateLabel.color = tint;
        }
        if (view.Icon != null)
            view.Icon.color = state.Phase == AbilitySlotVisualPhase.Unavailable ?
                new Color(view.IconColor.r, view.IconColor.g, view.IconColor.b, view.IconColor.a * .45f) : view.IconColor;
    }
}
