using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class TankPreparedDashTests
{
    private static Type Rules {
        get {
            var type = typeof(TankMeleeCombo).Assembly.GetType("TankPreparedDashRules");
            Assert.That(type, Is.Not.Null, "Prepared dash must have release eligibility and stop geometry.");
            return type;
        }
    }
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public void OnlyReleaseFromFreezeCanDash(bool frozen, bool held, bool expected)
    {
        Assert.That(Rules.GetMethod("ShouldDash").Invoke(null, new object[] {frozen, held}), Is.EqualTo(expected));
    }
    [Test] public void StopPointKeepsAimHeightAndStandsBeforeSurface()
    {
        var result = (Vector3)Rules.GetMethod("Destination").Invoke(null,
            new object[] {new Vector3(0, 1.6f, 10), Vector3.forward, 1f, new Vector3(0,1.6f,0)});
        Assert.That(result, Is.EqualTo(new Vector3(0,0,9)));
    }
    [Test] public void LastStepCannotOvershoot()
    {
        var velocity = (Vector3)Rules.GetMethod("Velocity").Invoke(null,
            new object[] {Vector3.zero, new Vector3(0,0,.1f), 40f, .02f});
        Assert.That(velocity.z, Is.EqualTo(5f).Within(.001f));
    }
    [Test] public void CrosshairRestoresAuthoredColorAndAlphaOnDisable()
    {
        var go = new GameObject("crosshair test", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        try {
            var graphic = go.GetComponent<UnityEngine.UI.Image>();
            var authored = new Color(.2f,.5f,.8f,.4f);
            graphic.color = authored;
            var ui = go.AddComponent<LocalPlayerEnemyCrosshair>();
            typeof(LocalPlayerEnemyCrosshair).GetMethod("OnEnable", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
            ui.ApplyTargetState(true);
            Assert.That(graphic.color, Is.EqualTo(new Color(1,0,0,.4f)));
            ui.ApplyTargetState(false);
            Assert.That(graphic.color, Is.EqualTo(authored));
            ui.ApplyTargetState(true);
            typeof(LocalPlayerEnemyCrosshair).GetMethod("OnDisable", BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,null);
            Assert.That(graphic.color, Is.EqualTo(authored));
        } finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    [Test] public void WorldObjectOnHitMaskIsNotAnEnemy()
    {
        var go = new GameObject("world on hit mask");
        try {
            go.layer = 9;
            Assert.That(PlayerEnemyAimQuery.TryGetLivingEnemy(go, 1 << 9, out _), Is.False);
            Assert.That(PlayerEnemyAimQuery.TryGetLivingEnemy(null, ~0, out _), Is.False);
        } finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    [Test] public void AngledDashKeepsSourceEyeOffsetInsteadOfPuttingFeetOnEnemyHead()
    {
        Vector3 direction = new Vector3(0,1,1).normalized;
        var point = new Vector3(0,5,10);
        var result = TankPreparedDashRules.Destination(point, direction, 1.2f, Vector3.up*1.6f);
        Assert.That(Vector3.Distance(result+Vector3.up*1.6f,point), Is.EqualTo(1.2f).Within(.00001f));
        Assert.That(Vector3.Dot(point-result-Vector3.up*1.6f,direction),Is.GreaterThan(0));
    }
}
