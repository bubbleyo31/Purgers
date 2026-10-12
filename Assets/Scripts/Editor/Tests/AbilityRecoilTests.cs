using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>各技能後座力的無副作用預設、獨立時機與完整視線方向。</summary>
[Category("PurgersRegression")]
public sealed class AbilityRecoilTests
{
    private object Settings()
    {
        var type = typeof(PlayerAbilityRuntime).Assembly.GetType("PlayerAbilityRecoilSettings");
        Assert.That(type, Is.Not.Null, "所有 E Runtime 必須共用後座力設定。");
        return Activator.CreateInstance(type);
    }
    private void Set(object s, string name, object value)
    {
        var f = s.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(f, Is.Not.Null, name);
        f.SetValue(s, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : value);
    }
    private object Resolve(object s, string name, int timing, float pitch = 0, float yaw = 0)
    {
        var m = s.GetType().GetMethod(name);
        Assert.That(m, Is.Not.Null, name);
        var t = Enum.ToObject(m.GetParameters()[0].ParameterType, timing);
        return m.Invoke(s, name == "ResolveCamera" ? new[] { t } : new[] { t, (object)pitch, yaw });
    }
    [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void BothTimingsTriggerEachStageAndInstantOnlyOnce(int trigger)
    {
        var s=Settings(); Set(s,"cameraEnabled",true); Set(s,"cameraTiming",3); Set(s,"cameraAngles",new Vector2(-2,1));
        Set(s,"movementEnabled",true); Set(s,"movementTiming",3); Set(s,"movementSpeed",5f);
        Assert.That(Resolve(s,"ResolveCamera",trigger),Is.EqualTo(new Vector2(-2,1)));
        Assert.That(Resolve(s,"ResolveMovement",trigger),Is.EqualTo(Vector3.back*5));
    }
    [Test] public void TimingRetainsOldValuesAndSupportsFlags()
    {
        Assert.That(Attribute.IsDefined(typeof(PlayerAbilityRecoilTiming),typeof(FlagsAttribute)),Is.True);
        Assert.That((int)PlayerAbilityRecoilTiming.BeforeCast,Is.EqualTo(1));
        Assert.That((int)PlayerAbilityRecoilTiming.AfterCast,Is.EqualTo(2));
    }
    [Test] public void ExistingAssetsRemainInertUntilConfigured()
    {
        var s = Settings();
        foreach (int t in new[] { 1, 2, 3 }) {
            Assert.That(Resolve(s, "ResolveCamera", t), Is.EqualTo(Vector2.zero));
            Assert.That(Resolve(s, "ResolveMovement", t), Is.EqualTo(Vector3.zero));
        }
    }
    [Test] public void IndependentTimings()
    {
        var s = Settings();
        Set(s, "cameraEnabled", true); Set(s, "cameraTiming", 1); Set(s, "cameraAngles", new Vector2(-3, 2));
        Set(s, "movementEnabled", true); Set(s, "movementTiming", 2); Set(s, "movementSpeed", 4f);
        Assert.That(Resolve(s, "ResolveCamera", 1), Is.EqualTo(new Vector2(-3, 2)));
        Assert.That(Resolve(s, "ResolveCamera", 2), Is.EqualTo(Vector2.zero));
        Assert.That(Resolve(s, "ResolveMovement", 1), Is.EqualTo(Vector3.zero));
        Assert.That(Resolve(s, "ResolveMovement", 2), Is.EqualTo(Vector3.back * 4));
    }
    [TestCase(-45f, 90f)] [TestCase(90f, 0f)] [TestCase(0f, 180f)]
    public void DefaultDirectionOpposesFullAim(float pitch, float yaw)
    {
        var s = Settings(); Set(s, "movementEnabled", true); Set(s, "movementTiming", 2); Set(s, "movementSpeed", 5f);
        var actual = (Vector3)Resolve(s, "ResolveMovement", 2, pitch, yaw);
        Assert.That(Vector3.Distance(actual, Quaternion.Euler(pitch, yaw, 0) * Vector3.back * 5), Is.LessThan(.0001f));
    }
    [Test] public void DirectionLengthDoesNotMultiplySpeedAndZeroFallsBack()
    {
        var s = Settings(); Set(s, "movementEnabled", true); Set(s, "movementTiming", 1); Set(s, "movementSpeed", 4f);
        Set(s, "movementLocalDirection", new Vector3(10, 5, -10));
        Assert.That(((Vector3)Resolve(s, "ResolveMovement", 1)).magnitude, Is.EqualTo(4).Within(.0001f));
        Set(s, "movementLocalDirection", Vector3.zero);
        Assert.That(Resolve(s, "ResolveMovement", 1), Is.EqualTo(Vector3.back * 4));
    }
    [TestCase(1)] [TestCase(2)] public void InstantSkillsResolveEitherTimingOnce(int configured)
    {
        var s = Settings(); Set(s, "cameraEnabled", true); Set(s, "cameraTiming", configured); Set(s, "cameraAngles", Vector2.one);
        Assert.That(Resolve(s, "ResolveCamera", 3), Is.EqualTo(Vector2.one));
        Assert.That(Resolve(s, "ResolveCamera", 0), Is.EqualTo(Vector2.zero));
    }
    [Test] public void UnconfiguredDisabledAndNegativeSpeedCannotKick()
    {
        var s = Settings(); Set(s, "cameraEnabled", true); Set(s, "cameraAngles", Vector2.one);
        Set(s, "movementEnabled", true); Set(s, "movementSpeed", 5f);
        Assert.That(Resolve(s, "ResolveCamera", 3), Is.EqualTo(Vector2.zero));
        Assert.That(Resolve(s, "ResolveMovement", 3), Is.EqualTo(Vector3.zero));
        Set(s, "movementTiming", 1); Set(s, "movementSpeed", -5f);
        Assert.That(Resolve(s, "ResolveMovement", 1), Is.EqualTo(Vector3.zero));
        Set(s, "cameraTiming", 1); Set(s, "cameraEnabled", false);
        Assert.That(Resolve(s, "ResolveCamera", 1), Is.EqualTo(Vector2.zero));
    }
}
