using System.Collections.Generic;
using Fusion;
using Purgers.GameFlow.Stage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只讀本機玩家的 Loadout 與鈎索充能，呈現戰鬥 HUD。</summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerBattleAbilityHUD : MonoBehaviour
{
    [SerializeField] private RectTransform slotsRoot;
    [SerializeField] private GameObject slotTemplate;
    // Retained for old prefab serialization; both categories now share the center.
    [SerializeField, HideInInspector] private Vector2 focusIconOffset;
    [SerializeField] private Vector2 focusKeyOffset;
    [SerializeField] private Image grappleFill;
    [SerializeField] private GameObject emptyGrappleMark;
    [SerializeField] private TMP_Text grappleKeyLabel;
    [SerializeField] private Image grappleKeyBackground;
    [SerializeField] private CanvasGroup visualGroup;

    [Header("中央下方技能時間條")]
    [SerializeField, Tooltip("技能時間條根物件；未接線時只保留既有技能槽。顯示施法延遲、閃現待命及自身效果時間，不顯示地上治療包或衝刺安全期限。")]
    private GameObject timedAbilityRoot;
    [SerializeField, Tooltip("剩餘時間填色。使用 Simple Image 及水平錨點比例，不依賴 Sprite 的 Filled 模式；子物件需使用左側 pivot。")]
    private Image timedAbilityFill;
    [SerializeField, Tooltip("技能名稱文字；讀目前裝備 Definition 的顯示名稱。")]
    private TMP_Text timedAbilityName;
    [SerializeField, Tooltip("狀態文字：施放準備、待命或效果持續。")]
    private TMP_Text timedAbilityState;
    [SerializeField, Tooltip("剩餘秒數文字，以一位小數呈現。")]
    private TMP_Text timedAbilitySeconds;
    [SerializeField, Tooltip("施法延遲的填色。僅改外觀，不改施法時間。")]
    private Color castingBarColor = new Color(1f, .78f, .2f, 1f);
    [SerializeField, Tooltip("閃現待命的填色。僅改外觀，不改第二次 E 的期限。")]
    private Color armedBarColor = new Color(.3f, .8f, 1f, 1f);
    [SerializeField, Tooltip("自己持續效果的填色。僅改外觀，不改效果或冷卻。")]
    private Color activeBarColor = new Color(.25f, 1f, .9f, 1f);

    [Header("已確認技能槽配色")]
    [SerializeField, Tooltip("可用技能與未按下鍵帽的暖米白底色。")]
    private Color slotReadyColor = new Color32(241, 237, 217, 255);
    [SerializeField, Tooltip("按住對應按鍵時的柔黃底色。")]
    private Color slotPressedColor = new Color32(213, 196, 94, 255);
    [SerializeField, Tooltip("技能不可用時的低明度底色；不改技能資格。")]
    private Color slotUnavailableColor = new Color32(111, 115, 99, 255);
    private readonly List<SlotView> views = new List<SlotView>();
    private StageHudController stageHud;
    private InputManager inputManager;

    private sealed class SlotView
    {
        public GameObject Root;
        public Image Background;
        public Image Icon;
        public TMP_Text Fallback;
        public Image CooldownShade;
        public TMP_Text CooldownText;
        public TMP_Text KeyText;
        public Image KeyBackground;
        public Vector2 KeyPosition;
    }

    private void Awake()
    {
        stageHud = GetComponentInParent<StageHudController>();
        if (slotTemplate != null) slotTemplate.SetActive(false);
        if (grappleFill != null)
        {
            grappleFill.type = Image.Type.Filled;
            grappleFill.fillMethod = Image.FillMethod.Radial360;
            grappleFill.fillClockwise = true;
            grappleFill.fillOrigin = (int)Image.Origin360.Top;
        }
        Hide();
    }

    private void Update()
    {
        NetworkRunner runner = stageHud != null ? stageHud.HudRunner : null;
        if (runner == null || !runner.IsRunning || !runner.LocalPlayer.IsRealPlayer ||
            !runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject playerObject) ||
            playerObject == null || !playerObject.IsValid)
        {
            Hide();
            return;
        }

        PlayerGrappleCharges charges = playerObject.GetComponent<PlayerGrappleCharges>();
        PlayerAbilityRuntimeManager manager = playerObject.GetComponent<PlayerAbilityRuntimeManager>();
        if (charges == null || manager == null)
        {
            Hide();
            return;
        }

        if (visualGroup != null) visualGroup.alpha = 1f;
        if (inputManager == null) inputManager = runner.GetComponent<InputManager>();
        RefreshGrapple(charges);
        RefreshSlots(manager);
        RefreshTimedAbility(playerObject.GetComponent<Player>(), manager);
    }

    private void Hide()
    {
        if (timedAbilityRoot != null) timedAbilityRoot.SetActive(false);
        if (visualGroup != null) visualGroup.alpha = 0f;
        foreach (SlotView view in views)
            if (view.Root != null) view.Root.SetActive(false);
    }

    private void RefreshTimedAbility(Player player, PlayerAbilityRuntimeManager manager)
    {
        if (timedAbilityRoot == null) return;
        if (player == null || player.Health == null || !player.Health.IsAlive)
        {
            timedAbilityRoot.SetActive(false);
            return;
        }
        for (int slot = 0; slot < PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; slot++)
        {
            PlayerAbilityRuntime runtime = manager.GetEquippedRuntimeAtSlot(slot);
            if (runtime == null || runtime.Definition == null || !runtime.IsAvailableForCurrentProfession ||
                !runtime.TryGetModule<IPlayerAbilityHudState>(out var source)) continue;
            PlayerAbilityTimedHudState state = source.TimedHudState;
            if (!state.IsVisible) continue;
            timedAbilityRoot.SetActive(true);
            if (timedAbilityName != null && timedAbilityName.text != runtime.Definition.DisplayName)
                timedAbilityName.text = runtime.Definition.DisplayName;
            if (timedAbilityState != null && timedAbilityState.text != state.StateLabel)
                timedAbilityState.text = state.StateLabel;
            if (timedAbilitySeconds != null) timedAbilitySeconds.SetText("{0:1}s", state.RemainingSeconds);
            if (timedAbilityFill != null)
            {
                RectTransform fill = timedAbilityFill.rectTransform;
                Vector2 maximum = fill.anchorMax;
                maximum.x = state.NormalizedRemaining;
                if (fill.anchorMax != maximum) fill.anchorMax = maximum;
                Color color = state.Phase == PlayerActiveAbilityPhase.Casting ? castingBarColor :
                    state.Phase == PlayerActiveAbilityPhase.Armed ? armedBarColor : activeBarColor;
                if (timedAbilityFill.color != color) timedAbilityFill.color = color;
            }
            return;
        }
        timedAbilityRoot.SetActive(false);
    }

    private void OnDisable() => Hide();

    private void RefreshGrapple(PlayerGrappleCharges charges)
    {
        float ratio = charges.NormalizedEnergy;
        if (grappleFill != null)
        {
            grappleFill.fillAmount = ratio;
            grappleFill.color = ratio <= 0.25f
                ? new Color(1f, 0.22f, 0.2f, 0.95f)
                : ratio <= 0.5f
                    ? new Color(1f, 0.78f, 0.18f, 0.95f)
                    : new Color32(54, 171, 246, 255);
        }
        // 尚有尾數但不足一次發射費時，也顯示不可再次出鈎。
        if (emptyGrappleMark != null) emptyGrappleMark.SetActive(!charges.HasCharge);
        KeyCode key = inputManager != null ? inputManager.GrappleKey : KeyCode.Q;
        if (grappleKeyLabel != null) grappleKeyLabel.text = key.ToString().ToUpperInvariant();
        if (grappleKeyBackground != null)
            grappleKeyBackground.color = Input.GetKey(key)
                ? new Color(0.2f, 0.78f, 0.95f, 0.95f)
                : new Color32(204, 190, 199, 255);
    }

    private void RefreshSlots(PlayerAbilityRuntimeManager manager)
    {
        int shown = 0;
        // 命中能力靠左，專注能力靠右；同類多槽保持 Loadout 原序。
        for (int pass = 0; pass < 2; pass++)
        {
            PlayerAbilityCategory category = pass == 0
                ? PlayerAbilityCategory.GrappleHit : PlayerAbilityCategory.GrappleFocus;
            for (int slot = 0; slot < PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; slot++)
            {
                PlayerAbilityRuntime runtime = manager.GetEquippedRuntimeAtSlot(slot);
                if (runtime == null || runtime.Definition == null ||
                    runtime.Definition.Category != category) continue;
                SlotView view = GetView(shown++);
                if (view != null) BindView(view, runtime, category);
            }
        }
        for (int i = shown; i < views.Count; i++) views[i].Root.SetActive(false);
    }

    private SlotView GetView(int index)
    {
        if (index < views.Count) return views[index];
        if (slotsRoot == null || slotTemplate == null) return null;
        GameObject root = Instantiate(slotTemplate, slotsRoot);
        root.name = "AbilitySlot_" + index;
        SlotView view = new SlotView
        {
            Root = root,
            Background = root.GetComponent<Image>(),
            Icon = Find<Image>(root.transform, "Icon"),
            Fallback = Find<TMP_Text>(root.transform, "Fallback"),
            CooldownShade = Find<Image>(root.transform, "CooldownShade"),
            CooldownText = Find<TMP_Text>(root.transform, "CooldownText"),
            KeyText = Find<TMP_Text>(root.transform, "Key/Label"),
            KeyBackground = Find<Image>(root.transform, "Key")
        };
        if (view.Icon != null)
        {
            CenterSlotContent(view.Icon.rectTransform);
            view.Icon.preserveAspect = true;
        }
        if (view.Fallback != null) CenterSlotContent(view.Fallback.rectTransform);
        if(view.KeyBackground!=null)view.KeyPosition=view.KeyBackground.rectTransform.anchoredPosition;
        views.Add(view);
        return view;
    }

    private static T Find<T>(Transform root, string path) where T : Component
    {
        Transform child = root.Find(path);
        return child != null ? child.GetComponent<T>() : null;
    }

    private void BindView(SlotView view, PlayerAbilityRuntime runtime,
        PlayerAbilityCategory category)
    {
        view.Root.SetActive(true);
        bool focus=category==PlayerAbilityCategory.GrappleFocus;
        if(view.KeyBackground!=null)view.KeyBackground.rectTransform.anchoredPosition=view.KeyPosition+(focus?focusKeyOffset:Vector2.zero);
        Sprite icon = runtime.Definition.HudIcon;
        if (view.Icon != null)
        {
            view.Icon.sprite = icon;
            view.Icon.enabled = icon != null;
        }
        if (view.Fallback != null)
        {
            view.Fallback.gameObject.SetActive(icon == null);
            string label = runtime.Definition.DisplayName;
            view.Fallback.text = runtime.TryGetModule<IPlayerAbilityHudState>(out _) && !string.IsNullOrEmpty(label)
                ? label.Substring(0, Mathf.Min(2, label.Length)) : "✶";
        }
        float cooldown = 0f;
        if (runtime.TryGetModule<IPlayerAbilityHudState>(out var activeHud))
            cooldown = activeHud.CooldownRemainingSeconds;
        else if (runtime.TryGetModule(out SupportAerialAbility aerial))
            cooldown = aerial.CooldownRemainingSeconds;
        else if (runtime.TryGetModule(out TankAirDashAbility dash))
            cooldown = dash.CooldownRemainingSeconds;
        bool cooling = cooldown > 0.01f;
        bool active = activeHud != null && activeHud.IsAbilityActive;
        if (view.CooldownShade != null) view.CooldownShade.gameObject.SetActive(cooling && !active);
        if (view.CooldownText != null)
        {
            view.CooldownText.gameObject.SetActive(cooling || active);
            if (active)
            {
                view.CooldownText.color = new Color(.25f, 1f, .9f);
                view.CooldownText.text = activeHud.ActiveRemainingSeconds > .01f
                    ? Mathf.CeilToInt(activeHud.ActiveRemainingSeconds).ToString() : "…";
            }
            else if (cooling)
            {
                view.CooldownText.color = Color.white;
                view.CooldownText.text = Mathf.CeilToInt(cooldown).ToString();
            }
        }
        KeyCode key = category == PlayerAbilityCategory.GrappleFocus
            ? (inputManager != null ? inputManager.GrappleFocusKey : KeyCode.E)
            : (inputManager != null ? inputManager.GrappleKey : KeyCode.Q);
        if (view.KeyText != null) view.KeyText.text = key.ToString().ToUpperInvariant();
        if (view.KeyBackground != null)
            view.KeyBackground.color = Input.GetKey(key)
                ? slotPressedColor
                : slotReadyColor;
        if (view.Background != null)
            view.Background.color = runtime.IsAvailableForCurrentProfession &&
                (!runtime.TryGetModule<IPlayerAbilityHudState>(out var availabilityHud) || availabilityHud.IsUsable)
                ? slotReadyColor
                : slotUnavailableColor;
    }

    private static void CenterSlotContent(RectTransform rect)
    {
        // Image.preserveAspect uses the RectTransform pivot when letterboxing.
        // A top-left pivot shifts tall/wide sprites even at identical pixel sizes.
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
    }
}
