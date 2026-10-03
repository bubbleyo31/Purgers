using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

[Category("PurgersRegression")]
public sealed class WeaponImpactEffectsTests
{
    private static void Set(WeaponImpactSettings settings, string field, object value) =>
        typeof(WeaponImpactSettings).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, value);

    [Test]
    public void PerPlayerCapacityEvictsOldestAndTracksMovingSurface()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var settings = ScriptableObject.CreateInstance<WeaponImpactSettings>();
        var material = new Material(Shader.Find("Purgers/BulletHole"));
        var surface = new GameObject("TestSurface");
        SceneManager.MoveGameObjectToScene(surface, scene);
        Set(settings, "maxBulletHolesPerPlayer", 2);
        Set(settings, "bulletHoleMaterials", new[] { material });
        var first = new WeaponImpactVisuals(settings, scene);
        var second = new WeaponImpactVisuals(settings, scene);
        try
        {
            first.Present(Vector3.zero, Vector3.forward, surface.transform, 1, 0, 0);
            var oldest = scene.GetRootGameObjects().Single(x => x.name == "LocalWeaponImpacts").transform.GetChild(0).gameObject;
            first.Present(Vector3.right, Vector3.forward, surface.transform, 1, 0, 0);
            first.Present(Vector3.right * 2, Vector3.forward, surface.transform, 1, 0, 0);
            second.Present(Vector3.up, Vector3.forward, surface.transform, 1, 0, 0);
            Assert.That(oldest == null, Is.True, "FIFO must destroy the oldest object.");
            Assert.That(first.BulletHoleCount, Is.EqualTo(2));
            Assert.That(second.BulletHoleCount, Is.EqualTo(1), "Shooters must have independent quotas.");
            surface.transform.position = Vector3.up * 3;
            first.Tick(0); second.Tick(0);
            var positions = scene.GetRootGameObjects().Where(x => x.name == "LocalWeaponImpacts")
                .SelectMany(x => x.GetComponentsInChildren<MeshRenderer>()).Select(x => x.transform.position).ToArray();
            Assert.That(positions.Any(p => Vector3.Distance(p, new Vector3(1, 3, settings.SurfaceOffset)) < 0.0001f), Is.True);
            Set(settings, "maxBulletHolesPerPlayer", 1);
            first.Tick(0);
            Assert.That(first.BulletHoleCount, Is.EqualTo(1));
            surface.SetActive(false);
            first.Tick(0); second.Tick(0);
            Assert.That(first.BulletHoleCount + second.BulletHoleCount, Is.Zero);
        }
        finally
        {
            first.Dispose(); second.Dispose();
            UnityEngine.Object.DestroyImmediate(settings); UnityEngine.Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void SparkCapacityCompletionTimeoutAndDisposeAreBounded()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var settings = ScriptableObject.CreateInstance<WeaponImpactSettings>();
        Set(settings, "sparkPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/VFX/SparkOnWall_vfx/Spark_vfx.prefab"));
        Set(settings, "maxSparksPerPlayer", 2);
        var visuals = new WeaponImpactVisuals(settings, scene);
        try
        {
            for (int i = 0; i < 3; i++) visuals.Present(Vector3.zero, Vector3.forward, null, 2, -1, 0);
            Assert.That(visuals.SparkCount, Is.EqualTo(2));
            var root = scene.GetRootGameObjects().Single(x => x.name == "LocalWeaponImpacts");
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            visuals.Tick(0.1f);
            Assert.That(visuals.SparkCount, Is.Zero, "Finished particle instances must be removed without waiting for timeout.");
            visuals.Present(Vector3.zero, Vector3.forward, null, 2, -1, 0);
            visuals.Tick(settings.SparkTimeout);
            Assert.That(visuals.SparkCount, Is.Zero);
            visuals.Present(Vector3.zero, Vector3.forward, null, 2, -1, 0);
            visuals.Dispose();
            Assert.That(root == null, Is.True);
            Assert.That(visuals.SparkCount, Is.Zero);
        }
        finally { visuals.Dispose(); UnityEngine.Object.DestroyImmediate(settings); EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void ApprovedProfileExcludesObliqueAndReferencesRequestedSpark()
    {
        var profile = AssetDatabase.LoadAssetAtPath<WeaponImpactSettings>("Assets/Art/VFX/BulletHoles/WeaponImpactSettings.asset");
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile.VariantCount, Is.EqualTo(3));
        var expected = new[] { "BulletHole_01_Compact", "BulletHole_02_Radial", "BulletHole_04_Fractured" };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.That(profile.GetMaterial(i).mainTexture.name, Is.EqualTo(expected[i]));
            Assert.That(ShaderUtil.ShaderHasError(profile.GetMaterial(i).shader), Is.False);
        }
        Assert.That(AssetDatabase.GetAssetPath(profile.SparkPrefab), Is.EqualTo("Assets/Art/VFX/SparkOnWall_vfx/Spark_vfx.prefab"));
    }

    [Test]
    public void RequestedSparkExpiresAfterItsParticlesWithoutWaitingTenSecondDuration()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var settings = AssetDatabase.LoadAssetAtPath<WeaponImpactSettings>("Assets/Art/VFX/BulletHoles/WeaponImpactSettings.asset");
        var visuals = new WeaponImpactVisuals(settings, scene);
        try
        {
            visuals.Present(Vector3.zero, Vector3.forward, null, 2, -1, 0);
            var systems = scene.GetRootGameObjects().Single().GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems) ps.Simulate(0.1f, false, true);
            Assert.That(systems.Sum(ps => ps.particleCount), Is.GreaterThan(0));
            visuals.Tick(0.1f);
            Assert.That(visuals.SparkCount, Is.EqualTo(1), "Live particles must survive StopEmitting.");
            foreach (var ps in systems)
            {
                ps.Simulate(2f, false, false);
                // EditMode Simulate 會重新開啟發射狀態；還原 Runtime 已停止發射的狀態，保留粒子。
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            visuals.Tick(2f);
            Assert.That(visuals.SparkCount, Is.Zero);
        }
        finally { visuals.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void UnspawnedRelayDoesNotReadNetworkedStateOrPublish()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var go = new GameObject("UnspawnedRelay");
        SceneManager.MoveGameObjectToScene(go, scene);
        try
        {
            var relay = go.AddComponent<PlayerWeaponImpactEffects>();
            Assert.DoesNotThrow(() => relay.PublishConfirmedHit(null, 1, go, Vector3.zero, Vector3.forward));
            Assert.That(scene.GetRootGameObjects().Length, Is.EqualTo(1));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void InvalidHitOrMissingSurfaceCannotCreateFloatingHole()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var settings = AssetDatabase.LoadAssetAtPath<WeaponImpactSettings>("Assets/Art/VFX/BulletHoles/WeaponImpactSettings.asset");
        var visuals = new WeaponImpactVisuals(settings, scene);
        try
        {
            visuals.Present(Vector3.zero, Vector3.forward, null, 1, 0, 0);
            visuals.Present(new Vector3(float.NaN, 0, 0), Vector3.forward, null, 3, 0, 0);
            visuals.Present(Vector3.zero, Vector3.zero, null, 3, 0, 0);
            Assert.That(visuals.BulletHoleCount + visuals.SparkCount, Is.Zero);
        }
        finally { visuals.Dispose(); EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void HoleAndSparkUseIndependentHitLayerMasks()
    {
        var type = typeof(AttackRifle).Assembly.GetType("WeaponImpactSettings");
        Assert.That(type, Is.Not.Null, "命中特效需要獨立的彈孔／火花 Layer 篩選。");
        var settings = ScriptableObject.CreateInstance(type);
        try
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            type.GetField("bulletHoleLayers", flags).SetValue(settings, (LayerMask)(1 << 10));
            type.GetField("sparkLayers", flags).SetValue(settings, (LayerMask)(1 << 11));
            var method = type.GetMethod("GetEffectsForLayer");
            Assert.That(method.Invoke(settings, new object[] { 10 }), Is.EqualTo((byte)1));
            Assert.That(method.Invoke(settings, new object[] { 11 }), Is.EqualTo((byte)2));
            Assert.That(method.Invoke(settings, new object[] { 12 }), Is.EqualTo((byte)0));
            Assert.That(method.Invoke(settings, new object[] { -1 }), Is.EqualTo((byte)0));
            Assert.That(method.Invoke(settings, new object[] { 32 }), Is.EqualTo((byte)0));
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
}
