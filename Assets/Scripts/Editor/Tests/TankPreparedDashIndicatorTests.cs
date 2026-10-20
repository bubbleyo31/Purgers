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
    public void ProjectionFitsObjectInsideCameraViewport()
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
                Rect rect = (Rect)args[4];
                Assert.That(float.IsNaN(rect.width) || float.IsInfinity(rect.width), Is.False);
                Assert.That(float.IsNaN(rect.height) || float.IsInfinity(rect.height), Is.False);
                Assert.That(rect.width, Is.GreaterThan(0)); Assert.That(rect.height, Is.GreaterThan(0));
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

    // 真正的膠囊定義與正式 A/B 資產一起核對，避免只重寫一遍 UI 的公式。
    static Bounds HitboxBounds(Fusion.Hitbox hitbox)
    {
        var method = typeof(TankPreparedDashPreviewQuery).GetMethod("GetHitboxLocalBounds",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "部位框需要沿 Fusion 的實際局部幾何。");
        return (Bounds)method.Invoke(null, new object[] { hitbox });
    }

    [TestCase("A")]
    [TestCase("B")]
    public void JellyfishCapsuleFramesMatchActualColliderHeight(string variant)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Enemy/Variants/Enemy_Ranged_" + variant + ".prefab");
        Assert.That(prefab, Is.Not.Null);
        int count = 0;
        foreach (var capsule in prefab.GetComponentsInChildren<CapsuleCollider>(true))
        {
            var hitbox = capsule.GetComponent<Fusion.Hitbox>();
            if (hitbox == null || hitbox.Type != Fusion.HitboxTypes.Capsule) continue;
            Bounds bounds = HitboxBounds(hitbox);
            Assert.That(bounds.center, Is.EqualTo(capsule.center), capsule.name);
            Assert.That(bounds.size.y, Is.EqualTo(capsule.height).Within(.0001f), capsule.name);
            Assert.That(bounds.size.x, Is.EqualTo(capsule.radius * 2).Within(.0001f), capsule.name);
            Assert.That(bounds.size.z, Is.EqualTo(capsule.radius * 2).Within(.0001f), capsule.name);
            count++;
        }
        Assert.That(count, Is.EqualTo(4), "每個正式水母 Prefab 有四個長觸鬚膠囊。");
    }

    [TestCase(7.15f, 2.64f)]
    [TestCase(1f, 2f)]
    public void CapsuleBoundsContainFusionEndpointsWithoutExtraHeight(float height, float radius)
    {
        var go = new GameObject("capsule bounds", typeof(Fusion.Hitbox));
        try
        {
            var hitbox = go.GetComponent<Fusion.Hitbox>();
            hitbox.Type = Fusion.HitboxTypes.Capsule;
            hitbox.CapsuleExtents = height; hitbox.CapsuleRadius = radius;
            hitbox.Offset = new Vector3(1, 2, 3);
            Bounds bounds = HitboxBounds(hitbox);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Vector3 top = (Vector3)typeof(Fusion.Hitbox).GetProperty("CapsuleTopCenter", flags).GetValue(hitbox);
            Vector3 bottom = (Vector3)typeof(Fusion.Hitbox).GetProperty("CapsuleBottomCenter", flags).GetValue(hitbox);
            Assert.That(bounds.max.y, Is.EqualTo(top.y + radius).Within(.0001f));
            Assert.That(bounds.min.y, Is.EqualTo(bottom.y - radius).Within(.0001f));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OversizedProjectionKeepsRealEdgesInsteadOfMakingFullscreenCorners(bool crossesNearPlane)
    {
        var go = new GameObject("close target camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>();
            camera.pixelRect = new Rect(100, 50, 800, 600); camera.aspect = 4f / 3f;
            camera.fieldOfView = 60; camera.nearClipPlane = .3f;
            var bounds = new Bounds(new Vector3(0, 0, crossesNearPlane ? .4f : 3f), new Vector3(12, 12, 1));
            Assert.That(TankPreparedDashIndicatorRules.TryProject(camera, bounds, Matrix4x4.identity, 0, out var rect), Is.True);
            Assert.That(rect.xMin, Is.LessThan(camera.pixelRect.xMin));
            Assert.That(rect.xMax, Is.GreaterThan(camera.pixelRect.xMax));
            Assert.That(rect.yMin, Is.LessThan(camera.pixelRect.yMin));
            Assert.That(rect.yMax, Is.GreaterThan(camera.pixelRect.yMax));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void PartialOffscreenProjectionDoesNotMoveItsOutsideEdgeOntoViewport()
    {
        var go = new GameObject("partial target camera", typeof(Camera));
        try
        {
            var camera = go.GetComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 800, 600); camera.aspect = 4f / 3f; camera.fieldOfView = 60;
            Assert.That(TankPreparedDashIndicatorRules.TryProject(camera,
                new Bounds(new Vector3(6, 0, 8), new Vector3(4, 2, 1)), Matrix4x4.identity, 0, out var rect), Is.True);
            Assert.That(rect.xMin, Is.InRange(camera.pixelRect.xMin, camera.pixelRect.xMax));
            Assert.That(rect.xMax, Is.GreaterThan(camera.pixelRect.xMax));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void BracketClipsToCurrentCameraViewportWithoutReceivingInput()
    {
        var go = new GameObject("indicator viewport", typeof(LocalTankPreparedDashIndicator));
        var cameraObject = new GameObject("viewport camera", typeof(Camera));
        try
        {
            var indicator = go.GetComponent<LocalTankPreparedDashIndicator>();
            var camera = cameraObject.GetComponent<Camera>();
            var ensure = typeof(LocalTankPreparedDashIndicator).GetMethod("EnsureBracket", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (Rect viewport in new[] { new Rect(100, 50, 640, 480), new Rect(20, 30, 320, 240) })
            {
                camera.pixelRect = viewport;
                ensure.Invoke(indicator, new object[] { camera });
                Canvas.ForceUpdateCanvases();
                var graphic = go.GetComponentInChildren<TankTargetBracketGraphic>();
                Assert.That(graphic, Is.Not.Null);
                var mask = graphic.GetComponentInParent<UnityEngine.UI.RectMask2D>();
                Assert.That(mask, Is.Not.Null, "原始投影四角必須由相機 viewport 裁切，不能拉回螢幕邊緣。");
                Assert.That(graphic.raycastTarget, Is.False);
                var corners = new Vector3[4]; mask.rectTransform.GetWorldCorners(corners);
                Assert.That(corners[0].x, Is.EqualTo(camera.pixelRect.xMin).Within(.1f));
                Assert.That(corners[0].y, Is.EqualTo(camera.pixelRect.yMin).Within(.1f));
                Assert.That(corners[2].x, Is.EqualTo(camera.pixelRect.xMax).Within(.1f));
                Assert.That(corners[2].y, Is.EqualTo(camera.pixelRect.yMax).Within(.1f));
            }
        }
        finally
        {
            var canvas = go.GetComponentInChildren<Canvas>();
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    [TestCase(true, true, 0f, true)]
    [TestCase(true, false, 0f, false)]
    [TestCase(false, true, 0f, false)]
    [TestCase(true, true, 2f, false)]
    public void GroundSegmentsDoNotConnectAcrossMissingSamplesOrSteps(bool first, bool second, float height, bool expected)
        => Assert.That(Call("CanJoinGroundSamples", first, second, Vector3.zero, new Vector3(.4f, height, 0), .6f), Is.EqualTo(expected));
}
