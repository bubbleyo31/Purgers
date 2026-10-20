using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Purgers.Progression;

public sealed partial class LocalPlayerRewardHUD
{
    [SerializeField] private Vector2 replacementTitleOffset = new Vector2(0, -80);
    [SerializeField] private Vector2 focusSelectionPanelSize = new Vector2(920, 450);
    private RectTransform replacementWindow;
    private TMP_Text replacementTitle, selectedSkillName, selectionInstructions;
    private Image selectedSkillIcon;
    private ChoiceCard[] replacementCards;
    public bool IsFocusReward(string id) => GetFocusRewardDefinition(id) != null;
    public PlayerAbilityDefinition GetFocusRewardDefinition(string id) =>
        rewardCatalog != null && rewardCatalog.TryFind(id, out var reward) &&
        reward.IsImplementedAbilityReward && reward.Category == RewardCategory.GrappleFocus ? reward.AbilityDefinition : null;

    private void ShowReplacementWindow(bool visible)
    {
        if (!visible) { if (replacementWindow != null) replacementWindow.gameObject.SetActive(false); return; }
        if (!runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject owner) || owner == null) return;
        var manager = owner.GetComponent<PlayerAbilityRuntimeManager>();
        if (manager == null) return;
        EnsureReplacementWindow();
        PresentIncomingSkill(inputManager.SelectedFocusDefinition, inputManager.FocusLootSelectionPending);
        for (int slot = 0; slot < 3; slot++)
        {
            var runtime = manager.GetFocusRuntime(slot);
            BindReplacementCard(slot, runtime != null ? runtime.Definition : null, inputManager.FocusKey(slot));
        }
    }
    // Dedicated slot picker. Only font/style are shared with rewards; no reward card is cloned.
    private void EnsureReplacementWindow()
    {
        if (replacementWindow == null)
        {
            replacementWindow = MakeRect("FocusSlotSelectionWindow", choiceWindow.parent, new Vector2(1920,1080), Vector2.zero);
            replacementWindow.anchorMin = Vector2.zero; replacementWindow.anchorMax = Vector2.one;
            replacementWindow.offsetMin = replacementWindow.offsetMax = Vector2.zero;
            replacementWindow.SetAsLastSibling();
            var shade = replacementWindow.gameObject.AddComponent<Image>();
            shade.color = new Color(0,0,0,.55f); shade.raycastTarget = false;
            var panel = MakeRect("SlotSelectionPanel", replacementWindow, focusSelectionPanelSize, replacementTitleOffset);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(.055f,.095f,.12f,.98f); panelImage.raycastTarget = false;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.75f,.67f,.34f,1); outline.effectDistance = new Vector2(2,-2);
            replacementTitle = MakeText("SelectSlotTitle",panel,new Vector2(820,48),new Vector2(0,178),30);
            replacementTitle.text = "選擇裝備位置";
            selectedSkillIcon = MakeIcon("IncomingSkillIcon",panel,new Vector2(64,64),new Vector2(-250,107));
            selectedSkillName = MakeText("IncomingSkillName",panel,new Vector2(540,64),new Vector2(65,107),24);
            selectedSkillName.alignment = TextAlignmentOptions.MidlineLeft;
            replacementCards = new ChoiceCard[3];
            for(int slot=0;slot<3;slot++)
            {
                var root = MakeRect("Destination_"+slot,panel,new Vector2(254,204),new Vector2((slot-1)*282,-49));
                var background = root.gameObject.AddComponent<Image>();
                background.color = new Color(.13f,.19f,.22f,1); background.raycastTarget = false;
                var card = new ChoiceCard { root=root.gameObject };
                card.mouseButton = MakeText("SlotKey",root,new Vector2(180,34),new Vector2(0,76),23);
                card.icon = MakeIcon("EquippedIcon",root,new Vector2(60,60),new Vector2(0,20));
                card.fallbackIcon = MakeText("EmptyIcon",root,new Vector2(60,60),new Vector2(0,20),38);
                card.title = MakeText("EquippedName",root,new Vector2(230,35),new Vector2(0,-35),20);
                card.description = MakeText("PlacementAction",root,new Vector2(230,30),new Vector2(0,-76),18);
                replacementCards[slot]=card;
            }
            selectionInstructions = MakeText("Instructions",panel,new Vector2(850,40),new Vector2(0,-191),18);
        }
        replacementWindow.gameObject.SetActive(true);
    }
    private void PresentIncomingSkill(PlayerAbilityDefinition definition, bool loot)
    {
        selectedSkillName.text = "已選擇：" + (definition != null ? definition.DisplayName : "專注技能");
        selectedSkillIcon.sprite = definition != null ? definition.HudIcon : null;
        selectedSkillIcon.enabled = selectedSkillIcon.sprite != null;
        string modifier = inputManager != null ? inputManager.RewardHoldKeyLabel : "ALT";
        selectionInstructions.text = "按槽位技能鍵確認　・　" + (loot ? "右鍵取消" : "放開 " + modifier + " 取消");
    }
    private void BindReplacementCard(int slot, PlayerAbilityDefinition definition, KeyCode key)
    {
        var card=replacementCards[slot]; bool occupied=definition!=null;
        card.title.text=occupied?definition.DisplayName:"空槽";
        card.description.text=occupied?"替換此技能":"放入此槽";
        card.description.color=occupied?new Color(1,.75f,.45f,1):new Color(.6f,1,.85f,1);
        card.icon.sprite=occupied?definition.HudIcon:null;card.icon.enabled=card.icon.sprite!=null;
        card.fallbackIcon.text=occupied?"●":"＋";card.fallbackIcon.gameObject.SetActive(card.icon.sprite==null);
        if(card.promptLayout==null)card.promptLayout=HudKeyPromptLayout.Inside(card.mouseButton);
        card.promptLayout.Refresh(key);card.promptLayout.SetState(false,key==KeyCode.None);
    }
    private static RectTransform MakeRect(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;
        rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
    }
    private TMP_Text MakeText(string name,Transform parent,Vector2 size,Vector2 position,float fontSize)
    {
        var rect=MakeRect(name,parent,size,position);
        var text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        var source=windowTitle!=null?windowTitle:leftCard.title;
        text.font=source.font;text.fontSharedMaterial=source.fontSharedMaterial;
        text.color=new Color(.95f,.94f,.86f,1);text.fontSize=fontSize;
        text.enableAutoSizing=true;text.fontSizeMin=14;text.fontSizeMax=fontSize;
        text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;return text;
    }
    private static Image MakeIcon(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var rect=MakeRect(name,parent,size,position);var image=rect.gameObject.AddComponent<Image>();
        image.preserveAspect=true;image.raycastTarget=false;return image;
    }
#if UNITY_EDITOR
    public RectTransform CreateReplacementPreview(PlayerAbilityDefinition[] definitions, PlayerAbilityDefinition incoming = null)
    {
        EnsureReplacementWindow(); PresentIncomingSkill(incoming != null ? incoming : definitions!=null&&definitions.Length>0?definitions[0]:null,false);
        for(int slot=0;slot<3;slot++)
            BindReplacementCard(slot,definitions!=null&&slot<definitions.Length?definitions[slot]:null,
                slot==0?KeyCode.E:slot==1?KeyCode.Z:KeyCode.X);
        return replacementWindow;
    }
#endif
    private void OnDestroy()
    {
        if(replacementWindow!=null)Destroy(replacementWindow.gameObject);
    }
}
