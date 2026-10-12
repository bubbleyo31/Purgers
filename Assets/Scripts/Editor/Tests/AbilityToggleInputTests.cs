using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

/// <summary>有效 Fusion 物件的受控輸入／結算測試；不等同獨立 Host／Client。</summary>
[Category("PurgersRegression")]
public sealed class AbilityToggleInputTests
{
    private NetworkRunner runner;
    private Player player;
    private NetworkRunner client;
    private bool clientPress;
    private static string networkStep;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static NetInput Press { get { var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true); return input; } }

    private IEnumerator StartRunner()
    {
        runner = new GameObject("Ability toggle verification").AddComponent<NetworkRunner>();
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var sceneManager = runner.gameObject.AddComponent<GrappleVisualVerificationSceneManager>();
        sceneManager.IsSceneTakeOverEnabled = false;
        var start = runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config, SceneManager = sceneManager });
        float deadline = Time.realtimeSinceStartup + 30f;
        while (!start.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(start.IsCompleted, Is.True, "Runner startup timeout");
        Assert.That(start.GetAwaiter().GetResult().Ok, Is.True);
        int initialTick = runner.Tick.Raw;
        while ((runner.SceneManager.IsBusy || runner.Tick.Raw == initialTick || runner.Tick.Raw <= 0) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(runner.Tick.Raw, Is.GreaterThan(0));
        Assert.That(runner.Tick.Raw, Is.Not.EqualTo(initialTick));
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000, 100, 1000), Quaternion.identity, runner.LocalPlayer);
        runner.SetPlayerObject(runner.LocalPlayer, obj);
        player = obj.GetComponent<Player>();
        player.enabled = false;
        player.ProfessionRuntimeManager.enabled = false;
    }

    private T Spawn<T>(string path) where T : Component
    {
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(path);
        Assert.That(definition, Is.Not.Null, "Definition asset: " + path);
        Assert.That(definition.RuntimePrefab, Is.Not.Null, "Runtime prefab: " + path);
        Assert.That(player, Is.Not.Null, "Spawned player");
        Assert.That(player.Object, Is.Not.Null, "Player NetworkObject");
        var obj = runner.Spawn(definition.RuntimePrefab, player.transform.position, Quaternion.identity, player.Object.InputAuthority,
            (r, o) => o.GetComponent<PlayerAbilityRuntime>().InitializeBeforeSpawn(player.Object, 0));
        Assert.That(obj, Is.Not.Null, "Spawned ability object: " + path);
        var module = obj.GetComponent<T>();
        Assert.That(module, Is.Not.Null, "Ability module: " + typeof(T).Name);
        ((IPlayerAbilityRuntimeModule)module).BindOwnerPlayer(player);
        return module;
    }

    [UnityTest]
    public IEnumerator AerialPressReleaseRepressPreservesMomentumAndOriginalEndConditions()
    {
        SessionState.SetBool(AbilityToggleTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        yield return StartRunner();
        var aerial = Spawn<SupportAerialAbility>("Assets/_Project_Assets/Data/PlayerAbility/Definitions/SupportAerial.asset");
        SetField(aerial, "debugAbility", false);
        var kcc = player.Movement.KCC;
        Set(player.StateMachine, "CurrentState", PlayerMovementState.GrappleAirborne);
        kcc.Data.IsGrounded = false;
        Vector3 velocity = new Vector3(12, 8, -5);
        kcc.SetDynamicVelocity(velocity);
        aerial.SimulateAbility(Press, Press.Buttons);
        Assert.That(aerial.IsAbilityActive, Is.False, "Holding before eligibility must not start the ability");
        aerial.SimulateAbility(Press, default);
        Assert.That(aerial.IsAbilityActive, Is.True);
        Assert.That(aerial.IsCoolingDown, Is.False);
        Assert.That(aerial.GetMovementInputMultiplier(default), Is.Zero);
        aerial.SimulateAbility(Press, Press.Buttons);
        Assert.That(aerial.IsAbilityActive, Is.True, "Holding must not toggle off");
        aerial.SimulateAbility(default, Press.Buttons);
        Assert.That(aerial.IsAbilityActive, Is.True, "Releasing E must preserve the active effect");
        aerial.SimulateAbility(Press, default);
        Assert.That(aerial.IsAbilityActive, Is.False, "Second press ends immediately");
        Assert.That(kcc.Data.DynamicVelocity, Is.EqualTo(velocity));
        Assert.That(aerial.GetMovementInputMultiplier(default), Is.EqualTo(1f));
        Assert.That(aerial.CooldownRemainingSeconds, Is.EqualTo(GetField<float>(aerial, "cooldownDuration")).Within(runner.DeltaTime));
        aerial.SimulateAbility(Press, default);
        Assert.That(aerial.IsAbilityActive, Is.False, "Cooldown blocks restart");
        Set(aerial, "CooldownTimer", TickTimer.None);
        aerial.SimulateAbility(Press, Press.Buttons);
        Assert.That(aerial.IsAbilityActive, Is.False, "Cooldown expiration cannot turn a hold into a press");
        foreach (string ending in new[] { "ground", "state", "timeout", "profession", "control" })
        {
            aerial.SetProfessionAvailable(true);
            Set(aerial, "CooldownTimer", TickTimer.None);
            Set(player.StateMachine, "CurrentState", PlayerMovementState.GrappleAirborne);
            kcc.Data.IsGrounded = false;
            aerial.SimulateAbility(Press, default);
            Assert.That(aerial.IsAbilityActive, Is.True, ending);
            if (ending == "ground") kcc.Data.IsGrounded = true;
            if (ending == "state") Set(player.StateMachine, "CurrentState", PlayerMovementState.Airborne);
            if (ending == "timeout") Set(aerial, "ActiveTimer", TickTimer.CreateFromTicks(runner, -1));
            if (ending == "profession") aerial.SetProfessionAvailable(false);
            var input = new NetInput();
            if (ending == "control") input.BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Gameplay;
            aerial.SimulateAbility(input, Press.Buttons);
            Assert.That(aerial.IsAbilityActive, Is.False, ending + " still ends the ability");
            Assert.That(aerial.IsCoolingDown, Is.True, ending + " starts cooldown");
        }
        Set(aerial, "CooldownTimer", TickTimer.None);
        Set(player.StateMachine, "CurrentState", PlayerMovementState.Airborne);
        aerial.SimulateAbility(Press, default);
        Assert.That(aerial.IsAbilityActive, Is.False);
        Set(player.StateMachine, "CurrentState", PlayerMovementState.GrappleAirborne);
        kcc.Data.IsGrounded = true;
        aerial.SimulateAbility(Press, default);
        Assert.That(aerial.IsAbilityActive, Is.False);
        yield break;
    }

    [UnityTest]
    public IEnumerator AerialTimedHudUsesActivationSnapshotAndExistingStageBar()
    {
        SessionState.SetBool(AbilityToggleTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        yield return StartRunner();
        var aerial = Spawn<SupportAerialAbility>("Assets/_Project_Assets/Data/PlayerAbility/Definitions/SupportAerial.asset");
        var source = (object)aerial as IPlayerAbilityHudState;
        Assert.That(source, Is.Not.Null, "空中緩速必須提供中央時間條快照。");
        SetField(aerial, "debugAbility", false);
        var manager = player.AbilityRuntimeManager;
        var slots = (NetworkDictionary<int, NetworkObject>)typeof(PlayerAbilityRuntimeManager)
            .GetProperty("ActiveAbilityRuntimeObjects", Flags).GetValue(manager);
        slots.Clear(); slots.Add(0, aerial.Object);
        Set(manager, "LoadoutRevision", 1);
        Set(manager, "StartingLoadoutApplied", (NetworkBool)true);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var hudRoot = Object.Instantiate(prefab);
        try
        {
            var hud = hudRoot.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
            Assert.That(hud, Is.Not.Null);
            var bar = GetField<GameObject>(hud, "timedAbilityRoot");
            var fill = GetField<UnityEngine.UI.Image>(hud, "timedAbilityFill");
            var seconds = GetField<TMPro.TMP_Text>(hud, "timedAbilitySeconds");
            var refresh = typeof(LocalPlayerBattleAbilityHUD).GetMethod("RefreshTimedAbility", Flags);
            float duration = GetField<float>(aerial, "maximumActiveDuration");
            foreach (string ending in new[] { "cancel", "ground", "timeout" })
            {
                SetField(aerial, "maximumActiveDuration", duration);
                Set(aerial, "CooldownTimer", TickTimer.None);
                Set(player.StateMachine, "CurrentState", PlayerMovementState.GrappleAirborne);
                player.Movement.KCC.Data.IsGrounded = false;
                aerial.SimulateAbility(Press, default);
                Assert.That(source.TimedHudState.DurationSeconds, Is.EqualTo(duration));
                // 模擬中途才首次觀察；本機設定改變不得重建這次施放的總長。
                SetField(aerial, "maximumActiveDuration", duration * 3f);
                Set(aerial, "ActiveTimer", TickTimer.CreateFromSeconds(runner, duration * .5f));
                var snapshot = source.TimedHudState;
                Assert.That(snapshot.DurationSeconds, Is.EqualTo(duration));
                Assert.That(snapshot.NormalizedRemaining, Is.EqualTo(.5f).Within(runner.DeltaTime / duration));
                refresh.Invoke(hud, new object[] { player, manager });
                Assert.That(bar.activeSelf, Is.True, ending);
                Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(snapshot.NormalizedRemaining).Within(.001f));
                Assert.That(seconds.text, Does.EndWith("s"));
                Assert.That(GetField<TMPro.TMP_Text>(hud, "timedAbilityState").text, Is.EqualTo("效果持續"));
                Assert.That(GetField<TMPro.TMP_Text>(hud, "timedAbilityName").text,
                    Is.EqualTo(aerial.GetComponent<PlayerAbilityRuntime>().Definition.DisplayName));
                if (ending == "ground") player.Movement.KCC.Data.IsGrounded = true;
                if (ending == "timeout") Set(aerial, "ActiveTimer", TickTimer.CreateFromTicks(runner, -1));
                aerial.SimulateAbility(ending == "cancel" ? Press : default, default);
                Assert.That(source.TimedHudState.IsVisible, Is.False, ending);
                Assert.That(source.CooldownRemainingSeconds, Is.GreaterThan(0f));
                refresh.Invoke(hud, new object[] { player, manager });
                Assert.That(bar.activeSelf, Is.False, ending + " hides central bar during cooldown");
            }
        }
        finally { Object.Destroy(hudRoot); }
    }

    [UnityTest]
    public IEnumerator BulletTimeSecondPressUsesNormalFinishAndResolvesDamageExactlyOnce()
    {
        SessionState.SetBool(AbilityToggleTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        yield return StartRunner();
        var time = Spawn<PlayerBulletTimeAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/BulletTime.asset");
        SetField(time, "castDelaySeconds", 0f);
        foreach (bool manualEnd in new[] { true, false })
        {
            Set(time, "CooldownTimer", TickTimer.None);
            time.SimulateAbility(Press, default);
            Assert.That(time.IsSessionActive, Is.True);
            int sequence = time.ActivationSequence;
            float health = player.Health.CurrentHealth;
            var request = new DamageRequest { BaseDamage = 7, RequestedDamage = 7, DamageType = DamageType.Ability,
                Attacker = runner.LocalPlayer, SourceObject = player.gameObject, SourceNetworkObject = player.Object };
            DamageReceiverUtility.TryApplyDamage(player.gameObject, request, out var queued);
            Assert.That(queued.Deferred, Is.True);
            Assert.That(player.Health.CurrentHealth, Is.EqualTo(health));
            time.SimulateAbility(Press, Press.Buttons);
            time.SimulateAbility(default, Press.Buttons);
            Assert.That(time.IsSessionActive, Is.True, "Hold and release do not end bullet time");
            if (manualEnd) time.SimulateAbility(Press, default);
            else { Set(time, "PhaseTimer", TickTimer.CreateFromTicks(runner, -1)); time.FixedUpdateNetwork(); }
            Assert.That(time.IsSessionActive, Is.False, "Manual press and timeout share the finish path");
            Assert.That(player.Health.CurrentHealth, Is.EqualTo(health - 7).Within(.01f));
            Assert.That(time.AffectedPlayerCount, Is.Zero);
            Assert.That(PlayerBulletTimeSessionRegistry.IsPlayerAffected(player), Is.False);
            Assert.That(time.CooldownRemainingSeconds, Is.EqualTo(GetField<float>(time, "cooldownSeconds")).Within(runner.DeltaTime));
            time.SimulateAbility(Press, default);
            time.FixedUpdateNetwork();
            Assert.That(time.ActivationSequence, Is.EqualTo(sequence), "Cancel does not count as a new cast");
            Assert.That(player.Health.CurrentHealth, Is.EqualTo(health - 7).Within(.01f), "No duplicate settlement");
        }
        Set(time, "CooldownTimer", TickTimer.None);
        time.SimulateAbility(Press, default);
        var other = Spawn<PlayerBulletTimeAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/BulletTime.asset");
        SetField(other, "castDelaySeconds", 0f);
        other.SimulateAbility(Press, default);
        time.SimulateAbility(Press, default);
        Assert.That(other.IsSessionActive, Is.True, "Cancelling one session preserves the other");
        Assert.That(PlayerBulletTimeSessionRegistry.IsPlayerAffected(player), Is.True);
        other.SimulateAbility(Press, default);
        Assert.That(PlayerBulletTimeSessionRegistry.IsPlayerAffected(player), Is.False);
        var shield = Spawn<PlayerShieldAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shield.asset");
        SetField(shield, "castDelaySeconds", 0f);
        shield.SimulateAbility(Press, default);
        int shieldSequence = shield.ActivationSequence;
        shield.SimulateAbility(Press, default);
        Assert.That(shield.Phase, Is.EqualTo(PlayerActiveAbilityPhase.Active), "Other E skills keep default non-cancellable behavior");
        Assert.That(shield.ActivationSequence, Is.EqualTo(shieldSequence));
        yield break;
    }

    [UnityTest]
    [Explicit("需要 Photon 連線；獨立執行受控 Host/Client 驗證")]
    public IEnumerator HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState()
    {
        SessionState.SetBool(AbilityToggleTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        // 封閉函式必須在 Domain reload 後建立，否則捕捉的區域變數容器會遺失。
        yield return RunNetworkScenario();
    }

    private IEnumerator RunNetworkScenario()
    {
        Assert.That(Application.isPlaying, Is.True, "Network verification must run in Play Mode");
        System.IO.File.WriteAllText("Temp/AbilityToggle/network-progress.txt", "Host/Client verification\n");
        Application.logMessageReceived += CaptureNetworkError;
        string session = "purgers-toggle-" + System.Guid.NewGuid().ToString("N");
        yield return Connect(true, session);
        SpawnPlayer(runner.LocalPlayer);
        var hostTime = Spawn<PlayerBulletTimeAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/BulletTime.asset");
        SetField(hostTime, "durationSeconds", 60f); // 僅測試實例，留出連線／晚加入時間。
        SetField(hostTime, "castDelaySeconds", 0f);
        hostTime.SimulateAbility(Press, default);
        Assert.That(hostTime.IsSessionActive, Is.True);
        yield return Connect(false, session);
        yield return Until(() => client.FindObject(hostTime.Object.Id) != null, "late join ability snapshot");
        var hostProxy = client.FindObject(hostTime.Object.Id).GetComponent<PlayerBulletTimeAbility>();
        yield return Until(() => hostProxy.IsSessionActive, "late join active state");
        Assert.That(hostProxy.Object.HasStateAuthority, Is.False);
        hostProxy.SimulateAbility(Press, default);
        Assert.That(hostTime.IsSessionActive, Is.True, "Proxy cannot end authoritative session");
        hostTime.SimulateAbility(Press, default);
        yield return Until(() => !hostProxy.IsSessionActive && hostProxy.AffectedPlayerCount == 0, "host cancellation replicated");
        var close = client.Shutdown();
        while (!close.IsCompleted) yield return null;
        client = null;
        yield return Connect(false, session);
        yield return Until(() => client.FindObject(hostTime.Object.Id) != null, "reconnect ability snapshot");
        hostProxy = client.FindObject(hostTime.Object.Id).GetComponent<PlayerBulletTimeAbility>();
        Assert.That(hostProxy.IsSessionActive, Is.False, "Reconnect cannot restart a cancelled session");

        SpawnPlayer(client.LocalPlayer);
        var time = Spawn<PlayerBulletTimeAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/BulletTime.asset");
        SetField(time, "durationSeconds", 30f);
        SetField(time, "castDelaySeconds", 0f);
        var driver = runner.gameObject.AddComponent<AbilityToggleNetworkDriver>();
        driver.InputPlayer = client.LocalPlayer;
        driver.Module = time;
        runner.AddGlobal(driver);
        yield return Until(() => client.FindObject(time.Object.Id) != null && client.FindObject(player.Object.Id) != null, "client owned objects");
        var own = client.FindObject(player.Object.Id).GetComponent<Player>();
        var ownTime = client.FindObject(time.Object.Id).GetComponent<PlayerBulletTimeAbility>();
        Assert.That(own.Object.HasInputAuthority && !own.Object.HasStateAuthority, Is.True);
        clientPress = true;
        yield return Until(() => time.IsSessionActive && ownTime.IsSessionActive, "client first E activates on Host and replicates");
        int sequence = time.ActivationSequence;
        int tick = runner.Tick.Raw;
        yield return Until(() => runner.Tick.Raw > tick + 10, "client hold");
        Assert.That(time.IsSessionActive, Is.True, "Held Client E does not cancel");
        clientPress = false;
        yield return Until(() => !driver.PreviousButtons.IsSet(InputButton.Ability1), "client release received");
        Assert.That(time.IsSessionActive, Is.True);
        float hp = player.Health.CurrentHealth;
        DamageReceiverUtility.TryApplyDamage(player.gameObject, new DamageRequest { BaseDamage = 7, RequestedDamage = 7,
            DamageType = DamageType.Ability, Attacker = client.LocalPlayer, SourceObject = player.gameObject,
            SourceNetworkObject = player.Object }, out var pending);
        Assert.That(pending.Deferred, Is.True);
        clientPress = true;
        yield return Until(() => !time.IsSessionActive && !ownTime.IsSessionActive, "client second E ends on Host and replicates");
        Assert.That(time.ActivationSequence, Is.EqualTo(sequence));
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(hp - 7).Within(.01f));
        yield return Until(() => Mathf.Abs(own.Health.CurrentHealth - (hp - 7)) < .01f, "settled health replicated");
        clientPress = false;
        yield return Until(() => !driver.PreviousButtons.IsSet(InputButton.Ability1), "release before aerial");

        var aerial = Spawn<SupportAerialAbility>("Assets/_Project_Assets/Data/PlayerAbility/Definitions/SupportAerial.asset");
        SetField(aerial, "maximumActiveDuration", 30f);
        SetField(aerial, "debugAbility", false);
        Set(player.StateMachine, "CurrentState", PlayerMovementState.GrappleAirborne);
        player.Movement.KCC.Data.IsGrounded = false;
        player.Movement.KCC.FixedData.IsGrounded = false;
        driver.Module = aerial;
        yield return Until(() => client.FindObject(aerial.Object.Id) != null, "client aerial object");
        var ownAerial = client.FindObject(aerial.Object.Id).GetComponent<SupportAerialAbility>();
        clientPress = true;
        yield return Until(() => aerial.IsAbilityActive && ownAerial.IsAbilityActive, "client aerial first press");
        clientPress = false;
        yield return Until(() => !driver.PreviousButtons.IsSet(InputButton.Ability1), "client aerial release");
        Assert.That(aerial.IsAbilityActive, Is.True);
        clientPress = true;
        yield return Until(() => !aerial.IsAbilityActive && !ownAerial.IsAbilityActive, "client aerial second press");
        Assert.That(aerial.IsCoolingDown, Is.True);
        driver.Module = null;
        clientPress = false;
    }

    private void SpawnPlayer(PlayerRef authority)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000, 100, 1000), Quaternion.identity, authority);
        runner.SetPlayerObject(authority, obj);
        player = obj.GetComponent<Player>();
        player.enabled = false;
        player.ProfessionRuntimeManager.enabled = false;
    }

    private IEnumerator Connect(bool host, string session)
    {
        var peer = new GameObject(host ? "Toggle Host" : "Toggle Client").AddComponent<NetworkRunner>();
        if (host) runner = peer; else client = peer;
        peer.ProvideInput = true;
        var events = peer.gameObject.AddComponent<NetworkEvents>();
        events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { var data = new NetInput(); data.Buttons.Set(InputButton.Ability1, !host && clientPress); input.Set(data); });
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = peer.gameObject.AddComponent<ShockwaveVerificationSceneManager>();
        manager.IsSceneTakeOverEnabled = false;
        var settings = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);
        settings.FixedRegion = "asia";
        var start = peer.StartGame(new StartGameArgs { GameMode = host ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = manager, CustomPhotonAppSettings = settings, PlayerCount = 2, IsVisible = false });
        yield return Until(() => start.IsCompleted, "Host/Client connection", 45f);
        Assert.That(start.GetAwaiter().GetResult().Ok, Is.True);
        yield return Until(() => peer.IsRunning && !peer.SceneManager.IsBusy && peer.Tick.Raw > 0, "network tick");
    }

    private static IEnumerator Until(System.Func<bool> condition, string label, float timeout = 15f)
    {
        networkStep = label;
        System.IO.File.AppendAllText("Temp/AbilityToggle/network-progress.txt", "BEGIN " + label + "\n");
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, label);
        System.IO.File.AppendAllText("Temp/AbilityToggle/network-progress.txt", "PASS " + label + "\n");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (client != null)
        {
            var close = client.Shutdown();
            float clientDeadline = Time.realtimeSinceStartup + 15f;
            while (!close.IsCompleted && Time.realtimeSinceStartup < clientDeadline) yield return null;
            client = null;
        }
        if (runner != null)
        {
            var shutdown = runner.Shutdown();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!shutdown.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (runner != null)
            {
                if (Application.isPlaying) Object.Destroy(runner.gameObject);
                else Object.DestroyImmediate(runner.gameObject);
            }
            runner = null;
        }
        Application.logMessageReceived -= CaptureNetworkError;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
    private static void CaptureNetworkError(string message, string stack, UnityEngine.LogType type)
    {
        if (type == UnityEngine.LogType.Error || type == UnityEngine.LogType.Exception)
            System.IO.File.AppendAllText("Temp/AbilityToggle/network-progress.txt", "ERROR during " + networkStep + ": " + message + "\n" + stack + "\n");
    }
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
    private static FieldInfo Field(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Flags); if (field != null) return field; }
        throw new System.MissingFieldException(name);
    }
    private static void SetField(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T GetField<T>(object target, string name) => (T)Field(target, name).GetValue(target);
}

[InitializeOnLoad]
internal sealed class AbilityToggleTestResults : UnityEditor.TestTools.TestRunner.Api.ICallbacks
{
    internal const string ActiveKey = "Purgers.AbilityToggleTestResults.Active";
    static AbilityToggleTestResults()
    {
        if (SessionState.GetBool(ActiveKey, false))
            ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>().RegisterCallbacks(new AbilityToggleTestResults());
    }
    public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor tests) { }
    public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
    public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
    public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
    {
        System.IO.Directory.CreateDirectory("Temp/AbilityToggle");
        var lines = new System.Collections.Generic.List<string>();
        Append(result, lines);
        System.IO.File.WriteAllLines("Temp/AbilityToggle/latest-tests.txt", lines);
        SessionState.SetBool(ActiveKey, false);
    }
    private static void Append(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result, System.Collections.Generic.List<string> lines)
    {
        lines.Add(result.FullName + " | " + result.ResultState + " | passed=" + result.PassCount + " failed=" + result.FailCount + " skipped=" + result.SkipCount + " | " + result.Message + " | " + result.StackTrace);
        foreach (var child in result.Children) Append(child, lines);
    }
}

/// <summary>受控 Host 固定 Tick 入口：輸入來自真實 Client 封包，隔離不相關移動／職業模擬。</summary>
public sealed class AbilityToggleNetworkDriver : SimulationBehaviour
{
    public PlayerRef InputPlayer;
    public IPlayerAbilityRuntimeModule Module;
    public NetworkButtons PreviousButtons;
    public override void FixedUpdateNetwork()
    {
        if (!Runner.IsServer || !Runner.TryGetInputForPlayer(InputPlayer, out NetInput input)) return;
        Module?.SimulateAbility(input, PreviousButtons);
        PreviousButtons = input.Buttons;
    }
}
