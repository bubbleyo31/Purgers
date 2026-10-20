#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Purgers.Enemy.Encounter;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

/// <summary>唯讀作者配置檢查；不擲骰、不生成、不建立巡邏區、不保存資產。</summary>
public static class EnemySpawnZoneDiagnostics
{
    public sealed class Notice
    {
        public readonly MessageType Type;
        public readonly string Text;
        public Notice(MessageType type, string text) { Type = type; Text = text; }
    }

    public static string DescribeConfiguration(EnemySpawnZone zone) =>
        string.Join("\n", Inspect(zone).Select(item => item.Text));

    public static List<Notice> Inspect(EnemySpawnZone zone)
    {
        var result = new List<Notice>();
        void Error(string text) => result.Add(new Notice(MessageType.Error, text));
        void Warn(string text) => result.Add(new Notice(MessageType.Warning, text));
        var chunk = zone.GetComponentInParent<MapChunk>();
        if (chunk == null) Error("不在 MapChunk 下：正式 Encounter 不會登記此區域。請放在目標 Chunk 的子階層。");
        if (!zone.isActiveAndEnabled) Warn("區域目前停用：正式 Encounter 不會登記或啟動它。");
        if (!ValidSize(zone.SpawnLocalBounds.size) || !Finite(zone.SpawnLocalBounds.center) ||
            !Finite(zone.transform.lossyScale) || Mathf.Abs(zone.transform.lossyScale.x * zone.transform.lossyScale.y * zone.transform.lossyScale.z) < 0.000001f)
            Error("出生盒尺寸／中心／Transform 縮放無效；三軸尺寸必須有限且大於 0，縮放不可為 0。");
        if (!zone.GetGates().Any(gate => gate.isActiveAndEnabled))
            Error("缺少啟用的 Gate：在此 Zone 子階層配置 EnemySpawnApproachGate，並確認物件與元件已啟用。");

        bool ground = false, flying = false;
        long demand = 0;
        int enabled = 0;
        if (zone.UsesEnemyEntries)
        {
            for (int i = 0; i < zone.EnemyEntries.Count; i++)
            {
                var entry = zone.EnemyEntries[i];
                if (entry == null) { Error($"清單第 {i + 1} 項為空。"); continue; }
                if (entry.ChancePercent <= 0) continue;
                enabled++;
                demand += entry.Count;
                CheckPrefab(entry.Prefab, $"清單第 {i + 1} 項", null);
            }
            if (enabled == 0) Warn("混合清單全部為 0% 或空項目：不會生成，也不會回退使用舊版清單。");
        }
        else
        {
            if (zone.EnemyPrefabs == null || zone.EnemyPrefabs.Count == 0) Error("敵人清單為空：請指定至少一個敵人 Prefab。");
            else
                for (int i = 0; i < zone.EnemyPrefabs.Count; i++)
                    CheckPrefab(zone.EnemyPrefabs[i], $"舊版清單第 {i + 1} 項", zone.LocomotionKind);
            demand = zone.EnemiesPerActivation;
        }

        if (ground && chunk != null)
        {
            var surfaces = chunk.GetComponentsInChildren<NavMeshSurface>(true);
            if (surfaces.Length != 1 || surfaces[0].navMeshData == null)
                Error("地面敵人：Chunk 必須有唯一且已烘焙 NavMeshData 的 NavMeshSurface；請在 Editor 烘焙。");
            else result.Add(new Notice(MessageType.Info, "地面：已找到預烘焙資料。盒內 NavMesh、實體地板與完整巡邏路徑須等實際地圖載入後判定。"));
        }
        if (flying)
        {
            var area = zone.FlyingPatrolArea;
            if (area != null && (chunk == null || area.GetComponentInParent<MapChunk>() != chunk || area.PointCount == 0))
                Error("飛行人工 Patrol Area 必須屬於同一 Chunk，且至少有一個巡邏點。");
            else if (area == null)
                result.Add(new Notice(MessageType.Info, "飛行：使用既有自動巡邏規劃。可按「檢查飛行空間」以各 Prefab 真實膠囊預檢目前載入的幾何。"));
        }

        var directors = FindDirectors(zone);
        if (directors.Length == 0)
            Warn("此場景沒有可判讀的 EnemyEncounterDirector（Prefab 模式通常如此）。遮罩、預算、玩家與執行中導航待 Play 驗證。");
        else if (directors.Length > 1)
            Warn("此場景有多個 Director，無法唯一判定設定來源；請在 Play 選取實際 Host 生成的 Zone。");
        else
        {
            var settings = new SerializedObject(directors[0]);
            foreach (string field in new[] { "worldOcclusionMask", "spawnOccupancyMask" })
                if (settings.FindProperty(field).intValue == 0) Error($"Director 的 {field} 為空：所有怪物都會被拒絕。");
            // StageFlow 與 MapState 在 Spawned 時可從同物件補齊。
            if (settings.FindProperty("mapSelection").objectReferenceValue == null)
                Error("Director 未指定 Map Selection：正式生成會停在配置檢查。");
            int cap = Math.Min(settings.FindProperty("maximumAlivePerZone").intValue,
                Math.Min(settings.FindProperty("maximumAliveEnemies").intValue, settings.FindProperty("maximumOwnedNetworkObjects").intValue));
            if (demand > cap) Warn($"全部抽中時需求 {demand} 隻，超過目前最小數量上限 {cap}；後續項目可能被取消。");
            float interval = settings.FindProperty("minimumSecondsBetweenSpawns").floatValue;
            float lifetime = settings.FindProperty("activationLifetimeSeconds").floatValue;
            if ((demand - 1) * interval > lifetime)
                Warn($"全部抽中後，理想情況也至少需 {(demand - 1) * interval:0.##} 秒；已超過啟動期限 {lifetime:0.##} 秒。");
        }
        return result;

        void CheckPrefab(NetworkPrefabRef reference, string label, EnemyLocomotionKind? expected)
        {
            var asset = ResolvePrefab(reference);
            if (asset == null) { Error($"{label}：Prefab 未指定或 GUID 無法解析。"); return; }
            var actor = asset.GetComponent<EnemyActor>();
            if (asset.GetComponent<NetworkObject>() == null || actor == null || actor.Definition == null)
            { Error($"{label} {asset.name}：缺少 NetworkObject／EnemyActor／EnemyDefinition。"); return; }
            try
            {
                if (!NetworkProjectConfig.Global.PrefabTable.GetId((NetworkObjectGuid)reference).IsValid)
                    Error($"{label} {asset.name}：尚未登記於 Fusion Prefab Table。");
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            { Error($"{label} {asset.name}：Fusion Prefab Table 無法解析：{exception.Message}"); }
            var kind = actor.Definition.LocomotionKind;
            if (expected.HasValue && expected != kind) Error($"{label} {asset.name}：移動類型 {kind} 與舊版 Zone 的 {expected} 不符。");
            if (kind == EnemyLocomotionKind.Stationary)
            {
                if (asset.GetComponentInChildren<EnemyIdlePatrolBrain>(true) != null ||
                    asset.GetComponentInChildren<EnemyPatrolNavigator>(true) != null ||
                    asset.GetComponentInChildren<EnemyChaseBrain>(true) != null ||
                    asset.GetComponentInChildren<EnemyChaseMotor>(true) != null)
                    Error($"{label} {asset.name}：Stationary 不可保留巡邏／追逐 Brain 或 Navigator，出生前契約會拒絕。");
                return;
            }
            if (kind != EnemyLocomotionKind.Ground && kind != EnemyLocomotionKind.FreeFlying)
            { Error($"{label} {asset.name}：不支援的移動類型 {kind}。"); return; }
            ground |= kind == EnemyLocomotionKind.Ground;
            flying |= kind == EnemyLocomotionKind.FreeFlying;
            if (asset.GetComponentInChildren<EnemyIdlePatrolBrain>(true) == null)
                Error($"{label} {asset.name}：缺少 EnemyIdlePatrolBrain。");
            if (kind == EnemyLocomotionKind.FreeFlying &&
                (asset.GetComponent<EnemyFlyingPatrolNavigator>() == null || asset.GetComponent<EnemyIdlePatrolBrain>() == null))
                Error($"{label} {asset.name}：飛行候選 Root 缺少 Flying Navigator／Patrol Brain。");
        }
    }

    public static EnemyEncounterDirector[] FindDirectors(EnemySpawnZone zone) =>
        Resources.FindObjectsOfTypeAll<EnemyEncounterDirector>().Where(director =>
            !EditorUtility.IsPersistent(director) && director.gameObject.scene == zone.gameObject.scene).ToArray();

    public static GameObject ResolvePrefab(NetworkPrefabRef reference)
    {
        if (!reference.IsValid) return null;
        string path = AssetDatabase.GUIDToAssetPath(((NetworkObjectGuid)reference).ToString().Replace("-", ""));
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    public static string CheckFlyingSpace(EnemySpawnZone zone)
    {
        if (EditorUtility.IsPersistent(zone) || !zone.gameObject.scene.IsValid())
            return "待驗證：請開啟 Chunk Prefab 模式或載入場景後再預檢空間。";
        var physics = zone.gameObject.scene.GetPhysicsScene();
        var refs = zone.UsesEnemyEntries
            ? zone.EnemyEntries.Where(e => e != null && e.IsEnabled).Select(e => e.Prefab)
            : zone.EnemyPrefabs;
        var lines = new List<string>();
        foreach (var reference in refs.Distinct())
        {
            var asset = ResolvePrefab(reference);
            var actor = asset != null ? asset.GetComponent<EnemyActor>() : null;
            if (actor == null || actor.Definition == null || actor.Definition.LocomotionKind != EnemyLocomotionKind.FreeFlying) continue;
            var navigator = asset.GetComponent<EnemyFlyingPatrolNavigator>();
            var brain = asset.GetComponent<EnemyIdlePatrolBrain>();
            if (navigator == null || brain == null) { lines.Add($"{asset.name}：缺少飛行 Navigator／Patrol Brain。"); continue; }
            if (zone.FlyingPatrolArea != null) { lines.Add($"{asset.name}：使用人工 Area；完整可達性待 Host 的正式出生判定。"); continue; }
            var bounds = zone.SpawnLocalBounds;
            bounds.center += zone.transform.InverseTransformVector(navigator.SpawnRootOffset);
            var points = EnemyFlyingPatrolPlanner.Plan(zone.transform.localToWorldMatrix, zone.FlyingPatrolLocalBounds, bounds,
                zone.FlyingPatrolSamplesPerAxis, zone.FlyingPatrolMaximumPoints,
                Mathf.Max(zone.FlyingPatrolMinimumSpacing, brain.PatrolArrivalDistance * 2f + 0.01f),
                (from, to) => navigator.IsPassageClear(physics, from, to));
            lines.Add(points.Count >= 2 ? $"{asset.name}：空間取樣通過，找到 {points.Count} 個相通巡邏點。"
                : $"{asset.name}：空間取樣失敗，只找到 {points.Count} 點（至少需 2）。檢查盒體重疊、取樣密度、膠囊尺寸與 Obstacle Mask。");
        }
        return (lines.Count == 0 ? "目前沒有可檢查的飛行候選。" : string.Join("\n", lines)) +
            "\n僅代表本次、目前載入的幾何；不保證玩家遮擋、距離、預算或動態占用通過。";
    }

    private static bool Finite(Vector3 v) =>
        !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    private static bool ValidSize(Vector3 size) => Finite(size) && size.x > 0 && size.y > 0 && size.z > 0;
}
#endif
