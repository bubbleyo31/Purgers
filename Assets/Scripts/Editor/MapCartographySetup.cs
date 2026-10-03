using System;
using System.Linq;
using Purgers.Map;
using Purgers.GameFlow.Stage;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Explicit editor migration. Never called automatically or included in a Player assembly.</summary>
public static class MapCartographySetup
{
    private const string PrefabPath = "Assets/Prefabs/Map/MapChunk_Prototype.prefab";
    private const string DataPath = "Assets/Prefabs/Map/MapChunk_Prototype_Cartography.asset";
    private const string BossPrefabPath = "Assets/Prefabs/Map/MapChunk_Boss.prefab";
    private const string BossDataPath = "Assets/Prefabs/Map/MapChunk_Boss_Cartography.asset";
    private const string LegacyBossDataPath = "Assets/Prefabs/Map/New Map Cartography Data.asset";
    private const string BossNavMeshPath = "Assets/Prefabs/Map/NavMesh-MapChunk_Boss.asset";
    private const string BossEnemyPath = "Assets/Prefabs/Enemy/Variants/Enemy_Boss.prefab";

    [MenuItem("Tools/Purgers/Map/Bake Prototype Cartography")]
    public static void BakePrototype()
    {
        BakeCartography(PrefabPath, DataPath);
    }

    [MenuItem("Tools/Purgers/Map/Bake SafeHouse Cartography")]
    public static void BakeSafeHouse()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit Mode only.");
        const string path = "Assets/Scenes/SafeHouse.unity";
        const string dataPath = "Assets/Prefabs/Map/SafeHouse_Cartography.asset";
        var scene = SceneManager.GetSceneByPath(path);
        bool owned = !scene.IsValid() || !scene.isLoaded;
        if (owned) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var roots = scene.GetRootGameObjects();
            var environment = roots.Single(r => r.name == "GrayboxEnvironment");
            var terminal = roots.Single(r => r.name == "StartTerminal");
            // Copy only colliders: avoid running gameplay scripts in the bake scene.
            var colliders = environment.GetComponentsInChildren<BoxCollider>()
                .Concat(terminal.GetComponentsInChildren<BoxCollider>())
                .Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length == 0) throw new InvalidOperationException("No SafeHouse graybox colliders.");
            Bounds bounds = colliders[0].bounds;
            foreach (var collider in colliders)
            {
                bounds.Encapsulate(collider.bounds);
                var copy = new GameObject("BakeCollider");
                SceneManager.MoveGameObjectToScene(copy, preview);
                copy.transform.SetPositionAndRotation(collider.transform.position, collider.transform.rotation);
                copy.transform.localScale = collider.transform.lossyScale;
                var box = copy.AddComponent<BoxCollider>();
                box.center = collider.center; box.size = collider.size;
            }
            // The current SafeHouse is a single open graybox floor, without NavMesh.
            // Low platforms (<= 0.6m above Floor) share its fill; furniture/walls are bright.
            float floorY = environment.transform.Find("Floor").GetComponent<Collider>().bounds.max.y;
            const float spacing = 0.25f;
            Vector2 min = new Vector2(bounds.min.x - 1, bounds.min.z - 1);
            int width = Mathf.CeilToInt((bounds.size.x + 2) / spacing);
            int height = Mathf.CeilToInt((bounds.size.z + 2) / spacing);
            var cells = new byte[width * height];
            var elevations = new float[cells.Length];
            Physics.SyncTransforms();
            var physics = preview.GetPhysicsScene();
            uint hash = 2166136261;
            for (int i = 0; i < cells.Length; i++)
            {
                var origin = new Vector3(min.x + (i % width + 0.5f) * spacing,
                    bounds.max.y + 1, min.y + (i / width + 0.5f) * spacing);
                if (physics.Raycast(origin, Vector3.down, out RaycastHit hit, bounds.size.y + 2,
                    ~0, QueryTriggerInteraction.Ignore))
                {
                    elevations[i] = hit.point.y;
                    cells[i] = hit.point.y <= floorY + 0.6f ? (byte)2 : (byte)1;
                }
                hash = unchecked((hash ^ cells[i]) * 16777619);
                hash = unchecked((hash ^ (uint)Mathf.RoundToInt(elevations[i] * 100)) * 16777619);
            }
            if (!cells.Contains((byte)2)) throw new InvalidOperationException("SafeHouse bake has no floor.");
            var data = AssetDatabase.LoadAssetAtPath<MapCartographyData>(dataPath);
            if (data == null) { data = ScriptableObject.CreateInstance<MapCartographyData>(); AssetDatabase.CreateAsset(data, dataPath); }
            data.SetBakedData(min, spacing, width, height, cells, elevations, unchecked((int)hash));
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cartography] SafeHouse baked {width}x{height} (graybox floor plan).");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (owned) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Purgers/Map/Configure, Bake and Wire Phase 4-F Boss")]
    public static void ConfigureBakeAndWireBoss()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Bake must run in Edit Mode.");
        if (NavMesh.CalculateTriangulation().vertices.Length != 0)
            throw new InvalidOperationException("請先切到沒有 NavMesh 的 _Menu 場景再烘焙，避免混入其他導航資料。");

        MoveLegacyBossCartography();
        ConfigureAndBakeBossPrefab();
        BakeCartography(BossPrefabPath, BossDataPath);
        WireBossGameScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[Phase4-F] Boss Chunk、Cartography、Boss Prefab 與 Game Scene 接線完成。");
    }

    private static void BakeCartography(string prefabPath, string dataPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Bake must run in Edit Mode.");
        if (NavMesh.CalculateTriangulation().vertices.Length != 0)
            throw new InvalidOperationException("請先切到沒有 NavMesh 的 _Menu 場景再烘焙，避免混入其他導航資料。");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            throw new MissingReferenceException($"Missing prefab: {prefabPath}");

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var chunk = root.GetComponent<MapChunk>();
            var surface = root.GetComponentInChildren<NavMeshSurface>();
            if (surface == null || surface.navMeshData == null) throw new InvalidOperationException("Missing baked NavMesh.");
            surface.RemoveData();
            surface.enabled = false;
            NavMeshTriangulation nav;
            var navInstance = NavMesh.AddNavMeshData(surface.navMeshData, surface.transform.position, surface.transform.rotation);
            try { nav = NavMesh.CalculateTriangulation(); }
            finally { navInstance.Remove(); }
            const float spacing = 1.5f;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (ConnectorSide side in Enum.GetValues(typeof(ConnectorSide)))
            {
                Vector3 p = chunk.transform.InverseTransformPoint(chunk.GetConnector(side).transform.position);
                min = Vector2.Min(min, new Vector2(p.x, p.z)); max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            min -= Vector2.one * 3f; max += Vector2.one * 3f;
            int width = Mathf.CeilToInt((max.x - min.x) / spacing), height = Mathf.CeilToInt((max.y - min.y) / spacing);
            var cells = new byte[width * height];
            var heights = new float[cells.Length];
            Physics.SyncTransforms();
            var physics = scene.GetPhysicsScene();
            // Geometry is sampled only in the temporary scene's physics world.
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Vector3 origin = new Vector3(min.x + (x + 0.5f) * spacing, 1000f, min.y + (y + 0.5f) * spacing);
                if (physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                { cells[y * width + x] = 1; heights[y * width + x] = hit.point.y; }
            }
            // Rasterize the isolated, already baked ground NavMesh; never rebuild it from source meshes.
            for (int t = 0; t < nav.indices.Length; t += 3)
            {
                Vector3 a = nav.vertices[nav.indices[t]], b = nav.vertices[nav.indices[t + 1]], c = nav.vertices[nav.indices[t + 2]];
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < 0.00001f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - min.x) / spacing), 0, width - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x, c.x) - min.x) / spacing), 0, width - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z, b.z, c.z) - min.y) / spacing), 0, height - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z, b.z, c.z) - min.y) / spacing), 0, height - 1);
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float px = min.x + (x + 0.5f) * spacing, pz = min.y + (y + 0.5f) * spacing;
                    float u = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / denominator;
                    float v = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / denominator;
                    if (u < 0 || v < 0 || u + v > 1) continue;
                    int cell = y * width + x;
                    float elevation = u * a.y + v * b.y + (1 - u - v) * c.y;
                    if (cells[cell] != 2 || elevation > heights[cell]) heights[cell] = elevation;
                    cells[cell] = 2;
                }
            }
            if (!cells.Contains((byte)2)) throw new InvalidOperationException("Bake contains no walkable cells.");
            var data = AssetDatabase.LoadAssetAtPath<MapCartographyData>(dataPath);
            if (data == null) { data = ScriptableObject.CreateInstance<MapCartographyData>(); AssetDatabase.CreateAsset(data, dataPath); }
            // Content identity includes geometry, not a process-dependent string hash.
            uint hash = 2166136261;
            for (int i = 0; i < cells.Length; i++)
            { hash = unchecked((hash ^ cells[i]) * 16777619); hash = unchecked((hash ^ (uint)Mathf.RoundToInt(heights[i] * 100)) * 16777619); }
            data.SetBakedData(min, spacing, width, height, cells, heights, unchecked((int)hash));
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Cartography] {prefab.name}: Baked {width}x{height}, walkable={cells.Count(v => v == 2)}, terrain={cells.Count(v => v == 1)}, version={data.ContentVersion}");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        var edit = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var serialized = new SerializedObject(edit.GetComponent<MapChunk>());
            serialized.FindProperty("cartography").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapCartographyData>(dataPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(edit, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(edit); }
    }

    private static void MoveLegacyBossCartography()
    {
        if (AssetDatabase.LoadAssetAtPath<MapCartographyData>(BossDataPath) != null)
            return;

        if (AssetDatabase.LoadAssetAtPath<MapCartographyData>(LegacyBossDataPath) == null)
            return;

        string error = AssetDatabase.MoveAsset(LegacyBossDataPath, BossDataPath);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException(error);
    }

    private static void ConfigureAndBakeBossPrefab()
    {
        GameObject edit = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        try
        {
            MapChunk chunk = edit.GetComponent<MapChunk>();
            if (chunk == null) chunk = edit.AddComponent<MapChunk>();

            Renderer[] renderers = edit.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("Boss Chunk 沒有可烘焙的 Renderer。");

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            Renderer ground = renderers
                .OrderByDescending(value => value.bounds.size.x * value.bounds.size.z)
                .First();
            float groundY = ground.bounds.max.y;

            foreach (MeshFilter filter in edit.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                    continue;

                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }

            Transform connectorRoot = GetOrCreateChild(edit.transform, "Connectors");
            MapConnector[] connectors =
            {
                ConfigureConnector(connectorRoot, "N", ConnectorSide.North,
                    new Vector3(bounds.center.x, groundY, bounds.max.z),
                    new Vector3(bounds.center.x, groundY, bounds.max.z - 18f), 180f),
                ConfigureConnector(connectorRoot, "E", ConnectorSide.East,
                    new Vector3(bounds.max.x, groundY, bounds.center.z),
                    new Vector3(bounds.max.x - 18f, groundY, bounds.center.z), 270f),
                ConfigureConnector(connectorRoot, "S", ConnectorSide.South,
                    new Vector3(bounds.center.x, groundY, bounds.min.z),
                    new Vector3(bounds.center.x, groundY, bounds.min.z + 18f), 0f),
                ConfigureConnector(connectorRoot, "W", ConnectorSide.West,
                    new Vector3(bounds.min.x, groundY, bounds.center.z),
                    new Vector3(bounds.min.x + 18f, groundY, bounds.center.z), 90f)
            };

            var chunkSerialized = new SerializedObject(chunk);
            SerializedProperty connectorProperty = chunkSerialized.FindProperty("connectors");
            connectorProperty.arraySize = connectors.Length;
            for (int index = 0; index < connectors.Length; index++)
                connectorProperty.GetArrayElementAtIndex(index).objectReferenceValue = connectors[index];
            chunkSerialized.ApplyModifiedPropertiesWithoutUndo();

            Transform bossSpawn = GetOrCreateChild(edit.transform, "BossSpawnPoint");
            if (bossSpawn.GetComponent<BossSpawnPoint>() == null)
                bossSpawn.gameObject.AddComponent<BossSpawnPoint>();
            bossSpawn.localPosition = new Vector3(bounds.center.x, groundY + 0.1f, bounds.center.z);
            bossSpawn.localRotation = Quaternion.identity;

            NavMeshSurface surface = edit.GetComponent<NavMeshSurface>();
            if (surface == null) surface = edit.AddComponent<NavMeshSurface>();
            NavMeshSurface prototypeSurface = AssetDatabase
                .LoadAssetAtPath<GameObject>(PrefabPath)
                .GetComponentInChildren<NavMeshSurface>(true);
            if (prototypeSurface == null)
                throw new MissingReferenceException("Prototype Chunk 缺少 NavMeshSurface。");
            EditorUtility.CopySerialized(prototypeSurface, surface);
            surface.navMeshData = null;
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

            PrefabUtility.SaveAsPrefabAsset(edit, BossPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(edit); }

        edit = PrefabUtility.LoadPrefabContents(BossPrefabPath);
        try
        {
            NavMeshSurface surface = edit.GetComponent<NavMeshSurface>();
            surface.BuildNavMesh();
            NavMeshData generated = surface.navMeshData;
            surface.RemoveData();
            if (generated == null)
                throw new InvalidOperationException("Boss Chunk NavMesh 烘焙失敗。");

            NavMeshData stored = AssetDatabase.LoadAssetAtPath<NavMeshData>(BossNavMeshPath);
            if (stored == null)
            {
                AssetDatabase.CreateAsset(generated, BossNavMeshPath);
                stored = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, stored);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(stored);
            }
            surface.navMeshData = stored;

            NavMeshDataInstance nav = NavMesh.AddNavMeshData(
                stored, edit.transform.position, edit.transform.rotation);
            try
            {
                foreach (MapConnector connector in edit.GetComponentsInChildren<MapConnector>(true))
                    SnapToNavMesh(connector.PlayerSpawnPoint);
                SnapToNavMesh(edit.GetComponentInChildren<BossSpawnPoint>(true).transform);
            }
            finally { nav.Remove(); }

            PrefabUtility.SaveAsPrefabAsset(edit, BossPrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(edit); }
    }

    private static MapConnector ConfigureConnector(
        Transform parent,
        string name,
        ConnectorSide side,
        Vector3 localPosition,
        Vector3 spawnLocalPosition,
        float inwardYaw)
    {
        Transform connectorTransform = GetOrCreateChild(parent, name);
        connectorTransform.localPosition = localPosition;
        connectorTransform.localRotation = Quaternion.Euler(0f, inwardYaw + 180f, 0f);

        Transform spawn = GetOrCreateChild(connectorTransform, "PlayerSpawnPoint");
        spawn.position = parent.root.TransformPoint(spawnLocalPosition);
        spawn.rotation = parent.root.rotation * Quaternion.Euler(0f, inwardYaw, 0f);

        Transform blocker = GetOrCreateChild(connectorTransform, "BlockerSpawnPoint");
        blocker.localPosition = Vector3.zero;
        blocker.localRotation = Quaternion.identity;

        MapConnector connector = connectorTransform.GetComponent<MapConnector>();
        if (connector == null) connector = connectorTransform.gameObject.AddComponent<MapConnector>();
        var serialized = new SerializedObject(connector);
        serialized.FindProperty("side").enumValueIndex = (int)side;
        serialized.FindProperty("playerSpawnPoint").objectReferenceValue = spawn;
        serialized.FindProperty("blockerSpawnPoint").objectReferenceValue = blocker;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return connector;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null) return child;
        var gameObject = new GameObject(name);
        child = gameObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private static void SnapToNavMesh(Transform target)
    {
        if (target == null || !NavMesh.SamplePosition(
                target.position + Vector3.up * 2f,
                out NavMeshHit hit,
                30f,
                NavMesh.AllAreas))
        {
            throw new InvalidOperationException(
                $"Boss Chunk 找不到 {target?.name ?? "<missing>"} 附近的 NavMesh。");
        }

        target.position = hit.position + Vector3.up * 0.1f;
    }

    private static void WireBossGameScene()
    {
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Game.unity");
        bool owned = !scene.IsValid() || !scene.isLoaded;
        if (owned)
            scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Additive);

        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            MapRunSelectionPrototype selection = roots
                .SelectMany(value => value.GetComponentsInChildren<MapRunSelectionPrototype>(true))
                .Single();
            StageFlowController flow = roots
                .SelectMany(value => value.GetComponentsInChildren<StageFlowController>(true))
                .Single();
            NetworkMapState map = flow.GetComponent<NetworkMapState>();
            MapChunk bossChunk = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath)
                .GetComponent<MapChunk>();

            var selectionSerialized = new SerializedObject(selection);
            selectionSerialized.FindProperty("bossChunkPrefab").objectReferenceValue = bossChunk;
            selectionSerialized.ApplyModifiedPropertiesWithoutUndo();

            var mapSerialized = new SerializedObject(map);
            SerializedProperty catalog = mapSerialized.FindProperty("catalog");
            bool catalogContainsBoss = Enumerable.Range(0, catalog.arraySize)
                .Any(index => catalog.GetArrayElementAtIndex(index).objectReferenceValue == bossChunk);
            if (!catalogContainsBoss)
            {
                catalog.arraySize++;
                catalog.GetArrayElementAtIndex(catalog.arraySize - 1).objectReferenceValue = bossChunk;
            }
            mapSerialized.ApplyModifiedPropertiesWithoutUndo();

            BossStageObjectiveController objective =
                flow.GetComponent<BossStageObjectiveController>();
            if (objective == null)
                objective = flow.gameObject.AddComponent<BossStageObjectiveController>();

            string bossGuid = AssetDatabase.AssetPathToGUID(BossEnemyPath);
            Fusion.NetworkPrefabRef bossPrefab = Fusion.NetworkPrefabRef.Parse(bossGuid);
            if (!bossPrefab.IsValid)
                throw new InvalidOperationException("Enemy_Boss 無法解析成 NetworkPrefabRef。");
            typeof(BossStageObjectiveController)
                .GetField("bossPrefab", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                .SetValue(objective, bossPrefab);
            EditorUtility.SetDirty(objective);

            var flowSerialized = new SerializedObject(flow);
            flowSerialized.FindProperty("bossObjectiveController").objectReferenceValue = objective;
            flowSerialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (MapChunk instance in roots
                .SelectMany(value => value.GetComponentsInChildren<MapChunk>(true))
                .Where(value => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(value.gameObject) == BossPrefabPath))
            {
                MapChunk[] duplicateComponents = instance.GetComponents<MapChunk>();
                if (duplicateComponents.Length < 2) continue;
                foreach (MapChunk component in duplicateComponents)
                {
                    if (PrefabUtility.GetCorrespondingObjectFromSource(component) == null)
                        UnityEngine.Object.DestroyImmediate(component);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (owned) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Purgers/Map/Wire Phase 4 前置-4 Game HUD")]
    public static void WireGame()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode only.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Game.unity");
        bool owned = !scene.IsValid() || !scene.isLoaded;
        if (owned) scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var stage = roots.SelectMany(r => r.GetComponentsInChildren<StageFlowController>(true)).Single();
            var selection = roots.SelectMany(r => r.GetComponentsInChildren<MapRunSelectionPrototype>(true)).Single();
            var map = stage.GetComponent<NetworkMapState>();
            if (map == null) map = stage.gameObject.AddComponent<NetworkMapState>();
            var state = new SerializedObject(map);
            state.FindProperty("selection").objectReferenceValue = selection;
            var catalog = state.FindProperty("catalog"); catalog.arraySize = 1;
            catalog.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<MapChunk>();
            state.ApplyModifiedPropertiesWithoutUndo();
            var hud = roots.Single(r => r.name == "StageHUD");
            var content = hud.transform.Find("ReferenceFrame/TopLeftColumn/MinimapSlot/MinimapContent")
                ?? hud.transform.Find("TopLeftColumn/MinimapSlot/MinimapContent");
            var view = hud.GetComponent<LocalMinimapController>();
            if (view == null) view = hud.AddComponent<LocalMinimapController>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("map").objectReferenceValue = map;
            serialized.FindProperty("compactContent").objectReferenceValue = content;
            serialized.FindProperty("placeholder").objectReferenceValue = content.parent.Find("Placeholder").gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (owned) EditorSceneManager.CloseScene(scene, true); }
    }
}
