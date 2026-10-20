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
using UnityEngine.UI;

/// <summary>Explicit, temporary same-Editor Host/Client check. Never saves player/scene state.</summary>
public sealed class MomentumGaugeRuntimeVerification : SimulationBehaviour
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static NetworkRunner host, client;
    static Player hostPlayer, clientPlayer;
    static float clientEnergy = .5f;
    static string session;
    static readonly List<GameObject> temporary = new List<GameObject>();
    static readonly List<string> report = new List<string>();
    static readonly PropertyInfo energyProperty = typeof(PlayerGrappleMomentumEnergy).GetProperty("NormalizedEnergy");
    static readonly PropertyInfo timerProperty = typeof(PlayerGrappleMomentumEnergy).GetProperty("MaximumBoostTimer", Private);

    public override void FixedUpdateNetwork()
    {
        foreach (var p in new[] { hostPlayer, clientPlayer })
        {
            if (p == null || p.Object == null || !p.Object.IsValid || !p.Object.HasStateAuthority) continue;
            p.Movement.KCC.SetPosition(new Vector3(p == hostPlayer ? 7000 : 7004, 700, 7000));
            p.Movement.KCC.SetDynamicVelocity(Vector3.zero);
            var energy = p.GetComponent<PlayerGrappleMomentumEnergy>();
            float value = p == hostPlayer ? .25f : clientEnergy;
            energyProperty.SetValue(energy, value);
            timerProperty.SetValue(energy, value >= 1 ? TickTimer.CreateFromSeconds(Runner, .25f) : TickTimer.None);
        }
    }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException(label); report.Add("PASS " + label); }
    static async Task Until(Func<bool> predicate, string label)
    {
        Stage = label; float end = Time.realtimeSinceStartup + 25;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(15); }
    }
    static async Task<NetworkRunner> Connect(bool server)
    {
        var go = new GameObject(server ? "Gauge verification Host" : "Gauge verification Client"); temporary.Add(go);
        var runner = go.AddComponent<NetworkRunner>(); runner.ProvideInput = true;
        var events = go.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => input.Set(default(NetInput)));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = go.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session, Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 2, IsVisible = false });
        Check(result.Ok, "Runner connects " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "first tick"); return runner;
    }
    static LocalPlayerSpeedSlider Hud(NetworkRunner runner)
    {
        var root = new GameObject("Temporary gauge HUD"); temporary.Add(root); root.SetActive(false);
        var bar = new GameObject("Gauge", typeof(RectTransform), typeof(Slider)); bar.transform.SetParent(root.transform);
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(bar.transform);
        var slider = bar.GetComponent<Slider>(); slider.fillRect = (RectTransform)fill.transform;
        fill.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MomentumSourcePath);
        fill.GetComponent<Image>().type = Image.Type.Filled;
        var owner = root.AddComponent<LocalPlayerSpeedSlider>();
        Set(owner, "runner", runner); Set(owner, "speedSlider", slider); Set(owner, "speedVisualRoot", bar);
        root.SetActive(true); return owner;
    }
    const string MomentumSourcePath = "Assets/_Project_Assets/UI/BattleHUD/Layout/Momentum.png";
    static Material Flow(LocalPlayerSpeedSlider hud) => Get<Material>(hud, "gaugeFlowMaterial");
    static bool Full(LocalPlayerSpeedSlider hud) => Get<bool>(hud, "maximumBoostStyleApplied");

    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning)) throw new InvalidOperationException("Needs Play Mode with no running Runner.");
        Status = "執行中"; report.Clear(); session = "gauge-" + Guid.NewGuid().ToString("N"); clientEnergy = .5f;
        try
        {
            host = await Connect(true); host.AddGlobal(host.gameObject.AddComponent<MomentumGaugeRuntimeVerification>());
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var first = host.Spawn(prefab, new Vector3(7000,700,7000), Quaternion.identity, host.LocalPlayer);
            host.SetPlayerObject(host.LocalPlayer, first); hostPlayer = first.GetComponent<Player>();
            var hostHud = Hud(host); await Until(() => Flow(hostHud) != null, "host gauge bind");
            Check(hostPlayer.Object.HasStateAuthority && hostPlayer.Object.HasInputAuthority, "host authority");
            client = await Connect(false);
            await Until(() => client.FindObject(first.Id) != null, "late join existing player replica");
            var second = host.Spawn(prefab, new Vector3(7004,700,7000), Quaternion.identity, client.LocalPlayer);
            host.SetPlayerObject(client.LocalPlayer, second); clientPlayer = second.GetComponent<Player>();
            var clientHud = Hud(client); await Until(() => Flow(clientHud) != null, "client gauge bind");
            var viewed = Get<Player>(clientHud, "boundPlayer");
            Check(viewed.Object.HasInputAuthority && !viewed.Object.HasStateAuthority, "client input authority only");
            Check(!Full(clientHud) && !Full(hostHud), "partial values keep normal color and speed");
            float phase = Flow(clientHud).GetFloat("_Phase"); await Task.Delay(150);
            Check(!Mathf.Approximately(phase, Flow(clientHud).GetFloat("_Phase")), "partial client gauge animates");
            Vector2 size = Get<RectTransform>(clientHud, "gaugeRect").sizeDelta;
            Vector3 scale = Get<RectTransform>(clientHud, "gaugeRect").localScale;
            clientEnergy = 1;
            await Until(() => Full(clientHud), "replicated full state");
            Check(((Color32)Get<Image>(clientHud, "gaugeFillImage").color).Equals(new Color32(37,255,217,255)), "full client color 25FFD9");
            Check(!Full(hostHud), "client full state does not leak to host HUD");
            Check(Get<RectTransform>(clientHud, "gaugeRect").sizeDelta == size && Get<RectTransform>(clientHud, "gaugeRect").localScale == scale, "full state keeps size and scale");
            var lateHud = Hud(client); await Until(() => Flow(lateHud) != null && Full(lateHud), "late binding reconstructs full presentation");
            Check(Flow(lateHud) != Flow(clientHud), "late HUD owns separate material");
            clientEnergy = .5f; await Until(() => !Full(clientHud) && !Full(lateHud), "exit full restores normal state");
            clientHud.enabled = false; await Task.Delay(30); Check(Flow(clientHud) == null, "disable releases flow material");
            clientHud.enabled = true; await Until(() => Flow(clientHud) != null, "reenable rebinds");
            host.Despawn(second); clientPlayer = null;
            await Until(() => Flow(clientHud) == null, "despawn releases old presentation");
            var replacement = host.Spawn(prefab, new Vector3(7004,700,7000), Quaternion.identity, client.LocalPlayer);
            host.SetPlayerObject(client.LocalPlayer, replacement); clientPlayer = replacement.GetComponent<Player>();
            await Until(() => Flow(clientHud) != null && Get<Player>(clientHud,"boundPlayer") != viewed, "replacement binds");
            await client.Shutdown(); client = null; await host.Shutdown(); host = null;
            await Until(() => Flow(clientHud) == null && Flow(hostHud) == null, "disconnect clears materials");
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[GaugeVerification] " + ex); }
        finally
        {
            hostPlayer = clientPlayer = null;
            if (client != null) { await client.Shutdown(); client = null; }
            if (host != null) { await host.Shutdown(); host = null; }
            foreach (var go in temporary) if (go != null) UnityEngine.Object.Destroy(go); temporary.Clear();
            report.Add("RESULT " + Status);
            Directory.CreateDirectory("Temp/MomentumGauge"); File.WriteAllLines("Temp/MomentumGauge/host-client.txt", report);
        }
    }
}
#endif
