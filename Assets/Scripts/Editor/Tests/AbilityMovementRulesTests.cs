using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class AbilityMovementRulesTests
{
    private static Vector3 Step(ref Vector3 distance, ref float seconds, float dt)
    {
        var type=typeof(PlayerAbilityRuntime).Assembly.GetType("AbilityMovementRules");
        Assert.That(type,Is.Not.Null,"需要與地面摩擦無關的技能位移規則。");
        var args=new object[]{distance,seconds,dt};
        var value=(Vector3)type.GetMethod("ConsumeLinearDecay").Invoke(null,args);
        distance=(Vector3)args[0]; seconds=(float)args[1]; return value;
    }
    [TestCase(30)] [TestCase(60)] [TestCase(120)]
    public void DecayHasSameTotalDistanceAtEveryTickRate(int hz)
    {
        Vector3 remaining=Vector3.back*2, total=Vector3.zero; float seconds=.4f;
        for(int i=0;i<hz;i++)total+=Step(ref remaining,ref seconds,1f/hz);
        Assert.That(Vector3.Distance(total,Vector3.back*2),Is.LessThan(.00001f));
        Assert.That(remaining,Is.EqualTo(Vector3.zero));Assert.That(seconds,Is.Zero);
    }
    [Test] public void DecaySlowsDownAndClampsLastPartialTick()
    {
        Vector3 remaining=Vector3.right*2;float seconds=.4f;
        float first=Step(ref remaining,ref seconds,.1f).x;
        float second=Step(ref remaining,ref seconds,.1f).x;
        Assert.That(first,Is.GreaterThan(second));
        float last=Step(ref remaining,ref seconds,1).x;
        Assert.That(first+second+last,Is.EqualTo(2).Within(.00001f));
    }
    [Test] public void ZeroTimeDoesNotConsumeAndOverlappingDistanceIsPreserved()
    {
        Vector3 remaining=Vector3.back*2;float seconds=.4f;
        Assert.That(Step(ref remaining,ref seconds,0),Is.EqualTo(Vector3.zero));
        Vector3 total=Step(ref remaining,ref seconds,.1f);
        remaining+=Vector3.left;seconds=.4f;
        total+=Step(ref remaining,ref seconds,1);
        Assert.That(Vector3.Distance(total,Vector3.back*2+Vector3.left),Is.LessThan(.00001f));
    }
}
