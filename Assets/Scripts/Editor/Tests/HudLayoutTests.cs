using System.Reflection;
using NUnit.Framework;
using Purgers.GameFlow.SafeHouse;
using Purgers.GameFlow.Stage;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class HudLayoutTests
{
    [TestCase(typeof(StageHudController))]
    [TestCase(typeof(SafeHouseHudController))]
    public void HudSurvivesFusionSceneMigration(System.Type controllerType)
    {
        var source = EditorSceneManager.NewPreviewScene();
        var destination = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("MigratingHud");
        SceneManager.MoveGameObjectToScene(root, source);
        try
        {
            var controller = root.AddComponent(controllerType);
            controllerType.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            SceneManager.MoveGameObjectToScene(root, destination);
            // Fusion merges the loaded scene into its Runner scene. Its old handle
            // no longer owns the HUD when the unload notification arrives.
            controllerType.GetMethod("OnSceneUnloaded", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { source });
            Assert.That(root != null && root.activeSelf, Is.True,
                "Migrating into the Fusion Runner scene must not destroy the current HUD.");
        }
        finally
        {
            if (root) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(source);
            EditorSceneManager.ClosePreviewScene(destination);
        }
    }

    [Test]
    public void NewStageHudReplacesThePreviousHudForTheSameRunner()
    {
        var registry = typeof(StageHudController).Assembly.GetType(
            "Purgers.GameFlow.Stage.StageHudLifetimeRegistry");
        Assert.That(registry, Is.Not.Null,
            "Stage HUD needs a Runner-scoped owner so an older level cannot remain visible.");

        var claim = registry.GetMethod("Claim", BindingFlags.Public | BindingFlags.Static);
        var reset = registry.GetMethod("ResetForTests", BindingFlags.Public | BindingFlags.Static);
        Assert.That(claim, Is.Not.Null);
        Assert.That(reset, Is.Not.Null);

        var runnerObject = new GameObject("Runner");
        var firstHudObject = new GameObject("StageHUD-Level1");
        var secondHudObject = new GameObject("StageHUD-Level2");
        try
        {
            var runner = runnerObject.AddComponent<Fusion.NetworkRunner>();
            var firstHud = firstHudObject.AddComponent<StageHudController>();
            var secondHud = secondHudObject.AddComponent<StageHudController>();

            claim.Invoke(null, new object[] { runner, 1, firstHud });
            claim.Invoke(null, new object[] { runner, 2, secondHud });

            Assert.That(firstHudObject.activeSelf, Is.False,
                "The previous level HUD must disappear in the same frame that the next level HUD claims ownership.");
            Assert.That(secondHudObject.activeSelf, Is.True);
        }
        finally
        {
            reset?.Invoke(null, null);
            if (runnerObject) Object.DestroyImmediate(runnerObject);
            if (firstHudObject) Object.DestroyImmediate(firstHudObject);
            if (secondHudObject) Object.DestroyImmediate(secondHudObject);
        }
    }

    [Test]
    public void StageHudOwnershipIsIndependentPerRunner()
    {
        var registry = typeof(StageHudController).Assembly.GetType(
            "Purgers.GameFlow.Stage.StageHudLifetimeRegistry");
        Assert.That(registry, Is.Not.Null);

        var claim = registry.GetMethod("Claim", BindingFlags.Public | BindingFlags.Static);
        var reset = registry.GetMethod("ResetForTests", BindingFlags.Public | BindingFlags.Static);
        var runnerAObject = new GameObject("Runner-A");
        var runnerBObject = new GameObject("Runner-B");
        var hudAObject = new GameObject("StageHUD-A");
        var hudBObject = new GameObject("StageHUD-B");
        try
        {
            var runnerA = runnerAObject.AddComponent<Fusion.NetworkRunner>();
            var runnerB = runnerBObject.AddComponent<Fusion.NetworkRunner>();
            var hudA = hudAObject.AddComponent<StageHudController>();
            var hudB = hudBObject.AddComponent<StageHudController>();

            claim.Invoke(null, new object[] { runnerA, 1, hudA });
            claim.Invoke(null, new object[] { runnerB, 4, hudB });

            Assert.That(hudAObject.activeSelf, Is.True);
            Assert.That(hudBObject.activeSelf, Is.True);
        }
        finally
        {
            reset?.Invoke(null, null);
            if (runnerAObject) Object.DestroyImmediate(runnerAObject);
            if (runnerBObject) Object.DestroyImmediate(runnerBObject);
            if (hudAObject) Object.DestroyImmediate(hudAObject);
            if (hudBObject) Object.DestroyImmediate(hudBObject);
        }
    }

    [TestCase(1, 1, 4, false, "關卡等級  1　循環  1/4")]
    [TestCase(4, 4, 4, true, "關卡等級  4　Boss 關")]
    [TestCase(5, 1, 4, false, "關卡等級  5　循環  1/4")]
    public void StageHeaderShowsCycleAndBossRoute(
        int stageLevel,
        int cycleStage,
        int cycleLength,
        bool isBossStage,
        string expected)
    {
        Assert.That(
            StageHudText.BuildStageHeader(
                stageLevel,
                cycleStage,
                cycleLength,
                isBossStage),
            Is.EqualTo(expected));
    }

    [TestCase(600f, "10:00")]
    [TestCase(59.1f, "01:00")]
    [TestCase(0f, "00:00")]
    [TestCase(-1f, "00:00")]
    public void StageCountdownContainsOnlyClockDigits(float seconds, string expected)
    {
        var method = typeof(StageHudController).GetMethod("FormatTime",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method.Invoke(null, new object[] { seconds }), Is.EqualTo(expected));
    }

    [TestCase(SafeHousePhase.WaitingForHost, false)]
    [TestCase(SafeHousePhase.ReadyCheck, true)]
    [TestCase(SafeHousePhase.Countdown, true)]
    [TestCase(SafeHousePhase.LoadingStage, false)]
    public void ReadyPanelIsVisibleOnlyDuringVoting(SafeHousePhase phase, bool expected)
    {
        var method = typeof(SafeHouseHudController).GetMethod("ShouldShowReadyPanel",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, "Ready visibility must exclude LoadingStage.");
        Assert.That(method.Invoke(null, new object[] { phase }), Is.EqualTo(expected));
    }

    [TestCase("Game", "StageHUD", "StagePanel")]
    [TestCase("SafeHouse", "SafeHouseHUD", "StageLevelPanel")]
    public void LeftColumnReservesMapBeforeStatusAndReady(string sceneName, string hudName, string statusName)
    {
        string path = "Assets/Scenes/" + sceneName + ".unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!alreadyLoaded)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var hud = System.Array.Find(scene.GetRootGameObjects(), go => go.name == hudName);
            Assert.That(hud, Is.Not.Null);
            var column = hud.transform.Find("ReferenceFrame/TopLeftColumn") ?? hud.transform.Find("TopLeftColumn");
            Assert.That(column, Is.Not.Null);
            var map = column.Find("MinimapSlot") as RectTransform;
            var status = column.Find(statusName) as RectTransform;
            var ready = column.Find("ReadyCheckSlot") as RectTransform;
            Assert.That(map, Is.Not.Null);
            Assert.That(status, Is.Not.Null);
            Assert.That(ready, Is.Not.Null);
            Assert.That(map.GetSiblingIndex(), Is.LessThan(status.GetSiblingIndex()));
            Assert.That(status.GetSiblingIndex(), Is.LessThan(ready.GetSiblingIndex()));
            Assert.That(map.Find("MinimapContent").childCount, Is.Zero, "No minimap logic or content yet.");
            Assert.That(map.anchoredPosition.y - map.sizeDelta.y, Is.GreaterThan(status.anchoredPosition.y));
            Assert.That(status.anchoredPosition.y - status.sizeDelta.y, Is.GreaterThan(ready.anchoredPosition.y));
            Assert.That(hud.GetComponent<Canvas>().sortingOrder, Is.LessThan(short.MaxValue));
        }
        finally
        {
            if (!alreadyLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [TestCase("Game", "StageHUD", typeof(StageHudController))]
    [TestCase("SafeHouse", "SafeHouseHUD", typeof(SafeHouseHudController))]
    public void StageScopedHudControllerOwnsTheWholeSceneHud(
        string sceneName,
        string hudName,
        System.Type controllerType)
    {
        string path = "Assets/Scenes/" + sceneName + ".unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!alreadyLoaded)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var hud = System.Array.Find(scene.GetRootGameObjects(), go => go.name == hudName);
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.GetComponent(controllerType), Is.Not.Null,
                "The lifetime owner must sit on the HUD root so every stage-specific child is cleared together.");

            var stageControllers = hud.GetComponentsInChildren<StageHudController>(true);
            var safeHouseControllers = hud.GetComponentsInChildren<SafeHouseHudController>(true);
            Assert.That(stageControllers.Length + safeHouseControllers.Length, Is.EqualTo(1));

            var minimaps = hud.GetComponentsInChildren<LocalMinimapController>(true);
            Assert.That(minimaps.Length, Is.EqualTo(1),
                "Each scene owns one minimap, bound to its own data source.");
            if (sceneName == "SafeHouse")
            {
                var mapSerialized = new SerializedObject(minimaps[0]);
                Assert.That(mapSerialized.FindProperty("map").objectReferenceValue, Is.Null);
                Assert.That(mapSerialized.FindProperty("safeHouse").objectReferenceValue, Is.Not.Null);
                var data = mapSerialized.FindProperty("safeHouseCartography").objectReferenceValue
                    as Purgers.Map.MapCartographyData;
                Assert.That(data != null && data.IsValid, Is.True);
            }

            if (sceneName == "Game")
            {
                var stageHud = hud.GetComponent<StageHudController>();
                var stageSerialized = new SerializedObject(stageHud);
                var minimapSerialized = new SerializedObject(minimaps[0]);
                Assert.That(stageSerialized.FindProperty("flowController").objectReferenceValue,
                    Is.Not.Null);
                Assert.That(minimapSerialized.FindProperty("map").objectReferenceValue,
                    Is.Not.Null);
            }
        }
        finally
        {
            if (!alreadyLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void MapChunkPrefabDoesNotContainStageScopedUi()
    {
        const string chunkPath = "Assets/Prefabs/Map/MapChunk_Prototype.prefab";
        const string hudPath = "Assets/Prefabs/UI/StageHUD.prefab";
        var chunk = AssetDatabase.LoadAssetAtPath<GameObject>(chunkPath);
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);

        Assert.That(chunk, Is.Not.Null);
        Assert.That(hud, Is.Not.Null);
        Assert.That(chunk.GetComponentsInChildren<StageHudController>(true), Is.Empty,
            "A generated MapChunk must never create a screen-space Stage HUD.");
        Assert.That(chunk.GetComponentsInChildren<LocalMinimapController>(true), Is.Empty,
            "A generated MapChunk must never create a local minimap controller.");
        Assert.That(hud.GetComponent<StageHudController>(), Is.Not.Null);
        Assert.That(hud.GetComponent<LocalMinimapController>(), Is.Not.Null);
    }

    [Test]
    public void SpeedVisualAnchorsToCanvasAndHidesBonusWithGauge()
    {
        const string path = "Assets/Scenes/_Menu.unity";
        var scene = SceneManager.GetSceneByPath(path);
        bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!alreadyLoaded)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            LocalPlayerSpeedSlider controller = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                controller = root.GetComponentInChildren<LocalPlayerSpeedSlider>(true);
                if (controller) break;
            }
            Assert.That(controller, Is.Not.Null);
            var serialized = new SerializedObject(controller);
            var visual = (GameObject)serialized.FindProperty("speedVisualRoot").objectReferenceValue;
            var bonus = (TMPro.TMP_Text)serialized.FindProperty("damageBonusLabel").objectReferenceValue;
            var gauge = (UnityEngine.UI.Slider)serialized.FindProperty("speedSlider").objectReferenceValue;
            var extraction = (GameObject)serialized.FindProperty("extractionPromptRoot")?.objectReferenceValue;
            Assert.That(visual.transform.parent.GetComponent<BattleHudReferenceFrame>(), Is.Not.Null,
                "The lower HUD must share the uniformly scaled drawing frame.");
            Assert.That(bonus.transform.IsChildOf(visual.transform), Is.True);
            Assert.That(gauge.transform.IsChildOf(visual.transform), Is.True);
            Assert.That(controller.transform.IsChildOf(visual.transform), Is.False);
            Assert.That(((RectTransform)visual.transform).anchorMin, Is.EqualTo(new Vector2(0f, 0f)));
            Assert.That(((RectTransform)visual.transform).anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(visual.GetComponentsInChildren<TMPro.TMP_Text>(true).Length, Is.EqualTo(2));
            Assert.That(gauge.minValue, Is.Zero);
            Assert.That(gauge.maxValue, Is.EqualTo(1f));
            Assert.That(extraction, Is.Not.Null);
            Assert.That(extraction.transform.IsChildOf(visual.transform), Is.True);
            Assert.That(extraction.GetComponent<UnityEngine.UI.Image>().color.g,
                Is.GreaterThan(extraction.GetComponent<UnityEngine.UI.Image>().color.r));
        }
        finally
        {
            if (!alreadyLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [TestCase(0.9f, true, false, 1f, 10f, 2f, 1f, true)]
    [TestCase(1f, true, true, 0.1f, 10f, 2f, 1f, true)]
    [TestCase(1f, false, true, 1f, 10f, 2f, 1f, false)]
    [TestCase(1f, false, false, 0.5f, 10f, 2f, 0.75f, false)]
    [TestCase(0.5f, true, false, 1f, 10f, 2f, 0.6f, false)]
    public void MomentumEnergyPeakHoldIsRefreshableWithoutStacking(
        float energy,
        bool charging,
        bool maximumBoostActive,
        float deltaTime,
        float chargeDuration,
        float decayDuration,
        float expectedEnergy,
        bool expectedRefresh)
    {
        var rules = typeof(PlayerGrappleMomentumEnergy).Assembly
            .GetType("PlayerGrappleMomentumEnergyRules");
        Assert.That(rules, Is.Not.Null);
        var evaluate = rules.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static);
        Assert.That(evaluate, Is.Not.Null);
        object step = evaluate.Invoke(null, new object[]
        {
            energy, charging, maximumBoostActive, deltaTime, chargeDuration, decayDuration
        });
        var stepType = step.GetType();
        float actualEnergy = (float)stepType.GetField("Energy").GetValue(step);
        bool actualRefresh = (bool)stepType.GetField("RefreshMaximumBoost").GetValue(step);
        Assert.That(actualEnergy, Is.EqualTo(expectedEnergy).Within(0.0001f));
        Assert.That(actualRefresh, Is.EqualTo(expectedRefresh));
    }
}
