using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class TankPreparedDashIndicatorTests
{
    // Reflection keeps the test-only baseline compilable before the new presentation is imported.
    static Type Rules
    {
        get
        {
            var type = typeof(TankMeleeCombo).Assembly.GetType("TankPreparedDashIndicatorRules");
            Assert.That(type, Is.Not.Null, "Tank needs an owner-only, grounded distance ring and a text-free single-target frame.");
            return type;
        }
    }
    static object Call(string name, params object[] args) => Rules.GetMethod(name).Invoke(null, args);

    [TestCase(true, true, true, false, true)]
    [TestCase(false, true, true, false, false)]
    [TestCase(true, false, true, false, false)]
    [TestCase(true, true, false, false, false)]
    [TestCase(true, true, true, true, false)]
    public void PresentationRequiresOwnLivePlayerAndNoControlBlock(bool local, bool alive, bool active, bool blocked, bool expected)
        => Assert.That(Call("CanPresent", local, alive, active, blocked), Is.EqualTo(expected));

    [TestCase(true, true, true, 1f)]
    [TestCase(true, true, false, .35f)]
    [TestCase(true, false, true, 0f)]
    [TestCase(false, true, true, 0f)]
    public void AirHidesOnlyTheGroundRing(bool preparing, bool grounded, bool frozen, float expected)
        => Assert.That(Call("RingAlpha", preparing, grounded, frozen), Is.EqualTo(expected));

    [TestCase(false, 2f, false)]
    [TestCase(true, 2f, true)]
    [TestCase(false, 6f, true)]
    public void LargeTargetUsesSelectedHitRegion(bool explicitlyLarge, float height, bool expected)
        => Assert.That(Call("UseHitRegion", explicitlyLarge, new Bounds(Vector3.zero, new Vector3(1, height, 1)), 4f), Is.EqualTo(expected));

    [Test]
    public void ProjectionFitsObjectAndClipsAgainstCameraViewport()
    {
        var go = new GameObject("projection camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 800, 600);
            object[] args = { camera, new Bounds(new Vector3(0, 0, 8), new Vector3(2, 3, 1)), Matrix4x4.identity, 10f, default(Rect) };
            Assert.That(Call("TryProject", args), Is.True);
            var rect = (Rect)args[4];
            Assert.That(rect.center.x, Is.EqualTo(400).Within(1));
            Assert.That(rect.center.y, Is.EqualTo(300).Within(1));
            Assert.That(rect.height, Is.GreaterThan(rect.width));
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(0)); Assert.That(rect.xMax, Is.LessThanOrEqualTo(800));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [TestCase(-5f, false)]
    [TestCase(.3f, true)]
    [TestCase(5f, true)]
    public void NearPlaneCrossingNeverMirrorsTheFrame(float depth, bool expected)
    {
        var go = new GameObject("near plane camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 800, 600); camera.nearClipPlane = .3f;
            object[] args = { camera, new Bounds(new Vector3(0, 0, depth), Vector3.one), Matrix4x4.identity, 10f, default(Rect) };
            Assert.That(Call("TryProject", args), Is.EqualTo(expected));
            if (expected)
            {
                Rect rect = (Rect)args[4]; Assert.That(float.IsNaN(rect.width), Is.False);
                Assert.That(rect.width, Is.InRange(0, camera.pixelRect.width + .001f)); Assert.That(rect.height, Is.InRange(0, camera.pixelRect.height + .001f));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void SelectedRegionRemainsSmallerThanWholeLargeEnemy()
    {
        var go = new GameObject("region camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 800, 600);
            object[] body = { camera, new Bounds(new Vector3(0, 0, 10), new Vector3(5, 7, 2)), Matrix4x4.identity, 10f, default(Rect) };
            object[] head = { camera, new Bounds(new Vector3(0, 1, 10), new Vector3(2, 1.5f, 1)), Matrix4x4.identity, 10f, default(Rect) };
            Assert.That(Call("TryProject", body), Is.True); Assert.That(Call("TryProject", head), Is.True);
            Assert.That(((Rect)head[4]).height, Is.LessThan(((Rect)body[4]).height * .5f));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void BracketHasNoTextAndDoesNotReceiveInput()
    {
        var type = typeof(TankMeleeCombo).Assembly.GetType("TankTargetBracketGraphic");
        Assert.That(type, Is.Not.Null);
        var go = new GameObject("bracket", typeof(RectTransform));
        try
        {
            var graphic = (UnityEngine.UI.Graphic)go.AddComponent(type);
            Assert.That(graphic.raycastTarget, Is.False);
            Assert.That(go.GetComponentsInChildren<TMPro.TMP_Text>(true), Is.Empty);
            Assert.That(go.GetComponentsInChildren<UnityEngine.UI.Text>(true), Is.Empty);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void UnspawnedWeaponCannotReadNetworkedStateOrCreatePresentation()
    {
        var method = typeof(TankMeleeCombo).GetMethod("TryGetPreparedDashPreview");
        Assert.That(method, Is.Not.Null);
        var go = new GameObject("unspawned Tank");
        try
        {
            var weapon = go.AddComponent<TankMeleeCombo>();
            object[] args = { null };
            Assert.That(method.Invoke(weapon, args), Is.False);
            Assert.That(go.GetComponentInChildren<Canvas>(true), Is.Null);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void ExplicitlyOffscreenTargetDoesNotBecomeAnEdgeFrame()
    {
        var go = new GameObject("offscreen camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 800, 600);
            object[] args = { camera, new Bounds(new Vector3(30, 0, 8), Vector3.one), Matrix4x4.identity, 30f, default(Rect) };
            Assert.That(Call("TryProject", args), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void RotatedNonuniformHitboxProjectsItsActualTransformedCorners()
    {
        var go = new GameObject("rotated hitbox camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 800, 600);
            Bounds local = new Bounds(Vector3.up, new Vector3(1, 2, 1));
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(0, -1, 8), Quaternion.Euler(0, 40, 15), new Vector3(2, 1, .5f));
            object[] args = { camera, local, matrix, 0f, default(Rect) };
            Assert.That(Call("TryProject", args), Is.True);
            Rect rect = (Rect)args[4];
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = camera.WorldToScreenPoint(matrix.MultiplyPoint3x4(corner));
                Assert.That(point.x, Is.InRange(rect.xMin - .01f, rect.xMax + .01f));
                Assert.That(point.y, Is.InRange(rect.yMin - .01f, rect.yMax + .01f));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [TestCase(true, true, 0f, true)]
    [TestCase(true, false, 0f, false)]
    [TestCase(false, true, 0f, false)]
    [TestCase(true, true, 2f, false)]
    public void GroundSegmentsDoNotConnectAcrossMissingSamplesOrSteps(bool first, bool second, float height, bool expected)
        => Assert.That(Call("CanJoinGroundSamples", first, second, Vector3.zero, new Vector3(.4f, height, 0), .6f), Is.EqualTo(expected));
}
