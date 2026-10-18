using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Enemy.Encounter;
using Purgers.Map;
using UnityEditor;
using UnityEngine;

/// <summary>隔離 Runner 驗證混合生成；直接驅動 Director 內部啟動，不冒充自然 Gate 穿越。</summary>
public static class EnemySpawnCompositionVerification
{
    public static string Status { get; private set; } = "未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client;
    private static bool multiplayer;
    private static string session;
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private static readonly List<string> report = new List<string>();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    private static object Get(object o, string field) => o.GetType().GetField(field, Private).GetValue(o);
    private static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private).Invoke(o, args);
    private static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); report.Add("PASS " + label); }
    private static async Task Until(Func<bool> predicate, string label, float seconds = 25)
    {
        Stage = label; float end = Time.realtimeSinceStartup + seconds;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    public static async void Begin(bool network = false, int stationaryCount = 1)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無現存 Runner 的 Play Mode。");
        multiplayer = network; session = "composition-" + Guid.NewGuid().ToString("N"); report.Clear(); Status = "執行中";
        var randomState = UnityEngine.Random.state;
        try
        {
            host = await Connect(true);
            var chunkRoot = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/MapChunk_Prototype.prefab"));
            temporary.Add(chunkRoot); host.MoveToRunnerScene(chunkRoot); chunkRoot.transform.position = new Vector3(2000, 0, 2000);
            var chunk = chunkRoot.GetComponent<MapChunk>();
            foreach (var old in chunkRoot.GetComponentsInChildren<EnemySpawnZone>(true)) old.enabled = false;
            var navigation = chunkRoot.AddComponent<MapRuntimeNavigationPrototype>();
            Check(navigation.TryLoadAndPlanSingleChunk(chunk, 8128), "load pre-baked Chunk navigation and automatic ground patrol");
            Vector3 point;
            Check(navigation.GeneratedGroundAreas[0].TryGetPoint(0, chunkRoot.transform.position, out point), "ground patrol point available");
            var zoneObject = new GameObject("Mixed verification zone"); zoneObject.transform.SetParent(chunkRoot.transform); zoneObject.transform.position = point;
            var zone = zoneObject.AddComponent<EnemySpawnZone>();
            Set(zone, "spawnBoxCenter", new Vector3(0, 10, 0)); Set(zone, "spawnBoxSize", new Vector3(24, 22, 24));
            Set(zone, "flyingPatrolBoxCenter", new Vector3(0, 12, 0)); Set(zone, "flyingPatrolBoxSize", new Vector3(36, 22, 36));
            Set(zone, "requireSpawnOcclusion", false);
            var gate = new GameObject("Gate").AddComponent<EnemySpawnApproachGate>(); gate.transform.SetParent(zone.transform, false);
            string[] names = { "Enemy_Melee_A", "Enemy_Ranged_A", "Enemy_Ranged_C_Projectile" };
            var entries = names.Select((name, i) => new EnemySpawnEntry((NetworkPrefabRef)NetworkObjectGuid.Parse(
                AssetDatabase.AssetPathToGUID("Assets/Prefabs/Enemy/Variants/" + name + ".prefab")), 100, i == 2 ? stationaryCount : 2)).ToArray();
            Set(zone, "enemyEntries", entries);

            var owner = new GameObject("Composition Director", typeof(NetworkObject), typeof(EnemyEncounterDirector), typeof(NetworkMapState));
            temporary.Add(owner); host.MoveToRunnerScene(owner);
            var director = owner.GetComponent<EnemyEncounterDirector>(); director.enabled = false;
            var map = owner.GetComponent<NetworkMapState>(); map.enabled = false;
            ((IList)Get(map, "chunks")).Add(chunk);
            Set(director, "mapState", map); Set(director, "navigation", navigation);
            Set(director, "maximumAlivePerZone", 20); Set(director, "maximumAliveEnemies", 30);
            Set(director, "minimumPlayerDistance", .1f); Set(director, "minimumForwardDistance", 0f);
            Set(director, "maximumSpawnDistanceFromTrigger", 250f); Set(director, "candidateAttempts", 256);
            Set(director, "groundSampleDistance", 2f); Set(director, "worldOcclusionMask", (LayerMask)1);
            Set(director, "spawnOccupancyMask", (LayerMask)LayerMask.GetMask("Player", "Hit Mask"));
            Set(director, "debugEncounter", true);
            new NetworkObjectBaker().Bake(owner);
            host.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { owner.GetComponent<NetworkObject>() }, default);
            Check(director.Object.HasStateAuthority, "Director State Authority belongs to host");
            Check((bool)Call(director, "HasActiveGroundCandidates"), "ground readiness uses actual mixed Prefab kind");
            Set(entries[0], "chancePercent", 0f);
            Check(!(bool)Call(director, "HasActiveGroundCandidates"), "zero-chance ground does not block flying and stationary on navigation");
            Set(entries[0], "chancePercent", 100f);

            var playerPosition = point - Vector3.forward * 70;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); host.MoveToRunnerScene(floor);
            floor.transform.position = playerPosition - Vector3.up * .5f; floor.transform.localScale = new Vector3(8, 1, 8);
            var player = host.Spawn(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>(), playerPosition, Quaternion.identity, host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer, player);
            await Until(() => player.GetComponent<PlayerHealth>().IsAlive, "player alive");
            Physics.SyncTransforms(); Call(director, "CollectPlayers");
            var type = typeof(EnemyEncounterDirector).GetNestedType("ZoneRuntime", BindingFlags.NonPublic);
            var runtime = Activator.CreateInstance(type, true); type.GetField("Zone").SetValue(runtime, zone); type.GetField("Gates").SetValue(runtime, new[] { gate });
            Check((bool)Call(director, "TryBuildCandidates", runtime, chunk, 0), "candidate build ready");
            var candidates = (IList)type.GetField("Candidates").GetValue(runtime);
            Check(candidates.Count == 3, "one zone resolves Ground, FreeFlying and Stationary");
            foreach (var candidate in candidates)
            {
                var kind = (EnemyLocomotionKind)candidate.GetType().GetField("Kind").GetValue(candidate);
                var area = (EnemyPatrolArea)candidate.GetType().GetField("PatrolArea").GetValue(candidate);
                Check(kind == EnemyLocomotionKind.Stationary ? area == null : area != null && area.PointCount > 0, kind + " automatic patrol contract");
            }
            Check(zone.FlyingPatrolArea == null, "automatic flight does not overwrite manual field");
            // 固定亂數首點放置實體阻擋，確認正式選點會跳過占用並改抽其他位置。
            var stationary = candidates.Cast<object>().First(c =>
                (EnemyLocomotionKind)c.GetType().GetField("Kind").GetValue(c) == EnemyLocomotionKind.Stationary);
            UnityEngine.Random.InitState(97531);
            Vector3 blockedPoint = zone.RandomPoint(EnemyLocomotionKind.Stationary);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Sphere); temporary.Add(blocker);
            host.MoveToRunnerScene(blocker); blocker.layer = LayerMask.NameToLayer("Hit Mask");
            blocker.transform.position = blockedPoint + Vector3.up * .9f;
            blocker.transform.localScale = Vector3.one * 4f;
            Physics.SyncTransforms();
            Check((bool)Call(director, "IsOccupied", blockedPoint), "first stationary sample is physically occupied");
            UnityEngine.Random.InitState(97531);
            var snapshots = (IList)Get(director, "players");
            object[] selection = { runtime, stationary, Vector3.forward, snapshots[0], Vector3.zero };
            Check((bool)typeof(EnemyEncounterDirector).GetMethod("TrySelectSpawnPosition", Private).Invoke(director, selection),
                "occupied stationary sample retries and finds another point");
            Vector3 selected = (Vector3)selection[4];
            Check(zone.Contains(selected) && Vector3.Distance(selected, blockedPoint) > 2.75f,
                "replacement stationary point is inside box and clear of blocker");
            blocker.GetComponent<Collider>().enabled = false;
            UnityEngine.Object.Destroy(blocker);
            UnityEngine.Random.InitState(44381); Call(director, "BuildActivationBatches", runtime);
            Set(director, "pendingZone", runtime); Set(director, "pendingGate", gate); Set(director, "pendingPlayer", host.LocalPlayer);
            Set(director, "pendingForward", Vector3.forward); Set(director, "pendingExpiresAt", Time.time + 15);
            for (int i = 0; i < 8 && Get(director, "pendingZone") != null; i++)
            {
                Call(director, "ContinuePendingActivation"); await Task.Delay(800);
            }
            var enemies = UnityEngine.Object.FindObjectsOfType<EnemyActor>().Where(e => e.Runner == host).ToArray();
            foreach (var kind in new[] { EnemyLocomotionKind.Ground, EnemyLocomotionKind.FreeFlying, EnemyLocomotionKind.Stationary })
            {
                int expected = kind == EnemyLocomotionKind.Stationary ? stationaryCount : 2;
                Check(enemies.Count(e => e.Definition.LocomotionKind == kind) == expected, kind + " spawned specified quantity " + expected);
            }
            var stationaryEnemies = enemies.Where(e => e.IsStationary).ToArray();
            Check(stationaryEnemies.All(e => zone.Contains(e.transform.position)),
                "all stationary spawn roots remain inside the authored box");
            Check(stationaryEnemies.SelectMany((a, i) => stationaryEnemies.Skip(i + 1)
                .Select(b => Vector3.Distance(a.transform.position, b.transform.position))).All(d => d > .1f),
                "stationary enemies occupy distinct scattered positions");
            foreach (var enemy in enemies.Where(e => !e.IsStationary))
                Check(enemy.GetComponent<EnemyIdlePatrolBrain>().PatrolArea != null, enemy.name + " patrol bound before Spawned");
            Check(Get(director, "pendingZone") == null, "completed activation has no remaining plan");
            Check(((RecentZoneLock<EnemySpawnZone>)Get(director, "recentZones")).IsLocked(zone), "successful mixed activation enters FIFO once");
            if (network)
            {
                var ids = enemies.Select(e => e.Object.Id).ToArray();
                client = await Connect(false);
                await Until(() => ids.All(id => client.FindObject(id) != null), "late join receives all mixed enemies");
                await Until(() => stationaryEnemies.All(e =>
                    Vector3.Distance(e.transform.position, client.FindObject(e.Object.Id).transform.position) < .05f),
                    "late join receives stationary positions");
                Check(true, "late join stationary positions match host within 0.05m");
                Check(ids.All(id => !client.FindObject(id).HasStateAuthority), "client replicas cannot own gameplay authority");
                Check(!UnityEngine.Object.FindObjectsOfType<EnemyPatrolArea>().Any(a => a.gameObject.scene == client.FindObject(ids[0]).gameObject.scene), "client generates no patrol areas");
                await client.Shutdown(); client = null; client = await Connect(false);
                await Until(() => ids.All(id => client.FindObject(id) != null), "reconnect receives all mixed enemies");
                await Until(() => stationaryEnemies.All(e =>
                    Vector3.Distance(e.transform.position, client.FindObject(e.Object.Id).transform.position) < .05f),
                    "reconnect receives stationary positions");
                Check(true, "reconnect stationary positions match host within 0.05m");
                Check(true, "late join and reconnect preserve spawned composition");
            }
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            if (client != null) await client.Shutdown(); if (host != null) await host.Shutdown(); client = host = null;
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj); temporary.Clear();
            UnityEngine.Random.state = randomState; Directory.CreateDirectory("Temp/EnemySpawnComposition");
            File.WriteAllLines("Temp/EnemySpawnComposition/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Composition Host" : "Composition Client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true; var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = multiplayer ? (server ? GameMode.Host : GameMode.Client) : GameMode.Single,
            SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner starts " + (server ? "host" : "client")); await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "first tick"); return runner;
    }
}
