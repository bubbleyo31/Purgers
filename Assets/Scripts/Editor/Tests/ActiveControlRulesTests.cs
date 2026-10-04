using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class ActiveControlRulesTests
{
    private static object Rule(string name, params object[] arguments)
    {
        Type type = typeof(Player).Assembly.GetType("ActiveControlRules");
        Assert.That(type, Is.Not.Null, "主動控制規則尚未實作。");
        MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, name);
        return method.Invoke(null, arguments);
    }

    [Test]
    public void DashUsesHorizontalCharacterYawAndNormalizesDiagonalInput()
    {
        Vector3 direction = (Vector3)Rule("ResolveDashDirection", new Vector2(1f, 1f), 90f);
        Assert.That(direction.y, Is.EqualTo(0f).Within(0.00001f));
        Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.00001f));
        Assert.That(Vector3.Distance(direction, new Vector3(1f, 0f, -1f).normalized), Is.LessThan(0.00001f));
    }

    [Test]
    public void DashWithoutDirectionUsesCharacterForward()
    {
        Vector3 direction = (Vector3)Rule("ResolveDashDirection", Vector2.zero, 90f);
        Assert.That(Vector3.Distance(direction, Vector3.right), Is.LessThan(0.00001f));
    }

    [TestCase(4f, 30f, 0.1f, float.PositiveInfinity, 0.02f, 3f)]
    [TestCase(0.5f, 30f, 0.1f, float.PositiveInfinity, 0.02f, 0.5f)]
    [TestCase(4f, 30f, 0.1f, 1f, 0.02f, 0.98f)]
    [TestCase(4f, 30f, 0.1f, 0.01f, 0.02f, 0f)]
    public void TravelNeverOvershootsRemainingDistanceOrCollision(float remaining, float speed,
        float deltaTime, float hitDistance, float skin, float expected)
    {
        float actual = (float)Rule("LimitTravel", remaining, speed, deltaTime, hitDistance, skin);
        Assert.That(actual, Is.EqualTo(expected).Within(0.00001f));
    }

    [TestCase(8f, .25f, .5f, .14583333f, false)]
    [TestCase(8f, .25f, .1f, .08f, true)]
    [TestCase(.05f, .25f, float.PositiveInfinity, .05f, false)]
    [TestCase(8f, 1f, .5f, .48f, true)]
    public void SlowedDashUsesFinalTravelForObstacleCompletion(float remaining, float multiplier,
        float hitDistance, float expectedTravel, bool expectedObstacle)
    {
        object result = Rule("ResolveDashStep", remaining, 35f, 1f / 60f, multiplier, hitDistance, .02f);
        Type type = result.GetType();
        float velocity = (float)type.GetField("Velocity").GetValue(result);
        bool reached = (bool)type.GetField("ObstacleReached").GetValue(result);
        Assert.That(velocity * (1f / 60f) * multiplier, Is.EqualTo(expectedTravel).Within(.00001f));
        Assert.That(reached, Is.EqualTo(expectedObstacle));
    }

    [Test]
    public void FloorAndSeparatingContactDoNotCountAsWallImpact()
    {
        Assert.That((bool)Rule("IsWallImpact", Vector3.up, Vector3.forward), Is.False);
        Assert.That((bool)Rule("IsWallImpact", Vector3.forward, Vector3.forward), Is.False);
        Assert.That((bool)Rule("IsWallImpact", Vector3.back, Vector3.forward), Is.True);
    }

    [TestCase(110, 100, 1, 111)]
    [TestCase(100, 100, 1, 100)]
    [TestCase(90, 100, 10, 90)]
    [TestCase(0, 100, 10, 0)]
    public void PauseExtendsPendingDeadlineWithoutRevivingExpiredOrMissingTimers(
        int targetTick, int currentTick, int pausedTicks, int expected)
    {
        Assert.That((int)Rule("ExtendPendingDeadline", targetTick, currentTick, pausedTicks), Is.EqualTo(expected));
    }

    [TestCase(typeof(EnemyMeleeSwingAttack))]
    [TestCase(typeof(EnemyMeleeDashAttack))]
    [TestCase(typeof(EnemyRangedProjectileAttack))]
    [TestCase(typeof(EnemyRangedBeamAttack))]
    public void EveryExistingAttackOwnsItsPauseTimerImplementation(Type optionType)
    {
        MethodInfo method = optionType.GetMethod("PauseSimulationTick", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null, "停止 TickOption 仍會讓 Fusion timer 到期。");
        Assert.That(method.DeclaringType, Is.EqualTo(optionType), "每種攻擊必須明確保留自己的階段與附加 timer。");
    }
}
