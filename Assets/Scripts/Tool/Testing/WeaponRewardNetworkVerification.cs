#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>明確命令列啟動的獨立 Host/Client 驗證。隔離存檔，不改正式資產。</summary>
public sealed class WeaponRewardNetworkVerification : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private NetworkRunner runner, runnerPrefab;
    private string role, session, output;
    private bool finished;
    private float started;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var args=Environment.GetCommandLineArgs();
        int index=Array.IndexOf(args,"--weapon-reward-test");
        if(index<0 || index+3>=args.Length || FindObjectOfType<WeaponRewardNetworkVerification>()!=null)return;
        var menu=FindObjectOfType<MultiClimb.Menu.MenuConnectionBehaviour>();
        if(menu==null)return;
        var probe=new GameObject("WeaponRewardNetworkVerification").AddComponent<WeaponRewardNetworkVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.role=args[index+1];probe.session=args[index+2];probe.output=Path.GetFullPath(args[index+3]);
        probe.runnerPrefab=(NetworkRunner)menu.GetType().GetField("networkRunnerPrefab",Private).GetValue(menu);
        Directory.CreateDirectory(probe.output);Application.runInBackground=true;probe.started=Time.realtimeSinceStartup;probe.Run();
    }
    private void Update(){if(!finished && Time.realtimeSinceStartup-started>240)Fail("global timeout");}
    private string FileFor(string name)=>Path.Combine(output,name);
    private void Mark(string name)=>File.WriteAllText(FileFor(name),"PASS");
    private bool Has(string name)=>File.Exists(FileFor(name));
    private void Log(string text)=>File.AppendAllText(FileFor(role+".log"),DateTime.UtcNow.ToString("O")+" "+text+"\n");
    private void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);Log("PASS "+message);}
    private void Fail(string message){if(finished)return;finished=true;Log("FAIL "+message);File.WriteAllText(FileFor(role+".failed"),message);Application.Quit(2);}
    private async Task Wait(Func<bool> predicate,string label)
    {
        float deadline=Time.realtimeSinceStartup+50;
        while(!predicate()){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(label);await Task.Delay(50);}
    }
    private Player[] Players()=>runner==null || !runner.IsRunning ? Array.Empty<Player>() :
        runner.ActivePlayers.Select(p=>runner.TryGetPlayerObject(p,out var o)&&o!=null&&o.IsValid?o.GetComponent<Player>():null)
        .Where(p=>p!=null&&p.Health!=null&&p.AbilityRuntimeManager.LoadoutRevision>0&&p.ProfessionRuntimeManager.CurrentRuntimeObject!=null).ToArray();
    private Player Own()=>Players().FirstOrDefault(p=>p.Object.InputAuthority==runner.LocalPlayer);
    private Player Other()=>Players().FirstOrDefault(p=>p.Object.InputAuthority!=runner.LocalPlayer);
    private GameLogic Logic=>GameLogic.GetPrimaryForRunner(runner);
    private async Task Connect()
    {
        runner=Instantiate(runnerPrefab);runner.ProvideInput=true;
        var context=runner.gameObject.AddComponent<GameSaveRuntimeContext>();
        if(role=="Host"){
            var repo=new JsonGameSaveRepository(Path.Combine(output,"Saves"));
            var result=repo.CreateNew("Weapon network isolated verification");Check(result.Success,"isolated save created");
            context.InitializeHost(repo,result.Value);
        }else context.InitializeClientReadOnly();
        var manager=runner.gameObject.AddComponent<NetworkSceneManagerDefault>();manager.IsSceneTakeOverEnabled=false;
        var scene=new NetworkSceneInfo();scene.AddSceneRef(SceneRef.FromIndex(1),LoadSceneMode.Additive);
        var settings=new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(settings);settings.FixedRegion="asia";
        var started=await runner.StartGame(new StartGameArgs{CustomPhotonAppSettings=settings,
            GameMode=role=="Host"?GameMode.Host:GameMode.Client,SessionName=session,
            Scene=role=="Host"?scene:(NetworkSceneInfo?)null,SceneManager=manager,PlayerCount=2});
        Check(started.Ok,"StartGame "+started.ShutdownReason);
        var menu=FindObjectOfType<Fusion.Menu.FusionMenuUIMain>(true);if(menu!=null)menu.gameObject.SetActive(false);
        await Wait(()=>Own()!=null,"local player");await Task.Delay(200);
    }
    private void DraftWeapon(Player player, PlayerProfessionType target)
    {
        var catalog=ScriptableObject.CreateInstance<PlayerRewardCatalog>();
        var reward=Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog").Rewards.First(r=>r.IsImplementedWeaponReward&&r.WeaponProfession==target);
        typeof(PlayerRewardCatalog).GetField("rewards",Private).SetValue(catalog,new[]{reward});
        typeof(GameLogic).GetField("rewardCatalog",Private).SetValue(Logic,catalog);
        Check((bool)typeof(GameLogic).GetMethod("TryDebugLevelUpStateAuthority",Private).Invoke(Logic,new object[]{player.Object.InputAuthority}),"Host creates weapon draft");
    }
    private async void Run()
    {
        try{
            if(role!="Host")await Wait(()=>Has("Host.ready"),"late join ready");
            await Connect();
            if(role=="Host")await Host();else await Client();
            Mark(role+".passed");finished=true;
            if(role=="Host")await Wait(()=>Has("Client.passed"),"client completion");
            await runner.Shutdown();Application.Quit(0);
        }catch(Exception exception){Fail(exception.ToString());}
    }
    private async Task Host()
    {
        DraftWeapon(Own(),PlayerProfessionType.Tank);
        Check(Logic.TryGetPlayerRewardDraft(runner.LocalPlayer,out var draft),"Host draft readable");
        Check(Logic.TryClaimRewardStateAuthority(runner.LocalPlayer,draft.DraftRevision,0,out _),"Host claims Tank");
        Mark("Host.ready");
        await Wait(()=>Other()!=null && Has("Client.late"),"late client snapshot");
        DraftWeapon(Other(),PlayerProfessionType.Support);Mark("claim");
        await Wait(()=>Other().Profession.IsSupport && Has("Client.claim"),"remote Support claim");
        Check(Logic.TryGetPlayerExperience(Other().Object.InputAuthority,out var xp)&&xp.PendingRewards==0,"remote reward consumed exactly once");
        var old=Other().Object.Id;
        Other().Health.ReceiveDamage(new DamageRequest{BaseDamage=100000,RequestedDamage=100000,DamageType=DamageType.Environment});
        await Wait(()=>Other()!=null&&Other().Object.Id!=old&&Other().Health.IsAlive,"remote respawn");
        Check(Other().Profession.IsSupport,"remote weapon survives death");Mark("respawn");
        await Wait(()=>Has("Client.respawn"),"client respawn observed");
        Logic.PrepareForAuthoritativeSceneTransition();await runner.LoadScene(SceneRef.FromIndex(2),LoadSceneMode.Single);
        await Wait(()=>Players().Length==2&&!runner.SceneManager.IsBusy,"Game loaded");await Task.Delay(1500);
        Check(Own().Profession.IsTank&&Other().Profession.IsSupport,"both weapons survive Game transition");Mark("game");
        await Wait(()=>Has("Client.game"),"client Game observed");
        Logic.PrepareForAuthoritativeSceneTransition();await runner.LoadScene(SceneRef.FromIndex(1),LoadSceneMode.Single);
        await Wait(()=>Players().Length==2&&!runner.SceneManager.IsBusy,"SafeHouse loaded");await Task.Delay(500);Mark("safe");
        await Wait(()=>Has("Client.safe"),"client SafeHouse observed");Mark("reconnect");
        await Wait(()=>Players().Length==1,"client disconnected");
        await Wait(()=>Players().Length==2&&Has("Client.reconnected"),"client reconnected");
        Check(Own().Profession.IsTank&&Other().Profession.IsAttack,"Host retains weapon; rejoining guest follows new-player policy");
        var context=runner.GetComponent<GameSaveRuntimeContext>();
        Check(Logic.TryWriteHostExperienceToSave(context.ActiveSave),"write Host snapshot");
        Check(PlayerRewardSaveBridge.ReadHost(context.ActiveSave).EquippedWeaponId=="weapon.tank","Host saved Tank independently of guest");
        Logic.ResetPlayerExperiencesForFailedRun();await Task.Delay(300);
        Check(Players().All(p=>p.Profession.IsAttack),"failed run resets all players to Attack");Mark("reset");
        await Wait(()=>Has("Client.reset"),"client reset observed");
    }
    private async Task Client()
    {
        await Wait(()=>Other()!=null&&Other().Profession.IsTank,"late join Tank");
        Check(Own().Profession.IsAttack,"new guest starts with Attack");
        Check(!Own().ProfessionRuntimeManager.TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Support,out _),"Client cannot directly change weapon");
        Mark("Client.late");
        await Wait(()=>Has("claim")&&Logic.TryGetPlayerRewardDraft(runner.LocalPlayer,out var d)&&d.ChoiceCount==1,"replicated weapon draft");
        Logic.TryGetPlayerRewardDraft(runner.LocalPlayer,out var draft);
        int revision=Own().AbilityRuntimeManager.LoadoutRevision;
        var abilityIds=Enumerable.Range(0,PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots)
            .Select(i=>Own().AbilityRuntimeManager.GetEquippedRuntimeAtSlot(i)).Where(r=>r!=null).Select(r=>r.Object.Id).ToArray();
        Logic.RPC_RequestRewardChoice(draft.DraftRevision,0);
        await Wait(()=>Own().Profession.IsSupport&&Own().ProfessionRuntimeManager.CurrentRuntimeProfession==PlayerProfessionType.Support,"Support result replicated");
        Logic.RPC_RequestRewardChoice(draft.DraftRevision,0);await Task.Delay(300);
        Check(Own().AbilityRuntimeManager.LoadoutRevision==revision,"Client Q/E revision unchanged");
        foreach(var id in abilityIds)Check(runner.TryFindObject(id,out _),"Client Q/E identity preserved "+id);
        var old=Own().Object.Id;Mark("Client.claim");
        await Wait(()=>Has("respawn")&&Own()!=null&&Own().Object.Id!=old,"Client respawn");
        Check(Own().Profession.IsSupport,"Client sees Support after respawn");old=Own().Object.Id;Mark("Client.respawn");
        await Wait(()=>Has("game")&&Own()!=null&&Own().Object.Id!=old&&!runner.SceneManager.IsBusy,"Client Game");
        Check(Own().Profession.IsSupport&&Other().Profession.IsTank,"Client sees both weapons in Game");old=Own().Object.Id;Mark("Client.game");
        await Wait(()=>Has("safe")&&Own()!=null&&Own().Object.Id!=old&&!runner.SceneManager.IsBusy,"Client SafeHouse");
        Check(Own().Profession.IsSupport,"Client weapon persists on return");Mark("Client.safe");
        await Wait(()=>Has("reconnect"),"reconnect request");await runner.Shutdown();await Task.Delay(1500);await Connect();
        await Wait(()=>Other()!=null,"Host after reconnect");Check(Own().Profession.IsAttack&&Other().Profession.IsTank,"reconnected snapshots");Mark("Client.reconnected");
        await Wait(()=>Has("reset")&&Players().All(p=>p.Profession.IsAttack),"failure reset replicated");Mark("Client.reset");
    }
}
#endif
