using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class ReloadCameraIntegrationTests
{
    private GameObject rig;
    private Scene preview;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    private static void Call(object o, string method) => o.GetType().GetMethod(method, Private).Invoke(o, null);
    private static void Angle(Quaternion actual, Quaternion expected) => Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.04f));
    [SetUp] public void Setup()
    {
        preview = EditorSceneManager.NewPreviewScene();
        rig = new GameObject("isolated reload camera");
        rig.SetActive(false);
        SceneManager.MoveGameObjectToScene(rig, preview);
    }
    [TearDown] public void Cleanup()
    {
        Object.DestroyImmediate(rig);
        EditorSceneManager.ClosePreviewScene(preview);
    }
    private Transform Child(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
    [Test] public void ParentTurnAndCameraEffectCannotFeedBackIntoBoneSample()
    {
        var effects = Child("effects", rig.transform);
        var model = Child("model", effects);
        var arm = Child("arm", model);
        var weapon = Child("Root", arm);
        weapon.localRotation = Quaternion.Euler(10, 20, 30);
        var baseline = Quaternion.Inverse(model.rotation) * weapon.rotation;
        arm.localRotation = Quaternion.Euler(0, 30, 0);
        var expected = Quaternion.Inverse(model.rotation) * weapon.rotation;
        for (int i = 0; i < 30; i++)
        {
            rig.transform.rotation = Quaternion.Euler(i * 2, i * 12, 0);
            effects.localRotation = Quaternion.Euler(i, -i, i * .3f);
            Angle(Quaternion.Inverse(model.rotation) * weapon.rotation, expected);
        }
        Assert.That(Quaternion.Angle(baseline, expected), Is.EqualTo(30).Within(.05));
    }
    [Test] public void ShakeCompletionPreservesReloadAndStaleOwnerCannotClearReplacement()
    {
        var effects = Child("effects", rig.transform);
        var sink = rig.AddComponent<FirstPersonHitCameraShake>();
        Set(sink, "cameraEffectsRoot", effects);
        var authored = Quaternion.Euler(2, 3, 4);
        Set(sink, "baseLocalRotation", authored);
        Set(sink, "baseLocalPosition", new Vector3(1, 2, 3));
        rig.SetActive(true);
        var first = Child("owner1", rig.transform);
        var second = Child("owner2", rig.transform);
        var reload = Quaternion.Euler(1, 2, 0);
        sink.SetReloadRotation(first, reload);
        Set(sink, "shakeRotation", Quaternion.Euler(-1, 0, 2));
        Call(sink, "ApplyEffectRotation");
        Angle(effects.localRotation, authored * reload * Quaternion.Euler(-1, 0, 2));
        Call(sink, "ResetShakeTransform");
        Angle(effects.localRotation, authored * reload);
        Assert.That(effects.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
        sink.SetReloadRotation(second, Quaternion.Euler(0, 1, 0));
        sink.ClearReloadRotation(first);
        Angle(effects.localRotation, authored * Quaternion.Euler(0, 1, 0));
        sink.ClearReloadRotation(second);
        Angle(effects.localRotation, authored);
    }
    [Test] public void DisabledSinkClearsBothContributions()
    {
        var effects = Child("effects", rig.transform);
        var sink = rig.AddComponent<FirstPersonHitCameraShake>();
        Set(sink, "cameraEffectsRoot", effects);
        Set(sink, "baseLocalRotation", Quaternion.identity);
        Set(sink, "reloadRotation", Quaternion.Euler(3, 0, 0));
        Set(sink, "shakeRotation", Quaternion.Euler(0, 2, 0));
        Call(sink, "OnDisable");
        Angle(effects.localRotation, Quaternion.identity);
    }
    [Test] public void UnspawnedOrUnboundDriverDoesNotReadNetworkedState()
    {
        var driver = rig.AddComponent<FirstPersonViewModelActionAnimator>();
        Assert.That(driver.TryGetLocalReloadCameraState(out _, out _), Is.False);
        int previous = driver.BindingVersion;
        driver.UnbindGameplaySources();
        Assert.That(driver.BindingVersion, Is.Not.EqualTo(previous));
        Assert.That(driver.TryGetLocalReloadCameraState(out _, out _), Is.False);
    }
    [TestCase("3d421ceae2128d74ea561e5e37f164d4", "RifleVine_Arms_Draft03", "2c7c8e328072ad944a1928157bac364a")]
    [TestCase("1d2a979e7a5c2144c81927185aecc5bd", "SMGBloom_Arms_Draft01", "ffc8164cfdd358b4aa4793e1a78b7ef9")]
    public void AuthoredReloadMovesRootThroughParentsAndReturnsToStart(string prefabGuid, string modelName, string clipGuid)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(prefabGuid));
        // 只複製模型進隔離 Preview Scene；不取樣、儲存或改寫原始資產。
        var model = Object.Instantiate(prefab.transform.Find(modelName).gameObject, rig.transform);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(clipGuid));
        var root = model.transform.Find("第一人稱手/骨骼.r/骨骼.001.r/骨骼.002.r/骨骼.003.r/Root");
        Assert.That(root, Is.Not.Null);
        clip.SampleAnimation(model, 0);
        var initial = Quaternion.Inverse(model.transform.rotation) * root.rotation;
        var local = root.localRotation;
        float largest = 0;
        for (int i = 1; i <= 120; i++)
        {
            clip.SampleAnimation(model, clip.length * i / 120f);
            var pose = Quaternion.Inverse(model.transform.rotation) * root.rotation;
            largest = Mathf.Max(largest, Quaternion.Angle(initial, pose));
            Angle(root.localRotation, local);
        }
        Assert.That(largest, Is.GreaterThan(20));
        Angle(Quaternion.Inverse(model.transform.rotation) * root.rotation, initial);
    }
    [Test] public void AttenuationStillWorksWhenAmplitudeHitsTheLimit()
    {
        var motion = new ReloadCameraMotion();
        motion.Prepare(false, Quaternion.identity); motion.Prepare(true, Quaternion.identity);
        var result = motion.Step(Quaternion.Euler(0, 90, 0), Quaternion.identity, Vector3.one, 4,
            600, 120, 540, .2f, 0, 0, 1);
        Angle(result, Quaternion.Euler(0, .8f, 0));
    }
}
