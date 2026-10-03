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
    private bool loggedAmbiguousRunner;

    public bool CanPresentRewardSelection =>
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
        if (!TryResolveRunner() || !CanPresentRewardSelection)
        {
            SetWindowVisible(false);
            SetPromptVisible(false);
            SetExperienceVisible(false);
            return;
        }

        GameLogic logic = GameLogic.GetPrimaryForRunner(runner);
        if (logic == null ||
            !logic.TryGetPlayerExperience(runner.LocalPlayer,
                out PlayerExperienceState experience) ||
            !logic.TryGetPlayerRewardDraft(runner.LocalPlayer,
                out PlayerRewardNetworkState draft))
        {
            SetWindowVisible(false);
            SetPromptVisible(false);
            SetExperienceVisible(false);
            return;
        }

        SetExperienceVisible(true);
        RefreshExperience(experience, draft.ChoiceCount);
        bool pending = experience.PendingRewards > 0;
        RefreshExperienceStyle(pending);
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
        if (ownerStageHud != null)
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
        return true;
    }

    private void RefreshExperience(
        PlayerExperienceState experience, int choiceCount)
    {
        int level = Mathf.Max(1, experience.Level);
        int required = PlayerExperienceRules.RequiredExperience(level);
        if (experienceBar != null)
        {
            experienceBar.interactable = false;
            experienceBar.minValue = 0f;
            experienceBar.maxValue = 1f;
            experienceBar.SetValueWithoutNotify(
                experience.PendingRewards > 0
                    ? 1f
                    : Mathf.Clamp01((float)experience.Experience / required));
        }
        if (levelLabel != null)
            levelLabel.text = level.ToString();
        if (experienceLabel != null)
            experienceLabel.text = experience.PendingRewards > 0
                ? $"待選獎勵 ×{experience.PendingRewards}"
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

        PlayerAbilityDefinition ability = reward.AbilityDefinition;
        card.title.text = ability != null ? ability.DisplayName : rewardId;
        card.description.text = reward.Description;
        card.mouseButton.text = mouseButton;
        Sprite icon = ability != null ? ability.HudIcon : null;
        if (card.icon != null)
        {
            card.icon.enabled = icon != null;
            card.icon.sprite = icon;
        }
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
