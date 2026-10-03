using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class BulletTracerAppearanceTests
{
    private GameObject root;
    private BulletTracerLineDrawer drawer;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Tracer appearance fixture");
        drawer = root.AddComponent<BulletTracerLineDrawer>();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    private void Set(string name, object value)
    {
        var field = typeof(BulletTracerLineDrawer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "缺少外觀設定：" + name);
        field.SetValue(drawer, value);
    }

    private object Call(string name, params object[] args) =>
        typeof(BulletTracerLineDrawer).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(drawer, args);

    private static Gradient MakeGradient() => new Gradient
    {
        colorKeys = new[]
        {
            new GradientColorKey(Color.red, 0f),
            new GradientColorKey(Color.blue, 1f)
        },
        alphaKeys = new[]
        {
            new GradientAlphaKey(0.2f, 0f),
            new GradientAlphaKey(0.8f, 1f)
        }
    };

    [Test]
    public void HiddenSegmentTravelsAndRevealsAtItsCurrentPosition()
    {
        float step = Time.deltaTime;
        Assert.That(step, Is.GreaterThan(0f));
        Set("displayDelay", step * 1.5f);
        Set("tracerRoot", root.transform);
        Set("animateTravel", true);
        Set("travelSpeed", 1f / step);
        Set("hideAfterPassingHitPoint", false);
        Set("visibleDuration", 1f);
        var routine = (IEnumerator)Call("PlayTracerRoutine", Vector3.zero, Vector3.forward * 10f);

        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(routine.Current, Is.Null, "延遲期間仍必須逐幀飛行");
        var line = root.GetComponentInChildren<LineRenderer>();
        Assert.That(line.enabled, Is.False);
        float hiddenHead = line.GetPosition(1).z;
        Assert.That(hiddenHead, Is.EqualTo(1f).Within(0.001f));

        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(line.enabled, Is.True);
        Assert.That(line.GetPosition(1).z, Is.EqualTo(2f).Within(0.001f));
        Assert.That(line.GetPosition(0).z, Is.EqualTo(1.5f).Within(0.001f),
            "只能從開放顯示位置長出，不能補畫隱藏路段");
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(line.GetPosition(0).z, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(line.GetPosition(0).z, Is.EqualTo(2f).Within(0.001f));
        (routine as System.IDisposable)?.Dispose();
    }

    [TestCase(1.5f, 1.5f, true)]
    [TestCase(2f, 2f, false)]
    [TestCase(3f, 2f, false)]
    public void FinalHoldDoesNotRestoreHiddenPath(float delaySteps, float expectedTail, bool visible)
    {
        float step = Time.deltaTime;
        Assert.That(step, Is.GreaterThan(0f));
        Set("displayDelay", step * delaySteps);
        Set("tracerRoot", root.transform);
        Set("animateTravel", true);
        Set("travelSpeed", 1f / step);
        Set("tracerLength", 1f);
        Set("passThroughDistance", 0f);
        Set("hideAfterPassingHitPoint", false);
        Set("visibleDuration", step * 10f);
        var routine = (IEnumerator)Call("PlayTracerRoutine", Vector3.zero, Vector3.forward);
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(routine.MoveNext(), Is.True);
        var line = root.GetComponentInChildren<LineRenderer>();
        for (int i = 0; i < 4; i++)
        {
            Assert.That(line.GetPosition(0).z, Is.EqualTo(expectedTail).Within(0.001f));
            Assert.That(line.GetPosition(1).z, Is.EqualTo(2f).Within(0.001f));
            Assert.That(line.enabled, Is.EqualTo(visible));
            Assert.That(routine.MoveNext(), Is.True);
        }
        (routine as System.IDisposable)?.Dispose();
    }

    [Test]
    public void PassingHitPointBeforeRevealNeverEnablesRenderer()
    {
        float step = Time.deltaTime;
        Assert.That(step, Is.GreaterThan(0f));
        Set("displayDelay", step * 1.5f);
        Set("tracerRoot", root.transform);
        Set("animateTravel", true);
        Set("travelSpeed", 1f / step);
        Set("tracerLength", 0.1f);
        Set("passThroughDistance", 10f);
        Set("hideAfterPassingHitPoint", true);
        var routine = (IEnumerator)Call("PlayTracerRoutine", Vector3.zero, Vector3.forward * 0.5f);
        Assert.That(routine.MoveNext(), Is.True);
        var line = root.GetComponentInChildren<LineRenderer>();
        Assert.That(line.enabled, Is.False);
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(line.GetPosition(1).z, Is.EqualTo(2f).Within(0.001f));
        Assert.That(line.enabled, Is.False, "通過命中點後不能因為到達延遲時間而重新顯示");
        (routine as System.IDisposable)?.Dispose();
    }

    [Test]
    public void ZeroDelayDisplaysSegmentImmediately()
    {
        Set("displayDelay", 0f);
        Set("tracerRoot", root.transform);
        Set("animateTravel", false);
        Set("hideAfterPassingHitPoint", false);
        Set("visibleDuration", 1f);
        var routine = (IEnumerator)Call("PlayTracerRoutine", Vector3.zero, Vector3.forward * 10f);

        Assert.That(routine.MoveNext(), Is.True);
        var line = root.GetComponentInChildren<LineRenderer>();
        Assert.That(line.enabled, Is.True);
        Assert.That(line.GetPosition(1), Is.Not.EqualTo(line.GetPosition(0)));
        (routine as System.IDisposable)?.Dispose();
    }

    [Test]
    public void SegmentEndsUseSmoothRoundedCaps()
    {
        var line = root.AddComponent<LineRenderer>();
        Call("SetupLineRenderer", line);
        Assert.That(line.numCapVertices, Is.GreaterThanOrEqualTo(12));
    }

    [TestCase(0f, false)]
    [TestCase(0.999f, false)]
    [TestCase(1f, true)]
    public void EnergyColorChangesOnlyAtFullCharge(float energy, bool full)
    {
        Set("tracerColor", Color.red);
        Set("maximumEnergyTracerColor", Color.cyan);
        Assert.That(Call("SelectTracerColor", energy), Is.EqualTo(full ? Color.cyan : Color.red));
        Assert.That(Call("SelectTracerColor", 0.5f), Is.EqualTo(Color.red));
    }

    [Test]
    public void MaximumColorCanBeDisabled()
    {
        Set("tracerColor", Color.red);
        Set("maximumEnergyTracerColor", Color.cyan);
        Set("useMaximumEnergyColor", false);
        Assert.That(Call("SelectTracerColor", 1f), Is.EqualTo(Color.red));
    }

    [Test]
    public void UnspawnedProfessionUsesNormalColorWithoutReadingNetworkState()
    {
        root.AddComponent<PlayerProfessionRuntime>();
        Set("tracerColor", Color.red);
        var line = root.AddComponent<LineRenderer>();
        Assert.DoesNotThrow(() => Call("SetupLineRenderer", line));
        Assert.That(line.startColor, Is.EqualTo(Color.red));
    }

    [Test]
    public void DefaultGradientPreservesExistingTracerColorIncludingAlpha()
    {
        var tint = new Color(0.7f, 0.3f, 0.1f, 0.35f);
        Set("tracerColor", tint);
        var line = root.AddComponent<LineRenderer>();
        Call("SetupLineRenderer", line);

        foreach (float position in new[] { 0f, 0.5f, 1f })
        {
            Color actual = line.colorGradient.Evaluate(position);
            Assert.That(actual.r, Is.EqualTo(tint.r).Within(1f / 255f));
            Assert.That(actual.g, Is.EqualTo(tint.g).Within(1f / 255f));
            Assert.That(actual.b, Is.EqualTo(tint.b).Within(1f / 255f));
            Assert.That(actual.a, Is.EqualTo(tint.a).Within(1f / 255f));
        }
    }

    [Test]
    public void GradientControlsDifferentColorsAndTransparencyAlongLine()
    {
        Set("tracerColor", Color.white);
        Set("tracerGradient", MakeGradient());
        var line = root.AddComponent<LineRenderer>();
        Call("SetupLineRenderer", line);

        Color tail = line.colorGradient.Evaluate(0f);
        Color head = line.colorGradient.Evaluate(1f);
        Assert.That(tail.r, Is.EqualTo(1f).Within(1f / 255f));
        Assert.That(tail.b, Is.EqualTo(0f).Within(1f / 255f));
        Assert.That(tail.a, Is.EqualTo(0.2f).Within(1f / 255f));
        Assert.That(head.r, Is.EqualTo(0f).Within(1f / 255f));
        Assert.That(head.b, Is.EqualTo(1f).Within(1f / 255f));
        Assert.That(head.a, Is.EqualTo(0.8f).Within(1f / 255f));
    }

    [Test]
    public void FadeKeepsSpatialColorsAndRelativeAlphaWhenSettingsChange()
    {
        Set("tracerColor", Color.white);
        Set("tracerGradient", MakeGradient());
        Set("tracerRoot", root.transform);
        Set("animateTravel", false);
        Set("hideAfterPassingHitPoint", false);
        Set("visibleDuration", 0f);
        Set("fadeDuration", 1000f);

        var routine = (IEnumerator)Call("PlayTracerRoutine", Vector3.zero, Vector3.forward * 10f);
        Assert.That(routine.MoveNext(), Is.True);
        var line = root.GetComponentInChildren<LineRenderer>();
        Set("tracerColor", Color.green);
        Set("tracerGradient", new Gradient());
        Assert.That(routine.MoveNext(), Is.True);

        Color tail = line.colorGradient.Evaluate(0f);
        Color head = line.colorGradient.Evaluate(1f);
        Assert.That(tail.r, Is.EqualTo(1f).Within(1f / 255f));
        Assert.That(head.b, Is.EqualTo(1f).Within(1f / 255f));
        Assert.That(tail.a, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.2f));
        Assert.That(head.a, Is.GreaterThan(tail.a));
        Assert.That(head.a, Is.LessThanOrEqualTo(0.8f));
        (routine as System.IDisposable)?.Dispose();
    }
}
