using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Purgers.GameFlow.Stage;
using Purgers.Map;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class BossStageAssetTests
{
    private const string BossChunkPath =
        "Assets/Prefabs/Map/MapChunk_Boss.prefab";
    private const string BossEnemyPath =
        "Assets/Prefabs/Enemy/Variants/Enemy_Boss.prefab";

    [Test]
    public void BossChunkContainsRequiredRuntimeContract()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            BossChunkPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<Fusion.NetworkObject>(), Is.Null,
            "Boss Chunk itself must remain a non-networked map prefab.");
        Assert.That(prefab.GetComponentsInChildren<Collider>(true),
            Is.Not.Empty);

        MapChunk chunk = prefab.GetComponent<MapChunk>();
        Assert.That(chunk, Is.Not.Null);
        Assert.That(chunk.Cartography, Is.Not.Null);
        Assert.That(chunk.Cartography.IsValid, Is.True);

        MapConnector[] connectors =
            prefab.GetComponentsInChildren<MapConnector>(true);
        Assert.That(connectors, Has.Length.EqualTo(4));
        Assert.That(connectors.Select(value => value.Side).Distinct().Count(),
            Is.EqualTo(4));
        Assert.That(connectors.All(value => value.PlayerSpawnPoint != null),
            Is.True);

        BossSpawnPoint[] bossSpawns =
            prefab.GetComponentsInChildren<BossSpawnPoint>(true)
                .Where(value => value.enabled &&
                    value.gameObject.activeSelf)
                .ToArray();
        Assert.That(bossSpawns, Has.Length.EqualTo(1));

        NavMeshSurface[] surfaces =
            prefab.GetComponentsInChildren<NavMeshSurface>(true);
        Assert.That(surfaces, Has.Length.EqualTo(1));
        Assert.That(surfaces[0].navMeshData, Is.Not.Null);
    }

    [Test]
    public void TemporaryBossPrefabSupportsAuthoritativeObjective()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            BossEnemyPath);

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<Fusion.NetworkObject>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<EnemyActor>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<TestDamageReceiver>(), Is.Not.Null);
    }

    [Test]
    public void GameSceneContainsBossAssetWiring()
    {
        const string scenePath = "Assets/Scenes/Game.unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool owned = !scene.IsValid() || !scene.isLoaded;
        if (owned)
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            MapRunSelectionPrototype selection = roots
                .SelectMany(value => value.GetComponentsInChildren<MapRunSelectionPrototype>(true))
                .Single();
            StageFlowController flow = roots
                .SelectMany(value => value.GetComponentsInChildren<StageFlowController>(true))
                .Single();
            BossStageObjectiveController objective =
                flow.GetComponent<BossStageObjectiveController>();
            NetworkMapState map = flow.GetComponent<NetworkMapState>();
            MapChunk bossChunk = AssetDatabase.LoadAssetAtPath<GameObject>(
                BossChunkPath).GetComponent<MapChunk>();

            var selectionSerialized = new SerializedObject(selection);
            Assert.That(selectionSerialized.FindProperty("bossChunkPrefab")
                .objectReferenceValue, Is.SameAs(bossChunk));

            var mapSerialized = new SerializedObject(map);
            SerializedProperty catalog = mapSerialized.FindProperty("catalog");
            Assert.That(Enumerable.Range(0, catalog.arraySize).Any(index =>
                catalog.GetArrayElementAtIndex(index).objectReferenceValue == bossChunk),
                Is.True);

            Assert.That(objective, Is.Not.Null);
            var flowSerialized = new SerializedObject(flow);
            Assert.That(flowSerialized.FindProperty("bossObjectiveController")
                .objectReferenceValue, Is.SameAs(objective));

            var prefabField = typeof(BossStageObjectiveController).GetField(
                "bossPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
            var prefabRef = (Fusion.NetworkPrefabRef)prefabField.GetValue(objective);
            Assert.That(prefabRef.IsValid, Is.True);

            string bossGuid = AssetDatabase.AssetPathToGUID(BossEnemyPath);
            Assert.That(Fusion.NetworkProjectConfigAsset.Global.Prefabs
                .OfType<Fusion.NetworkPrefabSourceStaticLazy>()
                .Any(value => string.Equals(
                    value.AssetGuid.ToString().Replace("-", string.Empty),
                    bossGuid,
                    StringComparison.OrdinalIgnoreCase)),
                Is.True);
        }
        finally
        {
            if (owned)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [TestCase("cells")]
    [TestCase("elevations")]
    public void CartographyPayloadIsHiddenFromDefaultInspector(string fieldName)
    {
        FieldInfo field = typeof(MapCartographyData).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null);
        Assert.That(field.GetCustomAttribute<HideInInspector>(), Is.Not.Null,
            $"{fieldName} is a large baked payload and must not be expanded " +
            "by Unity's default Inspector.");
    }
}
