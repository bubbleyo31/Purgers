using System.Collections.Generic;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class CombatRegressionTests
{
    private readonly List<GameObject> objects = new List<GameObject>();

    private GameObject NewObject(string name)
    {
        var instance = new GameObject(name);
        objects.Add(instance);
        return instance;
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void InvalidMeleeQueryClearsBothPreviousResults()
    {
        var oldKill = new DamageResult { Accepted = true, KilledTarget = true };
        var resolved = new List<DamageResult> { oldKill };
        var confirmed = new List<DamageResult> { oldKill };
        new MeleeDamageResolver().Resolve(default, resolved, confirmed);
        Assert.That(resolved, Is.Empty);
        Assert.That(confirmed, Is.Empty);
    }

    [Test]
    public void InvalidMeleeQueryAcceptsNullOutputLists()
    {
        Assert.DoesNotThrow(() => new MeleeDamageResolver().Resolve(default, null, null));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReceiverResultNotifiesSourceOnceAfterNormalization(bool lethal)
    {
        var source = NewObject("Source").AddComponent<CombatRegressionProbe>();
        source.OutgoingMultiplier = 2f;
        var target = NewObject("Target").AddComponent<CombatRegressionProbe>();
        target.KillTarget = lethal;
        var request = new DamageRequest { SourceObject = source.gameObject, BaseDamage = 10f, RequestedDamage = 10f };
        Assert.That(DamageReceiverUtility.TryApplyDamage(target.gameObject, request, out var result), Is.True);
        Assert.That(target.ReceiveCount, Is.EqualTo(1));
        Assert.That(source.NotificationCount, Is.EqualTo(1));
        Assert.That(result.AppliedDamage, Is.EqualTo(20f));
        Assert.That(source.LastResult.Request.BaseDamage, Is.EqualTo(10f));
        Assert.That(source.LastResult.Request.RequestedDamage, Is.EqualTo(20f));
        Assert.That(source.LastResult.TargetObject, Is.EqualTo(target.gameObject));
        Assert.That(source.LastResult.KilledTarget, Is.EqualTo(lethal));
    }

    [TestCase(true, DamageRejectReason.FullyBlocked, 0)]
    [TestCase(false, DamageRejectReason.Invulnerable, 1)]
    public void BlockedOrRejectedResultNotifiesExactlyOnce(bool blocked, DamageRejectReason reason, int receiveCount)
    {
        var source = NewObject("Source").AddComponent<CombatRegressionProbe>();
        var target = NewObject("Target").AddComponent<CombatRegressionProbe>();
        target.BlockFully = blocked;
        target.RejectDamage = !blocked;
        var request = new DamageRequest { SourceObject = source.gameObject, BaseDamage = 10f, RequestedDamage = 10f };
        Assert.That(DamageReceiverUtility.TryApplyDamage(target.gameObject, request, out var result), Is.True);
        Assert.That(result.Accepted, Is.False);
        Assert.That(source.NotificationCount, Is.EqualTo(1));
        Assert.That(source.LastResult.RejectReason, Is.EqualTo(reason));
        Assert.That(target.ReceiveCount, Is.EqualTo(receiveCount));
    }

    [Test]
    public void NoReceiverDoesNotRunOutgoingModifiersOrNotify()
    {
        var source = NewObject("Source").AddComponent<CombatRegressionProbe>();
        source.OutgoingMultiplier = 2f;
        var request = new DamageRequest { SourceObject = source.gameObject, RequestedDamage = 10f };
        Assert.That(DamageReceiverUtility.TryApplyDamage(NewObject("Wall"), request, out var result), Is.False);
        Assert.That(result.Request.RequestedDamage, Is.EqualTo(10f));
        Assert.That(source.NotificationCount, Is.Zero);
    }

    [Test]
    public void DisabledSourceDoesNotModifyOrReceiveNotification()
    {
        var source = NewObject("Source").AddComponent<CombatRegressionProbe>();
        source.OutgoingMultiplier = 2f;
        source.enabled = false;
        var target = NewObject("Target").AddComponent<CombatRegressionProbe>();
        var request = new DamageRequest { SourceObject = source.gameObject, RequestedDamage = 10f };
        DamageReceiverUtility.TryApplyDamage(target.gameObject, request, out var result);
        Assert.That(result.AppliedDamage, Is.EqualTo(10f));
        Assert.That(source.NotificationCount, Is.Zero);
    }

    [TestCase(0f, 1f)]
    [TestCase(10f, 1f)]
    [TestCase(15f, 0.625f)]
    [TestCase(20f, 0.25f)]
    [TestCase(30f, 0.25f)]
    public void WeaponFalloffPreservesBoundaryAndInterpolation(float distance, float expected)
    {
        Assert.That(WeaponHitUtility.CalculateDistanceDamageMultiplier(distance, 10f, 20f, 0.25f),
            Is.EqualTo(expected).Within(0.00001f));
    }

    [Test]
    public void NearestHitSkipsOwnerAndKeepsWorldOcclusion()
    {
        var owner = NewObject("Owner").AddComponent<NetworkObject>();
        var ownCollider = NewObject("OwnCollider");
        ownCollider.transform.SetParent(owner.transform);
        var wall = NewObject("Wall");
        var enemy = NewObject("Enemy");
        var hits = new List<LagCompensatedHit>
        {
            new LagCompensatedHit { GameObject = ownCollider, Distance = 1f },
            new LagCompensatedHit { GameObject = enemy, Distance = 5f },
            new LagCompensatedHit { GameObject = wall, Distance = 3f }
        };
        Assert.That(WeaponHitUtility.TryGetNearestValidHit(hits, owner, out var nearest), Is.True);
        Assert.That(nearest.GameObject, Is.EqualTo(wall));
    }
}
