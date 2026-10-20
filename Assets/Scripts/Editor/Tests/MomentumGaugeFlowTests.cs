using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class MomentumGaugeFlowTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void MaximumStyleKeepsAuthoredScaleAndNormalSize()
    {
        var root = new GameObject("Gauge test owner");
        root.SetActive(false);
        var gauge = new GameObject("Gauge", typeof(RectTransform), typeof(Image));
        gauge.transform.SetParent(root.transform);
        try
        {
            var owner = root.AddComponent<LocalPlayerSpeedSlider>();
            var rect = (RectTransform)gauge.transform;
            rect.localScale = new Vector3(.9f, 1.1f, 1);
            Set(owner, "gaugeRect", rect);
            Set(owner, "gaugeFillImage", gauge.GetComponent<Image>());
            Set(owner, "normalGaugeSize", new Vector2(562, 85));
            Set(owner, "maximumBoostGaugeSize", new Vector2(700, 100));
            Call(owner, "ApplyMaximumBoostStyle", false, true);
            Vector2 size = rect.sizeDelta;
            Call(owner, "ApplyMaximumBoostStyle", true, false);
            Assert.That(rect.sizeDelta, Is.EqualTo(size), "Full state must not change authored gauge dimensions.");
            var oldPulse = typeof(LocalPlayerSpeedSlider).GetMethod("RefreshMaximumBoostPulse", Private);
            oldPulse?.Invoke(owner, null);
            Assert.That(rect.localScale, Is.EqualTo(new Vector3(.9f, 1.1f, 1)), "Do not overwrite the authored scale to animate the gauge.");
            Assert.That((Color32)gauge.GetComponent<Image>().color, Is.EqualTo(new Color32(37,255,217,255)));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void FullTransitionDoublesSpeedWithoutResettingPhaseAndExitRestoresNormalSpeed()
    {
        var root = new GameObject("Flow test"); root.SetActive(false);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(root.transform);
        try
        {
            var owner = root.AddComponent<LocalPlayerSpeedSlider>();
            var image = fill.GetComponent<Image>(); Set(owner, "gaugeFillImage", image);
            Call(owner, "RefreshGaugeFlow", .2f);
            Assert.That(image.material.shader.name, Is.EqualTo("Purgers/UI/Momentum Gauge Flow"));
            Assert.That(image.material.GetFloat("_Phase"), Is.EqualTo(.1f).Within(.0001));
            Call(owner, "ApplyMaximumBoostStyle", true, true);
            Call(owner, "RefreshGaugeFlow", 0f);
            Assert.That(image.material.GetFloat("_Phase"), Is.EqualTo(.1f).Within(.0001), "Changing color/speed cannot jump the pattern.");
            Call(owner, "RefreshGaugeFlow", .2f);
            Assert.That(image.material.GetFloat("_Phase"), Is.EqualTo(.3f).Within(.0001));
            Call(owner, "ApplyMaximumBoostStyle", false, false);
            Call(owner, "RefreshGaugeFlow", .2f);
            Assert.That(image.material.GetFloat("_Phase"), Is.EqualTo(.4f).Within(.0001));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void DisabledHudRestoresOriginalMaterialAndReenableCanRecreateFlow()
    {
        var root = new GameObject("Lifetime test"); root.SetActive(false);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(root.transform);
        try
        {
            var owner = root.AddComponent<LocalPlayerSpeedSlider>();
            var image = fill.GetComponent<Image>(); Set(owner, "gaugeFillImage", image);
            var original = image.material;
            Call(owner, "RefreshGaugeFlow", .2f);
            var temporary = image.material;
            Call(owner, "OnDisable");
            Assert.That(image.material, Is.SameAs(original));
            Assert.That(temporary == null, Is.True, "Transient material must be destroyed in EditMode.");
            Call(owner, "RefreshGaugeFlow", .2f);
            Assert.That(image.material, Is.Not.SameAs(original));
            Assert.That(image.material.GetFloat("_Phase"), Is.EqualTo(.1f).Within(.0001));
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void ParallelHudInstancesDoNotSharePhaseOrMaterial()
    {
        var a = new GameObject("A"); var b = new GameObject("B"); a.SetActive(false); b.SetActive(false);
        try
        {
            var imageA = a.AddComponent<Image>(); var imageB = b.AddComponent<Image>();
            var ownerA = a.AddComponent<LocalPlayerSpeedSlider>(); var ownerB = b.AddComponent<LocalPlayerSpeedSlider>();
            Set(ownerA, "gaugeFillImage", imageA); Set(ownerB, "gaugeFillImage", imageB);
            Call(ownerA, "RefreshGaugeFlow", .2f); Call(ownerB, "RefreshGaugeFlow", .4f);
            Assert.That(imageA.material, Is.Not.SameAs(imageB.material));
            Assert.That(imageA.material.GetFloat("_Phase"), Is.EqualTo(.1f).Within(.0001));
            Assert.That(imageB.material.GetFloat("_Phase"), Is.EqualTo(.2f).Within(.0001));
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
}
