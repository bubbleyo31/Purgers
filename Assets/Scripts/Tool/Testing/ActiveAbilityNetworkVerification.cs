#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>命令列明確啟用的 E 技能雙程序驗證；獨立存檔、受控輸入與臨時 Runtime 參數，不保存 Scene/Prefab。</summary>
public sealed class ActiveAbilityNetworkVerification : MonoBehaviour, INetworkRunnerCallbacks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private NetworkRunner runner, runnerPrefab;
    private string role, session, output;
    private bool pressE, finished;
    private float started;
    private string waitingFor = "bootstrap";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--active-ability-test");
        if (index < 0 || index + 3 >= args.Length || FindObjectOfType<ActiveAbilityNetworkVerification>() != null) return;
        var menu = FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null) return;
        var probe = new GameObject("ActiveAbilityNetworkVerification").AddComponent<ActiveAbilityNetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.role = args[index + 1]; probe.session = args[index + 2]; probe.output = Path.GetFullPath(args[index + 3]);
        probe.runnerPrefab = (NetworkRunner)menu.GetType().GetField("networkRunnerPrefab", Private).GetValue(menu);
        Directory.CreateDirectory(probe.output);
        Application.runInBackground = true;
        probe.started = Time.realtimeSinceStartup;
        probe.Run();
    }
    private void Update()
    {
        if (!finished && Time.realtimeSinceStartup - started > 240f) Fail("global timeout: " + waitingFor);
    }
    private string FileFor(string name) => Path.Combine(output, name);
    private void Log(string message) => File.AppendAllText(FileFor(role + ".log"), DateTime.UtcNow.ToString("O") + " " + message + "\n");
    private void Mark(string name) => File.WriteAllText(FileFor(name), "PASS");
    private bool Has(string name) => File.Exists(FileFor(name));
    private void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Log("PASS " + message);
    }
    private void Fail(string message)
    {
        if (finished) return;
        finished = true; Log("FAIL " + message); File.WriteAllText(FileFor(role + ".failed"), message); Application.Quit(2);
    }
    private async Task Wait(Func<bool> predicate, string label, float seconds = 35f)
    {
        waitingFor = label; float start = Time.realtimeSinceStartup;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup - start > seconds) throw new TimeoutException(label);
            await Task.Delay(50);
        }
    }
    private Player[] Players() => runner == null || !runner.IsRunning ? new Player[0] :
        runner.ActivePlayers.Select(p => runner.TryGetPlayerObject(p, out var o) && o != null && o.IsValid ? o.GetComponent<Player>() : null)
            .Where(p => p != null && p.Health != null && p.AbilityRuntimeManager.LoadoutRevision > 0).ToArray();
    private Player Own() => Players().FirstOrDefault(p => p.Object.InputAuthority == runner.LocalPlayer);
    private Player Other() => Players().FirstOrDefault(p => p.Object.InputAuthority != runner.LocalPlayer);
    private static T Skill<T>(Player player) where T : class => PlayerAbilityQualification.GetModule<T>(player);
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    private PlayerAbilityDefinition Definition(string key) => Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards
        .Select(r => r.AbilityDefinition).First(d => d != null && d.name == key);
    private async Task Equip(Player player, string key)
    {
        Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(Definition(key), out string error), key + " equip " + error);
        await Task.Delay(150);
    }
    private static void Press(PlayerActiveAbilityBase skill)
    {
        var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true); skill.SimulateAbility(input, default);
    }
    private async Task Connect()
    {
        runner = Instantiate(runnerPrefab); runner.ProvideInput = true;
        var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();
        if (role == "Host")
        {
            var repository = new JsonGameSaveRepository(Path.Combine(output, "Saves"));
            var save = repository.CreateNew("E ability isolated network verification");
            if (!save.Success) throw new Exception(save.Error);
            context.InitializeHost(repository, save.Value);
        }
        else context.InitializeClientReadOnly();
        var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>(); manager.IsSceneTakeOverEnabled = false;
        var scene = new NetworkSceneInfo(); scene.AddSceneRef(SceneRef.FromIndex(1), LoadSceneMode.Additive);
        var settings = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings); settings.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { CustomPhotonAppSettings = settings,
            GameMode = role == "Host" ? GameMode.Host : GameMode.Client, SessionName = session,
            Scene = role == "Host" ? scene : (NetworkSceneInfo?)null, SceneManager = manager, PlayerCount = 2 });
        Check(result.Ok, "StartGame " + result.ShutdownReason);
        // 加在正式 callback 後，提供受控 NetInput，保留既有場景載入回報 callback。
        runner.AddCallbacks(this);
        var menu = FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);
        if (menu != null) menu.gameObject.SetActive(false);
        await Wait(() => Own() != null, "own player");
    }
    private async void Run()
    {
        try
        {
            if (role != "Host") await Wait(() => Has("Host.ready"), "late join host ready");
            await Connect();
            if (role == "Host") await Host(); else await Client();
            await Task.Delay(2000);
            if (runner != null && runner.IsRunning) await runner.Shutdown();
            finished = true; Mark(role + ".complete"); Log("PASS all controlled network checks and shutdown");
            Application.Quit(0);
        }
        catch (Exception ex) { Fail(waitingFor + ": " + ex); }
    }
    private async Task Publish(int step)
    {
        Mark("step." + step); await Wait(() => Has("Client." + step), "client confirms step " + step);
        Log("PASS shared step " + step);
    }
    private async Task Host()
    {
        await Equip(Own(), "Experience");
        typeof(PlayerExperienceAbility).GetField("durationSeconds", Private).SetValue(Skill<PlayerExperienceAbility>(Own()), 60f);
        Press(Skill<PlayerActiveAbilityBase>(Own()));
        Mark("Host.ready");
        await Wait(() => Players().Length == 2, "late client");
        await Publish(0);
        await Equip(Other(), "Shield");
        typeof(PlayerShieldAbility).GetField("levelOneSeconds", Private).SetValue(Skill<PlayerShieldAbility>(Other()), 30f);
        Mark("request.shield");
        await Wait(() => Skill<PlayerShieldAbility>(Other()) != null && Skill<PlayerShieldAbility>(Other()).CurrentShield > 0, "remote E shield");
        Check(Skill<PlayerShieldAbility>(Other()).ActivationSequence == 1, "remote input activates once");
        await Publish(1);
        Other().Movement.KCC.SetPosition(Own().Movement.KCC.Data.TargetPosition + Vector3.forward * 2);
        await Equip(Other(), "BulletTime");
        typeof(PlayerBulletTimeAbility).GetField("durationSeconds", Private).SetValue(Skill<PlayerBulletTimeAbility>(Other()), 30f);
        Mark("request.time");
        await Wait(() => Skill<PlayerBulletTimeAbility>(Other()) != null && Skill<PlayerBulletTimeAbility>(Other()).IsSessionActive, "remote E bullet time");
        Check(PlayerBulletTimeSessionRegistry.IsPlayerAffected(Own()), "host included in remote caster snapshot");
        float hp = Own().Health.CurrentHealth;
        DamageReceiverUtility.TryApplyDamage(Own().gameObject, new DamageRequest { BaseDamage = 7, RequestedDamage = 7,
            DamageType = DamageType.Ability, Attacker = Other().Object.InputAuthority, SourceObject = Other().gameObject,
            SourceNetworkObject = Other().Object }, out var result);
        Check(result.Deferred && Near(Own().Health.CurrentHealth, hp), "remote bullet time defers host damage");
        await Publish(2);
        await Equip(Other(), "Shield");
        Check(Near(Own().Health.CurrentHealth, hp - 14), "unequip settles once with current experience damage multiplier");
        Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(Own()), "host slowdown cleared");
        await Publish(3);
        var oldId = Other().Object.Id;
        Other().Health.ReceiveDamage(new DamageRequest { BaseDamage = 100000, RequestedDamage = 100000, DamageType = DamageType.Environment });
        await Wait(() => Other() != null && Other().Object.Id != oldId && Other().Health.IsAlive, "remote respawn");
        Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(Other()), "respawn does not inherit old session");
        await Publish(4);
        var logic = GameLogic.GetPrimaryForRunner(runner);
        logic.PrepareForAuthoritativeSceneTransition(); await runner.LoadScene(SceneRef.FromIndex(2), LoadSceneMode.Single);
        await Task.Delay(4000);
        await Wait(() => Players().Length == 2 && Own().Health.IsAlive && Other().Health.IsAlive && !runner.SceneManager.IsBusy, "Game transition");
        await Publish(5);
        logic = GameLogic.GetPrimaryForRunner(runner);
        logic.PrepareForAuthoritativeSceneTransition(); await runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Single);
        await Task.Delay(4000);
        await Wait(() => Players().Length == 2 && !runner.SceneManager.IsBusy, "SafeHouse return");
        await Publish(6);
        Mark("reconnect");
        await Wait(() => Players().Length == 1, "client disconnected");
        await Wait(() => Players().Length == 2, "client reconnected");
        await Publish(7);
    }
    private async Task Client()
    {
        await Wait(() => Other() != null && Skill<PlayerExperienceAbility>(Other()) != null &&
            Skill<PlayerExperienceAbility>(Other()).IsAbilityActive && Has("step.0"), "late join active ability");
        Check(Own().Object.HasInputAuthority && !Own().Object.HasStateAuthority, "client input authority only");
        int sequence = Skill<PlayerExperienceAbility>(Other()).ActivationSequence;
        Press(Skill<PlayerExperienceAbility>(Other()));
        Check(Skill<PlayerExperienceAbility>(Other()).ActivationSequence == sequence, "proxy cannot activate remote ability");
        Check(!Own().AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(Definition("Shield"), out _), "client cannot equip authoritatively");
        Mark("Client.0");
        await Wait(() => Has("request.shield") && Skill<PlayerShieldAbility>(Own()) != null, "shield equipped on client");
        var readyShield = Skill<PlayerShieldAbility>(Own());
        Check(readyShield.Phase == PlayerActiveAbilityPhase.Ready && readyShield.ActivationSequence == 0, "client shield starts ready");
        Press(readyShield);
        Check(readyShield.Phase == PlayerActiveAbilityPhase.Ready && readyShield.ActivationSequence == 0 && readyShield.CurrentShield == 0,
            "client direct activation cannot change authoritative shield state");
        pressE = true; await Task.Delay(300); pressE = false;
        await Wait(() => Has("step.1") && Skill<PlayerShieldAbility>(Own()).CurrentShield > 0, "shield replicated");
        Check(Skill<PlayerShieldAbility>(Own()).TimedHudState.IsVisible, "client timed HUD snapshot");
        Mark("Client.1");
        await Wait(() => Has("request.time") && Skill<PlayerBulletTimeAbility>(Own()) != null, "bullet time equipped on client");
        pressE = true; await Task.Delay(300); pressE = false;
        await Wait(() => Has("step.2") && PlayerBulletTimeSessionRegistry.IsPlayerAffected(Other()), "remote snapshot replicated");
        Check(PlayerBulletTimeSessionRegistry.GetPlayerMovementMultiplier(Other()) < 1, "client sees host slowdown");
        Mark("Client.2");
        await Wait(() => Has("step.3") && !PlayerBulletTimeSessionRegistry.IsPlayerAffected(Other()), "unequip cleanup replicated");
        Check(Near(Other().Health.CurrentHealth, Other().Health.MaximumHealth - 14), "settled health replicated");
        NetworkId previousPlayerId = Own().Object.Id;
        Mark("Client.3");
        for (int step = 4; step <= 6; step++)
        {
            int current = step;
            await Wait(() => Has("step." + current) && Players().Length == 2 && Own().Health.IsAlive &&
                Own().Object.Id != previousPlayerId && !runner.SceneManager.IsBusy && SceneReady(current),
                "lifecycle step " + current);
            Log("PASS local player changed " + previousPlayerId + " -> " + Own().Object.Id + " step " + current);
            previousPlayerId = Own().Object.Id;
            Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(Own()), "no stale ability after lifecycle step " + step);
            Mark("Client." + step);
        }
        await Wait(() => Has("reconnect"), "reconnect request");
        await runner.Shutdown(); await Task.Delay(1500); await Connect();
        await Wait(() => Has("step.7") && Other() != null, "reconnect snapshot");
        Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(Own()), "reconnect has no old session");
        Mark("Client.7");
    }
    private bool SceneReady(int step)
    {
        if (step == 5) return FindObjectsOfType<Purgers.GameFlow.Stage.StageFlowController>()
            .Any(f => f.Object != null && f.Object.IsValid && f.Runner == runner);
        if (step == 6) return FindObjectsOfType<Purgers.GameFlow.SafeHouse.SafeHouseFlowController>()
            .Any(f => f.Object != null && f.Object.IsValid && f.Runner == runner);
        return true;
    }
    public void OnInput(NetworkRunner r, NetworkInput input)
    {
        var data = new NetInput(); data.Buttons.Set(InputButton.Ability1, pressE); input.Set(data);
    }
    public void OnPlayerJoined(NetworkRunner r, PlayerRef p) { }
    public void OnPlayerLeft(NetworkRunner r, PlayerRef p) { }
    public void OnInputMissing(NetworkRunner r, PlayerRef p, NetworkInput i) { }
    public void OnShutdown(NetworkRunner r, ShutdownReason s) { }
    public void OnConnectedToServer(NetworkRunner r) { }
    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason s) { }
    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest q, byte[] t) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress a, NetConnectFailedReason s) { }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr m) { }
    public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> s) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> d) { }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken t) { }
    public void OnReliableDataReceived(NetworkRunner r, PlayerRef p, ReliableKey k, ArraySegment<byte> d) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef p, ReliableKey k, float v) { }
    public void OnSceneLoadDone(NetworkRunner r) { }
    public void OnSceneLoadStart(NetworkRunner r) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject o, PlayerRef p) { }
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject o, PlayerRef p) { }
}
#endif
