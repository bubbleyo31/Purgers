#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Fusion;
using Purgers.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 明確命令列啟用的雙程序鈎索能量驗證。使用獨立存檔及輸出目錄，不修改 Scene／Prefab。
/// 驗證權威資源、晚加入、升級模式、HUD、重生及切場；受控扣點不等同自然出鈎物理驗收。
/// </summary>
public sealed class GrappleEnergyNetworkVerification : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private NetworkRunner runner;
    private NetworkRunner runnerPrefab;
    private string role, session, output;
    private bool failed;
    private float started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--grapple-energy-test");
        if (index < 0 || index + 3 >= args.Length || FindObjectOfType<GrappleEnergyNetworkVerification>() != null) return;
        var menu = FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if (menu == null) return;
        var probe = new GameObject("GrappleEnergyVerification").AddComponent<GrappleEnergyNetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.role = args[index + 1];
        probe.session = args[index + 2];
        probe.output = Path.GetFullPath(args[index + 3]);
        probe.runnerPrefab = (NetworkRunner)menu.GetType().GetField("networkRunnerPrefab", Private).GetValue(menu);
        Directory.CreateDirectory(probe.output);
        Application.runInBackground = true;
        probe.started = Time.realtimeSinceStartup;
        probe.StartCoroutine(probe.Run());
    }

    private void Update()
    {
        if (!failed && Time.realtimeSinceStartup - started > 240f) Fail("timeout");
    }

    private async System.Threading.Tasks.Task<bool> Connect()
    {
        runner = Instantiate(runnerPrefab);
        runner.ProvideInput = true;
        var context = runner.gameObject.AddComponent<GameSaveRuntimeContext>();
        if (role == "Host")
        {
            var repository = new JsonGameSaveRepository(Path.Combine(output, "Saves"));
            var save = repository.CreateNew("Grapple energy isolated verification");
            if (!save.Success) throw new Exception(save.Error);
            context.InitializeHost(repository, save.Value);
        }
        else context.InitializeClientReadOnly();
        var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
        manager.IsSceneTakeOverEnabled = false;
        var scene = new NetworkSceneInfo();
        scene.AddSceneRef(SceneRef.FromIndex(1), LoadSceneMode.Additive);
        var settings = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);
        settings.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs {
            CustomPhotonAppSettings = settings, GameMode = role == "Host" ? GameMode.Host : GameMode.Client,
            SessionName = session, Scene = role == "Host" ? scene : (NetworkSceneInfo?)null,
            SceneManager = manager, PlayerCount = 2 });
        Log("StartGame=" + result.Ok + ", reason=" + result.ShutdownReason);
        return result.Ok;
    }

    private IEnumerator Run()
    {
        var connect = Connect();
        while (!connect.IsCompleted) yield return null;
        if (connect.IsFaulted || !connect.Result) { Fail("connect failed"); yield break; }
        var menu = FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);
        if (menu != null) menu.gameObject.SetActive(false);
        if (role == "Host") yield return Host();
        else yield return Client();
    }

    private Player[] Players()
    {
        if (runner == null || !runner.IsRunning) return new Player[0];
        return runner.ActivePlayers.Select(p => runner.TryGetPlayerObject(p, out NetworkObject obj) && obj != null && obj.IsValid
            ? obj.GetComponent<Player>() : null).Where(p => p != null && p.GrappleCharges.MaximumEnergy > 0f).ToArray();
    }
    private Player Own() => Players().FirstOrDefault(p => p.Object.InputAuthority == runner.LocalPlayer);
    private Player Other() => Players().FirstOrDefault(p => p.Object.InputAuthority != runner.LocalPlayer);
    private string FileFor(string name) => Path.Combine(output, name);
    private void Log(string text) => File.AppendAllText(FileFor(role + ".log"), DateTime.UtcNow.ToString("O") + " " + text + "\n");
    private void Fail(string text) { if (failed) return; failed = true; Log("FAIL " + text); File.WriteAllText(FileFor(role + ".failed"), text); Application.Quit(2); }
    private void Check(bool value, string message) { if (!value) { Fail(message); throw new InvalidOperationException(message); } }
    private static bool Near(float actual, float expected) => Mathf.Abs(actual - expected) < 0.01f;

    private IEnumerator Publish(int step, float hostEnergy, float clientEnergy, float hostMax, float clientMax)
    {
        yield return new WaitForSecondsRealtime(1f);
        Check(Near(Own().GrappleCharges.CurrentEnergy, hostEnergy), "Host own energy step " + step);
        Check(Near(Other().GrappleCharges.CurrentEnergy, clientEnergy), "Host remote energy step " + step);
        File.WriteAllText(FileFor("step." + step), string.Join("|", step, hostEnergy, clientEnergy, hostMax, clientMax));
        File.WriteAllText(FileFor("step." + step + ".ready"), "1");
        Log("STEP " + step + " Host=" + hostEnergy + "/" + hostMax + " Client=" + clientEnergy + "/" + clientMax);
        while (!File.Exists(FileFor("Client." + step + ".pass"))) yield return null;
    }

    private IEnumerator Host()
    {
        while (Own() == null) yield return null;
        Check(Own().GrappleCharges.ConsumeCharge(), "initial launch");
        // Client 延遲啟動；晚加入必須看到已消耗的 49，而不是本地初始化的 50。
        while (Players().Length < 2) yield return null;
        yield return Publish(0, 49f, 50f, 50f, 50f);
        Own().GrappleCharges.ConsumePullEnergy(2.5f);
        yield return Publish(1, 46.5f, 50f, 50f, 50f);
        GameLogic logic = GameLogic.GetPrimaryForRunner(runner);
        Check(logic.TryAwardPlayerExperience(runner.LocalPlayer, 5), "full level up");
        yield return Publish(2, 75f, 50f, 75f, 50f);
        var other = Other();
        other.GrappleCharges.ConsumePullEnergy(40f);
        typeof(PlayerGrappleCharges).GetField("levelUpMode", Private).SetValue(other.GrappleCharges, GrappleEnergyLevelUpMode.AddCapacityDifference);
        Check(logic.TryAwardPlayerExperience(other.Object.InputAuthority, 5), "difference level up");
        yield return Publish(3, 75f, 35f, 75f, 75f);
        yield return new WaitForSecondsRealtime(3f);
        Own().GrappleCharges.RestoreChargeFromKill();
        Other().GrappleCharges.RestoreChargeFromKill();
        yield return Publish(4, 75f, 35f, 75f, 75f);
        var energy = Own().GrappleCharges;
        energy.ConsumePullEnergy(74.5f);
        Check(!energy.ConsumeCharge() && Near(energy.CurrentEnergy, .5f), "insufficient launch must not spend");
        energy.ConsumePullEnergy(.5f);
        Check(!energy.ConsumeCharge(), "empty launch rejected");
        yield return Publish(5, 0f, 35f, 75f, 75f);
        NetworkId oldId = Own().Object.Id;
        Own().Health.ReceiveDamage(new DamageRequest { RequestedDamage = 10000f, BaseDamage = 10000f, DamageType = DamageType.Environment });
        while (Own() == null || Own().Object.Id == oldId) yield return null;
        yield return Publish(6, 75f, 35f, 75f, 75f);
        foreach (var p in Players()) p.GrappleCharges.ConsumePullEnergy(10f);
        logic.PrepareForAuthoritativeSceneTransition();
        runner.LoadScene(SceneRef.FromIndex(2), LoadSceneMode.Single);
        yield return new WaitForSecondsRealtime(5f);
        while (Players().Length < 2 || !Near(Own().GrappleCharges.CurrentEnergy, 75f) || !Near(Other().GrappleCharges.CurrentEnergy, 75f)) yield return null;
        yield return Publish(7, 75f, 75f, 75f, 75f);
        foreach (var p in Players()) p.GrappleCharges.ConsumePullEnergy(10f);
        logic.PrepareForAuthoritativeSceneTransition();
        runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Single);
        yield return new WaitForSecondsRealtime(5f);
        while (Players().Length < 2 || !Near(Own().GrappleCharges.CurrentEnergy, 75f) || !Near(Other().GrappleCharges.CurrentEnergy, 75f)) yield return null;
        yield return Publish(8, 75f, 75f, 75f, 75f);
        File.WriteAllText(FileFor("reconnect"), "1");
        while (Players().Length == 2) yield return null;
        while (Players().Length < 2 || Other().GrappleCharges.AppliedPlayerLevel != 1) yield return null;
        yield return Publish(9, 75f, 50f, 75f, 50f);
        File.WriteAllText(FileFor("Host.complete"), "PASS");
        Log("PASS all controlled network checks");
        yield return new WaitForSecondsRealtime(3f);
        Application.Quit(0);
    }

    private IEnumerator Client()
    {
        int lastStep = -1;
        while (lastStep < 9)
        {
            if (lastStep == 8 && File.Exists(FileFor("reconnect")))
            {
                var shutdown = runner.Shutdown();
                while (!shutdown.IsCompleted) yield return null;
                yield return new WaitForSecondsRealtime(2f);
                var reconnect = Connect();
                while (!reconnect.IsCompleted) yield return null;
                Check(!reconnect.IsFaulted && reconnect.Result, "reconnect");
                lastStep = -2; // 仍只接受 step 9，不能重做舊 step。
            }
            int nextStep = lastStep == -2 ? 9 : lastStep + 1;
            if (!File.Exists(FileFor("step." + nextStep + ".ready")) || Players().Length != 2) { yield return null; continue; }
            string[] parts = File.ReadAllText(FileFor("step." + nextStep)).Split('|');
            int step = int.Parse(parts[0]);
            if (step <= lastStep || (lastStep == -2 && step != 9)) { yield return null; continue; }
            float hostEnergy = float.Parse(parts[1]), clientEnergy = float.Parse(parts[2]);
            float hostMax = float.Parse(parts[3]), clientMax = float.Parse(parts[4]);
            var own = Own().GrappleCharges; var other = Other().GrappleCharges;
            if (!Near(other.CurrentEnergy, hostEnergy) || !Near(own.CurrentEnergy, clientEnergy) ||
                !Near(other.MaximumEnergy, hostMax) || !Near(own.MaximumEnergy, clientMax)) { yield return null; continue; }
            Check(Own().Object.HasInputAuthority && !Own().Object.HasStateAuthority, "client authority");
            Check(!other.ConsumeCharge(), "proxy must not spend");
            other.RefillForSceneTransitionStateAuthority();
            Check(Near(other.CurrentEnergy, hostEnergy), "client must not refill remote state");
            var hud = FindObjectOfType<LocalPlayerBattleAbilityHUD>();
            // SafeHouse 沒有戰鬥圓環；只在已掛載 HUD 的 Game 場景核對顯示。
            var fill = hud != null ? (Image)typeof(LocalPlayerBattleAbilityHUD).GetField("grappleFill", Private).GetValue(hud) : null;
            if (step == 7 && fill == null) { yield return null; continue; }
            if (fill != null && !Near(fill.fillAmount, clientEnergy / clientMax)) { yield return null; continue; }
            Log("STEP " + step + " replicated; ownHUD=" + (fill != null ? fill.fillAmount.ToString() : "not mounted in SafeHouse") + "; Host=" + hostEnergy + "; Client=" + clientEnergy);
            File.WriteAllText(FileFor("Client." + step + ".pass"), "PASS");
            lastStep = step;
            yield return null;
        }
        File.WriteAllText(FileFor("Client.complete"), "PASS");
        Log("PASS all controlled network checks");
        yield return new WaitForSecondsRealtime(2f);
        Application.Quit(0);
    }
}
#endif
