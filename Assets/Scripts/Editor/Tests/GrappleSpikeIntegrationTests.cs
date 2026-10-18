using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class GrappleSpikeIntegrationTests
{
    [Test] public void EachNetworkTargetHasIndependentHalfSecondInterval()
    {
        var ledger = new GrappleSpikeHitLedger();
        var a = new NetworkId { Raw = 101 }; var b = new NetworkId { Raw = 102 };
        ledger.Record(a, 100, 30);
        Assert.That(ledger.CanHit(a, 100), Is.False, "多 Collider 不能同 Tick 重傷");
        Assert.That(ledger.CanHit(a, 129), Is.False);
        Assert.That(ledger.CanHit(a, 130), Is.True);
        Assert.That(ledger.CanHit(b, 100), Is.True);
        ledger.Prune(129); Assert.That(ledger.CanHit(a, 129), Is.False);
        ledger.Prune(130); Assert.That(ledger.CanHit(a, 130), Is.True);
    }
    [Test] public void DifferentPlayersDoNotShareTargetImmunity()
    {
        var a = new GrappleSpikeHitLedger(); var b = new GrappleSpikeHitLedger();
        var target = new NetworkId { Raw = 20 };
        a.Record(target, 100, 30);
        Assert.That(b.CanHit(target, 100), Is.True);
    }
    [Test] public void SweptContactOriginAndKnockbackUseContactPosition()
    {
        var center = GrappleSpikeRules.ClosestCenter(Vector3.zero, Vector3.right * 20f, new Vector3(10, 1, 0));
        Assert.That(center, Is.EqualTo(Vector3.right * 10));
        var direction = GrappleSpikeRules.OutwardDirection(center, new Vector3(10, 5, 2), Vector3.left);
        Assert.That(direction, Is.EqualTo(Vector3.forward));
        Assert.That(GrappleSpikeRules.OutwardDirection(center, center, Vector3.left), Is.EqualTo(Vector3.left));
    }
    [Test] public void PrototypeHasBoundedCostAndUnitRadius()
    {
        Mesh mesh = GrappleSpikeAssetSetup.BuildPrototypeMesh();
        try
        {
            Assert.That(mesh.vertexCount, Is.EqualTo(160));
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(192));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));
            foreach (Vector3 v in mesh.vertices) Assert.That(v.magnitude, Is.LessThanOrEqualTo(1.00001f));
        }
        finally { Object.DestroyImmediate(mesh); }
    }
    [Test] public void AssetsHaveOneMatchingModuleAndPureLocalVisual()
    {
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(GrappleSpikeAssetSetup.DefinitionPath);
        Assert.That(definition, Is.Not.Null, "請先建立獨立尖刺資產");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrappleSpikeAssetSetup.PrefabPath);
        Assert.That(definition.AbilityId, Is.EqualTo("grapple.spike"));
        Assert.That(definition.Category, Is.EqualTo(PlayerAbilityCategory.GrappleHit));
        Assert.That(prefab.GetComponent<PlayerAbilityRuntime>().ValidateConfiguration(definition, out string error), Is.True, error);
        var spike = prefab.GetComponent<PlayerGrappleSpikeAbility>();
        Assert.That(spike.IsAbilityActive, Is.False, "Prefab 不讀 Networked 狀態");
        Assert.That(spike.IsSpikeVisible, Is.False);
        var serialized = new SerializedObject(spike);
        Assert.That(serialized.FindProperty("includeReleaseMomentum").boolValue, Is.False);
        Assert.That(serialized.FindProperty("repeatHitSeconds").floatValue, Is.EqualTo(.5f));
        var visual = serialized.FindProperty("spikeVisualPrefab").objectReferenceValue as GameObject;
        Assert.That(visual, Is.Not.Null);
        Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(visual.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
        Assert.That(visual.GetComponentsInChildren<NetworkObject>(true), Is.Empty);
    }
}

