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

/// <summary>真實 KCC 平地／空中位移量測；僅暫時 Runtime，不保存場景或資產。</summary>
public sealed class AbilityMotionRuntimeVerification : SimulationBehaviour
{
    public static string Status {get;private set;}="尚未執行";
    public static string Stage {get;private set;}="";
    private static NetworkRunner runner;
    private static AbilityMotionRuntimeVerification probe;
    private static bool press;
    private static readonly List<string> report=new List<string>();
    private Action next; private TaskCompletionSource<bool> completion;
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public override void FixedUpdateNetwork()
    {
        if(next==null)return;
        var action=next;var done=completion;next=null;completion=null;
        try{action();done.SetResult(true);}catch(Exception ex){done.SetException(ex);}
    }
    public static async void Begin()
    {
        if(!Application.isPlaying||FindObjectsOfType<NetworkRunner>().Any(r=>r.IsRunning))
            throw new InvalidOperationException("需 Play Mode 且沒有其他 Runner。");
        Status="執行中";report.Clear();press=false;
        try
        {
            runner=new GameObject("Ability motion verification").AddComponent<NetworkRunner>();runner.ProvideInput=true;
            var events=runner.gameObject.AddComponent<NetworkEvents>();events.OnInput=new NetworkEvents.InputEvent();
            events.OnInput.AddListener((r,i)=>{var input=new NetInput();input.Buttons.Set(InputButton.Ability1,press);i.Set(input);});
            var config=NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));config.PeerMode=NetworkProjectConfig.PeerModes.Single;
            foreach(var e in NetworkProjectConfig.Global.PrefabTable.GetEntries())config.PrefabTable.AddSource(e.Item2);
            var result=await runner.StartGame(new StartGameArgs{GameMode=GameMode.Single,Config=config,SceneManager=runner.gameObject.AddComponent<NetworkSceneManagerDefault>()});
            Check(result.Ok,"Single Runner starts");
            await Until(()=>!runner.SceneManager.IsBusy&&runner.Tick.Raw>0,"runner ready");
            probe=runner.gameObject.AddComponent<AbilityMotionRuntimeVerification>();runner.AddGlobal(probe);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj=runner.Spawn(prefab.GetComponent<NetworkObject>(),new Vector3(1500,100,1500),Quaternion.identity,runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer,obj);var owner=obj.GetComponent<Player>();
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Motion test floor";
            floor.transform.position=new Vector3(1500,99.5f,1500);floor.transform.localScale=new Vector3(100,1,100);
            int layer=0;for(int i=0;i<32;i++)if((owner.Movement.KCC.Settings.CollisionLayerMask.value&(1<<i))!=0 && i!=owner.gameObject.layer){layer=i;break;}floor.layer=layer;
            Physics.SyncTransforms();
            await Until(()=>owner.AbilityRuntimeManager.LoadoutRevision>0,"loadout ready");
            float groundRecoil=await Recoil(owner,false);
            float airRecoil=await Recoil(owner,true);
            float groundBlink=await Blink(owner,false);
            float airBlink=await Blink(owner,true);
            report.Add("MEASURE recoil ground="+groundRecoil+" air="+airRecoil+" expected=2");
            report.Add("MEASURE blink ground="+groundBlink+" air="+airBlink+" expected=8");
            Check(Mathf.Abs(groundRecoil-2)<.04f&&Mathf.Abs(airRecoil-2)<.04f,"Recoil has two metres on ground and in air");
            Check(Mathf.Abs(groundRecoil-airRecoil)<.03f,"Recoil ground and air agree");
            Check(Mathf.Abs(groundBlink-8)<.04f&&Mathf.Abs(airBlink-8)<.04f,"Blink has eight metres without overshoot");
            Check(Mathf.Abs(groundBlink-airBlink)<.03f,"Blink ground and air agree");
            float landed=await Recoil(owner,true,true);
            Check(Mathf.Abs(landed-2)<.04f && owner.Movement.KCC.FixedData.IsGrounded,"Recoil preserves distance while landing");
            Check(Mathf.Abs(await Recoil(owner,false,false,true)-2)<.04f,"Ground recoil preserves distance with existing sideways velocity");
            Check(Mathf.Abs(await Recoil(owner,true,false,true)-2)<.04f,"Air recoil preserves distance with existing sideways velocity");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=layer;
            wall.transform.position=new Vector3(1500,101,1498.5f);wall.transform.localScale=new Vector3(10,4,.2f);Physics.SyncTransforms();
            float blocked=await Recoil(owner,false);
            Check(blocked>0 && blocked<1.5f,"Recoil stops before wall");
            float stopped=owner.Movement.KCC.FixedData.TargetPosition.z;
            Destroy(wall);await Task.Delay(300);
            Check(Mathf.Abs(stopped-owner.Movement.KCC.FixedData.TargetPosition.z)<.03f,"Blocked recoil does not replay after wall disappears");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=layer;
            wall.transform.position=new Vector3(1500,101,1503.5f);wall.transform.localScale=new Vector3(10,4,.2f);Physics.SyncTransforms();
            float blinkWall=await Blink(owner,false);
            Check(blinkWall>0 && blinkWall<3.5f,"Blink still stops before wall");Destroy(wall);
            await Upward(owner);
            Destroy(floor);Status="通過";
        }
        catch(Exception ex){Status="失敗";report.Add("FAIL "+Stage+" "+ex);}
        finally
        {
            press=false;if(runner!=null)try{await runner.Shutdown();}catch(Exception ex){Status="失敗";report.Add("FAIL cleanup "+ex);}
            runner=null;probe=null;
            report.Insert(0,DateTime.UtcNow.ToString("O")+" "+Status+"; controlled Single Runner, not independent multiplayer");
            File.WriteAllLines(Path.GetFullPath("Temp/AbilityMotionRuntimeVerification.txt"),report);
            Debug.Log("[AbilityMotionRuntimeVerification] "+Status);
        }
    }
    private static async Task<float> Recoil(Player owner,bool air,bool landing=false,bool moving=false)
    {
        var runtime=await Equip(owner,"Shield");var skill=runtime.GetComponent<PlayerActiveAbilityBase>();
        var s=Field(runtime,"recoil").GetValue(runtime);
        Set(s,"cameraEnabled",false);Set(s,"movementEnabled",true);Set(s,"movementSpeed",10f);
        Set(s,"movementTiming",PlayerAbilityRecoilTiming.AfterCast);Set(s,"movementLocalDirection",Vector3.back);
        var duration=s.GetType().GetField("movementDecayDuration",Hidden);if(duration!=null)duration.SetValue(s,.4f);
        await Pose(owner,air);
        if(landing) await Tick(()=>{owner.Movement.KCC.SetPosition(new Vector3(1500,100.8f,1500));owner.Movement.KCC.SetDynamicVelocity(Vector3.down*6);});
        if(moving) await Tick(()=>owner.Movement.KCC.SetDynamicVelocity(Vector3.right*3));
        float startX=owner.Movement.KCC.FixedData.TargetPosition.x;
        float start=owner.Movement.KCC.FixedData.TargetPosition.z;
        int sequence=skill.ActivationSequence;press=true;await Until(()=>skill.ActivationSequence>sequence,"recoil accepted "+air);press=false;
        int tick=runner.Tick.Raw;await Until(()=>runner.Tick.Raw>=tick+Mathf.CeilToInt(.8f/runner.DeltaTime),"recoil completed "+air);
        float distance=start-owner.Movement.KCC.FixedData.TargetPosition.z;
        report.Add("RECOIL air="+air+" landing="+landing+" moving="+moving+" distance="+distance+" grounded="+owner.Movement.KCC.FixedData.IsGrounded);
        if(moving)Check(owner.Movement.KCC.FixedData.TargetPosition.x>startX+.03f,"Existing sideways velocity is preserved");
        return distance;
    }
    private static async Task<float> Blink(Player owner,bool air)
    {
        var runtime=await Equip(owner,"Blink");var blink=runtime.GetComponent<PlayerBlinkAbility>();
        var s=Field(runtime,"recoil").GetValue(runtime);Set(s,"cameraEnabled",false);Set(s,"movementEnabled",false);
        Set(blink,"dashDistance",8f);Set(blink,"dashSpeed",35f);
        await Pose(owner,air);
        press=true;await Until(()=>blink.Phase==PlayerActiveAbilityPhase.Armed,"blink armed "+air);press=false;
        await Tick(()=>{});
        int tick=runner.Tick.Raw;await Until(()=>runner.Tick.Raw>=tick+3,"release first E");
        float start=owner.Movement.KCC.FixedData.TargetPosition.z;
        press=true;await Until(()=>blink.IsDashing,"blink dash "+air);press=false;
        await Until(()=>!blink.IsDashing,"blink finish "+air);
        float end=owner.Movement.KCC.FixedData.TargetPosition.z;
        tick=runner.Tick.Raw;await Until(()=>runner.Tick.Raw>=tick+10,"blink residual "+air);
        float final=owner.Movement.KCC.FixedData.TargetPosition.z;
        report.Add("BLINK air="+air+" distance="+(end-start)+" residual="+(final-end));
        return final-start;
    }
    private static async Task Upward(Player owner)
    {
        var runtime=await Equip(owner,"Shield");var skill=runtime.GetComponent<PlayerActiveAbilityBase>();
        var s=Field(runtime,"recoil").GetValue(runtime);Set(s,"cameraEnabled",false);Set(s,"movementEnabled",true);
        Set(s,"movementSpeed",10f);Set(s,"movementTiming",PlayerAbilityRecoilTiming.AfterCast);
        Set(s,"movementDecayDuration",.4f);Set(s,"movementLocalDirection",Vector3.up);
        await Pose(owner,false);float start=owner.Movement.KCC.FixedData.TargetPosition.y, peak=start;
        int sequence=skill.ActivationSequence;press=true;await Until(()=>skill.ActivationSequence>sequence,"upward accepted");press=false;
        int tick=runner.Tick.Raw;
        while(runner.Tick.Raw<tick+Mathf.CeilToInt(.4f/runner.DeltaTime)){peak=Mathf.Max(peak,owner.Movement.KCC.FixedData.TargetPosition.y);await Task.Delay(10);}
        report.Add("UPWARD peak="+(peak-start));Check(peak>start+.1f,"Upward recoil leaves ground without ground-snap cancellation");
    }
    private static async Task<PlayerAbilityRuntime> Equip(Player owner,string name)
    {
        press=false;await Tick(()=>{});
        var def=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/"+name+".asset");
        Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(def,out string error),"equip "+name+" "+error);
        await Until(()=>PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner)!=null &&
            PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner).GetComponent<PlayerAbilityRuntime>().Definition==def,"equip ready");
        var skill=PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner);
        Set(skill,"castDelaySeconds",0f);Set(skill,"cooldownSeconds",0f); await Until(()=>!skill.IsAbilityActive && skill.CooldownRemainingSeconds<=0,"skill ready for next sample");
        return skill.GetComponent<PlayerAbilityRuntime>();
    }
    private static async Task Pose(Player owner,bool air)
    {
        await Tick(()=>{var k=owner.Movement.KCC;k.SetPosition(new Vector3(1500,air?150:100.03f,1500));k.SetLookRotation(0,0);k.SetDynamicVelocity(Vector3.zero);k.SetKinematicVelocity(Vector3.zero);});
        if(!air)await Until(()=>owner.Movement.KCC.FixedData.IsGrounded,"ground contact");
        await Tick(()=>{});
    }
    private static async Task Tick(Action action)
    {
        var done=new TaskCompletionSource<bool>();probe.next=action;probe.completion=done;
        if(await Task.WhenAny(done.Task,Task.Delay(10000))!=done.Task)throw new TimeoutException("fixed tick");await done.Task;
    }
    private static FieldInfo Field(object o,string name){for(var t=o.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Hidden);if(f!=null)return f;}throw new MissingFieldException(name);}
    private static void Set(object o,string name,object value)=>Field(o,name).SetValue(o,value);
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);report.Add("PASS "+label);}
    private static async Task Until(Func<bool> test,string label){Stage=label;float end=Time.realtimeSinceStartup+15;while(!test()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);await Task.Delay(15);}}
}
#endif
