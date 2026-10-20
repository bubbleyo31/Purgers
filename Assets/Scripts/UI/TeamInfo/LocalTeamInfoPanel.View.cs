using System;
using System.Collections.Generic;
using Fusion;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class LocalTeamInfoPanel
{
    static readonly Color PanelColor = new Color32(42,49,33,248), RowColor = new Color32(36,44,29,255),
        Cream = new Color32(242,238,219,255), Gold = new Color32(210,191,91,255), Muted = new Color32(173,181,152,255);
    RectTransform canvasRoot, window, tooltip;
    TMP_Text countLabel, tooltipTitle, tooltipBody;
    TeamInfoTooltipTarget hovered;
    readonly RowView[] rows = new RowView[4];
    readonly List<PlayerRewardDefinition> growthDefinitions = new List<PlayerRewardDefinition>();
    sealed class IconView
    {
        public RectTransform Root;
        public Image Icon, Shade, Background;
        public TMP_Text Number, State;
        public TeamInfoTooltipTarget Hover;
        public GrowthRewardRankView Rank;
    }
    sealed class RowView
    {
        public RectTransform Root, Skills, GrowthContent;
        public Image Portrait, Stripe, Weapon;
        public TMP_Text Name, HealthText, WeaponName;
        public LocalHealthSegmentView Health;
        public readonly List<IconView> SkillViews = new List<IconView>(), GrowthViews = new List<IconView>();
        public readonly List<PlayerAbilityRuntime> Abilities = new List<PlayerAbilityRuntime>();
        public readonly List<PlayerAbilityDefinition> RememberedAbilities = new List<PlayerAbilityDefinition>();
        public TeamAbilityDisplaySnapshot RememberedIds;
        public readonly List<PlayerRewardDefinition> Growth = new List<PlayerRewardDefinition>();
        public PlayerRef Player;
        public NetworkObject Target, WeaponTarget;
        public Player Body;
        public PlayerActionGate Gate;
        public PlayerHealth HP;
        public PlayerAbilityRuntimeManager AbilitiesSource;
        public PlayerProfessionRuntimeManager ProfessionSource;
        public IPlayerWeaponHUDSource WeaponSource;
    }
    static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
        rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(w,h); return rect;
    }
    static Image Image(Transform parent, string name, float x, float y, float w, float h, Color color, Sprite sprite = null)
    {
        var image = Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();
        image.color = color; image.sprite = sprite; image.raycastTarget = false; return image;
    }
    TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color)
    {
        var text = Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = settings.font; text.text = value; text.fontSize = size; text.color = color;
        text.richText = false; text.raycastTarget = false; text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow; text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }
    void Build()
    {
        if (canvasRoot != null) return;
        canvasRoot = Rect(transform,"Team Info Canvas",0,0,1920,1080);
        var canvas = canvasRoot.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;
        var scaler = canvasRoot.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = settings.referenceResolution; scaler.matchWidthOrHeight = .5f;
        canvasRoot.gameObject.AddComponent<GraphicRaycaster>();
        window = Rect(canvasRoot,"Fixed four player frame",0,0,1104,824);
        window.anchorMin = window.anchorMax = window.pivot = Vector2.one * .5f;
        window.sizeDelta = settings.windowSize;
        window.anchoredPosition = settings.windowOffset;
        window.localScale = Vector3.one * settings.scale;
        // Inner reference plane remains fixed: the four rows never depend on roster count.
        var plane = Rect(window,"Reference plane",0,0,1104,824);
        plane.localScale = new Vector3(settings.windowSize.x/1104f, settings.windowSize.y/824f,1);
        var background = Image(plane,"Panel",0,0,1104,824,PanelColor); background.raycastTarget = true;
        Image(plane,"Top rule",16,66,1072,2,Gold);
        Text(plane,"Title","隊伍資訊",32,12,600,48,36,Cream);
        countLabel = Text(plane,"Count","0 / 4 人",850,20,220,40,26,Cream); countLabel.alignment = TextAlignmentOptions.MidlineRight;
        for (int i=0; i<4; i++) rows[i] = CreateRow(plane,i);
        if (settings.catalog != null)
        {
            foreach (var definition in settings.catalog.Rewards)
                if (definition != null && definition.IsImplementedGrowthReward && !growthDefinitions.Exists(d => d.GrowthKind == definition.GrowthKind)) growthDefinitions.Add(definition);
            growthDefinitions.Sort((a,b) => ((int)a.GrowthKind).CompareTo((int)b.GrowthKind));
        }
        tooltip = Rect(canvasRoot,"Skill tooltip",0,0,360,150);
        Image(tooltip,"Background",0,0,360,150,new Color32(23,29,22,252));
        Image(tooltip,"Top edge",0,0,360,2,Gold);
        tooltipTitle = Text(tooltip,"Name","",14,8,332,30,23,Gold);
        tooltipBody = Text(tooltip,"Description","",14,40,332,100,19,Cream);
        tooltipBody.enableWordWrapping = true; tooltipBody.overflowMode = TextOverflowModes.Overflow;
        tooltip.gameObject.SetActive(false);
    }
    RowView CreateRow(Transform parent, int index)
    {
        var v = new RowView { Root = Rect(parent,"Player row "+(index+1),16,82+index*176,1072,164) };
        Image(v.Root,"Background",0,0,1072,164,RowColor);
        Image(v.Root,"Bottom rule",0,163,1072,1,new Color(1,1,1,.12f));
        Image(v.Root,"Portrait backing",18,12,124,140,new Color32(23,29,22,255));
        v.Portrait = Image(v.Root,"Portrait",22,14,120,136,Color.white); v.Portrait.preserveAspect = true;
        v.Stripe = Image(v.Root,"Identity stripe",18,12,10,140,Muted);
        v.Name = Text(v.Root,"Player name","尚無玩家",158,4,330,35,26,Cream);
        v.Health = Rect(v.Root,"Health segments",158,46,220,18).gameObject.AddComponent<LocalHealthSegmentView>(); v.Health.raycastTarget = false; v.Health.ConfigureTeamInfoPresentation();
        v.HealthText = Text(v.Root,"HP and shield","",386,38,116,30,18,Cream);
        v.HealthText.enableAutoSizing = true; v.HealthText.fontSizeMin = 12; v.HealthText.fontSizeMax = 18;
        v.Weapon = Image(v.Root,"Current weapon",510,16,132,50,Cream); v.Weapon.preserveAspect = true;
        v.Weapon.gameObject.AddComponent<TeamInfoSpriteCrop>().Settings = settings;
        v.WeaponName = Text(v.Root,"Weapon name","",648,22,90,40,15,Cream);
        v.WeaponName.enableAutoSizing = true; v.WeaponName.fontSizeMin = 11; v.WeaponName.fontSizeMax = 15;
        Text(v.Root,"Skills label","技能",158,70,560,24,18,Gold);
        v.Skills = Rect(v.Root,"Skills",158,98,572,60);
        for (int i=0; i<PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; i++) v.SkillViews.Add(CreateIcon(v.Skills,false));
        Image(v.Root,"Column separator",744,12,1,140,new Color(1,1,1,.22f));
        Text(v.Root,"Growth label","被動 / 成長",762,6,290,26,18,Muted);
        var viewport = Rect(v.Root,"Growth viewport",756,34,304,122);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(0,0,0,0); hit.raycastTarget = true;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
        scroll.viewport = viewport; v.GrowthContent = Rect(viewport,"Owned growth",0,0,304,122); scroll.content = v.GrowthContent;
        EmptyRow(v); return v;
    }
    IconView CreateIcon(RectTransform parent, bool growth)
    {
        var v = new IconView { Root = Rect(parent,growth?"Owned passive or growth":"Skill slot",0,0,60,60) };
        v.Background = Image(v.Root,"Slot background",0,0,60,60,growth?Cream:new Color32(68,76,53,255),growth?settings.growthBackground:settings.skillBackground);
        v.Icon = Image(v.Root,"Project icon",0,0,60,60,new Color32(23,29,22,255)); v.Icon.preserveAspect = true;
        v.Hover = v.Root.gameObject.AddComponent<TeamInfoTooltipTarget>(); v.Hover.Owner = this;
        var hit = v.Root.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
        if (growth)
        {
            var fit = v.Icon.gameObject.AddComponent<AbilityIconFrameFit>(); fit.ConfigureFrame(v.Root,AbilityIconFrameFit.FrameShape.Hexagon,1);
            fit.FitWholeSprite = true;
            var label = Text(v.Icon.transform,"Current rank","",0,0,60,60,24,Cream); label.font = settings.rankFont;
            v.Rank = v.Icon.gameObject.AddComponent<GrowthRewardRankView>(); v.Rank.ConfigureLabel(label);
        }
        else
        {
            v.Shade = Image(v.Root,"Full tile radial cooldown",0,0,60,60,new Color(0,0,0,settings.cooldownOpacity),settings.skillBackground);
            v.Shade.type = UnityEngine.UI.Image.Type.Filled; v.Shade.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            v.Shade.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top; v.Shade.fillClockwise = false;
            v.Number = Text(v.Root,"Cooldown number","",0,0,60,60,30,Color.white);
            v.Number.alignment = TextAlignmentOptions.Center; v.Number.fontStyle = FontStyles.Bold;
            v.Number.outlineColor = new Color32(15,19,14,255); v.Number.outlineWidth = .15f;
            v.State = Text(v.Root,"Availability","",0,43,60,17,11,Gold); v.State.alignment = TextAlignmentOptions.Center;
        }
        return v;
    }
    static void Place(IconView view, float x, float y, float size)
    {
        view.Root.anchoredPosition = new Vector2(x,-y);
        view.Root.localScale = Vector3.one * (size/60f);
    }
    void EmptyRow(RowView row)
    {
        row.Player = PlayerRef.None; row.Target = null; row.WeaponTarget = null; row.WeaponSource = null;
        row.Name.text = "尚無玩家"; row.Portrait.enabled = false; row.Stripe.color = Muted;
        row.Health.gameObject.SetActive(false); row.Health.ResetPresentation(); row.HealthText.text = "";
        row.Weapon.enabled = false; row.WeaponName.text = "";
        PresentSkills(row,0);
        foreach (var icon in row.GrowthViews) icon.Root.gameObject.SetActive(false);
    }
    void ClearRows() { foreach (var row in rows) EmptyRow(row); countLabel.text = "0 / 4 人"; HideTooltip(); }
    void PresentRow(RowView row, PlayerRef player, GameLogic logic)
    {
        if (player == PlayerRef.None || !logic.TryGetTeammateIdentity(player,out var identity)) { EmptyRow(row); return; }
        bool changed = row.Player != player; row.Player = player;
        row.Name.text = identity.Name.ToString() + (player == runner.LocalPlayer ? "  · 自己" : "") + (identity.IsDead ? "  · 已死亡" : "");
        row.Name.fontSize = 26;
        while (row.Name.GetPreferredValues(row.Name.text).x > 330 && row.Name.text.Length > 8)
            row.Name.text = row.Name.text.Substring(0,row.Name.text.Length-2) + "…";
        row.Stripe.color = TeammateDisplayRules.IdentityColor(identity.ColorSlot);
        row.Portrait.sprite = settings.portraits != null ? settings.portraits.GetPortrait(runner,player) : null;
        row.Portrait.enabled = row.Portrait.sprite != null;
        runner.TryGetPlayerObject(player,out var target);
        if (target == null || !target.IsValid) target = null;
        if (changed || row.Target != target)
        {
            row.Target = target; row.Health.ResetPresentation(); row.GrowthContent.anchoredPosition = Vector2.zero;
            row.Body = target != null ? target.GetComponent<Player>() : null;
            row.HP = target != null ? target.GetComponent<PlayerHealth>() : null;
            row.Gate = target != null ? target.GetComponent<PlayerActionGate>() : null;
            row.AbilitiesSource = target != null ? target.GetComponent<PlayerAbilityRuntimeManager>() : null;
            row.ProfessionSource = target != null ? target.GetComponent<PlayerProfessionRuntimeManager>() : null;
        }
        bool ready = target != null && row.Gate != null && row.Gate.IsFusionSpawned && row.HP != null;
        row.Health.gameObject.SetActive(ready && !identity.IsDead);
        if (ready)
        {
            row.Health.ApplyHealthSnapshot(row.HP.CurrentHealth,row.HP.MaximumHealth);
            var shield = PlayerAbilityQualification.GetModule<PlayerShieldAbility>(row.Body);
            float shieldHP = shield != null ? shield.CurrentShield : 0;
            row.Health.SetShieldHealth(shieldHP);
            row.HealthText.text = row.HP.IsDowned ? "倒地" : Mathf.CeilToInt(row.HP.CurrentHealth)+" / "+Mathf.CeilToInt(row.HP.MaximumHealth)+(shieldHP>0?" +"+Mathf.CeilToInt(shieldHP):"");
        }
        else row.HealthText.text = identity.IsDead ? "已死亡" : "同步中";
        NetworkObject weapon = ready && row.ProfessionSource != null ? row.ProfessionSource.CurrentRuntimeObject : null;
        if (weapon != null && !weapon.IsValid) weapon = null;
        if (weapon == null || weapon != row.WeaponTarget)
        {
            row.WeaponTarget = weapon; row.WeaponSource = null;
            if (weapon != null) foreach (var component in weapon.GetComponentsInChildren<MonoBehaviour>(true))
                if (component is IPlayerWeaponHUDSource source) { row.WeaponSource = source; break; }
        }
        bool hasWeapon = TryReadWeaponSnapshot(row.WeaponSource,out var snapshot);
        if (hasWeapon) { row.Weapon.sprite = snapshot.WeaponIcon; row.Weapon.enabled = snapshot.WeaponIcon != null; }
        else row.Weapon.enabled = false;
        row.WeaponName.text = "";
        var profession = ready && row.ProfessionSource != null ? row.ProfessionSource.CurrentRuntimeProfession : logic.ReadTeamWeapon(player);
        if (settings.catalog != null)
            foreach (var definition in settings.catalog.Rewards)
                if (definition != null && definition.IsImplementedWeaponReward && definition.WeaponProfession == profession)
                {
                    row.WeaponName.text = definition.DisplayName;
                    if (!hasWeapon) { row.Weapon.sprite = definition.HudIcon; row.Weapon.enabled = definition.HudIcon != null; }
                    break;
                }
        row.Abilities.Clear();
        if (ready && row.AbilitiesSource != null)
            for (int i=0; i<PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots; i++)
            {
                var runtime = row.AbilitiesSource.GetEquippedRuntimeAtSlot(i);
                if (runtime != null && runtime.Definition != null && runtime.TryReadPresentation(out _)) row.Abilities.Add(runtime);
            }
        var remembered = identity.IsDead && !ready ? identity.LastAbilityIds : default;
        if (changed || !remembered.Equals(row.RememberedIds))
        {
            row.RememberedIds = remembered; row.RememberedAbilities.Clear();
            if (settings.catalog != null)
                for (int slot=0; slot<TeamAbilityDisplaySnapshot.Capacity; slot++)
                {
                    var definition = remembered.Resolve(slot,settings.catalog);
                    if (definition != null) row.RememberedAbilities.Add(definition);
                }
        }
        PresentSkills(row,row.Abilities.Count > 0 ? row.Abilities.Count : row.RememberedAbilities.Count);
        var state = logic.GetPlayerGrowth(player); row.Growth.Clear();
        foreach (var definition in growthDefinitions) if (state.Rank(definition.GrowthKind) > 0) row.Growth.Add(definition);
        while (row.GrowthViews.Count < row.Growth.Count) row.GrowthViews.Add(CreateIcon(row.GrowthContent,true));
        var grid = new TeamInfoGrid(row.Growth.Count,304,settings.preferredIconSize,settings.minimumIconSize,settings.iconGap);
        row.GrowthContent.sizeDelta = new Vector2(304,Mathf.Max(122,grid.Height));
        float offset = Mathf.Max(0,(122-grid.Height)*.5f);
        for (int i=0; i<row.GrowthViews.Count; i++)
        {
            var icon = row.GrowthViews[i]; icon.Root.gameObject.SetActive(i<row.Growth.Count);
            if (i>=row.Growth.Count) continue;
            var definition = row.Growth[i]; Place(icon,grid.X(i),grid.Y(i)+offset,grid.Size);
            icon.Icon.sprite = definition.HudIcon; icon.Icon.enabled = definition.HudIcon != null;
            icon.Rank.Present(definition,state.Rank(definition.GrowthKind));
            icon.Hover.HasContent = true; icon.Hover.Title = definition.DisplayName;
            icon.Hover.Description = definition.Description; icon.Hover.State = PlayerGrowthRules.IsAttribute(definition.GrowthKind) ? "目前階級："+GrowthRewardRankView.RankLabel(definition.GrowthKind,state.Rank(definition.GrowthKind)) : "";
        }
    }
    static bool TryReadWeaponSnapshot(IPlayerWeaponHUDSource source, out PlayerWeaponHUDSnapshot snapshot)
    {
        snapshot = default;
        // Interface references do not participate in Unity destroyed-object null checks.
        return source is MonoBehaviour component && component != null && source.TryGetWeaponHUDSnapshot(out snapshot);
    }
    void PresentSkills(RowView row,int count)
    {
        int slots = Mathf.Max(4,count); var grid = new TeamInfoGrid(slots,572,60,44,14);
        for (int i=0; i<row.SkillViews.Count; i++)
        {
            var icon = row.SkillViews[i]; icon.Root.gameObject.SetActive(i<slots);
            if (i>=slots) continue;
            Place(icon,grid.X(i),0,grid.Size);
            bool owned = i<count;
            icon.Background.color = owned ? Cream : new Color32(68,76,53,255);
            icon.Icon.enabled = owned; icon.Shade.gameObject.SetActive(false); icon.Number.text = ""; icon.State.text = "";
            icon.Hover.HasContent = owned;
            if (!owned) continue;
            var runtime = i < row.Abilities.Count ? row.Abilities[i] : null;
            var definition = runtime != null ? runtime.Definition : row.RememberedAbilities[i];
            PlayerAbilityPresentation state = default;
            if (runtime != null) runtime.TryReadPresentation(out state);
            icon.Icon.sprite = definition.HudIcon; icon.Icon.enabled = definition.HudIcon != null;
            icon.Hover.Title = definition.DisplayName; icon.Hover.Description = "";
            if (settings.catalog != null) foreach (var reward in settings.catalog.Rewards)
                if (reward != null && reward.AbilityDefinition == definition) { icon.Hover.Description = reward.Description; break; }
            bool cooling = state.Remaining > .001f;
            icon.Shade.gameObject.SetActive(cooling); icon.Shade.fillAmount = TeamInfoGrid.CooldownFraction(state.Remaining,state.Duration);
            icon.Number.text = cooling ? Mathf.CeilToInt(state.Remaining).ToString() : "";
            icon.State.text = runtime == null ? "已死亡" : state.Active ? "生效中" : !state.Usable ? "不可用" : "";
            icon.Hover.State = runtime == null ? "已死亡" : cooling ? "冷卻中："+Mathf.CeilToInt(state.Remaining)+" 秒" : state.Active ? "生效中" : !state.Usable ? "目前條件不可用" : "";
        }
    }
    public void ShowTooltip(TeamInfoTooltipTarget target) { hovered = target; UpdateTooltip(); }
    public void HideTooltip(TeamInfoTooltipTarget target = null)
    { if (target != null && hovered != target) return; hovered = null; if (tooltip != null) tooltip.gameObject.SetActive(false); }
    void UpdateTooltip()
    {
        if (tooltip == null || hovered == null || !hovered.HasContent || !hovered.isActiveAndEnabled || !IsOpen) { if (tooltip != null) tooltip.gameObject.SetActive(false); return; }
        tooltip.gameObject.SetActive(true); tooltipTitle.text = hovered.Title;
        tooltipBody.text = hovered.Description + (string.IsNullOrEmpty(hovered.State)?"":"\n"+hovered.State);
        float height = Mathf.Max(96,tooltipBody.GetPreferredValues(tooltipBody.text,332,0).y+54);
        tooltip.sizeDelta = new Vector2(360,height);
        ((RectTransform)tooltip.GetChild(0)).sizeDelta = tooltip.sizeDelta;
        tooltipBody.rectTransform.sizeDelta = new Vector2(332,height-48);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot,Input.mousePosition,null,out var point);
        var area = canvasRoot.rect;
        float x = Mathf.Clamp(point.x-area.xMin+18,8,area.width-368);
        float y = Mathf.Clamp(area.yMax-point.y-height-12,8,Mathf.Max(8,area.height-height-8));
        tooltip.anchoredPosition = new Vector2(x,-y);
    }
}
