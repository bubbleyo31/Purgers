using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class AbilitySlotPresentationTests
{
    private static Type Rules => typeof(Player).Assembly.GetType("AbilitySlotPresentationRules");

    [Test]
    public void LateObservationUsesFullCooldownRatherThanRemainingTime()
    {
        Assert.That(Rules, Is.Not.Null, "尚未提供技能槽徑向冷卻規則。");
        var ratio = Rules.GetMethod("RemainingRatio");
        Assert.That((float)ratio.Invoke(null, new object[] { 3f, 12f }), Is.EqualTo(.25f));
        Assert.That((float)ratio.Invoke(null, new object[] { 0f, 12f }), Is.Zero);
    }

    [TestCase(float.NaN, 12f)]
    [TestCase(3f, float.PositiveInfinity)]
    [TestCase(3f, 0f)]
    public void InvalidDurationCannotBecomeAnInventedCountdown(float remaining, float duration)
    {
        Assert.That(Rules, Is.Not.Null);
        Assert.That((float)Rules.GetMethod("RemainingRatio").Invoke(null, new object[] { remaining, duration }), Is.Zero);
    }

    [TestCase(true, false, true, true)]
    [TestCase(true, false, false, false)]
    [TestCase(false, false, true, false)]
    [TestCase(true, true, true, false)]
    public void ReadyFlashOnlyOccursOnObservedCooldownCompletion(bool wasCooling, bool cooling, bool usable, bool expected)
    {
        Assert.That(Rules, Is.Not.Null);
        Assert.That((bool)Rules.GetMethod("ShouldFlash").Invoke(null, new object[] { wasCooling, cooling, usable }), Is.EqualTo(expected));
    }

    [Test]
    public void CooldownSourcesExposeAuthoritativeDuration()
    {
        foreach (Type type in new[] { typeof(PlayerActiveAbilityBase), typeof(SupportAerialAbility), typeof(TankAirDashAbility), typeof(PlayerGrappleSpikeAbility) })
            Assert.That(type.GetProperty("CooldownDurationSeconds"), Is.Not.Null, type.Name);
        foreach (Type type in new[] { typeof(PlayerActiveAbilityBase), typeof(SupportAerialAbility), typeof(TankAirDashAbility) })
        {
            var field = type.GetProperty("CooldownDuration", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, type.Name);
            Assert.That(field.GetCustomAttribute<Fusion.NetworkedAttribute>(), Is.Not.Null);
        }
    }

    private static object NewPulse()
    {
        Type type = typeof(Player).Assembly.GetType("AbilitySlotReadyPulse");
        Assert.That(type, Is.Not.Null);
        return Activator.CreateInstance(type);
    }
    private static float Pulse(object pulse, object source, bool cooling, bool usable, float time) =>
        (float)pulse.GetType().GetMethod("Sample").Invoke(pulse, new object[] { source, cooling, usable, time, .32f });

    [Test]
    public void CompletionFlashesOnceAndDecaysWithoutRestarting()
    {
        object pulse = NewPulse(), source = new object();
        Assert.That(Pulse(pulse, source, true, false, 1f), Is.Zero);
        Assert.That(Pulse(pulse, source, false, true, 2f), Is.EqualTo(1f));
        Assert.That(Pulse(pulse, source, false, true, 2.16f), Is.EqualTo(.5f).Within(.001f));
        Assert.That(Pulse(pulse, source, false, true, 3f), Is.Zero);
    }

    [Test]
    public void SwitchingOwnerOrHidingNeverReplaysCooldownCompletion()
    {
        object pulse = NewPulse(), source = new object();
        Pulse(pulse, source, true, false, 1f);
        Assert.That(Pulse(pulse, new object(), false, true, 2f), Is.Zero);
        Pulse(pulse, source, true, false, 3f);
        pulse.GetType().GetMethod("Reset").Invoke(pulse, null);
        Assert.That(Pulse(pulse, source, false, true, 4f), Is.Zero);
    }

    [Test]
    public void BlockedCompletionDoesNotFlashLaterWhenControlReturns()
    {
        object pulse = NewPulse(), source = new object();
        Pulse(pulse, source, true, false, 1f);
        Assert.That(Pulse(pulse, source, false, false, 2f), Is.Zero);
        Assert.That(Pulse(pulse, source, false, true, 3f), Is.Zero);
    }

    [TestCase(1f, 6400f)]
    [TestCase(.75f, 4800f)]
    [TestCase(.5f, 3200f)]
    [TestCase(.25f, 1600f)]
    [TestCase(0f, 0f)]
    public void RadialShadeCoversWholeSquareAndRetreatsClockwise(float ratio, float expectedArea)
    {
        Type type = typeof(Player).Assembly.GetType("AbilitySlotStateGraphic");
        Assert.That(type, Is.Not.Null);
        var root = new GameObject("Skill mask", typeof(RectTransform));
        try
        {
            var graphic = root.AddComponent(type) as Graphic;
            graphic.rectTransform.sizeDelta = new Vector2(80, 80);
            type.GetMethod("SetState").Invoke(graphic, new object[] { ratio, 0f, 0f, 3f, Color.black, Color.yellow, Color.white });
            using (var mesh = new VertexHelper())
            {
                type.GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { mesh });
                var vertices = new System.Collections.Generic.List<UIVertex>(); mesh.GetUIVertexStream(vertices);
                float area = 0;
                for (int i = 0; i < vertices.Count; i += 3)
                {
                    Vector3 a = vertices[i].position, b = vertices[i + 1].position, c = vertices[i + 2].position;
                    area += Vector3.Cross(b - a, c - a).magnitude * .5f;
                }
                Assert.That(area, Is.EqualTo(expectedArea).Within(.1f));
                if (ratio == .25f) foreach (UIVertex vertex in vertices)
                {
                    Assert.That(vertex.position.x, Is.LessThanOrEqualTo(.001f));
                    Assert.That(vertex.position.y, Is.GreaterThanOrEqualTo(-.001f));
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase("Recast", true, false, "再次施放")]
    [TestCase("Recast", false, false, "待命")]
    [TestCase("Active", true, true, "再次按下結束")]
    [TestCase("Active", true, false, "效果持續")]
    [TestCase("Awaiting", false, false, "等待拾取")]
    [TestCase("Cooling", false, false, "")]
    public void RecastAndToggleLabelsNeverPretendToBeCooldown(string phase, bool canPress, bool canEnd, string label)
    {
        Type type = typeof(Player).Assembly.GetType("AbilitySlotPresentationState");
        Assert.That(type, Is.Not.Null);
        object state = Activator.CreateInstance(type);
        var phaseField = type.GetField("Phase"); phaseField.SetValue(state, Enum.Parse(phaseField.FieldType, phase));
        type.GetField("CanPress").SetValue(state, canPress); type.GetField("CanEnd").SetValue(state, canEnd);
        Assert.That(type.GetProperty("Label").GetValue(state), Is.EqualTo(label));
    }

    [Test]
    public void ExistingPrefabTemplateCreatesWholeSlotOverlayWithoutChangingSourceArtOrKeyLayout()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var original = prefab.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
        var serialized = new UnityEditor.SerializedObject(original);
        var template = (GameObject)serialized.FindProperty("slotTemplate").objectReferenceValue;
        var root = new GameObject("HUD fixture", typeof(RectTransform));
        try
        {
            var hud = root.AddComponent<LocalPlayerBattleAbilityHUD>(); hud.enabled = false;
            typeof(LocalPlayerBattleAbilityHUD).GetField("slotTemplate", flags).SetValue(hud, template);
            typeof(LocalPlayerBattleAbilityHUD).GetField("slotsRoot", flags).SetValue(hud, root.transform);
            object view = typeof(LocalPlayerBattleAbilityHUD).GetMethod("GetView", flags).Invoke(hud, new object[] { 0 });
            var slot = (GameObject)view.GetType().GetField("Root").GetValue(view);
            var overlay = slot.transform.Find("AbilityStateOverlay") as RectTransform;
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.anchorMin, Is.EqualTo(Vector2.zero)); Assert.That(overlay.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(overlay.offsetMin, Is.EqualTo(Vector2.zero)); Assert.That(overlay.offsetMax, Is.EqualTo(Vector2.zero));
            Assert.That(overlay.GetComponent<Graphic>().raycastTarget, Is.False);
            Assert.That(slot.transform.Find("CooldownShade").gameObject.activeSelf, Is.False);
            Assert.That(slot.transform.Find("CooldownText").GetComponent<TMPro.TMP_Text>().alignment, Is.EqualTo(TMPro.TextAlignmentOptions.Center));
            Assert.That(slot.transform.Find("Icon").GetComponent<Image>().sprite == template.transform.Find("Icon").GetComponent<Image>().sprite, Is.True);
            Assert.That(((RectTransform)slot.transform.Find("Key")).anchoredPosition, Is.EqualTo(((RectTransform)template.transform.Find("Key")).anchoredPosition));
            Assert.That(typeof(LocalPlayerBattleAbilityHUD).GetMethod("GetView", flags).Invoke(hud, new object[] { 0 }), Is.SameAs(view));
            Assert.That(root.transform.childCount, Is.EqualTo(1), "重用槽位不得重複建立外框或提示");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
