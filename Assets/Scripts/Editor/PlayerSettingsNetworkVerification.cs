using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Settings;
using UnityEditor;
using UnityEngine;

/// <summary>Controlled real Host/Client input; no production save, scene or prefab is written.</summary>
public static class PlayerSettingsNetworkVerification
{
    public static string Status { get; private set; } = "Not run";
    public static string Stage { get; private set; } = "";
    static NetworkRunner host, client;
    static NetInput data;
    static PlayerSettingsNetworkProbe probe;
    static readonly List<string> report = new List<string>();
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static string session;
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning)) throw new InvalidOperationException("Requires idle Play Mode.");
        Status = "Running"; report.Clear(); session = "settings-" + Guid.NewGuid().ToString("N"); data = default;
        try
        {
            host = await Connect(true); client = await Connect(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj = host.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(4000, 5000, 4000), Quaternion.identity, client.LocalPlayer);
            host.SetPlayerObject(client.LocalPlayer, obj);
            var player = obj.GetComponent<Player>(); var sprint = obj.GetComponent<PlayerSprintLatchController>();
            probe = host.gameObject.AddComponent<PlayerSettingsNetworkProbe>(); probe.InputPlayer = client.LocalPlayer; host.AddGlobal(probe);
            await Until(() => client.TryFindObject(obj.Id, out var found) && found.GetComponent<Player>().Health.IsAlive, "Client input authority spawn");
            var remote = client.FindObject(obj.Id); Check(remote.HasInputAuthority && !remote.HasStateAuthority, "Client has input authority; Host owns state");
            data = new NetInput { Direction = Vector2.up, SettingsModes = (byte)(PlayerSettingsInputRules.Valid | PlayerSettingsInputRules.SprintHold) };
            data.Buttons.Set(InputButton.Sprint, true);
            await Until(() => sprint.IsSprintActive && probe.Last.SettingsModes == data.SettingsModes, "Client hold mode starts sprint on Host");
            data.Buttons.Set(InputButton.Sprint, false);
            await Until(() => !sprint.IsSprintActive && !probe.Last.Buttons.IsSet(InputButton.Sprint), "Client hold release cancels with Host local toggle default");
            data.SettingsModes = PlayerSettingsInputRules.Valid; data.Buttons.Set(InputButton.Sprint, true);
            await Until(() => sprint.IsSprintActive, "toggle first press"); data.Buttons.Set(InputButton.Sprint, false);
            await Until(() => !probe.Last.Buttons.IsSet(InputButton.Sprint), "toggle release received"); Check(sprint.IsSprintActive, "toggle survives key release");
            data.Buttons.Set(InputButton.Sprint, true); await Until(() => !sprint.IsSprintActive, "toggle second press cancels");
            data.Buttons.Set(InputButton.Sprint, false); await Until(() => !probe.Last.Buttons.IsSet(InputButton.Sprint), "release before next sprint");
            data.Buttons.Set(InputButton.Sprint, true); await Until(() => sprint.IsSprintActive, "sprint restarts");
            data.Direction = Vector2.zero; await Until(() => !sprint.IsSprintActive, "stopping movement cancels sprint");
            data.Buttons.Set(InputButton.Crouch, true); data.Buttons.Set(InputButton.Sprint, false);
            await Until(() => obj.GetComponent<PlayerSlideController>().IsCrouched, "crouch requested");
            var grapple = obj.GetComponent<PlayerGrapple>();
            Set(grapple, "CurrentPhase", GrapplePhase.PreFire); Set(grapple, "PhaseTimer", TickTimer.CreateFromSeconds(host, 1000));
            data = new NetInput { SettingsModes = PlayerSettingsInputRules.Valid, SettingsMenuOpen = true,
                BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Gameplay | Purgers.GameFlow.Control.PlayerControlMask.Movement | Purgers.GameFlow.Control.PlayerControlMask.Look };
            await Until(() => probe.Last.SettingsMenuOpen && grapple.CurrentPhase == GrapplePhase.Idle, "menu input releases grapple through production Player");
            Check(obj.GetComponent<PlayerSlideController>().IsCrouched, "menu preserves existing crouch");
            Check(!sprint.IsSprintActive, "menu clears sprint");
            int tick = host.Tick.Raw; float y = obj.transform.position.y;
            await Until(() => host.Tick.Raw >= tick + 8, "world continues ticking behind menu");
            Check(obj.transform.position.y < y, "menu does not freeze gravity");
            await Until(() => client.FindObject(obj.Id).GetComponent<PlayerGrapple>().CurrentPhase == GrapplePhase.Idle, "release snapshot reaches Client");
            var close = client.Shutdown(); await close; client = null;
            client = await Connect(false);
            await Until(() => client.TryFindObject(obj.Id, out var found) && found.IsValid, "reconnected observer receives existing player");
            var observer = client.FindObject(obj.Id); await Until(() => !observer.GetComponent<PlayerSprintLatchController>().IsSprintActive, "reconnect does not revive sprint");
            Check(observer.GetComponent<PlayerGrapple>().CurrentPhase == GrapplePhase.Idle, "late snapshot does not revive grapple");
            var menu = PlayerSettingsMenu.Instance;
            var main = menu.transform.root.GetComponentInChildren<Fusion.Menu.FusionMenuUIMain>(true);
            var wrapper = (Fusion.Menu.FusionMenuConnectionBehaviour)main.Connection;
            var original = wrapper.Connection;
            var repository = new Purgers.Progression.JsonGameSaveRepository(Path.GetFullPath("Temp/SettingsClientLeave-" + Guid.NewGuid().ToString("N")));
            var connection = new MultiClimb.Menu.MenuConnection(AssetDatabase.LoadAssetAtPath<Fusion.Menu.FusionMenuConfig>("Assets/Settings/MenuConfig.asset"), null, repository);
            typeof(MultiClimb.Menu.MenuConnection).GetField("_runner", Flags).SetValue(connection, client);
            typeof(MultiClimb.Menu.MenuConnection).GetMethod("ObserveSession", Flags).Invoke(connection, new object[] { client });
            client.gameObject.AddComponent<Purgers.Progression.GameSaveRuntimeContext>().InitializeClientReadOnly();
            wrapper.Connection = connection;
            try
            {
                await Until(() => menu.Model.Screen == SettingsScreen.Game && !menu.Model.Host, "production UI identifies Client");
                menu.Model.OpenPause(); menu.RefreshView();
                await Until(() => PlayerSettingsMenu.IsGameplayMenuOpen, "Client pause menu opens");
                await Task.Delay(50);
                Check(menu.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b => b.name == "Button 離開遊戲"), "Client sees leave without save");
                menu.Model.Popup = SettingsPopup.Leave; menu.Model.Yes();
                await Until(() => !menu.Model.Busy && !connection.IsConnected, "Client leaves through production settings and MenuConnection");
                Check(host.IsRunning && repository.List().Summaries.Count == 0, "Client leave preserves Host and writes no world save");
                Check(menu.Model.Screen == SettingsScreen.Main && !PlayerSettingsMenu.IsGameplayMenuOpen, "Client return clears menu lock");
                client = null;
            }
            finally { wrapper.Connection = original; }
            Status = "Passed";
        }
        catch (Exception e) { Status = "Failed"; report.Add("FAIL " + Stage + " : " + e); }
        finally
        {
            if (client) await client.Shutdown(); if (host) await host.Shutdown(); client = host = null;
            Directory.CreateDirectory("Documentation/ProjectArchitecture/Validation/PlayerSettings");
            File.WriteAllLines("Documentation/ProjectArchitecture/Validation/PlayerSettings/host-client.txt", report);
        }
    }
    static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Settings test Host" : "Settings test Client").AddComponent<NetworkRunner>();
        if (server) host = runner; else client = runner;
        runner.ProvideInput = true; var events = runner.gameObject.AddComponent<NetworkEvents>();
        foreach (var field in typeof(NetworkEvents).GetFields(Flags))
            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType) && field.GetValue(events) == null)
                field.SetValue(events, Activator.CreateInstance(field.FieldType));
        events.OnInput = new NetworkEvents.InputEvent(); events.OnInput.AddListener((r, input) => input.Set(server ? default(NetInput) : data));
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var start = runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client, Config = config, SceneManager = manager,
            SessionName = session, CustomPhotonAppSettings = app, PlayerCount = 3, IsVisible = false });
        await Until(() => start.IsCompleted, "connect " + (server ? "Host" : "Client"), 45); Check(start.GetAwaiter().GetResult().Ok, "runner connected");
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0 && !runner.SceneManager.IsBusy, "runner ready"); return runner;
    }
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
    static void Check(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); report.Add("PASS " + message); }
    static async Task Until(Func<bool> condition, string stage, float timeout = 25)
    {
        Stage = stage; float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition()) { if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException(stage); await Task.Delay(15); }
        report.Add("PASS " + stage);
    }
}
public sealed class PlayerSettingsNetworkProbe : SimulationBehaviour
{
    public PlayerRef InputPlayer; public NetInput Last;
    public override void FixedUpdateNetwork() { if (Runner.TryGetInputForPlayer(InputPlayer, out NetInput input)) Last = input; }
}
