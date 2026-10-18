using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.GameFlow.Control;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;
using TMPro;

/// <summary>隔離 Host／Client／晚加入與正式 UI 讀值；不保存遊戲進度或 Scene，不派送桌面輸入。</summary>
public static class TeamInfoNetworkVerification
{
    public static string Status { get; private set; }="Not started";
    public static string Stage { get; private set; }="";
    static readonly List<string> report=new List<string>();
    static readonly List<NetworkRunner> runners=new List<NetworkRunner>();
    static readonly List<GameObject> temporary=new List<GameObject>();
    static NetInput data;
    static string session;
    static readonly Vector3 Origin=new Vector3(7000,7000,7000);
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    public static async void Begin()
    {
        if(!Application.isPlaying || NetworkRunner.Instances.Any(r=>r.IsRunning)) throw new InvalidOperationException("需無 Runner 的隔離 Play Mode");
        Status="Running";report.Clear();runners.Clear();temporary.Clear();data=default;session="team-info-"+Guid.NewGuid().ToString("N");
        try
        {
            var host=await Connect(true);var logic=CreateLogic(host);
            var driver=host.gameObject.AddComponent<StageTimerPauseVerificationDriver>();host.AddGlobal(driver);
            await Until(()=>host.TryGetPlayerObject(host.LocalPlayer,out var obj)&&obj.GetComponent<PlayerActionGate>().IsFusionSpawned,"Host player ready");
            var client=await Connect(false);var remoteLogic=CreateLogic(client);
            await Until(()=>host.TryGetPlayerObject(client.LocalPlayer,out var obj)&&obj.GetComponent<PlayerAbilityRuntimeManager>().LoadoutRevision>0,"Client player on Host");
            var own=host.GetPlayerObject(host.LocalPlayer);var target=host.GetPlayerObject(client.LocalPlayer);var player=target.GetComponent<Player>();
            await Until(()=>client.TryFindObject(target.Id,out var obj)&&obj.GetComponent<PlayerActionGate>().IsFusionSpawned,"Client replica ready");
            var remote=client.FindObject(target.Id);
            Check(target.HasStateAuthority&&!target.HasInputAuthority&&remote.HasInputAuthority&&!remote.HasStateAuthority,"authority and input are separate");
            var catalog=TeamInfoPanelSettings.Load().catalog;
            var ability=catalog.Rewards.First(r=>r!=null&&r.AbilityDefinition!=null&&r.AbilityDefinition.RuntimePrefab.GetComponent<PlayerActiveAbilityBase>()!=null).AbilityDefinition;
            await driver.AtTick(()=>{
                Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(ability,out var error),"equip owned active ability: "+error);
                logic.SetTeammateNameStateAuthority(client.LocalPlayer,"隊友同步測試");
                SetProperty(player.Health,"CurrentHealth",60f);
                var reward=catalog.FindGrowth(PlayerGrowthKind.ReloadSpeed);
                var args=new object[]{client.LocalPlayer,player,reward,null};
                Check((bool)typeof(GameLogic).GetMethod("TryGrantGrowth",Flags).Invoke(logic,args),"grant growth through existing owner");
            });
            await Until(()=>remote.GetComponent<PlayerAbilityRuntimeManager>().GetEquippedAbilityIds().Contains(ability.AbilityId),"owned ability replicates to Client");
            var runtime=Find(player,ability);var module=runtime.GetComponent<PlayerActiveAbilityBase>();
            await driver.AtTick(()=>{
                typeof(PlayerActiveAbilityBase).GetField("cooldownSeconds",Flags).SetValue(module,60f);
                typeof(PlayerActiveAbilityBase).GetMethod("FinishAbility",Flags).Invoke(module,null);
            });
            var remoteRuntime=Find(remote.GetComponent<Player>(),ability);
            await Until(()=>remoteRuntime!=null&&remoteRuntime.TryReadPresentation(out var value)&&value.Remaining>0,"cooldown reaches input authority");
            runtime.TryReadPresentation(out var initial);remoteRuntime.TryReadPresentation(out var replicated);
            Check(initial.Duration==60&&replicated.Duration==60&&Mathf.Abs(initial.Remaining-replicated.Remaining)<.25f,"cooldown seconds and denominator agree");
            Check(!remote.GetComponent<PlayerAbilityRuntimeManager>().TryEquipRewardAbilityStateAuthority(ability,out _),"Client cannot mutate loadout authority");
            await Until(()=>remoteLogic.TryGetTeammateIdentity(client.LocalPlayer,out var identity)&&identity.Name.ToString()=="隊友同步測試"&&
                remoteLogic.GetPlayerGrowth(client.LocalPlayer).Rank(PlayerGrowthKind.ReloadSpeed)==1&&Mathf.Abs(remote.GetComponent<PlayerHealth>().CurrentHealth-60)<.01f,"name HP and growth snapshots agree");

            // Raw client intent deliberately carries forbidden buttons: production Player must filter on Host.
            var kcc=target.GetComponent<Fusion.Addons.KCC.KCC>();float yaw=kcc.Data.LookYaw;Vector3 position=target.transform.position;
            data=new NetInput{Direction=Vector2.up,LookDelta=new Vector2(30,40),BlockedControls=PlayerControlMask.Combat|PlayerControlMask.Look};
            data.Buttons.Set(InputButton.Fire,true);data.Buttons.Set(InputButton.Ability1,true);data.Buttons.Set(InputButton.QuickAction,true);data.Buttons.Set(InputButton.Grapple,true);
            int tick=host.Tick.Raw;await Until(()=>host.Tick.Raw>=tick+20,"20 live ticks with team-window control mask");
            Check(Vector3.Distance(new Vector3(position.x,0,position.z),new Vector3(target.transform.position.x,0,target.transform.position.z))>.01f,"movement continues while team window blocks combat");
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw,kcc.Data.LookYaw))<.01f,"Host rejects blocked look input");
            var buttons=(NetworkButtons)typeof(Player).GetProperty("PreviousButtons",Flags).GetValue(player);
            Check(!buttons.IsSet(InputButton.Fire)&&!buttons.IsSet(InputButton.Ability1)&&!buttons.IsSet(InputButton.Grapple)&&!buttons.IsSet(InputButton.QuickAction),"Host filters combat and skill buttons");
            runtime.TryReadPresentation(out var after);Check(after.Remaining<initial.Remaining,"cooldown continues behind panel");data=default;

            await driver.AtTick(()=>Check(player.GetComponent<PlayerProfessionRuntimeManager>().TryEquipRewardWeaponStateAuthority(PlayerProfessionType.Support,out _),"change weapon through existing owner"));
            await Until(()=>remote.GetComponent<PlayerProfessionRuntimeManager>().CurrentRuntimeProfession==PlayerProfessionType.Support,"weapon change replicates");
            CheckPanel(client,remoteLogic,"隊友同步測試",true);
            var late=await Connect(false);var lateLogic=CreateLogic(late);
            await Until(()=>late.TryFindObject(target.Id,out var obj)&&Find(obj.GetComponent<Player>(),ability)!=null,"late join receives equipped runtime");
            var lateRuntime=Find(late.FindObject(target.Id).GetComponent<Player>(),ability);
            await Until(()=>lateRuntime.TryReadPresentation(out var state)&&state.Remaining>0&&state.Duration==60,"late join receives authoritative cooldown denominator");
            runtime.TryReadPresentation(out var current);lateRuntime.TryReadPresentation(out var lateState);
            Check(Mathf.Abs(current.Remaining-lateState.Remaining)<.3f&&TeamInfoGrid.CooldownFraction(lateState.Remaining,lateState.Duration)<.99f,"late join does not restart cooldown fill");
            CheckPanel(late,lateLogic,"隊友同步測試",true);
            await driver.AtTick(()=>Check(player.Health.KillUnrescuable(),"authoritative death"));
            await Until(()=>lateLogic.TryGetTeammateIdentity(client.LocalPlayer,out var identity)&&identity.IsDead&&identity.LastAbilityIds.Contains(ability.AbilityId),"death retains display-only owned skill snapshot");
            CheckPanel(late,lateLogic,"隊友同步測試",false);
            await late.Shutdown();
            var reconnected=await Connect(false);var reconnectLogic=CreateLogic(reconnected);
            await Until(()=>reconnectLogic.TryGetTeammateIdentity(client.LocalPlayer,out var identity)&&identity.IsDead&&identity.LastAbilityIds.Contains(ability.AbilityId),"reconnected observer receives death snapshot");
            Check(reconnectLogic.GetPlayerGrowth(client.LocalPlayer).Rank(PlayerGrowthKind.ReloadSpeed)==1,"growth survives death and reconnect");
            Status="Passed "+report.Count;
        }
        catch(Exception error){Status="Failed";report.Add("FAIL "+Stage+" : "+error);Debug.LogError("TeamInfo verification: "+error);}
        finally
        {
            foreach(var runner in runners)if(runner!=null&&runner.IsRunning)await runner.Shutdown();
            foreach(var obj in temporary)if(obj!=null)UnityEngine.Object.Destroy(obj);
            Directory.CreateDirectory("Temp/TeamInfo");File.WriteAllLines("Temp/TeamInfo/host-client.txt",report);
        }
    }
    static PlayerAbilityRuntime Find(Player player,PlayerAbilityDefinition definition)
    {
        if(player==null||player.AbilityRuntimeManager==null)return null;
        for(int i=0;i<PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots;i++){var r=player.AbilityRuntimeManager.GetEquippedRuntimeAtSlot(i);if(r!=null&&r.Definition==definition)return r;}return null;
    }
    static void CheckPanel(NetworkRunner runner,GameLogic logic,string expected,bool alive)
    {
        LocalTeamInfoPanel.EnsureForRunner(runner);var panel=runner.GetComponent<LocalTeamInfoPanel>();panel.enabled=false;
        typeof(LocalTeamInfoPanel).GetField("settings",Flags).SetValue(panel,TeamInfoPanelSettings.Load());
        typeof(LocalTeamInfoPanel).GetMethod("Build",Flags).Invoke(panel,null);
        var state=(TeamInfoInputState)typeof(LocalTeamInfoPanel).GetField("input",Flags).GetValue(panel);
        state.Step(false,false,true,true,false,false); // OnDisable intentionally requires a release before reopening.
        state.Step(true,true,true,true,false,false);
        typeof(LocalTeamInfoPanel).GetMethod("LateUpdate",Flags).Invoke(panel,null);
        var labels=panel.GetComponentsInChildren<TMP_Text>(true);var name=labels.FirstOrDefault(t=>t.name=="Player name"&&t.text.Contains(expected));
        Check(name!=null,"real panel displays synchronized player name");
        var row=name.transform.parent;Check(row.Find("Current weapon").GetComponent<UnityEngine.UI.Image>().enabled,"real panel displays current or retained weapon");
        Check(row.Find("Skills").GetComponentsInChildren<TeamInfoTooltipTarget>().Any(t=>t.HasContent),"real panel displays owned skills including death snapshot");
        Check(row.Find("Growth viewport").GetComponentsInChildren<TeamInfoTooltipTarget>().Any(t=>t.HasContent),"real panel displays acquired growth");
        if(alive)Check(row.Find("Skills").GetComponentsInChildren<TMP_Text>().Any(t=>t.name=="Cooldown number"&&!string.IsNullOrEmpty(t.text)),"real panel displays synchronized cooldown number");
        state.Close(false);
    }
    static GameLogic CreateLogic(NetworkRunner runner)
    {
        var root=new GameObject("Team info GameLogic",typeof(NetworkObject));temporary.Add(root);runner.MoveToRunnerScene(root);
        var logic=root.AddComponent<GameLogic>();root.AddComponent<Purgers.GameFlow.Stage.StageFlowController>();
        typeof(GameLogic).GetField("playerPrefab",Flags).SetValue(logic,NetworkPrefabRef.Parse(AssetDatabase.AssetPathToGUID("Assets/Prefabs/KCC_Player.prefab")));
        typeof(GameLogic).GetField("debugPlayerLifecycle",Flags).SetValue(logic,false);
        typeof(GameLogic).GetField("fallbackSpawnPosition",Flags).SetValue(logic,Origin);
        new NetworkObjectBaker().Bake(root);runner.RegisterSceneObjects(SceneRef.FromIndex(0),new[]{root.GetComponent<NetworkObject>()},default);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);temporary.Add(floor);runner.MoveToRunnerScene(floor);
        floor.transform.position=Origin+Vector3.down*2;floor.transform.localScale=new Vector3(100,1,100);floor.layer=Mathf.Max(0,LayerMask.NameToLayer("Ground"));return logic;
    }
    static async Task<NetworkRunner> Connect(bool server)
    {
        var runner=new GameObject(server?"Team Host":"Team Client").AddComponent<NetworkRunner>();runners.Add(runner);temporary.Add(runner.gameObject);runner.ProvideInput=true;
        var events=runner.gameObject.AddComponent<NetworkEvents>();
        foreach(var field in typeof(NetworkEvents).GetFields(Flags))if(typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType)&&field.GetValue(events)==null)field.SetValue(events,Activator.CreateInstance(field.FieldType));
        events.OnInput=new NetworkEvents.InputEvent();events.OnInput.AddListener((r,input)=>input.Set(server?default(NetInput):data));
        var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Multiple;
        foreach(var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(entry.Item2);
        var manager=runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();manager.IsSceneTakeOverEnabled=false;
        var app=new Fusion.Photon.Realtime.FusionAppSettings();Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app);app.FixedRegion="asia";
        var task=runner.StartGame(new StartGameArgs{GameMode=server?GameMode.Host:GameMode.Client,SessionName=session,Config=config,SceneManager=manager,CustomPhotonAppSettings=app,PlayerCount=4,IsVisible=false});
        await Until(()=>task.IsCompleted,"connect "+(server?"Host":"Client"));Check(task.GetAwaiter().GetResult().Ok,"Runner connected");
        await Until(()=>runner.IsRunning&&runner.Tick.Raw>0&&!runner.SceneManager.IsBusy,"Runner first tick");return runner;
    }
    static void SetProperty(object obj,string name,object value)=>obj.GetType().GetProperty(name,Flags).SetValue(obj,value);
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);report.Add("PASS "+label);}
    static async Task Until(Func<bool> condition,string label)
    {
        Stage=label;float until=Time.realtimeSinceStartup+45;
        while(!condition()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label);await Task.Delay(25);}report.Add("PASS "+label);
    }
}
