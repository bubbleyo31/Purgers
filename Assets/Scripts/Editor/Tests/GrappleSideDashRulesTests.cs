using NUnit.Framework;
using UnityEngine;
[Category("PurgersRegression")]
public sealed class GrappleSideDashRulesTests
{
    private static object Rule(string name, params object[] args)
    {
        var type = typeof(Player).Assembly.GetType("GrappleSideDashRules");
        Assert.That(type, Is.Not.Null, "Grapple side dash rules not implemented");
        return type.GetMethod(name).Invoke(null, args);
    }
    [TestCase(-1,-1,.25f,true)] [TestCase(1,1,.1f,true)]
    [TestCase(-1,1,.1f,false)] [TestCase(1,1,.251f,false)]
    [TestCase(0,0,.1f,false)] [TestCase(1,1,-1f,false)]
    public void OnlyFreshSameSidePressPairQualifies(int previous,int current,float interval,bool expected)
    { Assert.That(Rule("IsDoubleTap",previous,current,interval,.25f),Is.EqualTo(expected)); }
    [TestCase(3f,1f,1f)] [TestCase(.1f,1f,0f)] [TestCase(0f,1f,0f)] [TestCase(8f,3f,2f)]
    public void CostIsTwiceLaunchCostAndNeverNegative(float energy,float cost,float expected)
    { Assert.That((float)Rule("EnergyAfterDash",energy,cost),Is.EqualTo(expected).Within(.0001f)); }
    [TestCase(-1,0f,-1f,0f)] [TestCase(1,90f,0f,-1f)]
    public void DirectionIsScreenHorizontalSideAtTrigger(int side,float yaw,float x,float z)
    { Assert.That(Vector3.Distance((Vector3)Rule("Direction",side,yaw),new Vector3(x,0f,z)),Is.LessThan(.0001f)); }
    [Test]
    public void IncrementAddsSideTravelWithoutReturningToAnOldWorldDestination()
    {
        var forward=new Vector3(0f,-2f,6f);
        var delta=(Vector3)Rule("SideDelta",Vector3.left,20f,150f,.1f,1f);
        Assert.That(forward+delta,Is.EqualTo(new Vector3(-15f,-2f,6f)));
        Assert.That(Rule("SideDelta",Vector3.left,2f,150f,.1f,1f),Is.EqualTo(Vector3.left*2f));
    }
    [Test]
    public void SlowMotionScalesIncrementAndZeroTimeDoesNotConsumeTravel()
    {
        Assert.That(Rule("SideDelta",Vector3.right,20f,150f,.1f,.25f),Is.EqualTo(Vector3.right*3.75f));
        Assert.That(Rule("SideDelta",Vector3.right,20f,150f,0f,1f),Is.EqualTo(Vector3.zero));
    }
}
