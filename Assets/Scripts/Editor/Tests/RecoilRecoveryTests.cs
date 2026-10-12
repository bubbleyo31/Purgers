using System.Reflection;
using NUnit.Framework;

/// <summary>只有實際玩家輸入能抵銷後座力，所有來源共用同一份額度。</summary>
[Category("PurgersRegression")]
public sealed class RecoilRecoveryTests
{
    private static MethodInfo Method(string name)
    {
        var t=typeof(WeaponRecoilApplication).Assembly.GetType("WeaponRecoilRecovery");
        Assert.That(t,Is.Not.Null,"技能與槍械必須共用回正规則。");
        var method=t.GetMethod(name); Assert.That(method,Is.Not.Null); return method;
    }
    private static float Consume(ref float budget,float accumulated)
    {
        object[] args={budget,accumulated}; float used=(float)Method("ConsumeManualPitch").Invoke(null,args); budget=(float)args[0];return used;
    }
    private static float Recover(ref float accumulated,float manual,bool pending,bool expired,float speed,float dt)
    {
        object[] args={accumulated,manual,pending,expired,speed,dt}; float step=(float)Method("Step").Invoke(null,args);accumulated=(float)args[0];return step;
    }
    [Test] public void OneManualInputCannotCancelTwoPoolsTwice()
    {
        float budget=1.5f,skill=-1,weapon=-2;
        float used=Consume(ref budget,skill); Recover(ref skill,used,false,false,20,.02f);
        used=Consume(ref budget,weapon); Recover(ref weapon,used,false,false,20,.02f);
        Assert.That(skill,Is.Zero);Assert.That(weapon,Is.EqualTo(-1.5f));Assert.That(budget,Is.Zero);
    }
    [TestCase(true,true)] [TestCase(false,false)]
    public void PendingApplicationOrDelayBlocksAutomaticRecovery(bool pending,bool expired)
    {
        float pool=-2; Assert.That(Recover(ref pool,0,pending,expired,20,.02f),Is.Zero);Assert.That(pool,Is.EqualTo(-2));
    }
    [Test] public void ManualInputReducesOnlyAppliedPoolEvenWhileWaiting()
    {
        float pool=-2; Assert.That(Recover(ref pool,.5f,true,false,20,.02f),Is.Zero);Assert.That(pool,Is.EqualTo(-1.5f));
    }
    [Test] public void RecoveryIsRateLimitedAndCannotOvershoot()
    {
        float pool=-.5f;Assert.That(Recover(ref pool,0,false,true,20,.02f),Is.EqualTo(.4f).Within(.00001f));
        Assert.That(Recover(ref pool,0,false,true,20,.02f),Is.EqualTo(.1f).Within(.00001f));Assert.That(pool,Is.Zero);
    }
    [Test] public void SystemRecoveryDoesNotConsumeManualBudget()
    {
        float budget=0,pool=-3;var used=Consume(ref budget,pool);
        Recover(ref pool,used,false,false,20,.02f);Assert.That(pool,Is.EqualTo(-3));Assert.That(budget,Is.Zero);
    }
    [Test] public void DownwardRecoilDoesNotAutoRecoverOrConsumeInput()
    {
        float pool=2,budget=1;Assert.That(Consume(ref budget,pool),Is.Zero);Assert.That(budget,Is.EqualTo(1));
        Assert.That(Recover(ref pool,0,false,true,20,.02f),Is.Zero);
    }
}
