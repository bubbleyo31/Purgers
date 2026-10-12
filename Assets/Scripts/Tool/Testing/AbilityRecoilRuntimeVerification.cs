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

/// <summary>明確啟動的真實 Fusion Single Runner 探測；只改暫時實例，不保存資產。
/// 每個觀測操作在 FixedUpdateNetwork 內執行，避免誤把 KCC RenderData 當成正式結果。</summary>
public sealed class AbilityRecoilRuntimeVerification : SimulationBehaviour
{
    public static string Status { get; private set; } = "尚未執行";
    private static readonly List<string> report = new List<string>();
    private static NetworkRunner testRunner;
    private static AbilityRecoilRuntimeVerification probe;
    private Action next;
    private TaskCompletionSource<bool> completion;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public override void FixedUpdateNetwork()
    {
        if (next == null) return;
        var action = next; var done = completion; next = null; completion = null;
        try { action(); done.SetResult(true); } catch (Exception ex) { done.SetException(ex); }
    }
    private static async Task Tick(Action action)
    {
        var done = new TaskCompletionSource<bool>(); probe.next = action; probe.completion = done;
        if (await Task.WhenAny(done.Task, Task.Delay(10000)) != done.Task) throw new TimeoutException("Fixed tick");
        await done.Task;
    }
    public static async void Begin()
    {
        if (!Application.isPlaying || testRunner != null || FindObjectsOfType<NetworkRunner>().Any(r => r.IsRunning))
            throw new InvalidOperationException("需 Play Mode，且沒有其他運行中 Runner。");
        Status = "執行中"; report.Clear();
        try
        {
            testRunner = new GameObject("Ability recoil verification runner").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
            var result = await testRunner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config,
                SceneManager = testRunner.gameObject.AddComponent<NetworkSceneManagerDefault>() });
            Check(result.Ok, "Single Runner started");
            await Until(() => !testRunner.SceneManager.IsBusy && testRunner.Tick.Raw > 0);
            probe = testRunner.gameObject.AddComponent<AbilityRecoilRuntimeVerification>(); testRunner.AddGlobal(probe);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj = testRunner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1300,200,1300), Quaternion.identity, testRunner.LocalPlayer);
            testRunner.SetPlayerObject(testRunner.LocalPlayer, obj);
            var owner = obj.GetComponent<Player>();
            await Until(() => PlayerAbilityQualification.HasRangedWeapon(owner));
            var cannon = Spawn(owner, "Active/PiercingCannon").GetComponent<PlayerPiercingCannonAbility>();
            var runtime = cannon.GetComponent<PlayerAbilityRuntime>();
            var settings = Get(runtime, "recoil");
            Set(settings, "cameraApplicationDuration", 0f);
            Set(settings, "cameraEnabled", true); Set(settings, "cameraAngles", new Vector2(-4, 2));
            Set(settings, "cameraTiming", PlayerAbilityRecoilTiming.BeforeCast);
            Set(settings, "movementEnabled", true); Set(settings, "movementSpeed", 5f);
            Set(settings, "movementTiming", PlayerAbilityRecoilTiming.AfterCast);
            Set(cannon, "castDelaySeconds", .4f); Set(cannon, "cooldownSeconds", 0f);
            var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
            await Tick(() => {
                Property(runtime,"RemainingMovementRecoil",Vector3.zero); Pose(owner, 0, 0); var velocity = ((Vector3)ReadProperty(runtime,"RemainingMovementRecoil"));
                cannon.SimulateAbility(input, default);
                Check(owner.Movement.KCC.IsInFixedUpdate, "Probe observes fixed KCC data");
                Check(cannon.Phase == PlayerActiveAbilityPhase.Casting, "Accepted press enters windup");
                Check(Near(owner.Movement.KCC.Data.LookPitch, -4), "Camera BeforeCast kicks immediately");
                Check(Vector3.Distance(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")), velocity) < .001f, "Movement AfterCast waits");
                cannon.SimulateAbility(input, default);
                Check(Near(owner.Movement.KCC.Data.LookPitch, -4), "Repeated press during cast cannot kick twice");
                Property(runtime,"RemainingMovementRecoil",Vector3.zero); Pose(owner, -30, 90);
                Property(cannon, "PhaseTimer", TickTimer.None);
                velocity = ((Vector3)ReadProperty(runtime,"RemainingMovementRecoil"));
                cannon.FixedUpdateNetwork();
                var expected = Quaternion.Euler(-30,90,0) * Vector3.back * .5f;
                Check(Vector3.Distance(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")) - velocity, expected) < .001f, "Movement uses release aim including pitch");
                Check(cannon.Phase == PlayerActiveAbilityPhase.Ready, "Release finishes cannon");
                var after = ((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")); cannon.FixedUpdateNetwork();
                Check(Vector3.Distance(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")), after) < .001f, "Later tick does not replay recoil");
            });
            Set(settings, "cameraTiming", PlayerAbilityRecoilTiming.AfterCast);
            Set(cannon, "castDelaySeconds", 0f);
            await Tick(() => {
                Property(runtime,"RemainingMovementRecoil",Vector3.zero); Pose(owner, 0, 90); cannon.SimulateAbility(input, default);
                Vector3 start = (Vector3)ReadProperty(cannon, "BeamStart"), end = (Vector3)ReadProperty(cannon, "BeamEnd");
                Check(Vector3.Dot((end-start).normalized, Vector3.right) > .999f, "Shot direction sampled before AfterCast camera kick");
                Check(Near(owner.Movement.KCC.Data.LookPitch, -4), "Zero windup applies camera once");
                Check(Vector3.Distance(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")), Vector3.left * .5f) < .001f, "Same-event movement uses pre-kick aim");
                cannon.SimulateAbility(input, input.Buttons);
                Check(Near(owner.Movement.KCC.Data.LookPitch, -4), "Held input cannot repeat kick");
                var blocked = input; blocked.BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Gameplay;
                cannon.SimulateAbility(blocked, default);
                Check(Near(owner.Movement.KCC.Data.LookPitch, -4), "Blocked press cannot kick");
            });
            Set(cannon, "castDelaySeconds", .4f);
            await Tick(() => {
                Property(runtime,"RemainingMovementRecoil",Vector3.zero); Pose(owner, 0, 0); cannon.SimulateAbility(input, default); cannon.SetProfessionAvailable(false);
                Property(cannon, "PhaseTimer", TickTimer.None); cannon.FixedUpdateNetwork();
                Check(Near(owner.Movement.KCC.Data.LookPitch, 0), "Cancelled windup cannot release camera recoil");
                Check(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")) == Vector3.zero, "Cancelled windup cannot release movement recoil");
            });
            await Tick(() => {
                cannon.SetProfessionAvailable(true); Set(cannon,"castDelaySeconds",0f);
                runtime.GetType().GetMethod("ResetCameraRecoil",Hidden).Invoke(runtime,null);
                Set(settings,"cameraTiming",(PlayerAbilityRecoilTiming)3);Set(settings,"movementTiming",(PlayerAbilityRecoilTiming)3);
                Set(settings,"cameraAngles",new Vector2(-4,0));Property(runtime,"RemainingMovementRecoil",Vector3.zero); Pose(owner,0,0);
                cannon.SimulateAbility(input,default);
                Check(Near(owner.Movement.KCC.Data.LookPitch,-8),"Both flags with zero windup apply two camera kicks");
                Check((int)ReadProperty(runtime,"CameraRecoilSequence")==2,"Both flags represent two distinct cast events");
                Vector3 expected=Vector3.back*.5f+Quaternion.Euler(-4,0,0)*Vector3.back*.5f;
                Check(Vector3.Distance(((Vector3)ReadProperty(runtime,"RemainingMovementRecoil")),expected)<.001f,"Both flags add movement at each event aim");
            });
            testRunner.Despawn(cannon.Object);
            foreach (int mask in new[] { 2, 1, 3 })
            {
                var blink = Spawn(owner, "Active/Blink").GetComponent<PlayerBlinkAbility>();
                var blinkRuntime = blink.GetComponent<PlayerAbilityRuntime>();
                ConfigureInstant(blinkRuntime);
                Set(Get(blinkRuntime,"recoil"),"cameraTiming",(PlayerAbilityRecoilTiming)mask);
                Set(Get(blinkRuntime,"recoil"),"movementEnabled",false);
                await Tick(() => {
                    Pose(owner,0,0); blink.SimulateAbility(input,default);
                    float prepared = (mask & 1) != 0 ? -4 : 0;
                    Check(blink.Phase == PlayerActiveAbilityPhase.Armed && Near(owner.Movement.KCC.Data.LookPitch,prepared),
                        "Blink preparation respects selected stage mask=" + mask);
                    var blocked = input; blocked.BlockedControls = Purgers.GameFlow.Control.PlayerControlMask.Movement;
                    blink.SimulateAbility(blocked,default);
                    Check(blink.Phase == PlayerActiveAbilityPhase.Armed && Near(owner.Movement.KCC.Data.LookPitch,prepared),
                        "Blocked Blink release cannot kick mask=" + mask);
                    blink.SimulateAbility(input,input.Buttons);
                    Check(blink.Phase == PlayerActiveAbilityPhase.Armed,"Held first E cannot release Blink mask=" + mask);
                    blink.SimulateAbility(input,default);
                    float released = prepared + ((mask & 2) != 0 ? -4 : 0);
                    Check(blink.IsDashing && Near(owner.Movement.KCC.Data.LookPitch,released),
                        "Blink actual dash respects selected stage mask=" + mask);
                    blink.SimulateAbility(input,default);
                    Check(Near(owner.Movement.KCC.Data.LookPitch,released),"Blink dash cannot replay recoil mask=" + mask);
                });
                testRunner.Despawn(blink.Object);
            }
            var expiredBlink = Spawn(owner,"Active/Blink").GetComponent<PlayerBlinkAbility>();
            ConfigureInstant(expiredBlink.GetComponent<PlayerAbilityRuntime>());
            Set(Get(expiredBlink.GetComponent<PlayerAbilityRuntime>(),"recoil"),"cameraTiming",PlayerAbilityRecoilTiming.AfterCast);
            Set(Get(expiredBlink.GetComponent<PlayerAbilityRuntime>(),"recoil"),"movementEnabled",false);
            await Tick(() => {
                Pose(owner,0,0); expiredBlink.SimulateAbility(input,default);
                Property(expiredBlink,"PhaseTimer",TickTimer.None);
                expiredBlink.SimulateAbility(input,default);
                Check(expiredBlink.Phase == PlayerActiveAbilityPhase.Ready && Near(owner.Movement.KCC.Data.LookPitch,0),
                    "Expired Blink cannot emit actual-release recoil");
            });
            testRunner.Despawn(expiredBlink.Object);
            var failedProjectile = Spawn(owner,"Active/Grenade").GetComponent<PlayerProjectileAbility>();
            ConfigureInstant(failedProjectile.GetComponent<PlayerAbilityRuntime>());
            Set(Get(failedProjectile.GetComponent<PlayerAbilityRuntime>(),"recoil"),"cameraTiming",PlayerAbilityRecoilTiming.AfterCast);
            Set(Get(failedProjectile.GetComponent<PlayerAbilityRuntime>(),"recoil"),"movementTiming",PlayerAbilityRecoilTiming.AfterCast);
            Set(failedProjectile,"castDelaySeconds",.4f);
            await Tick(() => {
                Pose(owner,0,0); failedProjectile.SimulateAbility(input,default);
                Check(failedProjectile.Phase == PlayerActiveAbilityPhase.Casting,"Projectile accepts preparation before release failure");
                Set(failedProjectile,"projectilePrefab",null);
                Property(failedProjectile,"PhaseTimer",TickTimer.None);
                failedProjectile.FixedUpdateNetwork();
                Check(failedProjectile.Phase == PlayerActiveAbilityPhase.Ready && Near(owner.Movement.KCC.Data.LookPitch,0),
                    "Failed projectile release cannot kick camera");
                Check(owner.Movement.KCC.Data.DynamicVelocity == Vector3.zero,"Failed projectile release cannot kick movement");
            });
            testRunner.Despawn(failedProjectile.Object);
            var support = Spawn(owner, "SupportAerialAbilityRuntime").GetComponent<SupportAerialAbility>();
            ConfigureInstant(support.GetComponent<PlayerAbilityRuntime>());
            await Tick(() => {
                Pose(owner, 0, 0);
                typeof(SupportAerialAbility).GetMethod("BeginAbility", Hidden).Invoke(support, null);
                Check(Near(owner.Movement.KCC.Data.LookPitch,-4), "Legacy successful start emits camera recoil");
                Check(((Vector3)ReadProperty(support,"StoredDynamicVelocity")) == Vector3.zero,
                    "Legacy slow keeps original stored momentum separate from recoil");
                Check(Vector3.Distance((Vector3)ReadProperty(support.GetComponent<PlayerAbilityRuntime>(),"RemainingMovementRecoil"),Vector3.back*.5f)<.001f,
                    "Legacy recoil queues its own finite displacement");
            });
            testRunner.Despawn(support.Object);
            await VerifyCameraRules(owner);
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + ex); }
        finally
        {
            if (testRunner != null) { try { await testRunner.Shutdown(); } catch (Exception ex) { Status = "失敗"; report.Add("FAIL cleanup " + ex); } }
            testRunner = null; probe = null;
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + Status + "; controlled Single Runner, not independent multiplayer");
            File.WriteAllLines(Path.GetFullPath("Temp/AbilityRecoilRuntimeVerification.txt"),report);
            Debug.Log("[AbilityRecoilRuntimeVerification] " + Status);
        }
    }
    private static PlayerAbilityRuntime Spawn(Player owner, string name)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/" + name + ".prefab");
        return testRunner.Spawn(p.GetComponent<NetworkObject>(), owner.transform.position, Quaternion.identity, owner.Object.InputAuthority,
            (r,o) => o.GetComponent<PlayerAbilityRuntime>().InitializeBeforeSpawn(owner.Object,0)).GetComponent<PlayerAbilityRuntime>();
    }
    private static void ConfigureInstant(PlayerAbilityRuntime runtime)
    {
        var s=Get(runtime,"recoil"); Set(s,"cameraApplicationDuration",0f); Set(s,"cameraEnabled",true); Set(s,"cameraTiming",(PlayerAbilityRecoilTiming)3);
        Set(s,"cameraAngles",new Vector2(-4,2)); Set(s,"movementEnabled",true); Set(s,"movementTiming",PlayerAbilityRecoilTiming.BeforeCast); Set(s,"movementSpeed",5f);
    }
    private static async Task VerifyCameraRules(Player owner)
    {
        var runtime=Spawn(owner,"Active/Shield");
        runtime.GetComponent<PlayerActiveAbilityBase>().enabled=false;
        var s=Get(runtime,"recoil");Set(s,"cameraEnabled",true);Set(s,"cameraTiming",PlayerAbilityRecoilTiming.BeforeCast);
        Set(s,"cameraAngles",new Vector2(-4,2));Set(s,"cameraApplicationDuration",testRunner.DeltaTime*4);Set(s,"cameraRecoveryDelay",10f);
        await Tick(()=>{
            Pose(owner,0,0);runtime.ApplyAbilityRecoil(PlayerAbilityRecoilEvent.BeforeCast);
            Check(Near(owner.Movement.KCC.Data.LookPitch,0),"Positive duration queues without immediate full kick");
            runtime.Simulate(default,default);
            Check(Near(owner.Movement.KCC.Data.LookPitch,-1),"First smoothing tick applies one quarter");
            float before=owner.Movement.KCC.Data.LookPitch;runtime.Simulate(default,default);
            Check(Near(owner.Movement.KCC.Data.LookPitch,before),"Repeated simulation cannot apply twice in same tick");
        });
        for(int i=0;i<3;i++) await Tick(()=>runtime.Simulate(default,default));
        await Tick(()=>{
            Check(Near(owner.Movement.KCC.Data.LookPitch,-4),"Smoothed camera reaches exact configured total");
            Check(Near(owner.Movement.KCC.Data.LookYaw,-2),"First yaw uses deterministic negative direction");
            runtime.Simulate(default,default);
            Check(Near(owner.Movement.KCC.Data.LookPitch,-4),"Unexpired recovery delay keeps applied angle");
            Set(s,"cameraRecoverySpeed",60f);Property(runtime,"CameraRecoveryTimer",TickTimer.None);
        });
        await Tick(()=>{
            runtime.Simulate(default,default);
            Check(Near(owner.Movement.KCC.Data.LookPitch,-4+60*testRunner.DeltaTime),"Automatic recovery uses configured degrees per second");
        });
        for(int i=0;i<5;i++) await Tick(()=>runtime.Simulate(default,default));
        await Tick(()=>{
            Check(Near(owner.Movement.KCC.Data.LookPitch,0),"Recovery finishes without overshoot");
            Check(Near(owner.Movement.KCC.Data.LookYaw,-2),"Yaw does not auto recover");
            Set(s,"cameraApplicationDuration",0f);runtime.ApplyAbilityRecoil(PlayerAbilityRecoilEvent.BeforeCast);
            Check(Near(owner.Movement.KCC.Data.LookYaw,0),"Next event alternates yaw");
            runtime.GetType().GetMethod("ResetCameraRecoil",Hidden).Invoke(runtime,null);
            Pose(owner,-89,0);runtime.ApplyAbilityRecoil(PlayerAbilityRecoilEvent.BeforeCast);
            Check(Near((float)ReadProperty(runtime,"AccumulatedCameraPitch"),-1),"KCC pitch clamp counts only actual applied angle");
        });
        await Tick(()=>{
            runtime.GetType().GetMethod("ResetCameraRecoil",Hidden).Invoke(runtime,null);Pose(owner,0,0);
            Property(runtime,"AccumulatedCameraPitch",-1f);Property(runtime,"CameraRecoveryTimer",TickTimer.CreateFromSeconds(testRunner,10));
            var weapon=owner.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<AttackRifle>();
            Check(weapon!=null,"Actual Rifle runtime available for interaction check");
            Property(weapon,"AccumulatedRecoilPitch",-2f);Property(weapon,"PendingRecoilPitch",0f);Property(weapon,"PendingRecoilYaw",0f);
            Property(weapon,"RecoilRecoveryTimer",TickTimer.CreateFromSeconds(testRunner,10));
            Set(owner.Movement,"lookSensitivity",1f);
            var look=new NetInput { LookDelta=new Vector2(1.5f,0) };
            owner.Movement.Simulate(look,default,false,0,false,1,false,false);
            runtime.Simulate(default,default);
            weapon.GetType().GetMethod("ProcessRecoilRecovery",Hidden).Invoke(weapon,null);
            Check(Near((float)ReadProperty(runtime,"AccumulatedCameraPitch"),0),"Skill consumes one degree of real manual input");
            Check(Near((float)ReadProperty(weapon,"AccumulatedRecoilPitch"),-1.5f),"Rifle consumes only remaining half degree");
            // 正式系統回正不再進入輸入額度。
            Property(runtime,"AccumulatedCameraPitch",-2f);Property(runtime,"CameraRecoveryTimer",TickTimer.None);
            Property(runtime,"LastCameraRecoveryTick",int.MinValue);runtime.Simulate(default,default);
            weapon.GetType().GetMethod("ProcessRecoilRecovery",Hidden).Invoke(weapon,null);
            Check(Near((float)ReadProperty(weapon,"AccumulatedRecoilPitch"),-1.5f),"Skill automatic recovery cannot erase Rifle recoil pool");
        });
        testRunner.Despawn(runtime.Object);
    }

    private static void Pose(Player owner, float pitch, float yaw)
    {
        var k=owner.Movement.KCC; k.SetPosition(new Vector3(1300,200,1300)); k.SetLookRotation(pitch,yaw); k.SetDynamicVelocity(Vector3.zero);
    }
    private static FieldInfo Field(object obj,string name)
    {
        for(var t=obj.GetType();t!=null;t=t.BaseType) { var f=t.GetField(name,Hidden); if(f!=null)return f; }
        throw new MissingFieldException(name);
    }
    private static object Get(object o,string n)=>Field(o,n).GetValue(o);
    private static void Set(object o,string n,object v)=>Field(o,n).SetValue(o,v);
    private static void Property(object o,string n,object v)=>o.GetType().GetProperty(n,Hidden|BindingFlags.Public).SetValue(o,v);
    private static object ReadProperty(object o,string n)=>o.GetType().GetProperty(n,Hidden|BindingFlags.Public).GetValue(o);
    private static bool Near(float a,float b)=>Mathf.Abs(a-b)<.001f;
    private static void Check(bool condition,string label) { if(!condition)throw new Exception(label); report.Add("PASS "+label); }
    private static async Task Until(Func<bool> predicate)
    {
        float end=Time.realtimeSinceStartup+20; while(!predicate()) { if(Time.realtimeSinceStartup>end)throw new TimeoutException("Runner/player ready"); await Task.Delay(50); }
    }
}
#endif
