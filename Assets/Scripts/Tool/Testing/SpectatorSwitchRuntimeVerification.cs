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
using Purgers.GameFlow.Stage;
using Purgers.GameFlow.Transition;

/// <summary>受控三 Runner：只操作暫時 Play 物件，不載入正式場景或寫入玩家存檔。</summary>
public static class SpectatorSwitchRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    public static string Stage { get; private set; } = "";
    private static NetworkRunner host, client, extra;
    private static string session;
    private static bool aim;
    private static GameObject hiddenMenu;
    private static readonly List<string> report = new List<string>();
    private static readonly List<UnityEngine.Object> temporary = new List<UnityEngine.Object>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    private static async Task Until(Func<bool> predicate, string label)
    {
        Stage = label; float end = Time.realtimeSinceStartup + 40;
        while (!predicate()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); await Task.Delay(25); }
    }
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Private | BindingFlags.Public).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    private static StageFlowController CreateStage(NetworkRunner runner)
    {
        var root=new GameObject("Temporary wipe stage",typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        var stage=root.AddComponent<StageFlowController>(); Field(stage,"debugFlow",false);
        // 此 fixture 在最後 Client 回報前停止 Stage 模擬，避免載入正式場景。
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(1),new[]{root.GetComponent<NetworkObject>()},default);
        if(runner.IsServer) Property(stage,"Phase",StagePhase.Completed);
        return stage;
    }
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要沒有其他 Runner 的 Play Mode。");
        Status="執行中"; report.Clear(); aim=false;
        session="spectator-switch-"+Guid.NewGuid().ToString("N");
        try
        {
            hiddenMenu=GameObject.Find("GameplayHUD Canvas/Menu"); if(hiddenMenu!=null) hiddenMenu.SetActive(false);
            host=await Connect(true);
            var driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic=CreateLogic(host);
            await Until(()=>host.TryGetPlayerObject(host.LocalPlayer,out _),"host player");
            client=await Connect(false); CreateLogic(client);
            await Until(()=>client.TryGetPlayerObject(client.LocalPlayer,out _) && client.TryGetPlayerObject(host.LocalPlayer,out _),"client replicas");
            var spectator=UnityEngine.Object.FindObjectOfType<LocalPlayerSpectatorController>();
            Field(spectator,"runner",client);
            foreach(var p in UnityEngine.Object.FindObjectsOfType<Player>()) if(p.Runner==host) p.GetComponent<PlayerLocalView>().enabled=false;
            await Until(()=>PlayerAbilityQualification.FindPlayer(host,client.LocalPlayer)!=null && PlayerAbilityQualification.FindPlayer(host,client.LocalPlayer).Health.IsRescueReady && PlayerAbilityQualification.FindPlayer(client,host.LocalPlayer).Health.IsRescueReady,"health ready");
            Call(spectator,"Update");
            Check((bool)typeof(LocalPlayerSpectatorController).GetField("hasObservedLocalPlayer",Private).GetValue(spectator),"死亡前已看見本機角色");
            await Task.Delay(500);
            var victim=PlayerAbilityQualification.FindPlayer(host,client.LocalPlayer);
            await driver.AtTick(()=>{ logic.SetTeammateNameStateAuthority(host.LocalPlayer,"Host 名稱"); Check(victim.Health.KillUnrescuable(),"權威正式死亡入口成功"); });
            await Until(()=>spectator.IsSpectating && spectator.SpectatedPlayer==host.LocalPlayer,"death enters spectator");
            Check(spectator.Candidates.Count==1,"死亡者排除，單一候選觀戰");
            Check(spectator.SwitchTarget(1) && spectator.SpectatedPlayer==host.LocalPlayer,"單一候選切換保持原玩家");
            extra=await Connect(false); CreateLogic(extra);
            await Until(()=>spectator.Candidates.Count==2,"late join candidate");
            Check(spectator.SpectatedPlayer==host.LocalPlayer,"晚加入不打斷目前目標");
            foreach(var p in UnityEngine.Object.FindObjectsOfType<Player>()) if(p.Runner!=client) p.GetComponent<PlayerLocalView>().enabled=false;
            var extraId=extra.LocalPlayer;
            await driver.AtTick(()=>logic.SetTeammateNameStateAuthority(extraId,"Teammate ID"));
            Check(spectator.SwitchTarget(1) && spectator.SpectatedPlayer==extraId,"E 入口切下一位");
            Check(spectator.SwitchTarget(-1) && spectator.SpectatedPlayer==host.LocalPlayer,"Q 入口切上一位");
            Check(spectator.SwitchTarget(-1) && spectator.SpectatedPlayer==extraId,"向前循環到最後一位");
            var source=PlayerAbilityQualification.FindPlayer(host,extraId);
            await driver.AtTick(()=>source.Health.ReceiveDamage(new DamageRequest{RequestedDamage=10000,BaseDamage=10000,DamageType=DamageType.Environment}));
            await Until(()=>PlayerAbilityQualification.FindPlayer(client,extraId).Health.IsDowned,"replicated downed");
            Check(spectator.SpectatedPlayer==extraId && spectator.Candidates.Count==2,"倒地仍可觀戰且不強制換目標");
            Check(!PlayerAbilityQualification.FindPlayer(client,extraId).Health.KillUnrescuable(),"Client 不可自行決定正式死亡");
            var hud=UnityEngine.Object.FindObjectOfType<LocalSpectatorHUD>(); Call(hud,"Update");
            var labels=hud.GetComponentsInChildren<TMPro.TMP_Text>().Select(t=>t.text).ToArray();
            Check(labels.Contains("Host 名稱") && labels.Contains("Teammate ID") && labels.Contains("倒地"),"正式 HUD 讀同步玩家名稱及倒地狀態");
            Check(hud.GetComponentsInChildren<UnityEngine.UI.Image>().Any(i=>i.name=="Portrait" && i.sprite!=null),"共用頭像已接線");
            using(Purgers.GameFlow.Control.LocalPlayerControl.Acquire(Purgers.GameFlow.Control.PlayerControlMask.AllInput,"verification"))
                Check(!spectator.SwitchTarget(1),"控制鎖阻擋切換");
            await driver.AtTick(()=>source.Health.KillUnrescuable());
            await Until(()=>spectator.Candidates.Count==1 && spectator.SpectatedPlayer==host.LocalPlayer,"dead target fallback");
            Check(spectator.Candidates.Count==1,"目標正式死亡自動改看下一位");
            await extra.Shutdown(); extra=null;
            Check(spectator.SpectatedPlayer==host.LocalPlayer,"無效／離線成員不留在名單");

            // 真正 Networked pending + Client RPC；刻意延後 Client 回報，禁止切正式關卡。
            StageFlowController stage=null;
            await driver.AtTick(()=>stage=CreateStage(host));
            var replica=CreateStage(client);
            await Until(()=>replica.IsNetworkReady,"stage replica");
            Field(replica,"nextWipeReportTime",float.PositiveInfinity);
            await driver.AtTick(()=>{ Property(stage,"Phase",StagePhase.Failed); Property(stage,"WipeTransitionPending",(NetworkBool)true); });
            var ht=LocalSceneTransition.Ensure(host); var ct=LocalSceneTransition.Ensure(client);
            await Until(()=>ht.IsWipePreparing && ct.IsWipePreparing,"both begin networked wipe");
            float started=Time.realtimeSinceStartup;
            await Task.Delay(700);
            Check(ct.Fade.Alpha>0 && ct.Fade.Alpha<1 && GameplayAudioTransitionGain.Gain>0 && GameplayAudioTransitionGain.Gain<1,"黑幕與聲音同時漸變");
            await Until(()=>ct.IsWipeReadyToLoad && ht.IsWipeReadyToLoad,"fade and two second hold");
            Check(Time.realtimeSinceStartup-started>=3.3f,"淡出加完成後停留，未提前準備切場");
            await Task.Delay(600);
            var acknowledgements=(HashSet<PlayerRef>)typeof(StageFlowController).GetField("wipeReadyPlayers",Private).GetValue(stage);
            Check(stage.Phase==StagePhase.Failed && acknowledgements.Contains(host.LocalPlayer) && !acknowledgements.Contains(client.LocalPlayer),"Host 等待較晚 Client 回報");
            Check(!spectator.SwitchTarget(1),"全滅黑幕中禁止切換");
            // 停止自動 Tick，仍用正式 RPC 收到最後一端回報，不觸發實際 LoadScene。
            host.RemoveGlobal(driver);
            stage.enabled=false;
            // 保留 Failed 供正式 RPC 驗證，同時停止 FixedUpdateNetwork，不觸發 LoadScene。
            host.SetIsSimulated(stage.Object,false);
            Field(replica,"nextWipeReportTime",0f);
            await Until(()=>acknowledgements.Contains(client.LocalPlayer),"client reliable acknowledgement");
            Check(acknowledgements.Count==2,"每位仍連線玩家以自己的 PlayerRef 回報");
            Property(stage,"WipeTransitionPending",(NetworkBool)false);
            Property(stage,"Phase",StagePhase.Completed);
            replica.enabled=false;
            ht.Cancel(); ct.Cancel();
            Check(GameplayAudioTransitionGain.Gain==1 && !ct.IsTransitioning && ct.Fade.Alpha==0,"取消清除黑幕、音量衰減與輸入鎖");
            // 同一 Runner 既有玩家／相機已就緒，驗證 BeginLoad -> SceneLoadDone 恢復路徑。
            ht.BeginWipe(); await Until(()=>ht.IsWipeReadyToLoad,"second wipe");
            ht.BeginLoad(); await Task.Delay(250);
            Check(ht.Fade.Alpha==1 && GameplayAudioTransitionGain.Gain==0,"載入尚未完成持續全黑靜音");
            ht.SceneLoadDone(); float reveal=Time.realtimeSinceStartup;
            await Until(()=>!ht.IsTransitioning,"ready camera reveal");
            Check(Time.realtimeSinceStartup-reveal>=1.45f && GameplayAudioTransitionGain.Gain==1 && ht.Fade.Alpha==0,"玩家相機就緒後 1.5 秒恢復畫面聲音");
            Status="通過";
        }
        catch(Exception e){Status="失敗";report.Add("FAIL "+Stage+": "+e);Debug.LogException(e);}
        finally
        {
            if(extra!=null){await extra.Shutdown();extra=null;}
            if(client!=null){await client.Shutdown();client=null;}
            if(host!=null){await host.Shutdown();host=null;}
            foreach(var item in temporary) if(item!=null) UnityEngine.Object.Destroy(item); temporary.Clear();
            if(hiddenMenu!=null) hiddenMenu.SetActive(true);
            Directory.CreateDirectory("Temp/SpectatorSwitchVerification");
            File.WriteAllLines("Temp/SpectatorSwitchVerification/host-client.txt",report);
        }
    }
    private static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root = new GameObject("Temporary spectator GameLogic", typeof(NetworkObject)); temporary.Add(root); runner.MoveToRunnerScene(root);
        var logic = root.AddComponent<GameLogic>();
        Field(logic, "playerPrefab", NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        Field(logic, "debugPlayerLifecycle", false);
        Field(logic, "useTeammateRescueOutsideSafeHouse", true);
        Field(logic, "fallbackSpawnPosition", new Vector3(1200, 1200, 1200));
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); runner.MoveToRunnerScene(floor);
        floor.transform.position = new Vector3(1200, 1198, 1200); floor.transform.localScale = new Vector3(100, 1, 100);
        floor.layer = LayerMask.NameToLayer("Ground") < 0 ? 0 : LayerMask.NameToLayer("Ground");
        return logic;
    }
    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Spectator host" : "Spectator client").AddComponent<NetworkRunner>(); temporary.Add(runner.gameObject);
        runner.ProvideInput = true;
        var events = runner.gameObject.AddComponent<NetworkEvents>(); events.OnInput = new NetworkEvents.InputEvent();
        events.OnInput.AddListener((r, input) => { var data = default(NetInput); data.Buttons.Set(InputButton.Aim, server && aim); input.Set(data); });
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global)); config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var scene = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>(); scene.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings(); Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs { GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = scene, CustomPhotonAppSettings = app, PlayerCount = 4, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner tick");
        return runner;
    }
}
#endif
