using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed partial class HudLayoutSession
{
    void FillTeamSamples(LocalTeamInfoPanel panel, TeamInfoPanelSettings settings, RectTransform frame)
    {
        frame.Find("Count").GetComponent<TMP_Text>().text = "3 / 4 人（排版範例）";
        var rewards = settings.catalog != null ? settings.catalog.Rewards : null;
        var abilities = rewards?.Where(d => d != null && d.IsImplementedAbilityReward).ToArray();
        var weapons = rewards?.Where(d => d != null && d.IsImplementedWeaponReward).ToArray();
        var growth = rewards?.Where(d => d != null && d.IsImplementedGrowthReward).ToArray();
        for (int i = 0; i < 3; i++)
        {
            var row = frame.Find("Player row " + (i + 1));
            row.Find("Player name").GetComponent<TMP_Text>().text = i == 0 ? "自己 · 玩家名稱" : "隊友 " + i;
            var portrait = row.Find("Portrait").GetComponent<Image>();
            portrait.sprite = settings.portraits != null ? settings.portraits.GetPortrait(null, default) : null;
            portrait.enabled = portrait.sprite != null;
            row.Find("Identity stripe").GetComponent<Image>().color = TeammateDisplayRules.IdentityColor(i);
            var health = row.Find("Health segments").GetComponent<LocalHealthSegmentView>();
            health.gameObject.SetActive(true); health.ApplyHealthSnapshot(i == 1 ? 60 : 100, 100);
            row.Find("HP and shield").GetComponent<TMP_Text>().text = i == 1 ? "60 / 100" : "100 / 100";
            if (weapons != null && weapons.Length > 0)
            {
                var weapon = weapons[i % weapons.Length]; var icon = row.Find("Current weapon").GetComponent<Image>();
                icon.sprite = weapon.HudIcon; icon.enabled = true;
                row.Find("Weapon name").GetComponent<TMP_Text>().text = weapon.DisplayName;
            }
            if (abilities != null && abilities.Length > 0)
                for (int slot = 0; slot < 2; slot++)
                {
                    var root = row.Find("Skills").GetChild(slot);
                    var icon = root.Find("Project icon").GetComponent<Image>();
                    icon.sprite = abilities[(i * 2 + slot) % abilities.Length].HudIcon; icon.enabled = true;
                    root.Find("Slot background").GetComponent<Image>().color = new Color32(242, 238, 219, 255);
                    if (slot == 1)
                    {
                        var shade = root.Find("Full tile radial cooldown").GetComponent<Image>();
                        shade.gameObject.SetActive(true); shade.fillAmount = .65f;
                        root.Find("Cooldown number").GetComponent<TMP_Text>().text = "8";
                    }
                }
            if (growth == null || growth.Length == 0) continue;
            int count = Mathf.Min(new[] { 1, 2, 8 }[i], growth.Length);
            var content = (RectTransform)row.Find("Growth viewport/Owned growth");
            var grid = new TeamInfoGrid(count, 304, settings.preferredIconSize, settings.minimumIconSize, settings.iconGap);
            for (int n = 0; n < count; n++)
            {
                var view = typeof(LocalTeamInfoPanel).GetMethod("CreateIcon", Private).Invoke(panel, new object[] { content, true });
                var type = view.GetType(); var icon = (Image)type.GetField("Icon").GetValue(view);
                var rect = (RectTransform)type.GetField("Root").GetValue(view);
                rect.anchoredPosition = new Vector2(grid.X(n), -grid.Y(n) - Mathf.Max(0, (122 - grid.Height) * .5f));
                rect.localScale = Vector3.one * (grid.Size / 60);
                icon.sprite = growth[n].HudIcon; icon.enabled = true;
                ((GrowthRewardRankView)type.GetField("Rank").GetValue(view)).Present(growth[n], n % 3 + 1);
            }
        }
    }

    void BuildExisting(RectTransform source)
    {
        Source = source;
        var sourceCanvas = source.GetComponentInParent<Canvas>();
        if (sourceCanvas != null)
        {
            var size = ((RectTransform)sourceCanvas.transform).rect.size;
            if (size.x > 1 && size.y > 1) { canvasRect.sizeDelta = size; Camera.orthographicSize = size.y * .5f; }
        }
        // Reproduce the authored transform chain, without cloning any scene/network/controller owners.
        var chain = new Stack<RectTransform>();
        for (var parent = source.parent as RectTransform; parent != null && parent.GetComponent<Canvas>() == null; parent = parent.parent as RectTransform)
            chain.Push(parent);
        RectTransform container = canvasRect;
        while (chain.Count > 0)
        {
            var original = chain.Pop(); var copy = Rect(container, original.name + "（參考容器）", original.rect.size);
            RectState.Read(original).Write(copy); container = copy;
        }
        var staging = new GameObject("Inactive staging"); staging.transform.SetParent(Root.transform, false); staging.SetActive(false);
        var clone = Object.Instantiate(source.gameObject, staging.transform, false);
        foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!(component is Graphic) && !(component is LayoutGroup) && !(component is LayoutElement) && !(component is ContentSizeFitter) && !(component is AspectRatioFitter))
                component.enabled = false;
        foreach (var nestedCanvas in clone.GetComponentsInChildren<Canvas>(true))
        {
            var rect = (RectTransform)nestedCanvas.transform; var layout = RectState.Read(rect);
            nestedCanvas.renderMode = RenderMode.WorldSpace; nestedCanvas.worldCamera = Camera;
            layout.Write(rect);
        }
        foreach (var camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var nestedCanvas in clone.GetComponentsInChildren<Canvas>(true))
        {
            var rect = (RectTransform)nestedCanvas.transform; var layout = RectState.Read(rect);
            nestedCanvas.renderMode = RenderMode.WorldSpace; nestedCanvas.worldCamera = Camera;
            layout.Write(rect);
        }
        foreach (var camera in clone.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
        clone.transform.SetParent(container, false); clone.SetActive(true); Object.DestroyImmediate(staging);
        Handle = (RectTransform)clone.transform; RectState.Read(source).Write(Handle);
        Handle.name = "可拖曳：" + source.name;
        foreach (var hud in clone.GetComponentsInChildren<LocalPlayerBattleAbilityHUD>(true)) FillAbilitySamples(hud);
        // A selected slots container does not contain its owning controller; attach an isolated preview owner.
        var owner = source.GetComponentInParent<LocalPlayerBattleAbilityHUD>();
        if (owner != null && source.GetComponent<LocalPlayerBattleAbilityHUD>() == null)
        {
            var serialized = new SerializedObject(owner);
            if (serialized.FindProperty("slotsRoot").objectReferenceValue == source)
            {
                var host = new GameObject("Skill sample owner", typeof(LocalPlayerBattleAbilityHUD)); host.transform.SetParent(Root.transform, false);
                var hud = host.GetComponent<LocalPlayerBattleAbilityHUD>(); hud.enabled = false;
                EditorUtility.CopySerialized(owner, hud);
                hud.enabled = false;
                typeof(LocalPlayerBattleAbilityHUD).GetField("slotsRoot", Private).SetValue(hud, Handle);
                var originalTemplate = serialized.FindProperty("slotTemplate").objectReferenceValue as GameObject;
                if (originalTemplate != null && originalTemplate.transform.parent == source)
                {
                    var copy = Handle.Find(originalTemplate.name);
                    if (copy != null)
                    {
                        copy.gameObject.SetActive(false);
                        typeof(LocalPlayerBattleAbilityHUD).GetField("slotTemplate", Private).SetValue(hud, copy.gameObject);
                    }
                }
                FillAbilitySamples(hud);
            }
        }
    }
    static void FillAbilitySamples(LocalPlayerBattleAbilityHUD hud)
    {
        var serialized = new SerializedObject(hud);
        var template = serialized.FindProperty("slotTemplate").objectReferenceValue as GameObject;
        if (template == null) return;
        // Only hide the cloned template; a preview owner may point at the original asset.
        if (template.transform.IsChildOf(hud.transform)) template.SetActive(false);
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Blink.asset");
        for (int i = 0; i < 2; i++)
        {
            var view = typeof(LocalPlayerBattleAbilityHUD).GetMethod("GetView", Private).Invoke(hud, new object[] { i });
            if (view == null) continue;
            var root = (GameObject)view.GetType().GetField("Root").GetValue(view); root.SetActive(true);
            var icon = root.transform.Find("Icon")?.GetComponent<Image>(); if (icon != null) { icon.sprite = definition != null ? definition.HudIcon : null; icon.enabled = true; }
            root.transform.Find("Fallback")?.gameObject.SetActive(false);
            var key = root.transform.Find("Key/Label")?.GetComponent<TMP_Text>(); if (key != null) key.text = i == 0 ? "Q" : "E";
            var state = i == 0 ? new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Cooling, CooldownRemaining = 8, CooldownDuration = 12 }
                : new AbilitySlotPresentationState { Phase = AbilitySlotVisualPhase.Active, PhaseRemaining = 3, PhaseDuration = 5, CanPress = true, CanEnd = true };
            typeof(LocalPlayerBattleAbilityHUD).GetMethod("PresentSlotState", Private).Invoke(hud, new[] { view, (object)state, new object() });
        }
    }
}
