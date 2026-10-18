using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class BeamPresentationIntegrationTests
{
    [Test]
    public void AllStagesKeepEndpointsAndOnlyShotShrinks()
    {
        var go = new GameObject("Beam visual test");
        var line = go.AddComponent<LineRenderer>();
        var settings = new BeamPresentationSettings();
        settings.particles.enabled = false;
        var visual = new BeamPresentationRuntime(go.transform, line);
        Vector3 start = new Vector3(1, 2, 3), end = new Vector3(10, 4, 9);
        try
        {
            visual.Present(settings, BeamPresentationStage.Hidden, 0, start, end);
            visual.Present(settings, BeamPresentationStage.Aim, 1, start, end);
            Assert.That(line.widthMultiplier, Is.EqualTo(settings.aimWidth));
            Assert.That(Vector4.Distance(line.startColor, settings.aimColor), Is.LessThan(.005f));
            visual.Present(settings, BeamPresentationStage.Charge, 1, start, end);
            Assert.That(line.widthMultiplier, Is.EqualTo(settings.chargeWidth));
            Assert.That(Vector4.Distance(line.startColor, settings.chargeColor), Is.LessThan(.005f));
            visual.Present(settings, BeamPresentationStage.Fired, 1, start, end, 1);
            Assert.That(line.widthMultiplier, Is.GreaterThan(settings.chargeWidth));
            visual.Present(settings, BeamPresentationStage.Fired, 1, start, end, .25f);
            Assert.That(line.widthMultiplier, Is.EqualTo(settings.shotWidth * .25f));
            Assert.That(line.GetPosition(0), Is.EqualTo(start));
            Assert.That(line.GetPosition(1), Is.EqualTo(end));
            visual.Present(settings, BeamPresentationStage.Fired, 1, start, end, 0);
            Assert.That(line.enabled, Is.False);
            visual.Present(settings, BeamPresentationStage.Aim, 2, start, end);
            Assert.That(line.widthMultiplier, Is.EqualTo(settings.aimWidth));
            visual.Hide();
            Assert.That(line.enabled, Is.False);
            Assert.That(line.widthMultiplier, Is.Zero);
        }
        finally { visual.Dispose(); Object.DestroyImmediate(go); }
    }

    [Test]
    public void ShotBurstIsBoundedOncePerAttackAndDoesNotChangeGameplayRandom()
    {
        var go = new GameObject("Beam particles test");
        var settings = new BeamPresentationSettings();
        var visual = new BeamPresentationRuntime(go.transform);
        var original = Random.state;
        try
        {
            visual.Present(settings, BeamPresentationStage.Hidden, 0, Vector3.zero, Vector3.forward * 8);
            visual.Present(settings, BeamPresentationStage.Fired, 1, Vector3.zero, Vector3.forward * 8);
            Assert.That(visual.BurstCount, Is.EqualTo(1));
            visual.Present(settings, BeamPresentationStage.Fired, 1, Vector3.zero, Vector3.forward * 8, .5f);
            Assert.That(visual.BurstCount, Is.EqualTo(1));
            Assert.That(Random.state, Is.EqualTo(original));
            var particles = go.GetComponentInChildren<ParticleSystem>();
            Assert.That(particles.main.maxParticles, Is.EqualTo(512));
            Assert.That(particles.collision.enabled, Is.False);
            Assert.That(particles.emission.enabled, Is.False);
            visual.Hide();
            Assert.That(particles.particleCount, Is.Zero);
            visual.Present(settings, BeamPresentationStage.Fired, 2, Vector3.zero, Vector3.forward * 8);
            Assert.That(visual.BurstCount, Is.EqualTo(2));
        }
        finally { Random.state = original; visual.Dispose(); Object.DestroyImmediate(go); }
    }

    [Test]
    public void LateArrivalRestoresWidthWithoutBurstOrStageOneShots()
    {
        var go = new GameObject("Late beam test");
        var line = go.AddComponent<LineRenderer>();
        var visual = new BeamPresentationRuntime(go.transform, line);
        try
        {
            var settings = new BeamPresentationSettings();
            visual.Present(settings, BeamPresentationStage.Fired, 55, Vector3.zero, Vector3.forward * 10, .3f);
            Assert.That(line.enabled, Is.True);
            Assert.That(line.widthMultiplier, Is.EqualTo(settings.shotWidth * .3f).Within(.0001f));
            Assert.That(visual.BurstCount, Is.Zero);
            Assert.That(visual.StageEventCount, Is.Zero);
        }
        finally { visual.Dispose(); Object.DestroyImmediate(go); }
    }

    [TestCase("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_B.prefab")]
    [TestCase("Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_Beam.prefab")]
    [TestCase("Assets/Prefabs/AbilityRuntime/Active/PiercingCannon.prefab")]
    public void ProductionPrefabsHaveIndependentSettingsAndColorCapableMaterials(string path)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var presenter = root.GetComponentInChildren<EnemyBeamPresenter>(true);
        Object component = presenter != null ? (Object)presenter : root.GetComponent<PlayerPiercingCannonAbility>();
        var data = new SerializedObject(component).FindProperty("presentation");
        Assert.That(data, Is.Not.Null);
        var material = data.FindPropertyRelative("lineMaterial").objectReferenceValue as Material;
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("Purgers/BeamEnergy"));
        Assert.That(data.FindPropertyRelative("particles.material").objectReferenceValue, Is.Not.Null);
        Assert.That(data.FindPropertyRelative("shotWidth").floatValue, Is.GreaterThan(data.FindPropertyRelative("chargeWidth").floatValue));
        foreach (string stage in new[] { "prepare", "aim", "charge", "fire" })
            Assert.That(data.FindPropertyRelative("audio." + stage + ".cue"), Is.Not.Null);
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject), Is.Zero);
    }

    [Test]
    public void CustomShotVisualSuppressesBuiltInBurstButRetainsTelegraphAndSingleEvent()
    {
        var go = new GameObject("Custom beam test");
        var line = go.AddComponent<LineRenderer>();
        var visual = new BeamPresentationRuntime(go.transform, line);
        try
        {
            var settings = new BeamPresentationSettings();
            visual.Present(settings, BeamPresentationStage.Hidden, 0, Vector3.zero, Vector3.forward);
            visual.Present(settings, BeamPresentationStage.Aim, 1, Vector3.zero, Vector3.forward, 1, false);
            Assert.That(line.enabled, Is.True);
            Assert.That(visual.Present(settings, BeamPresentationStage.Fired, 1, Vector3.zero, Vector3.forward, 1, false), Is.True);
            Assert.That(line.enabled, Is.False);
            Assert.That(visual.BurstCount, Is.Zero);
            Assert.That(visual.Present(settings, BeamPresentationStage.Fired, 1, Vector3.zero, Vector3.forward, .5f, false), Is.False);
        }
        finally { visual.Dispose(); Object.DestroyImmediate(go); }
    }

}
