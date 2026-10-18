using System;
using Fusion;
using Purgers.GameFlow.Stage;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local presentation of Host-owned XP and reward choices. This component
/// never decides or applies a reward. StageHUD owns its serialized UI tree.
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerRewardHUD : MonoBehaviour
{
    [Serializable]
    private sealed class ChoiceCard
    {
        public GameObject root;
        public TMP_Text title;
        public TMP_Text description;
        public TMP_Text mouseButton;
        public Image icon;
        public TMP_Text fallbackIcon;
        public GrowthRewardRankView rankView;
    }

    [Header("本機 Runner 與獎勵資料")]
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private PlayerRewardCatalog rewardCatalog;

    [Header("左下經驗群組")]
    [SerializeField] private CanvasGroup experienceGroup;
    [SerializeField] private Slider experienceBar;
    [SerializeField] private Image experienceFill;
    [SerializeField] private RectTransform experienceGaugeRoot;
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text experienceLabel;
    [SerializeField] private CanvasGroup altPrompt;
    [SerializeField] private TMP_Text altPromptLabel;

    [Header("獎勵選窗：完整畫面 Canvas 下的 Root")]
    [SerializeField] private RectTransform choiceWindow;
    [SerializeField] private CanvasGroup choiceWindowGroup;
    [SerializeField] private CanvasGroup backgroundDim;
    [SerializeField] private TMP_Text windowTitle;
    [SerializeField] private ChoiceCard leftCard;
    [SerializeField] private ChoiceCard middleCard;
    [SerializeField] private ChoiceCard rightCard;

    [Header("表現")]
    [SerializeField, Range(0f, 1f)] private float dimOpacity = 0f;
    [SerializeField, Min(0f)] private float promptPulseSpeed = 3f;
    [SerializeField] private Color normalExperienceColor =
        new Color(0.18f, 0.62f, 1f, 1f);
    [SerializeField] private Color readyExperienceColor =
        new Color(1f, 0.82f, 0.2f, 1f);

    private InputManager inputManager;
    private StageHudController ownerStageHud;
    private Purgers.GameFlow.SafeHouse.SafeHouseHudController ownerSafeHouseHud;
    private bool loggedAmbiguousRunner;

    public bool CanPresentRewardSelection =>
        IsHudConfigured && !LocalPlayerSpectatorController.IsObserving(runner);

    private bool IsHudConfigured =>
        isActiveAndEnabled && rewardCatalog != null &&
        experienceGroup != null && experienceBar != null &&
        levelLabel != null && experienceLabel != null &&
        altPrompt != null && altPromptLabel != null &&
        choiceWindow != null && choiceWindowGroup != null &&
        backgroundDim != null && windowTitle != null &&
        IsCardConfigured(leftCard) &&
        IsCardConfigured(middleCard) &&
        IsCardConfigured(rightCard);

    private static bool IsCardConfigured(ChoiceCard card) =>
        card != null && card.root != null && card.title != null &&
        card.description != null && card.mouseButton != null;

    private void Awake()
    {
        ownerStageHud = GetComponentInParent<StageHudController>();
        ownerSafeHouseHud = GetComponentInParent<Purgers.GameFlow.SafeHouse.SafeHouseHudController>();
        if (rewardCatalog == null)
            rewardCatalog = Resources.Load<PlayerRewardCatalog>(
                "Progression/PlayerRewardCatalog");
        SetWindowVisible(false);
        SetPromptVisible(false);
        SetExperienceVisible(false);
    }

    private void OnDisable()
    {
        if (inputManager != null)
            inputManager.UnregisterRewardHud(this);
        inputManager = null;
        SetWindowVisible(false);
        SetPromptVisible(false);
        SetExperienceVisible(false);
    }

    private void Update()
    {
        if (!TryResolveRunner() || !IsHudConfigured)
        {
            SetWindowVisible(false);
            SetPromptVisible(false);
            SetExperienceVisible(false);
            return;
        }

        PlayerRef viewed = LocalPlayerSpectatorController.ViewedPlayer(runner);
        bool observing = viewed != runner.LocalPlayer;
        GameLogic logic = GameLogic.GetPrimaryForRunner(runner);
        if (viewed == PlayerRef.None || logic == null ||
            !logic.TryGetPlayerExperience(viewed,
                out PlayerExperienceState experience, out int requiredExperience) ||
            !logic.TryGetPlayerRewardDraft(viewed,
                out PlayerRewardNetworkState draft))
        {
            SetWindowVisible(false);
            SetPromptVisible(false);
            SetExperienceVisible(false);
            return;
        }

        SetExperienceVisible(true);
        RefreshExperience(experience, draft.ChoiceCount, requiredExperience);
        bool pending = experience.PendingRewards > 0;
        RefreshExperienceStyle(pending);
        if (observing)
        {
            SetWindowVisible(false);
            SetPromptVisible(false);
            return;
        }
        SetPromptVisible(pending && !inputManager.IsRewardSelectionActive);
        if (pending && altPrompt != null && !inputManager.IsRewardSelectionActive)
        {
            altPrompt.alpha = Mathf.Lerp(0.45f, 1f,
                0.5f + 0.5f * Mathf.Sin(
                    Time.unscaledTime * promptPulseSpeed));
        }

        bool showChoices = pending && inputManager.IsRewardSelectionActive &&
            draft.ChoiceCount > 0;
        SetWindowVisible(showChoices);
        if (showChoices)
            RefreshChoices(draft);
    }

    private bool TryResolveRunner()
    {
        if (ownerSafeHouseHud != null)
            runner = ownerSafeHouseHud.HudRunner;
        else if (ownerStageHud != null)
            runner = ownerStageHud.HudRunner;
        else if (runner == null || !runner.IsRunning)
        {
            runner = null;
            NetworkRunner[] candidates = FindObjectsByType<NetworkRunner>(
                FindObjectsSortMode.None);
            foreach (NetworkRunner candidate in candidates)
            {
                if (candidate == null || !candidate.IsRunning ||
                    !candidate.LocalPlayer.IsRealPlayer)
                    continue;
                if (runner != null)
                {
                    runner = null;
                    if (!loggedAmbiguousRunner)
                    {
                        Debug.LogWarning(
                            "[Reward HUD] 找到多個本機 Runner，請在 Inspector 指定。",
                            this);
                        loggedAmbiguousRunner = true;
                    }
                    return false;
                }
                runner = candidate;
            }
        }

        if (runner == null || !runner.IsRunning ||
            !runner.LocalPlayer.IsRealPlayer)
        {
            if (inputManager != null)
                inputManager.UnregisterRewardHud(this);
            inputManager = null;
            return false;
        }

        InputManager current = runner.GetComponent<InputManager>();
        if (current == null)
            return false;
        if (inputManager != current)
        {
            if (inputManager != null)
                inputManager.UnregisterRewardHud(this);
            inputManager = current;
        }
        if (CanPresentRewardSelection)
            inputManager.RegisterRewardHud(this);
        else
            inputManager.UnregisterRewardHud(this);
        return true;
    }

    private void RefreshExperience(
        PlayerExperienceState experience, int choiceCount, int required)
    {
        int level = Mathf.Max(1, experience.Level);
        if (experienceBar != null)
        {
            experienceBar.interactable = false;
            experienceBar.minValue = 0f;
            experienceBar.maxValue = 1f;
            experienceBar.SetValueWithoutNotify(
                Mathf.Clamp01((float)experience.Experience / required));
        }
        if (levelLabel != null)
            levelLabel.text = level.ToString();
        if (experienceLabel != null)
            experienceLabel.text = experience.PendingRewards > 0
                ? $"{experience.Experience} / {required} · 待選 ×{experience.PendingRewards}"
                : $"{experience.Experience} / {required}";
        if (altPromptLabel != null)
        {
            string key = inputManager != null
                ? inputManager.RewardHoldKeyLabel : "ALT";
            bool pressed = inputManager != null &&
                inputManager.IsRewardHoldKeyPressed;
            altPromptLabel.text = choiceCount > 0
                ? $"按住 <color=#{(pressed ? "38D8F0" : "FFFFFF")}>{key}</color> 選擇獎勵"
                : "獎勵候選準備中";
        }
    }

    private void RefreshExperienceStyle(bool pending)
    {
        float pulse = pending
            ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * promptPulseSpeed)
            : 0f;
        if (experienceFill != null)
            experienceFill.color = pending
                ? Color.Lerp(normalExperienceColor, readyExperienceColor,
                    0.65f + 0.35f * pulse)
                : normalExperienceColor;
        if (experienceGaugeRoot != null)
            experienceGaugeRoot.localScale = Vector3.one *
                (pending ? 1.03f + 0.03f * pulse : 1f);
    }

    private void RefreshChoices(PlayerRewardNetworkState draft)
    {
        if (windowTitle != null)
            windowTitle.text = "獎勵選擇";
        RefreshCard(leftCard, draft.GetChoice(0), "左鍵");
        if (draft.ChoiceCount == 3)
        {
            RefreshCard(middleCard, draft.GetChoice(1), "中鍵");
            RefreshCard(rightCard, draft.GetChoice(2), "右鍵");
        }
        else
        {
            SetCardVisible(middleCard, false);
            RefreshCard(rightCard, draft.GetChoice(1), "右鍵");
        }
    }

    private void RefreshCard(ChoiceCard card, string rewardId,
        string mouseButton)
    {
        PlayerRewardDefinition reward = null;
        bool found = rewardCatalog != null &&
            rewardCatalog.TryFind(rewardId, out reward);
        SetCardVisible(card, found);
        if (!found)
            return;

        int displayRank = 1;
        card.title.text = reward.DisplayName;
        card.description.text = reward.Description;
        if (reward.IsImplementedGrowthReward && PlayerGrowthRules.IsAttribute(reward.GrowthKind))
        {
            var logic = GameLogic.GetPrimaryForRunner(runner);
            int rank = logic != null ? logic.GetPlayerGrowth(runner.LocalPlayer).Rank(reward.GrowthKind) : 0;
            int next = Mathf.Min(rank + 1, PlayerGrowthRules.MaximumRank(reward.GrowthKind));
            displayRank = next;
            card.title.text = $"{reward.DisplayName} · {next} 階";
            bool percent = reward.GrowthKind == PlayerGrowthKind.ReloadSpeed || reward.GrowthKind == PlayerGrowthKind.MeleeSpeed ||
                reward.GrowthKind == PlayerGrowthKind.GrappleCost || reward.GrowthKind == PlayerGrowthKind.FocusCooldown || reward.GrowthKind == PlayerGrowthKind.HitCooldown ||
                reward.GrowthKind == PlayerGrowthKind.GuardCooldown;
            float value = reward.GrowthValue(next);
            string formatted = percent ? $"{value * 100:0.#}%" : $"{value:0.#}";
            if (reward.GrowthKind == PlayerGrowthKind.GuardReduction) formatted = $"+{value * 100:0.#} 個百分點";
            if (reward.GrowthKind == PlayerGrowthKind.GuardStamina) formatted = $"+{value:0.#}";
            card.description.text = reward.Description + $"\n升階後：{formatted}（本輪保留）";
        }
        card.mouseButton.text = mouseButton;
        Sprite icon = card.rankView != null && reward.IsImplementedGrowthReward &&
            PlayerGrowthRules.IsAttribute(reward.GrowthKind) && reward.GrowthRankBackground != null
            ? reward.GrowthRankBackground : reward.HudIcon;
        if (card.icon != null)
        {
            card.icon.enabled = icon != null;
            card.icon.sprite = icon;
        }
        if (card.rankView != null) card.rankView.Present(reward, displayRank);
        if (card.fallbackIcon != null)
            card.fallbackIcon.gameObject.SetActive(icon == null);
    }

    private static void SetCardVisible(ChoiceCard card, bool visible)
    {
        if (card?.root != null && card.root.activeSelf != visible)
            card.root.SetActive(visible);
    }

    private void SetPromptVisible(bool visible)
    {
        if (altPrompt != null)
            altPrompt.alpha = visible ? 1f : 0f;
    }

    private void SetExperienceVisible(bool visible)
    {
        if (experienceGroup != null)
            experienceGroup.alpha = visible ? 1f : 0f;
        if (!visible)
            RefreshExperienceStyle(false);
    }

    private void SetWindowVisible(bool visible)
    {
        if (choiceWindowGroup != null)
        {
            choiceWindowGroup.alpha = visible ? 1f : 0f;
            choiceWindowGroup.interactable = false;
            choiceWindowGroup.blocksRaycasts = false;
        }
        if (backgroundDim != null)
        {
            backgroundDim.alpha = visible ? dimOpacity : 0f;
            backgroundDim.interactable = false;
            backgroundDim.blocksRaycasts = false;
        }
    }

}
