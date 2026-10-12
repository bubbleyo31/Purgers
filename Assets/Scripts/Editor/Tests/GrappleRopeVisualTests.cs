using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>鉤爪與繩身的呈現契約；不模擬命中、玩家物理或網路權威。</summary>
[Category("PurgersRegression")]
public sealed class GrappleRopeVisualTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private PlayerGrappleVisual visual;
    private LineRenderer line;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("GrappleVisualTest");
        root.SetActive(false);
        visual = root.AddComponent<PlayerGrappleVisual>();
        line = root.GetComponent<LineRenderer>();
        Set("grappleLine", line);
        line.useWorldSpace = true;
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    private void Set(string name, object value)
    {
        var field = typeof(PlayerGrappleVisual).GetField(name, Private);
        Assert.That(field, Is.Not.Null, "Missing visual configuration: " + name);
        field.SetValue(visual, value);
    }

    private object Call(string name, params object[] args)
    {
        var method = typeof(PlayerGrappleVisual).GetMethod(name, Private);
        Assert.That(method, Is.Not.Null, "Missing visual behavior: " + name);
        return method.Invoke(visual, args);
    }

    private void Draw(GrapplePhase phase, float extension = 0.5f, float dt = 0.016f)
        => Call("RenderRope", phase, Vector3.zero, Vector3.forward * 20f, extension, dt);

    [Test]
    public void ShootingHasWavesButBothEndpointsStayConnected()
    {
        Draw(GrapplePhase.Shooting);
        Assert.That(line.positionCount, Is.GreaterThan(2));
        Assert.That(line.GetPosition(0), Is.EqualTo(Vector3.zero));
        Assert.That(line.GetPosition(line.positionCount - 1), Is.EqualTo(Vector3.forward * 10f));
        float maximum = 0f;
        for (int i = 1; i < line.positionCount - 1; i++)
            maximum = Mathf.Max(maximum, Vector3.Cross(line.GetPosition(i), Vector3.forward).magnitude);
        Assert.That(maximum, Is.GreaterThan(0.1f));
    }

    [Test]
    public void WaveMovesEvenWhenExtensionHasNotChanged()
    {
        Draw(GrapplePhase.Shooting);
        Vector3 before = line.GetPosition(line.positionCount / 3);
        Draw(GrapplePhase.Shooting, dt: 0.1f);
        Assert.That(Vector3.Distance(before, line.GetPosition(line.positionCount / 3)), Is.GreaterThan(0.01f));
    }

    [Test]
    public void DisabledWavesKeepOriginalStraightLine()
    {
        Set("enableWaves", false);
        Draw(GrapplePhase.Shooting);
        for (int i = 0; i < line.positionCount; i++)
            Assert.That(Vector3.Cross(line.GetPosition(i), Vector3.forward).magnitude, Is.LessThan(0.00001f));
    }

    [Test]
    public void AttachedFinishesTighteningAndKeepsExactHitPoint()
    {
        Draw(GrapplePhase.Shooting);
        Draw(GrapplePhase.Attached, 1f, 0.01f);
        Draw(GrapplePhase.Attached, 1f, 1f);
        Assert.That(line.positionCount, Is.EqualTo(2));
        Assert.That(line.GetPosition(1), Is.EqualTo(Vector3.forward * 20f));
    }

    [Test]
    public void LateJoinAttachedStartsStraightWithoutReplayingLaunch()
    {
        Draw(GrapplePhase.Attached, 1f);
        Assert.That(line.positionCount, Is.EqualTo(2));
    }

    [Test]
    public void ZeroTighteningTimeImmediatelyStraightens()
    {
        Set("hitTightenDuration", 0f);
        Draw(GrapplePhase.Shooting);
        Draw(GrapplePhase.Attached, 1f);
        Assert.That(line.positionCount, Is.EqualTo(2));
    }

    [Test]
    public void MidFlightCancelDoesNotExtendToTarget()
    {
        Draw(GrapplePhase.Shooting, 0.4f);
        Draw(GrapplePhase.Retracting, 0.3f);
        Assert.That(line.GetPosition(line.positionCount - 1), Is.EqualTo(Vector3.forward * 6f));
        Draw(GrapplePhase.Retracting, 0f);
        Assert.That(line.enabled, Is.False);
        Assert.That(line.positionCount, Is.Zero);
    }

    [Test]
    public void StrongWavesAreLimitedOnVeryShortRopes()
    {
        Set("waveAmplitude", 100f);
        Call("RenderRope", GrapplePhase.Shooting, Vector3.zero, Vector3.forward * 0.02f, 0.5f, 0.016f);
        for (int i = 0; i < line.positionCount; i++)
            Assert.That(line.GetPosition(i).magnitude, Is.LessThan(0.03f));
    }

    [Test]
    public void HookConnectionTracksTipAndHidesWithRope()
    {
        var template = new GameObject("HookTemplate");
        try
        {
            Set("hookPrefab", template);
            Set("hookConnectionOffset", new Vector3(0f, 0f, -0.25f));
            Draw(GrapplePhase.Shooting);
            var hook = (GameObject)typeof(PlayerGrappleVisual).GetField("hookInstance", Private).GetValue(visual);
            Assert.That(hook, Is.Not.Null);
            Assert.That(Vector3.Distance(hook.transform.TransformPoint(new Vector3(0f, 0f, -0.25f)),
                line.GetPosition(line.positionCount - 1)), Is.LessThan(0.0001f));
            Draw(GrapplePhase.Retracting, 0.2f);
            Assert.That(Vector3.Distance(hook.transform.TransformPoint(new Vector3(0f, 0f, -0.25f)), Vector3.forward * 4f), Is.LessThan(0.0001f));
            Draw(GrapplePhase.Idle, 0f);
            Assert.That(hook.activeSelf, Is.False);
        }
        finally { Object.DestroyImmediate(template); }
    }

    [Test]
    public void RemoteOriginNeverUsesFirstPersonOrigin()
    {
        var first = new GameObject("FirstPersonOrigin");
        var third = new GameObject("ThirdPersonOrigin");
        try
        {
            first.transform.position = Vector3.one * 100f;
            third.transform.position = new Vector3(2f, 3f, 4f);
            Set("ropeOrigin", first.transform);
            Set("thirdPersonRopeOrigin", third.transform);
            Assert.That((Vector3)Call("GetRopeStartPosition"), Is.EqualTo(third.transform.position));
            Set("localVisualEnabled", true);
            Assert.That((Vector3)Call("GetRopeStartPosition"), Is.EqualTo(first.transform.position));
        }
        finally { Object.DestroyImmediate(first); Object.DestroyImmediate(third); }
    }

    [Test]
    public void UnspawnedVisualDoesNotReadNetworkedState()
    {
        Assert.DoesNotThrow(() => Call("UpdateLine"));
        Assert.That(line.enabled, Is.False);
    }

    [Test]
    public void HookIsReusedAndReleasedOnDespawn()
    {
        var template = new GameObject("ReusableHookTemplate");
        try
        {
            Set("hookPrefab", template);
            Draw(GrapplePhase.Shooting);
            var field = typeof(PlayerGrappleVisual).GetField("hookInstance", Private);
            var first = (GameObject)field.GetValue(visual);
            Call("OnDisable");
            Assert.That(first.activeSelf, Is.False);
            Assert.That(line.enabled, Is.False);
            Draw(GrapplePhase.Shooting);
            Assert.That(field.GetValue(visual), Is.SameAs(first));
            visual.UnbindNetworkVisual();
            Assert.That(first == null, Is.True);
            Assert.That(line.enabled, Is.False);
        }
        finally { Object.DestroyImmediate(template); }
    }

    [Test]
    public void AmplitudeCurveControlsWaveStrength()
    {
        Set("waveAmplitude", 0.5f);
        Draw(GrapplePhase.Shooting, dt: 0f);
        Vector3 small = line.GetPosition(line.positionCount / 3);
        Set("shootWaveAmplitude", AnimationCurve.Linear(0f, 2f, 1f, 2f));
        Draw(GrapplePhase.Shooting, dt: 0f);
        Vector3 large = line.GetPosition(line.positionCount / 3);
        Assert.That(Vector3.Cross(large, Vector3.forward).magnitude,
            Is.EqualTo(Vector3.Cross(small, Vector3.forward).magnitude * 2f).Within(0.0001f));
    }

    [Test]
    public void HookOffsetRespectsScaleAndRotationCorrection()
    {
        var template = new GameObject("OffsetHookTemplate");
        try
        {
            Set("hookPrefab", template);
            Set("hookScale", 2f);
            Set("hookRotationOffset", new Vector3(0f, 90f, 0f));
            Vector3 offset = new Vector3(0.1f, 0.2f, -0.3f);
            Set("hookConnectionOffset", offset);
            Draw(GrapplePhase.Shooting);
            var hook = (GameObject)typeof(PlayerGrappleVisual).GetField("hookInstance", Private).GetValue(visual);
            Assert.That(Vector3.Distance(hook.transform.TransformPoint(offset),
                line.GetPosition(line.positionCount - 1)), Is.LessThan(0.0001f));
        }
        finally { Object.DestroyImmediate(template); }
    }
}
