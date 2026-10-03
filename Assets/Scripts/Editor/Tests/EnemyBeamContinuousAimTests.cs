using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyBeamContinuousAimTests
{
    [TestCase(0.1f, 18f)]
    [TestCase(1f, 90f)]
    public void BetweenActionAimUsesBeamTurnSpeedWithoutMovingOrRequiringAttackState(float dt, float yaw)
    {
        var root = new GameObject("Beam Between Actions Test");
        try
        {
            var beam = root.AddComponent<EnemyRangedBeamAttack>();
            var support = typeof(EnemyCombatOption).GetProperty("SupportsBetweenActionAiming");
            Assert.That(support, Is.Not.Null);
            Assert.That(support.GetValue(beam), Is.True);
            Vector3 origin = new Vector3(13f, 7f, -4f);
            root.transform.position = origin;
            var context = new EnemyCombatContext(null, null, null, null,
                origin + new Vector3(10f, 20f, 0f), 10f, dt);
            var method = typeof(EnemyCombatOption).GetMethod("AimBetweenActions");
            Assert.That(method, Is.Not.Null);
            // No Spawned lifecycle: touching BeamEndPoint/timers/shot sequence would throw.
            method.Invoke(beam, new object[] { context });
            Assert.That(root.transform.position, Is.EqualTo(origin));
            Assert.That(Quaternion.Angle(root.transform.rotation, Quaternion.Euler(0f, yaw, 0f)), Is.LessThan(0.01f));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void OtherAttackTypesDoNotOptIntoBetweenActionAiming()
    {
        var root = new GameObject("Projectile Aim Opt In Test");
        try
        {
            var option = root.AddComponent<EnemyRangedProjectileAttack>();
            var support = typeof(EnemyCombatOption).GetProperty("SupportsBetweenActionAiming");
            Assert.That(support, Is.Not.Null);
            Assert.That(support.GetValue(option), Is.False);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
