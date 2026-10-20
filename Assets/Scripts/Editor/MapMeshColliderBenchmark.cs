using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Controlled A/B physics workload, isolated from production physics and networking.</summary>
public static class MapMeshColliderBenchmark
{
    public const string Evidence = "deliverables/MeshColliderAudit_20261020";
    public static string Status { get; private set; } = "尚未執行";
    public static Report Last { get; private set; }
    public static bool Running { get; private set; }

    [Serializable]
    public sealed class Timing
    {
        public double meanMs, p95Ms, maxMs;
        public static Timing From(IEnumerable<double> samples)
        {
            var sorted = samples.OrderBy(x => x).ToArray();
            if (sorted.Length == 0) throw new ArgumentException("沒有樣本");
            return new Timing { meanMs = sorted.Average(), p95Ms = sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * .95) - 1)], maxMs = sorted.Last() };
        }
    }

    [Serializable]
    public sealed class Report
    {
        public string utc, scope, root;
        public string strategy;
        public int sourceColliders, candidateMeshColliders, candidateBoxColliders, unchangedColliders, probeCount, probeMismatches;
        public int unchangedPrimitiveColliders;
        public int rayHitMismatches, rayDistanceMismatches, sphereMismatches, capsuleMismatches;
        public int dynamicBodiesPerScene, sampledSteps;
        public long sourceTriangles, candidateTriangles;
        public float maxRayDistanceError;
        public double baselineSetupMs, candidateSetupMs;
        public Timing baselineQueries, candidateQueries, baselineSimulation, candidateSimulation;
        public string[] excluded, limitations;
        public string[] differenceExamples;
        public string[] additionalExcludedMeshKeys;
        public float maxSphereDistanceError, maxCapsuleDistanceError;
    }

    private struct Probe { public Vector3 origin, direction; public float distance; }

    public static async void Begin()
    {
        if (Running || MapPhysicsMonitor.Running) { Status = "其他量測進行中"; return; }
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("請先進入 Play Mode；不會自動搶焦點");
        Running = true; Last = null;
        Status = "建立保形候選";
        Scene baselineScene = default, candidateScene = default;
        MapMeshColliderOptimizer.Plan plan = null;
        try
        {
            plan = MapMeshColliderOptimizer.Analyze(MapMeshColliderOptimizer.FindTarget());
            var report = new Report {
                utc = DateTime.UtcNow.ToString("O"), scope = "Editor Play Mode; isolated, deterministic synthetic physics A/B",
                root = plan.Root.name, strategy = "exact closed cuboids only; no merging or convex hulls",
                sourceColliders = plan.Baseline.Length, candidateMeshColliders = plan.CandidateMeshCount,
                candidateBoxColliders = plan.Candidates.Count, unchangedColliders = plan.CandidateMeshCount,
                unchangedPrimitiveColliders = plan.Root.GetComponentsInChildren<Collider>().Count(c => c.enabled && !(c is MeshCollider)),
                excluded = plan.Excluded.Select(kv => kv.Key + ": " + kv.Value).ToArray(),
                limitations = new[] {
                    "不是正式 Host/Client、KCC、AI 或自然戰鬥的幀時間；不能據此斷言整體 FPS 或 MeshCollider 獨占成本。",
                    "只替換有八角點、六封閉面、十二外向三角形的精確長方體，不合併、不使用 Convex 包住建築。",
                    "射線/掃掠抽樣不是所有路徑的數學證明；仍須實際鉤索、行走及射擊驗收。",
                    "BoxCollider 與 MeshCollider 的內部起點／背面查詢語義可能不同，Collider 身分與 triangleIndex 也會改變。",
                    "建立耗時包含物件配置和可能的共用 cooking 快取，不等同冷啟動載入時間。",
                    "樣本為同一 Editor 的合成工作負載，需目標平台 Development Player 驗證。"
                }
            };
            report.sourceTriangles = plan.Baseline.Sum(Triangles);
            var replaced = new HashSet<MeshCollider>(plan.Candidates.Select(c => c.Source));
            report.candidateTriangles = plan.Baseline.Where(c => !replaced.Contains(c)).Sum(Triangles);
            var sw = Stopwatch.StartNew();
            var meshKeys = new Dictionary<Collider, string>();
            baselineScene = SceneManager.CreateScene("ColliderBaseline_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            await Populate(baselineScene, plan, false, meshKeys);
            report.baselineSetupMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            candidateScene = SceneManager.CreateScene("ColliderCandidate_" + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            await Populate(candidateScene, plan, true, meshKeys);
            report.candidateSetupMs = sw.Elapsed.TotalMilliseconds;
            Physics.SyncTransforms();
            var a = baselineScene.GetPhysicsScene();
            var b = candidateScene.GetPhysicsScene();
            var probes = MakeProbes(plan.Baseline, 12000);
            var examples = new List<string>();
            var suspectMeshes = new HashSet<string>();
            Status = "比對原版／候選射線與膠囊通道";
            for (int i = 0; i < probes.Length; i++)
            {
                Compare(a, b, probes[i], report, examples, suspectMeshes, meshKeys);
                if (i % 512 == 0) await Task.Delay(1);
            }
            report.probeCount = probes.Length;
            report.differenceExamples = examples.ToArray();
            report.additionalExcludedMeshKeys = suspectMeshes.OrderBy(x => x).ToArray();
            report.probeMismatches = report.rayHitMismatches + report.rayDistanceMismatches +
                                     report.sphereMismatches + report.capsuleMismatches;
            // Warm both implementations before timing, alternate order to reduce order bias.
            for (int i = 0; i < 12; i++) { QueryBatch(a, probes, i); QueryBatch(b, probes, i); }
            var qa = new List<double>(); var qb = new List<double>();
            var sa = new List<double>(); var sb = new List<double>();
            Status = "量測每批 256 Raycast + 64 SphereCast + 64 CapsuleCast";
            for (int i = 0; i < 180; i++)
            {
                if ((i & 1) == 0) { qa.Add(MeasureQueries(a, probes, i)); qb.Add(MeasureQueries(b, probes, i)); }
                else { qb.Add(MeasureQueries(b, probes, i)); qa.Add(MeasureQueries(a, probes, i)); }
                if (i % 6 == 0) await Task.Delay(1);
            }
            var bodyPositions = GroundPositions(a, plan.Baseline);
            report.dynamicBodiesPerScene = bodyPositions.Count;
            AddBodies(baselineScene, bodyPositions);
            AddBodies(candidateScene, bodyPositions);
            Physics.SyncTransforms();
            Status = "量測接觸中的動態球體與靜態場景";
            for (int i = 0; i < 240; i++)
            {
                double ta, tb;
                if ((i & 1) == 0) { ta = Simulate(a); tb = Simulate(b); }
                else { tb = Simulate(b); ta = Simulate(a); }
                if (i >= 60) { sa.Add(ta); sb.Add(tb); }
                if (i % 6 == 0) await Task.Delay(1);
            }
            report.sampledSteps = sa.Count;
            report.baselineQueries = Timing.From(qa); report.candidateQueries = Timing.From(qb);
            report.baselineSimulation = Timing.From(sa); report.candidateSimulation = Timing.From(sb);
            Last = report;
            Directory.CreateDirectory(Evidence);
            var excludedMeshes = MapMeshColliderOptimizer.LoadMeshExclusions();
            excludedMeshes.UnionWith(suspectMeshes);
            File.WriteAllText(MapMeshColliderOptimizer.ExclusionsPath,
                JsonUtility.ToJson(new MapMeshColliderOptimizer.MeshExclusions { meshKeys = excludedMeshes.OrderBy(x => x).ToArray() }, true));
            if (report.probeMismatches == 0)
                File.WriteAllText(MapMeshColliderOptimizer.VerifiedPath, JsonUtility.ToJson(
                    new MapMeshColliderOptimizer.VerifiedPlan { fingerprint = MapMeshColliderOptimizer.Fingerprint(plan),
                        utc = report.utc, probes = report.probeCount, boxes = plan.Candidates.Count }, true));
            File.WriteAllText(Evidence + "/benchmark-exact-box-" + DateTime.UtcNow.ToString("HHmmss") + ".json",
                JsonUtility.ToJson(report, true));
            Status = "完成；差異事件 " + report.probeMismatches + "，尚未套用正式場景";
        }
        catch (Exception e)
        {
            Status = "失敗：" + e.Message;
            UnityEngine.Debug.LogException(e);
        }
        finally
        {
            if (baselineScene.IsValid() && baselineScene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(baselineScene);
                while (op != null && !op.isDone) await Task.Delay(10);
            }
            if (candidateScene.IsValid() && candidateScene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(candidateScene);
                while (op != null && !op.isDone) await Task.Delay(10);
            }
            plan?.Dispose();
            Running = false;
        }
    }

    public static long Triangles(MeshCollider c) => c.sharedMesh == null ? 0 :
        Enumerable.Range(0, c.sharedMesh.subMeshCount).Sum(s => (long)c.sharedMesh.GetIndexCount(s) / 3);

    private static Transform CopyTransform(Transform original, Scene scene, Dictionary<Transform, Transform> map)
    {
        if (map.TryGetValue(original, out var copy)) return copy;
        var parent = original.parent ? CopyTransform(original.parent, scene, map) : null;
        var go = new GameObject(original.name);
        SceneManager.MoveGameObjectToScene(go, scene);
        copy = go.transform;
        copy.SetParent(parent, false);
        copy.localPosition = original.localPosition;
        copy.localRotation = original.localRotation;
        copy.localScale = original.localScale;
        map.Add(original, copy);
        return copy;
    }

    private static MeshCollider CopyCollider(GameObject target, MeshCollider source, Mesh mesh)
    {
        target.layer = source.gameObject.layer;
        var c = target.AddComponent<MeshCollider>();
        c.convex = source.convex;
        MapMeshColliderOptimizer.CopySettings(source, c);
        c.cookingOptions = source.cookingOptions;
        c.sharedMesh = mesh;
        return c;
    }

    private static async Task Populate(Scene scene, MapMeshColliderOptimizer.Plan plan, bool candidate, Dictionary<Collider, string> meshKeys)
    {
        var map = new Dictionary<Transform, Transform>();
        var replaced = candidate ? new HashSet<MeshCollider>(plan.Candidates.Select(c => c.Source)) : new HashSet<MeshCollider>();
        int n = 0;
        foreach (var c in plan.Baseline)
        {
            if (replaced.Contains(c)) continue;
            var clone = CopyCollider(CopyTransform(c.transform, scene, map).gameObject, c, c.sharedMesh);
            meshKeys[clone] = MapMeshColliderOptimizer.MeshKey(c.sharedMesh);
            if (++n % 256 == 0) await Task.Delay(1);
        }
        foreach (var original in plan.Root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !(c is MeshCollider)))
        {
            if (original.attachedRigidbody) throw new InvalidOperationException("隔離量測只接受靜態地圖；請排除動態物件");
            var go = CopyTransform(original.transform, scene, map).gameObject;
            go.layer = original.gameObject.layer;
            Collider copy;
            if (original is BoxCollider box)
            {
                var c = go.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size; copy = c;
            }
            else if (original is SphereCollider sphere)
            {
                var c = go.AddComponent<SphereCollider>(); c.center = sphere.center; c.radius = sphere.radius; copy = c;
            }
            else if (original is CapsuleCollider capsule)
            {
                var c = go.AddComponent<CapsuleCollider>(); c.center = capsule.center; c.radius = capsule.radius;
                c.height = capsule.height; c.direction = capsule.direction; copy = c;
            }
            else throw new InvalidOperationException("不支援的地圖碰撞器：" + original.GetType().Name);
            MapMeshColliderOptimizer.CopySettings(original, copy);
        }
        if (!candidate) return;
        foreach (var c in plan.Candidates)
        {
            var go = CopyTransform(c.Source.transform, scene, map).gameObject;
            go.layer = c.Source.gameObject.layer;
            var box = go.AddComponent<BoxCollider>();
            box.center = c.Bounds.center; box.size = c.Bounds.size;
            MapMeshColliderOptimizer.CopySettings(c.Source, box);
            meshKeys[box] = MapMeshColliderOptimizer.MeshKey(c.Source.sharedMesh);
            if (++n % 64 == 0) await Task.Delay(1);
        }
    }

    private static Probe[] MakeProbes(MeshCollider[] cs, int count)
    {
        var rng = new System.Random(20261020);
        var result = new Probe[count];
        for (int i = 0; i < count; i++)
        {
            var bounds = cs[rng.Next(cs.Length)].bounds;
            var direction = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1).normalized;
            float reach = bounds.extents.magnitude + 2;
            result[i] = new Probe { origin = bounds.center + direction * reach, direction = -direction, distance = reach * 2 };
        }
        return result;
    }

    private static void Flag(RaycastHit a, RaycastHit b, HashSet<string> suspects, Dictionary<Collider, string> keys)
    {
        if (a.collider && keys.TryGetValue(a.collider, out string ka) && ka.Length > 0) suspects.Add(ka);
        if (b.collider && keys.TryGetValue(b.collider, out string kb) && kb.Length > 0) suspects.Add(kb);
    }

    private static void Compare(PhysicsScene a, PhysicsScene b, Probe p, Report report, List<string> examples,
        HashSet<string> suspects, Dictionary<Collider, string> keys)
    {
        bool ha = a.Raycast(p.origin, p.direction, out var hitA, p.distance, ~0, QueryTriggerInteraction.Ignore);
        bool hb = b.Raycast(p.origin, p.direction, out var hitB, p.distance, ~0, QueryTriggerInteraction.Ignore);
        if (ha != hb) { report.rayHitMismatches++; Flag(hitA, hitB, suspects, keys); }
        else if (ha)
        {
            float error = Mathf.Abs(hitA.distance - hitB.distance);
            report.maxRayDistanceError = Mathf.Max(report.maxRayDistanceError, error);
            if (error > .002f) { report.rayDistanceMismatches++; Flag(hitA, hitB, suspects, keys); }
        }
        ha = a.SphereCast(p.origin, .25f, p.direction, out hitA, p.distance, ~0, QueryTriggerInteraction.Ignore);
        hb = b.SphereCast(p.origin, .25f, p.direction, out hitB, p.distance, ~0, QueryTriggerInteraction.Ignore);
        if (ha && hb) report.maxSphereDistanceError = Mathf.Max(report.maxSphereDistanceError, Mathf.Abs(hitA.distance - hitB.distance));
        if (ha != hb || (ha && Mathf.Abs(hitA.distance - hitB.distance) > .002f))
        {
            report.sphereMismatches++;
            Flag(hitA, hitB, suspects, keys);
            if (examples.Count < 20) examples.Add("Sphere origin=" + p.origin.ToString("R") + " dir=" + p.direction.ToString("R") +
                " A=" + ha + "/" + hitA.distance + "/" + (hitA.collider ? hitA.collider.name : "") +
                " B=" + hb + "/" + hitB.distance + "/" + (hitB.collider ? hitB.collider.transform.parent.name : ""));
        }
        ha = a.CapsuleCast(p.origin, p.origin + Vector3.up, .3f, p.direction, out hitA, p.distance, ~0, QueryTriggerInteraction.Ignore);
        hb = b.CapsuleCast(p.origin, p.origin + Vector3.up, .3f, p.direction, out hitB, p.distance, ~0, QueryTriggerInteraction.Ignore);
        if (ha && hb) report.maxCapsuleDistanceError = Mathf.Max(report.maxCapsuleDistanceError, Mathf.Abs(hitA.distance - hitB.distance));
        if (ha != hb || (ha && Mathf.Abs(hitA.distance - hitB.distance) > .002f))
        {
            report.capsuleMismatches++;
            Flag(hitA, hitB, suspects, keys);
            if (examples.Count < 20) examples.Add("Capsule origin=" + p.origin.ToString("R") + " dir=" + p.direction.ToString("R") +
                " A=" + ha + "/" + hitA.distance + "/" + (hitA.collider ? hitA.collider.name : "") +
                " B=" + hb + "/" + hitB.distance + "/" + (hitB.collider ? hitB.collider.transform.parent.name : ""));
        }
    }

    private static void QueryBatch(PhysicsScene scene, Probe[] probes, int batch)
    {
        for (int i = 0; i < 256; i++)
        {
            var p = probes[(batch * 257 + i) % probes.Length];
            scene.Raycast(p.origin, p.direction, out _, p.distance, ~0, QueryTriggerInteraction.Ignore);
            if (i % 4 != 0) continue;
            scene.SphereCast(p.origin, .25f, p.direction, out _, p.distance, ~0, QueryTriggerInteraction.Ignore);
            scene.CapsuleCast(p.origin, p.origin + Vector3.up, .3f, p.direction, out _, p.distance, ~0, QueryTriggerInteraction.Ignore);
        }
    }

    private static double MeasureQueries(PhysicsScene scene, Probe[] probes, int batch)
    {
        var sw = Stopwatch.StartNew(); QueryBatch(scene, probes, batch); return sw.Elapsed.TotalMilliseconds;
    }

    private static List<Vector3> GroundPositions(PhysicsScene scene, MeshCollider[] colliders)
    {
        var positions = new List<Vector3>();
        foreach (var c in colliders)
        {
            var start = c.bounds.center + Vector3.up * (c.bounds.extents.y + 5);
            if (!scene.Raycast(start, Vector3.down, out var hit, 200, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < .7f) continue;
            var p = hit.point + Vector3.up * .55f;
            if (positions.Any(other => Vector3.Distance(p, other) < 2)) continue;
            positions.Add(p);
            if (positions.Count == 32) break;
        }
        return positions;
    }

    private static void AddBodies(Scene scene, List<Vector3> positions)
    {
        foreach (var position in positions)
        {
            var go = new GameObject("BenchmarkBody");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = position;
            go.AddComponent<SphereCollider>().radius = .5f;
            var body = go.AddComponent<Rigidbody>();
            body.sleepThreshold = 0;
        }
    }

    private static double Simulate(PhysicsScene scene)
    {
        var sw = Stopwatch.StartNew(); scene.Simulate(1f / 60); return sw.Elapsed.TotalMilliseconds;
    }
}
