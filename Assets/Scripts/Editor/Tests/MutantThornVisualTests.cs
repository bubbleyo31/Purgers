using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>驗證荊棘表現不移動碰撞根，以及停用／重用時還原美術姿勢。</summary>
[Category("PurgersRegression")]
public sealed class MutantThornVisualTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TestCase(false)]
    [TestCase(true)]
    public void PresentationKeepsCollisionRootAndRestoresAuthoredPose(bool hit)
    {
        var type = Type.GetType("MutantThornVisual, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, "荊棘表現元件尚未實作");
        var root = new GameObject("ThornTest");
        try
        {
            root.SetActive(false);
            root.transform.position = new Vector3(3, 4, 5);
            var collider = root.AddComponent<SphereCollider>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(.1f, .2f, .3f);
            visual.localScale = new Vector3(1, 2, 3);
            visual.localRotation = Quaternion.Euler(10, 20, 30);
            var pos = visual.localPosition;
            var scale = visual.localScale;
            var rot = visual.localRotation;
            var component = root.AddComponent(type);
            type.GetField("visualRoot", Flags).SetValue(component, visual);
            root.SetActive(true);
            type.GetMethod("OnEnable", Flags).Invoke(component, null);
            if (hit) type.GetMethod("PlayHitReaction", Flags).Invoke(component, new object[] { 1f });
            type.GetMethod("LateUpdate", Flags).Invoke(component, null);
            if (hit) Assert.That(Quaternion.Angle(visual.localRotation, rot), Is.GreaterThan(.1f));
            Assert.That(root.transform.position, Is.EqualTo(new Vector3(3, 4, 5)));
            Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(collider.radius, Is.EqualTo(.5f));
            type.GetMethod("OnDisable", Flags).Invoke(component, null);
            Assert.That(visual.localPosition, Is.EqualTo(pos));
            Assert.That(visual.localScale, Is.EqualTo(scale));
            Assert.That(Quaternion.Angle(visual.localRotation, rot), Is.LessThan(.001f));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(0f, 1f)]
    [TestCase(.3f, 0f)]
    [TestCase(10f, 0f)]
    public void HitEnvelopeFinishesWithoutResidualOffset(float elapsed, float expected)
    {
        var type = Type.GetType("MutantThornVisual, Assembly-CSharp");
        Assert.That(type, Is.Not.Null, "荊棘表現元件尚未實作");
        var value = (float)type.GetMethod("EvaluateHitEnvelope", BindingFlags.Public | BindingFlags.Static)
            .Invoke(null, new object[] { elapsed, .3f });
        Assert.That(value, Is.EqualTo(expected).Within(.0001f));
    }
}
