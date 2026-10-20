using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>只讀既有觀戰候選名單與同步姓名。圖片每次按 PlayerRef 查詢，非同步 provider 不可回填過期的 UI 綁定。</summary>
public sealed class LocalSpectatorHUD : MonoBehaviour
{
    [SerializeField, Tooltip("目前觀戰控制器；單一觀看端可留空自動尋找。")]
    private LocalPlayerSpectatorController controller;
    [SerializeField] private GameObject content;
    [SerializeField] private RectTransform cardsRoot;
    [SerializeField] private RectTransform cardTemplate;
    [SerializeField] private RectTransform previousKey, nextKey;
    [SerializeField] private TMP_Text waitingText;
    [SerializeField, Tooltip("玩家頭像來源；目前使用共用圖片，未來可替換玩家選圖／Steam provider。")]
    private PlayerPortraitProvider portraitProvider;
    [SerializeField, Tooltip("Provider 尚未就緒或取圖失敗時的共用頭像。")]
    private Sprite fallbackPortrait;
    [SerializeField, Min(40f)] private float cardSpacing = 144f;
    [SerializeField] private Color selectedColor = new Color32(213,196,94,255);
    [SerializeField] private Color idleColor = new Color32(94,100,87,220);
    private sealed class Card
    {
        public RectTransform Rect;
        public Image Border, Portrait;
        public TMP_Text Name, State;
    }
    private readonly List<Card> cards = new List<Card>();
    private HudKeyPromptLayout previousPrompt, nextPrompt;
    private void Update()
    {
        if (controller == null) controller = FindFirstObjectByType<LocalPlayerSpectatorController>();
        if (content == null || cardsRoot == null || cardTemplate == null) return;
        bool visible = controller != null && controller.isActiveAndEnabled && controller.IsSpectating && controller.Runner != null &&
            controller.Runner.IsRunning && !controller.IsTransitionBlocked;
        content.SetActive(visible);
        if (!visible) return;
        // Spectator Q/E remain fixed by the existing spectator input contract.
        if (previousPrompt == null && previousKey != null) previousPrompt = HudKeyPromptLayout.Inside(previousKey.GetComponent<TMP_Text>(), "‹");
        if (nextPrompt == null && nextKey != null) nextPrompt = HudKeyPromptLayout.Inside(nextKey.GetComponent<TMP_Text>(), "›");
        previousPrompt?.Refresh(KeyCode.Q); previousPrompt?.SetState(Input.GetKey(KeyCode.Q));
        nextPrompt?.Refresh(KeyCode.E); nextPrompt?.SetState(Input.GetKey(KeyCode.E));
        var runner = controller.Runner;
        var logic = GameLogic.GetPrimaryForRunner(runner);
        var roster = controller.Candidates;
        int count = roster.Count;
        if (waitingText != null) waitingText.gameObject.SetActive(count == 0);
        if (previousKey != null) previousKey.gameObject.SetActive(count > 0);
        if (nextKey != null) nextKey.gameObject.SetActive(count > 0);
        float half = count * cardSpacing * .5f;
        if (previousKey != null) previousKey.anchoredPosition = new Vector2(-half - 32f, 0);
        if (nextKey != null) nextKey.anchoredPosition = new Vector2(half + 32f, 0);
        while (cards.Count < count)
        {
            var rect = Instantiate(cardTemplate, cardsRoot);
            rect.name = "Player " + cards.Count;
            cards.Add(new Card { Rect = rect, Border = rect.Find("Border").GetComponent<Image>(),
                Portrait = rect.Find("Portrait").GetComponent<Image>(), Name = rect.Find("Name").GetComponent<TMP_Text>(),
                State = rect.Find("State").GetComponent<TMP_Text>() });
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i]; card.Rect.gameObject.SetActive(i < count);
            if (i >= count) continue;
            PlayerRef player = roster[i];
            card.Rect.anchoredPosition = new Vector2((i + .5f) * cardSpacing - half, 0f);
            bool selected = controller.SpectatedPlayer == player;
            card.Border.color = selected ? selectedColor : idleColor;
            string name = "Player " + (player.PlayerId + 1);
            Color color = Color.white;
            if (logic != null && logic.TryGetTeammateIdentity(player, out var identity))
            { name = identity.Name.ToString(); color = TeammateDisplayRules.IdentityColor(identity.ColorSlot); }
            if (card.Name.text != name) card.Name.text = name;
            card.Name.color = color;
            bool downed = runner.TryGetPlayerObject(player, out var obj) && obj != null && obj.IsValid &&
                obj.TryGetComponent<PlayerHealth>(out var health) && health.IsRescueReady && health.IsDowned;
            string state = downed ? "倒地" : selected ? "觀戰中" : "";
            if (card.State.text != state) card.State.text = state;
            Sprite portrait = portraitProvider != null ? portraitProvider.GetPortrait(runner, player) : null;
            if (portrait == null) portrait = fallbackPortrait;
            if (card.Portrait.sprite != portrait) card.Portrait.sprite = portrait;
        }
    }
    private void OnDisable() { if (content != null) content.SetActive(false); }
}
