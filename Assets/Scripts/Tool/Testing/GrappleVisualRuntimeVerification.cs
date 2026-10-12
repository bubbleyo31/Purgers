#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>明確呼叫的鉤索呈現受控檢查；只建立暫時 Runner／模型，不保存 Scene 或 Prefab。</summary>
public static class GrappleVisualRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> report = new List<string>();
    private static NetworkRunner host, client;
    private static GameObject hookTemplate;
    private static int failures;

    public static async void Begin(bool network)
    {
        if (!Application.isPlaying || host != null) throw new InvalidOperationException("需要 Play Mode 且尚未執行。");
        Status = "執行中"; failures = 0; report.Clear();
        try
        {
            string session = "GrappleVisual-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            host = await Start(network ? GameMode.Host : GameMode.Single, session, network);
            hookTemplate = new GameObject("Grapple verification hook template");
            hookTemplate.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var local = host.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000, 100, 1000),
                Quaternion.identity, host.LocalPlayer);
            var remote = host.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1003, 100, 1000),
                Quaternion.identity, PlayerRef.None);
            await Task.Delay(250);
            Configure(local); Configure(remote);
            Check(Bool(local, "networkVisualBound") && Bool(local, "localVisualEnabled"), "Host Input Authority has first-person binding");
            Check(Bool(remote, "networkVisualBound") && !Bool(remote, "localVisualEnabled"), "Host observer has third-person binding");
            await CheckPhases(local, "Host local");
            await CheckPhases(remote, "Host remote");

            // 已附著後才加入；Client 必須從同步狀態直接顯示直線，不重播射出。
            Inject(remote, GrapplePhase.Attached, 1f);
            if (network)
            {
                Status = "Client 連線中";
                client = await Start(GameMode.Client, session, true);
                NetworkObject proxy = null;
                await Until(() => client.TryFindObject(remote.Id, out proxy) && proxy != null && proxy.IsValid, "Client late join object");
                Configure(proxy);
                await Task.Delay(350);
                Check(Bool(proxy, "networkVisualBound") && !Bool(proxy, "localVisualEnabled"), "Client proxy automatically bound");
                var line = proxy.GetComponent<LineRenderer>();
                Check(line.enabled && line.positionCount == 2, "Client late join Attached is straight");
                Check((line.GetPosition(line.positionCount - 1) - remote.GetComponent<PlayerGrapple>().CurrentGrapplePoint).magnitude < .05f, "Client receives authoritative anchor");
                Inject(remote, GrapplePhase.Idle, 0f);
                await Task.Delay(200);
                Inject(remote, GrapplePhase.Shooting, .5f);
                await Task.Delay(250);
                Check(line.enabled && line.positionCount > 2 && Hook(proxy) != null && Hook(proxy).activeSelf, "Client sees Host shooting waves and hook");
                Inject(remote, GrapplePhase.Attached, 1f);
                await Task.Delay(350);
                Check(line.enabled && line.positionCount == 2, "Client sees Host hit tighten");
                Inject(remote, GrapplePhase.Retracting, .25f);
                await Task.Delay(250);
                Check(line.enabled, "Client sees Host retract");
                host.Despawn(remote);
                await Task.Delay(300);
                Check(proxy == null || !proxy.IsValid, "Client despawn removes remote visual owner");
                await client.Shutdown(); client = null;
                Status = "Client 重新連線中";
                client = await Start(GameMode.Client, session, true);
                NetworkObject rejoined = null;
                await Until(() => client.TryFindObject(local.Id, out rejoined) && rejoined != null && rejoined.IsValid, "Client reconnect object");
                Check(Bool(rejoined, "networkVisualBound"), "Client reconnect rebinds visual");
                var clientSource = host.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1006, 100, 1000),
                    Quaternion.identity, client.LocalPlayer);
                NetworkObject clientOwned = null;
                await Until(() => client.TryFindObject(clientSource.Id, out clientOwned) && clientOwned != null && clientOwned.IsValid,
                    "Client Input Authority object");
                Configure(clientSource); Configure(clientOwned);
                Check(clientOwned.HasInputAuthority && Bool(clientOwned, "localVisualEnabled"),
                    "Client Input Authority binds first-person visual");
                Inject(clientSource, GrapplePhase.Shooting, .5f);
                await Task.Delay(300); TickLocal(clientOwned);
                Check(clientOwned.GetComponent<LineRenderer>().positionCount > 2 && Hook(clientOwned) != null && Hook(clientOwned).activeSelf,
                    "Client Input Authority renders received shooting state");
                Check(!Bool(clientSource, "localVisualEnabled") && clientSource.GetComponent<LineRenderer>().positionCount > 2 &&
                    Hook(clientSource) != null && Hook(clientSource).activeSelf,
                    "Host observes Client-owned character waves and hook");
                var ownedHook = Hook(clientOwned);
                host.Despawn(clientSource);
                await Task.Delay(300);
                Check(ownedHook == null, "Client-owned hook destroyed after network despawn");
            }
            else
            {
                // 無外部連線時，只證明有效 Fusion 物件與 observer 分支，不稱為雙端同步。
                var hook = Hook(remote);
                host.Despawn(remote);
                await Task.Delay(100);
                Check(hook == null, "Despawn destroys hook instance");
            }
            host.Despawn(local);
        }
        catch (Exception ex) { failures++; report.Add("FAIL " + ex); }
        finally
        {
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            if (hookTemplate != null) UnityEngine.Object.Destroy(hookTemplate);
            Status = failures == 0 ? "通過" : "失敗 " + failures;
            string mode = network ? "HostClient" : "SingleRunner";
            Directory.CreateDirectory("Temp/GrappleVisualVerification");
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + mode + " " + Status);
            File.WriteAllLines("Temp/GrappleVisualVerification/" + mode + ".txt", report);
            Debug.Log("[GrappleVisualRuntimeVerification] " + mode + " " + Status);
        }
    }

    private static async Task<NetworkRunner> Start(GameMode mode, string session, bool multiple)
    {
        var r = new GameObject("Grapple Visual " + mode).AddComponent<NetworkRunner>();
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = multiple ? NetworkProjectConfig.PeerModes.Multiple : NetworkProjectConfig.PeerModes.Single;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var sceneManager = r.gameObject.AddComponent<GrappleVisualVerificationSceneManager>();
        sceneManager.IsSceneTakeOverEnabled = false;
        var settings = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);
        settings.FixedRegion = "asia";
        try
        {
            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25)))
            {
                var started = await r.StartGame(new StartGameArgs { GameMode = mode, SessionName = session, Config = config,
                    SceneManager = sceneManager, PlayerCount = 2, CustomPhotonAppSettings = settings,
                    StartGameCancellationToken = cts.Token });
                if (!started.Ok) throw new InvalidOperationException(mode + ": " + started.ShutdownReason);
            }
            await Until(() => !r.SceneManager.IsBusy, mode + " scene ready");
            // StartGame 完成後，還需等待首個模擬更新提交 KCC 所需的 RuntimeConfig。
            int initialTick = r.Tick.Raw;
            await Until(() => r.IsRunning && r.Tick.Raw != initialTick, mode + " first simulation update");
            return r;
        }
        catch
        {
            await r.Shutdown();
            throw;
        }
    }

    private static void Configure(NetworkObject obj)
    {
        // 不提供輸入，玩法不自行推進；只注入既有 Networked 資料以驗證讀取與繪圖。
        obj.GetComponent<Player>().enabled = false;
        var v = obj.GetComponent<PlayerGrappleVisual>();
        typeof(PlayerGrappleVisual).GetField("hookPrefab", Private).SetValue(v, hookTemplate);
    }

    private static void Inject(NetworkObject obj, GrapplePhase phase, float extension)
    {
        var g = obj.GetComponent<PlayerGrapple>();
        typeof(PlayerGrapple).GetProperty("CurrentPhase").SetValue(g, phase);
        Set(g, "GrappleWorldPoint", obj.transform.position + Vector3.forward * 20f + Vector3.up * 2f);
        Set(g, "CurrentShootDuration", 1000f);
        Set(g, "CurrentRetractDuration", 1000f);
        Set(g, "ExtensionAtRetractStart", extension);
        Set(g, "PhaseTimer", TickTimer.CreateFromSeconds(g.Runner, phase == GrapplePhase.Shooting ? 1000f * (1f - extension) : 1000f));
    }

    private static async Task CheckPhases(NetworkObject obj, string label)
    {
        var v = obj.GetComponent<PlayerGrappleVisual>();
        var line = obj.GetComponent<LineRenderer>();
        Inject(obj, GrapplePhase.Shooting, .5f);
        await Task.Delay(100);
        // 本地 CameraFollow 在 _Menu 沒有 target 時，明確觸發既有 after-camera 更新入口。
        TickLocal(obj);
        Check(line.enabled && line.positionCount > 2 && Hook(obj) != null && Hook(obj).activeSelf, label + " shooting waves and hook");
        Vector3 old = line.GetPosition(line.positionCount / 3);
        await Task.Delay(100); TickLocal(obj);
        Check(Vector3.Distance(old, line.GetPosition(line.positionCount / 3)) > .001f, label + " wave advances");
        Inject(obj, GrapplePhase.Attached, 1f);
        for (int i = 0; i < 20; i++) { await Task.Delay(20); TickLocal(obj); }
        Check(line.enabled && line.positionCount == 2, label + " hit straightens");
        Inject(obj, GrapplePhase.Retracting, .25f);
        await Task.Delay(100); TickLocal(obj);
        Check(line.enabled && Hook(obj).activeSelf, label + " retract retains hook");
        v.enabled = false;
        Check(!line.enabled && !Hook(obj).activeSelf, label + " disabled hides");
        v.enabled = true;
        Inject(obj, GrapplePhase.Idle, 0f);
        await Task.Delay(100); TickLocal(obj);
        Check(!line.enabled && !Hook(obj).activeSelf, label + " idle hides");
    }

    private static void TickLocal(NetworkObject obj)
    {
        if (obj.HasInputAuthority)
            typeof(PlayerGrappleVisual).GetMethod("OnAfterCameraUpdated", Private).Invoke(obj.GetComponent<PlayerGrappleVisual>(), null);
    }
    private static void Set(PlayerGrapple g, string name, object value) => typeof(PlayerGrapple).GetProperty(name, Private).SetValue(g, value);
    private static bool Bool(NetworkObject obj, string field) => (bool)typeof(PlayerGrappleVisual).GetField(field, Private).GetValue(obj.GetComponent<PlayerGrappleVisual>());
    private static GameObject Hook(NetworkObject obj) => (GameObject)typeof(PlayerGrappleVisual).GetField("hookInstance", Private).GetValue(obj.GetComponent<PlayerGrappleVisual>());
    private static void Check(bool pass, string text) { report.Add((pass ? "PASS " : "FAIL ") + text); if (!pass) failures++; }
    private static async Task Until(Func<bool> predicate, string label)
    {
        float start = Time.realtimeSinceStartup;
        while (!predicate()) { if (Time.realtimeSinceStartup - start > 15f) throw new TimeoutException(label); await Task.Delay(30); }
    }
}

/// <summary>
/// 僅供受控探針：沿用 Fusion 隔離物理場景，不載入遊戲 Scene。
/// Default manager 未 LoadScene 時保持 Busy；改以已建立的空 Runner Scene 接收測試物件。
/// </summary>
public sealed class GrappleVisualVerificationSceneManager : NetworkSceneManagerDefault, INetworkSceneManager
{
    public override bool IsBusy => !MainRunnerScene.IsValid() || !MainRunnerScene.isLoaded;

    bool INetworkSceneManager.MoveGameObjectToScene(GameObject obj, SceneRef sceneRef)
    {
        if (sceneRef != default) return false;
        if (obj.scene != MainRunnerScene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, MainRunnerScene);
        return true;
    }
}
#endif
