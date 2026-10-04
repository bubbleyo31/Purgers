using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class BulletTimeDamageTests
{
    private GameObject source;
    private GameObject target;
    private BulletTimeTestSource sourceBehaviour;
    private BulletTimeTestReceiver receiver;
    private BulletTimeTestIncoming incoming;

    [SetUp]
    public void SetUp()
    {
        source = new GameObject("BulletTime test source");
        target = new GameObject("BulletTime test target");
        sourceBehaviour = source.AddComponent<BulletTimeTestSource>();
        receiver = target.AddComponent<BulletTimeTestReceiver>();
        incoming = target.AddComponent<BulletTimeTestIncoming>();
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(source);
        if (target != null) UnityEngine.Object.DestroyImmediate(target);
    }

    private MethodInfo RequiredMethod(string name)
    {
        var method = typeof(DamageReceiverUtility).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "Deferred damage must use the shared pipeline: " + name);
        return method;
    }

    private object Prepare(GameObject hitObject)
    {
        var request = new DamageRequest { RequestedDamage = 10f, BaseDamage = 10f,
            DamageType = DamageType.Bullet, SourceObject = source };
        object[] args = { hitObject, request, null, null };
        Assert.That((bool)RequiredMethod("TryPrepareDamage").Invoke(null, args), Is.True);
        return args[2];
    }

    private bool Resolve(object pending, out DamageResult result)
    {
        object[] args = { pending, null };
        bool resolved = (bool)RequiredMethod("TryResolvePreparedDamage").Invoke(null, args);
        result = (DamageResult)args[1];
        return resolved;
    }

    [Test]
    public void CapturesOutgoingOnceAndAppliesCurrentIncomingOnlyOnFlush()
    {
        sourceBehaviour.Multiplier = 2f;
        incoming.Multiplier = 0.5f;
        object pending = Prepare(target);
        Assert.That(sourceBehaviour.Modifications, Is.EqualTo(1));
        Assert.That(incoming.Modifications, Is.Zero);
        Assert.That(receiver.Calls, Is.Zero);
        Assert.That(sourceBehaviour.Notifications, Is.Zero);

        sourceBehaviour.Multiplier = 9f;
        incoming.Multiplier = 0.25f;
        Assert.That(Resolve(pending, out var result), Is.True);
        Assert.That(result.AppliedDamage, Is.EqualTo(5f));
        Assert.That(result.Request.BaseDamage, Is.EqualTo(10f));
        Assert.That(sourceBehaviour.Modifications, Is.EqualTo(1));
        Assert.That(incoming.Modifications, Is.EqualTo(1));
        Assert.That(sourceBehaviour.Notifications, Is.EqualTo(1));
        Assert.That(receiver.Calls, Is.EqualTo(1));
    }

    [Test]
    public void APreparedHitCannotResolveOrNotifyTwice()
    {
        object pending = Prepare(target);
        Assert.That(Resolve(pending, out _), Is.True);
        Assert.That(Resolve(pending, out _), Is.False);
        Assert.That(receiver.Calls, Is.EqualTo(1));
        Assert.That(sourceBehaviour.Notifications, Is.EqualTo(1));
    }

    [Test]
    public void FullBlockIsEvaluatedAtFlushAndNotifiesOnce()
    {
        object pending = Prepare(target);
        incoming.Multiplier = 0f;
        Assert.That(Resolve(pending, out var result), Is.True);
        Assert.That(result.RejectReason, Is.EqualTo(DamageRejectReason.FullyBlocked));
        Assert.That(receiver.Calls, Is.Zero);
        Assert.That(sourceBehaviour.Notifications, Is.EqualTo(1));
        Assert.That(Resolve(pending, out _), Is.False);
    }

    [Test]
    public void DestroyedTargetIsDiscardedWithoutPhantomDamageOrNotification()
    {
        object pending = Prepare(target);
        UnityEngine.Object.DestroyImmediate(target);
        Assert.That(Resolve(pending, out var result), Is.False);
        Assert.That(result.Accepted, Is.False);
        Assert.That(sourceBehaviour.Notifications, Is.Zero);
    }

    [Test]
    public void NoReceiverDoesNotRunOutgoingModifiers()
    {
        var empty = new GameObject("No receiver");
        try
        {
            object[] args = { empty, new DamageRequest { RequestedDamage = 10f, SourceObject = source }, null, null };
            Assert.That((bool)RequiredMethod("TryPrepareDamage").Invoke(null, args), Is.False);
            Assert.That(sourceBehaviour.Modifications, Is.Zero);
            Assert.That(sourceBehaviour.Notifications, Is.Zero);
        }
        finally { UnityEngine.Object.DestroyImmediate(empty); }
    }
}

public sealed class BulletTimeTestSource : MonoBehaviour, IOutgoingDamageModifier, IOutgoingDamageResultListener
{
    public float Multiplier = 1f;
    public int Modifications;
    public int Notifications;
    public void ModifyOutgoingDamage(ref DamageRequest request)
    { Modifications++; request.RequestedDamage *= Multiplier; }
    public void OnOutgoingDamageResolved(in DamageResult result) { Notifications++; }
}

public sealed class BulletTimeTestIncoming : MonoBehaviour, IDamageRequestModifier
{
    public float Multiplier = 1f;
    public int Modifications;
    public void ModifyDamageRequest(ref DamageRequest request)
    {
        Modifications++;
        float before = request.RequestedDamage;
        request.RequestedDamage *= Multiplier;
        request.BlockedDamage += before - request.RequestedDamage;
    }
}

public sealed class BulletTimeTestReceiver : MonoBehaviour, IDamageReceiver
{
    public int Calls;
    public DamageResult ReceiveDamage(DamageRequest request)
    { Calls++; return DamageResult.CreateApplied(request, gameObject, request.RequestedDamage); }
}
