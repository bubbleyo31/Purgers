using System;
using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class FirstPersonMuzzleFlashTests
{
    private object playback;
    private Type type;

    [SetUp]
    public void SetUp()
    {
        type = typeof(AttackRifle).Assembly.GetType("FirstPersonMuzzleFlashPlayback");
        Assert.That(type, Is.Not.Null, "需要有限容量、序號去重的槍口火焰播放狀態。");
        playback = Activator.CreateInstance(type);
    }

    private bool Observe(int sequence, int state = 0, bool eligible = true) =>
        (bool)type.GetMethod("Observe").Invoke(playback, new object[] { sequence, state, eligible });
    private int Advance(float delta, float fps = 24f) =>
        (int)type.GetMethod("Advance").Invoke(playback, new object[] { delta, fps });
    private bool Flag(string name) => (bool)type.GetProperty(name).GetValue(playback);

    [Test]
    public void BindingNeverReplaysHistoricalShotsAndRepeatedSnapshotsNeverFire()
    {
        Assert.That(Observe(20), Is.False);
        Assert.That(Observe(20), Is.False);
        Assert.That(Observe(21), Is.True);
        Assert.That(Observe(21), Is.False);
    }

    [Test]
    public void PredictionRollbackAndCatchupDoNotDuplicateFlash()
    {
        Observe(40);
        Assert.That(Observe(41), Is.True);
        Assert.That(Observe(39), Is.False);
        Assert.That(Observe(40), Is.False);
        Assert.That(Observe(41), Is.False);
        Assert.That(Observe(42), Is.True);
    }

    [Test]
    public void RapidFireRestartsOnePlaybackAndEndsWithoutQueue()
    {
        Observe(0);
        for (int i = 1; i <= 10000; i++)
        {
            Assert.That(Observe(i), Is.True);
            Assert.That(Advance(0), Is.Zero);
            Advance(0.01f);
        }
        Assert.That(Advance(1), Is.EqualTo(-1));
        Assert.That(Flag("IsPlaying"), Is.False);
        Assert.That(Observe(10000), Is.False);
    }

    [Test]
    public void StateChangeReleasesTailAndBlockedShotsAreNotReplayed()
    {
        Observe(0);
        Observe(1);
        Assert.That(Observe(1, 1), Is.False);
        Assert.That(Flag("ReleaseRequested"), Is.True);
        Assert.That(Flag("IsPlaying"), Is.False);
        Assert.That(Observe(2, 1, false), Is.False);
        Assert.That(Observe(2, 1, true), Is.False);
        Assert.That(Observe(3, 1), Is.True);
    }

    [Test]
    public void RebindingAndSignedSequenceWrapRemainSafe()
    {
        Observe(int.MaxValue);
        Assert.That(Observe(int.MinValue), Is.True);
        type.GetMethod("Invalidate").Invoke(playback, null);
        Assert.That(Flag("IsPlaying"), Is.False);
        Assert.That(Observe(300), Is.False);
        Assert.That(Observe(301), Is.True);
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    [Test]
    public void RendererStaysBoundedFollowsMuzzleAndKeepsScreenSizeAfterCameraTurns()
    {
        var root = new GameObject("Muzzle flash isolated fixture");
        try
        {
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(root.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0.3f, -0.2f, 2f);
            var presenter = root.AddComponent<FirstPersonMuzzleFlash>();
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/MuzzleFlash/MuzzleFlash.mat");
            Assert.That(material, Is.Not.Null);
            Set(presenter, "muzzle", muzzle);
            Set(presenter, "viewCamera", camera);
            Set(presenter, "flashMaterial", material);
            Call(presenter, "EnsureVisual");
            var renderer = root.GetComponentInChildren<MeshRenderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            for (int i = 0; i < 1000; i++)
            {
                Call(presenter, "EnsureVisual");
                Call(presenter, "PresentFrame", i % 12);
            }
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length, Is.EqualTo(1));
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            Assert.That(renderer.transform.parent, Is.SameAs(muzzle));

            root.transform.SetPositionAndRotation(new Vector3(50, 12, -30), Quaternion.Euler(20, 120, 9));
            muzzle.localPosition = new Vector3(0.4f, -0.1f, 3f);
            camera.fieldOfView = 42f;
            Call(presenter, "PresentFrame", 3);
            Assert.That(Quaternion.Angle(renderer.transform.rotation, camera.transform.rotation), Is.LessThan(0.01f));
            // 預設 pivot=(0.5,0.38)：Quad 的局部 (0,-0.12,0) 必須落在真正槍口。
            Assert.That(Vector3.Distance(renderer.transform.TransformPoint(new Vector3(0, -0.12f, 0)), muzzle.position), Is.LessThan(0.0001f));
            var top = camera.WorldToViewportPoint(renderer.transform.TransformPoint(new Vector3(0, 0.5f, 0)));
            var bottom = camera.WorldToViewportPoint(renderer.transform.TransformPoint(new Vector3(0, -0.5f, 0)));
            Assert.That(top.y - bottom.y, Is.EqualTo(0.18f).Within(0.0001f));

            presenter.enabled = false;
            // 一般 MonoBehaviour 的生命週期不會在 EditMode 自動執行。
            Call(presenter, "OnDisable");
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
            Assert.That(mesh == null, Is.True, "停用必須釋放自建 Mesh。");
        }
        finally
        {
            var presenter = root.GetComponent<FirstPersonMuzzleFlash>();
            if (presenter != null) presenter.UnbindGameplaySources();
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void UnspawnedSourcesNeverReadNetworkStateOrCreateVisuals()
    {
        var root = new GameObject("Unspawned muzzle fixture");
        try
        {
            var rifle = root.AddComponent<AttackRifle>();
            var presenter = root.AddComponent<FirstPersonMuzzleFlash>();
            Assert.DoesNotThrow(() => presenter.BindGameplaySources(PlayerProfessionType.Attack,
                rifle, null, null, null, null, root.transform, null));
            Assert.DoesNotThrow(() => Call(presenter, "LateUpdate"));
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true), Is.Empty);
            presenter.UnbindGameplaySources();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies()
    {
        const string path = "Assets/Art/VFX/MuzzleFlash/StylizedMuzzleFlash_4x3.png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        Assert.That(texture.width, Is.EqualTo(1448));
        Assert.That(texture.height, Is.EqualTo(1086));
        Assert.That(importer.mipmapEnabled, Is.False);
        Assert.That(importer.alphaIsTransparency, Is.True);
        Assert.That(importer.isReadable, Is.False);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/VFX/MuzzleFlash/MuzzleFlash.mat");
        Assert.That(material.mainTexture, Is.SameAs(texture));
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
    }
}
