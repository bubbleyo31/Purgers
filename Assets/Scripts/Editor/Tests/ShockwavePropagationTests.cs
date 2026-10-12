using NUnit.Framework;
using UnityEngine;

/// <summary>固定波牆的精確連續平移掃掠：尺寸、位移、高度分離；不代表獨立多人驗收。</summary>
[Category("PurgersRegression")]
public sealed class ShockwavePropagationTests
{
    // R=4、內半徑3、半角60：後緣從 z=0 開始，中心正前方波帶位於 z=1.5～2.5。
    private static bool Hit(Vector3 delta, float previous, float current, float radius=4f,
        float distance=10f, float height=3f, float angle=60f, float thickness=1f)
        => ShockwavePropagationRules.ContainsSweptBand(delta, Vector3.forward, previous, current,
            radius, angle, thickness, distance, height);

    [Test] public void DistantEnemy_IsNotHitBeforeWaveArrives()
        => Assert.That(Hit(new Vector3(0,1,8),0,1),Is.False);
    [Test] public void FastWave_DoesNotSkipEnemyBetweenTicks()
        => Assert.That(Hit(new Vector3(0,1,8),0,10),Is.True);
    [Test] public void PassedWave_DoesNotHitEnemyEnteringBehindIt()
        => Assert.That(Hit(new Vector3(0,1,3),6,7),Is.False);
    [Test] public void ExistingThickness_CanHitBetweenTicks()
        => Assert.That(Hit(new Vector3(0,1,7),5,5.1f),Is.True);
    [Test] public void WholeTranslation_IsClampedAtDistance()
    {
        Assert.That(Hit(new Vector3(0,1,12.5f),9,100),Is.True);
        Assert.That(Hit(new Vector3(0,1,12.51f),9,100),Is.False);
        Assert.That(Hit(new Vector3(0,1,12.5f),11,100),Is.False);
    }
    [Test] public void Radius_DoesNotLimitTravel()
    {
        Assert.That(Hit(new Vector3(0,1,20),0,25,radius:2,distance:25),Is.True);
        Assert.That(Hit(new Vector3(0,1,20),0,25,radius:2,distance:10),Is.False);
    }
    [Test] public void Travel_DoesNotWidenWall()
        => Assert.That(Hit(new Vector3(3.6f,1,8),0,100,distance:100),Is.False);
    [Test] public void TopAndBottom_HaveSameHitWidth()
    {
        Assert.That(Hit(new Vector3(3.3f,0,5),3,5),Is.True);
        Assert.That(Hit(new Vector3(3.3f,3,5),3,5),Is.True);
        Assert.That(Hit(new Vector3(3.3f,3.01f,5),3,5),Is.False);
        Assert.That(Hit(new Vector3(3.3f,-.01f,5),3,5),Is.False);
    }
    [Test] public void AngularCut_IsSweptContinuously()
        => Assert.That(Hit(new Vector3(3.2f,1,1.2f),0,1),Is.True);
    [Test] public void HollowBehindArc_IsNotInstantlyHit()
        => Assert.That(Hit(new Vector3(0,1,.5f),0,0),Is.False);
    [Test] public void ZeroAngle_HasNoVisibleOrDamageArea()
        => Assert.That(Hit(new Vector3(0,1,5),0,10,angle:0),Is.False);
    [TestCase(20f)][TestCase(60f)][TestCase(90f)][TestCase(120f)][TestCase(180f)]
    public void EveryWallBoundary_CanBeHitAtSameTranslation(float half)
    {
        const float distance=7f,radius=4f;
        float offset=ShockwavePropagationRules.GetCenterOffset(radius,half,1f);
        foreach(float r in new[]{3f,4f})
            foreach(float yaw in new[]{-half,0f,half})
                foreach(float height in new[]{0f,3f})
                {
                    var point=ShockwavePropagationRules.SurfacePoint(r,yaw,height)+Vector3.forward*(distance+offset);
                    Assert.That(Hit(point,distance,distance,angle:half),Is.True,"邊界 "+point);
                }
    }
    [Test] public void AirborneRotatedCast_UsesCapturedFrame()
    {
        Vector3 origin=new Vector3(11,25,7),target=origin+new Vector3(8,2,0);
        Assert.That(ShockwavePropagationRules.ContainsSweptBand(target-origin,Vector3.right,0,10,4,60,1,10,3),Is.True);
        target.y=0;
        Assert.That(ShockwavePropagationRules.ContainsSweptBand(target-origin,Vector3.right,0,10,4,60,1,10,3),Is.False);
    }
    [Test] public void Thickness_ClampsToRadius()
    {
        Assert.That(ShockwavePropagationRules.GetThickness(2f,10f),Is.EqualTo(2f));
        Assert.That(Hit(new Vector3(0,1,1),0,0,radius:2,thickness:10),Is.True);
    }

    [TestCase(-35f,70f)] [TestCase(40f,120f)] [TestCase(-90f,130f)] [TestCase(90f,-80f)]
    public void TiltedWall_UsesLocalHeightAndFullYawAtVerticalAim(float pitch,float yaw)
    {
        Quaternion rotation=Quaternion.Euler(pitch,yaw,0f);
        foreach(float height in new[]{0f,3f})
            Assert.That(ShockwavePropagationRules.ContainsSweptBand(rotation*new Vector3(3.3f,height,5),
                rotation,3,5,4,60,1,10,3),Is.True);
        foreach(float height in new[]{-.02f,3.02f})
            Assert.That(ShockwavePropagationRules.ContainsSweptBand(rotation*new Vector3(3.3f,height,5),
                rotation,3,5,4,60,1,10,3),Is.False);
    }
    [Test] public void Offset_UsesAimRightUpForwardInMeters()
    {
        var origin=new Vector3(20,30,40);
        var rotation=Quaternion.Euler(-30,90,0);
        var point=ShockwavePropagationRules.EmissionOrigin(origin,rotation,new Vector3(2,3,4));
        Assert.That(Vector3.Distance(point,origin+rotation*Vector3.right*2+rotation*Vector3.up*3+
            rotation*Vector3.forward*4),Is.LessThan(.0001f));
    }
    [Test] public void ReverseTick_IsRejected()
        => Assert.That(Hit(new Vector3(0,1,5),5,4),Is.False);
}
