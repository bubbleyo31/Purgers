using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Menu;
using MultiClimb.Menu;
using Purgers.Settings;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;

public static class PlayerSettingsLeaveVerification
{
    public static string Status { get; private set; } = "Not run";
    public static string Stage { get; private set; } = "";
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning)) throw new InvalidOperationException("Idle Play Mode required.");
        Status = "Running"; var report = new List<string>();
        var menu = PlayerSettingsMenu.Instance;
        var main = menu.transform.root.GetComponentInChildren<FusionMenuUIMain>(true);
        var wrapper = main.Connection as FusionMenuConnectionBehaviour;
        if (!wrapper) throw new InvalidOperationException("Production wrapper missing.");
        var original = wrapper.Connection;
        var repository = new Repository(Path.GetFullPath("Temp/SettingsLeave-" + Guid.NewGuid().ToString("N")));
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        var connection = new SingleConnection(config, repository); wrapper.Connection = connection;
        try
        {
            Stage = "load formal SafeHouse";
            var args = new FusionMenuConnectArgs { Region = "asia", Creating = true };
            args.SetDefaults(config); args.Region = "asia";
            var connected = await connection.ConnectAsync(args);
            Check(connected.Success, "formal SafeHouse loaded: " + connected.DebugMessage, report);
            await Until(() => connection.Runner && GameLogic.GetPrimaryForRunner(connection.Runner) != null && !Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked, "formal GameLogic and loading gate", 45);
            var context = connection.Runner.GetComponent<GameSaveRuntimeContext>();
            await Until(() => GameLogic.GetPrimaryForRunner(connection.Runner).TryWriteHostExperienceToSave(context.ActiveSave), "Host progression snapshot", 30);
            await Until(() => menu.Model.Screen == SettingsScreen.Game && menu.Model.Host, "production wrapper resolves live Host", 10);
            main.Controller.Show<FusionMenuUIGameplay>(); menu.Model.OpenPause(); menu.RefreshView();
            await Until(() => PlayerSettingsMenu.IsGameplayMenuOpen, "gameplay menu control lease", 10);
            Check(menu.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text == connection.SessionName), "pause room code reads current session", report);
            Check(menu.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b => b.name == "Button 離開並保存遊戲"), "Host sees save and leave", report);
            var flow = Purgers.GameFlow.SafeHouse.SafeHouseFlowController.GetForRunner(connection.Runner);
            Check(flow != null, "same Runner safe house flow resolved", report);
            typeof(Purgers.GameFlow.SafeHouse.SafeHouseFlowController).GetProperty("Phase").SetValue(flow, Purgers.GameFlow.SafeHouse.SafeHousePhase.ReadyCheck);
            Check(menu.HasVote, "menu observes authoritative vote", report);
            menu.Model.Escape();
            Check(menu.Model.Screen == SettingsScreen.Pause, "vote ESC keeps current menu level", report);
            await Until(() => !menu.HasVote, "ESC cancels ready vote through formal authority", 10);
            report.Add("PASS ESC cancels whole vote before closing pause menu");
            repository.FailWrites = true; menu.Model.Popup = SettingsPopup.Leave; menu.Model.Yes();
            await Until(() => !menu.Model.Busy, "failed save completes", 10);
            Check(connection.IsConnected && menu.Model.Popup == SettingsPopup.Leave && menu.Model.Notice.StartsWith("離開失敗"), "failed disk write retains room and retry dialog", report);
            int before = repository.SuccessfulWrites; repository.FailWrites = false;
            string saveId = context.ActiveSave.SaveId; menu.Model.Yes();
            await Until(() => !menu.Model.Busy && !connection.IsConnected, "successful save then disconnect", 20);
            Check(repository.SuccessfulWrites > before && repository.Load(saveId).Success, "Host progress saved to isolated repository before leave", report);
            Check(menu.Model.Screen == SettingsScreen.Main && !PlayerSettingsMenu.IsGameplayMenuOpen, "return to main and release gameplay lock", report);
            Status = "Passed";
        }
        catch (Exception e) { Status = "Failed"; report.Add("FAIL " + Stage + " : " + e); }
        finally
        {
            await connection.DisconnectAsync(ConnectFailReason.UserRequest); wrapper.Connection = original;
            main.Controller.Show<FusionMenuUIMain>();
            Directory.CreateDirectory("Documentation/ProjectArchitecture/Validation/PlayerSettings");
            File.WriteAllLines("Documentation/ProjectArchitecture/Validation/PlayerSettings/host-leave.txt", report);
        }
    }
    static void Check(bool okay, string message, List<string> report) { if (!okay) throw new InvalidOperationException(message); report.Add("PASS " + message); }
    static async Task Until(Func<bool> condition, string step, float seconds)
    {
        Stage = step; float limit = Time.realtimeSinceStartup + seconds;
        while (!condition()) { if (Time.realtimeSinceStartup > limit) throw new TimeoutException(step); await Task.Delay(20); }
    }
    sealed class SingleConnection : MenuConnection
    {
        public SingleConnection(FusionMenuConfig config, IGameSaveRepository repository) : base(config, null, repository, connectionTimeoutSeconds:60) { }
        protected override Task<StartGameResult> StartRunnerAsync(NetworkRunner runner, StartGameArgs args)
        {
            args.GameMode = GameMode.Single;
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
            foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
            args.Config = config; return runner.StartGame(args);
        }
    }
    sealed class Repository : IGameSaveRepository
    {
        readonly JsonGameSaveRepository inner; public bool FailWrites; public int SuccessfulWrites;
        public Repository(string path) { inner = new JsonGameSaveRepository(path); }
        public string RootDirectory => inner.RootDirectory;
        public GameSaveRepositoryResult<GameSaveData> CreateNew(string name, int cycleLength = GameSaveSchema.DefaultCycleLength) => inner.CreateNew(name,cycleLength);
        public GameSaveCatalog List() => inner.List();
        public GameSaveRepositoryResult<GameSaveData> Load(string id) => inner.Load(id);
        public GameSaveRepositoryResult<bool> Delete(string id) => inner.Delete(id);
        public GameSaveRepositoryResult<GameSaveData> Write(GameSaveData save)
        {
            if (FailWrites) return GameSaveRepositoryResult<GameSaveData>.Failed("受控測試：保存失敗");
            var result = inner.Write(save); if (result.Success) SuccessfulWrites++; return result;
        }
    }
}
