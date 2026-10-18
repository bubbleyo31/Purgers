using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public class EnemyReflectorRulesTests
{
    private static object Rule(string name, params object[] args)
    {
        var type = typeof(EnemyProjectile).Assembly.GetType("EnemyReflectorRules");
        Assert.That(type, Is.Not.Null, "尚未接入反彈護盾敵人的共用規則");
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [TestCase(false, 2f, false)]
    [TestCase(true, 8f, false)]
    [TestCase(true, 2f, true)]
    public void HomingRequiresTargetAndNearDistance(bool target, float distance, bool expected)
    { Assert.That(Rule("ShouldHome", target, false, distance, 3f), Is.EqualTo(expected)); }
    [Test] public void ReflectedShotNeverHomes()
    { Assert.That(Rule("ShouldHome", true, true, 1f, 3f), Is.False); }
    [Test] public void HomingTurnIsBounded()
    {
        var direction = (Vector3)Rule("Turn", Vector3.forward, Vector3.right, 90f, .1f);
        Assert.That(Vector3.Angle(Vector3.forward, direction), Is.EqualTo(9f).Within(.01f));
    }
    [TestCase(false, 0f, false)]
    [TestCase(true, 0f, true)]
    [TestCase(true, 180f, false)]
    [TestCase(true, 90f, false)]
    public void OnlyRearIncomingDamagePassesDuringOpening(bool open, float angle, bool expected)
    {
        Vector3 incoming = Quaternion.Euler(0, angle, 0) * Vector3.forward;
        Assert.That(Rule("AllowsDamage", open, Vector3.forward, incoming, 120f), Is.EqualTo(expected));
    }
    [Test] public void UnknownDirectionCannotBypassShield()
    { Assert.That(Rule("AllowsDamage", true, Vector3.forward, Vector3.zero, 120f), Is.False); }
    [Test] public void ReflectionPaddingCatchesNearMissWithoutEnlargingDamageRadius()
    {
        Assert.That(Rule("WithinMeleeReflection",new Vector3(0,0,3.4f),Vector3.forward,3f,2f,120f,0f),Is.False);
        Assert.That(Rule("WithinMeleeReflection",new Vector3(0,0,3.4f),Vector3.forward,3f,2f,120f,.65f),Is.True);
        Assert.That(Rule("WithinMeleeReflection",new Vector3(0,0,4f),Vector3.forward,3f,2f,120f,.65f),Is.False);
    }
    [Test] public void ShotSphereIntersectionStopsAtActualSegmentEnd()
    {
        object[] args={Vector3.zero,Vector3.forward*4,Vector3.forward*5,.25f,0f};
        Assert.That(Rule("IntersectsSphereSegment",args),Is.False);
        args[1]=Vector3.forward*6;Assert.That(Rule("IntersectsSphereSegment",args),Is.True);
        Assert.That((float)args[4],Is.EqualTo(4.75f).Within(.001f));
    }
}
