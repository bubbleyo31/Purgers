using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class ActiveAbilityHudTests
{
    private static object Timed(PlayerActiveAbilityPhase phase, float remaining, float duration)
    {
        Type rules = typeof(Player).Assembly.GetType("ActiveAbilityHudRules");
        Assert.That(rules, Is.Not.Null, "尚未提供技能時間條快照規則。");
        MethodInfo create = rules.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.That(create, Is.Not.Null);
        return create.Invoke(null, new object[] { phase, remaining, duration });
    }

    private static T Value<T>(object snapshot, string name) =>
        (T)snapshot.GetType().GetProperty(name).GetValue(snapshot);

    [TestCase(PlayerActiveAbilityPhase.Casting, .5f, 2f, .25f, "施放準備")]
    [TestCase(PlayerActiveAbilityPhase.Armed, 2f, 5f, .4f, "待命")]
    [TestCase(PlayerActiveAbilityPhase.Active, 1f, 4f, .25f, "效果持續")]
    public void TimedBarUsesAuthoritativeDurationInsteadOfFirstObservedRemaining(
        PlayerActiveAbilityPhase phase, float remaining, float duration, float ratio, string label)
    {
        object snapshot = Timed(phase, remaining, duration);
        Assert.That(Value<bool>(snapshot, "IsVisible"), Is.True);
        Assert.That(Value<float>(snapshot, "NormalizedRemaining"), Is.EqualTo(ratio).Within(.00001f));
        Assert.That(Value<string>(snapshot, "StateLabel"), Is.EqualTo(label));
    }

    [TestCase(PlayerActiveAbilityPhase.Ready, 3f, 5f)]
    [TestCase(PlayerActiveAbilityPhase.AwaitingPickup, 10f, 10f)]
    [TestCase(PlayerActiveAbilityPhase.Dashing, 80f, 80f)]
    [TestCase(PlayerActiveAbilityPhase.Active, 0f, 2f)]
    [TestCase(PlayerActiveAbilityPhase.Casting, 0f, 0f)]
    public void CooldownWorldPickupAndDashSafetyTimerAreNotSelfBuffBars(
        PlayerActiveAbilityPhase phase, float remaining, float duration)
    {
        Assert.That(Value<bool>(Timed(phase, remaining, duration), "IsVisible"), Is.False);
    }

    [Test]
    public void SkillHudContractExposesTimedSnapshotAndSynchronizedDuration()
    {
        Assert.That(typeof(IPlayerAbilityHudState).GetProperty("TimedHudState"), Is.Not.Null);
        PropertyInfo duration = typeof(PlayerActiveAbilityBase).GetProperty("PhaseDurationSeconds",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(duration, Is.Not.Null, "晚加入不能以本機首次讀到的剩餘秒數重建總長。");
        Assert.That(duration.GetCustomAttribute<Fusion.NetworkedAttribute>(), Is.Not.Null);
    }

    [Test]
    public void ShieldUsesTwoWholeCellsForTwentyFiveAndForty()
    {
        var root=new GameObject("Whole shield",typeof(RectTransform));
        var source=new GameObject("HP",typeof(RectTransform),typeof(Slider));
        try{
            var view=root.AddComponent<LocalHealthSegmentView>();view.rectTransform.sizeDelta=new Vector2(400,28);
            var slider=source.GetComponent<Slider>();slider.maxValue=100;slider.value=40;
            typeof(LocalHealthSegmentView).GetField("healthSource",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,slider);
            view.SendMessage("Update");view.SetShieldHealth(25);
            Assert.That(view.VisibleShieldCells,Is.EqualTo(2));Assert.That(view.ShieldStartCell,Is.EqualTo(2));
            view.SetShieldHealth(40);Assert.That(view.VisibleShieldCells,Is.EqualTo(2));
            view.SetShieldHealth(0);Assert.That(view.VisibleShieldCells,Is.Zero);
        }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(source);}
    }

    private static float MaximumX(VertexHelper mesh)
    {
        float maximum = float.NegativeInfinity;
        var vertex = new UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            maximum = Mathf.Max(maximum, vertex.position.x);
        }
        return maximum;
    }
}



