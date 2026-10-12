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

/// <summary>明確啟用的單 Runner 扇波測試。所有數值只改臨時 Instance，不存 Scene／Prefab。</summary>
public static class ShockwaveRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private static NetworkRunner runner;
    private static readonly List<GameObject> temporary = new List<GameObject>();
    private static readonly List<string> report = new List<string>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null) throw new InvalidOperationException("需在 Play Mode 且無測試進行。");
        Status = "執行中"; report.Clear();
        try
        {
            runner = new GameObject("Shockwave verification runner").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
            var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            var started = await runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config, SceneManager = manager });
            Check(started.Ok, "Single Runner started");
            await Until(() => !runner.SceneManager.IsBusy, "scene manager");
            await Until(() => runner.Tick.Raw > 0, "initial simulation tick");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var playerObject = runner.Spawn(playerPrefab.GetComponent<NetworkObject>(), new Vector3(1000,100,1000), Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, playerObject);
            var player = playerObject.GetComponent<Player>();
            int worldLayer = 0;
            for (int layer=0; layer<32; layer++)
                if ((player.Movement.KCC.Settings.CollisionLayerMask.value & (1 << layer)) != 0 && layer != player.gameObject.layer)
                { worldLayer = layer; break; }
            Box(new Vector3(1000,99.5f,1000), new Vector3(50,1,50), worldLayer);
            await Until(() => player.AbilityRuntimeManager.LoadoutRevision > 0, "loadout");
            var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shockwave.asset");
            Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition, out string error), "equip: " + error);
            await Until(() => PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(player) != null, "shockwave");
            var wave = PlayerAbilityQualification.GetModule<PlayerShockwaveAbility>(player);
            Set(wave, "castDelaySeconds", 0f, typeof(PlayerActiveAbilityBase));
            Set(wave, "cooldownSeconds", 0f, typeof(PlayerActiveAbilityBase));
            Set(wave, "radius", 3f); Set(wave, "travelDistance", 12f); Set(wave, "waveHeight", 3f); Set(wave, "waveSpeed", 4f); Set(wave, "waveThickness", 1f);
            Set(wave, "damage", 7f); Set(wave, "hitMask", (LayerMask)~0); Set(wave, "obstructionMask", (LayerMask)(1 << worldLayer));
            player.Movement.KCC.SetPosition(new Vector3(1000,100.05f,1000));
            player.Movement.KCC.SetLookRotation(0,0);
            await Task.Delay(150);
            Vector3 origin = player.Movement.KCC.Data.TargetPosition;
            var near = Enemy(origin + new Vector3(-1,.5f,4), true);
            var far = Enemy(origin + new Vector3(2,.5f,8), true);
            var outside = Enemy(origin + new Vector3(8,.5f,0), true);
            var blocked = Enemy(origin + new Vector3(-2,.5f,8), true);
            // 遮擋案例使用單一已知位置的碰撞體，避免正式巨大骨架露出牆外影響測試。
            foreach(var collider in blocked.GetComponentsInChildren<Collider>())
                if(collider.name != "controlled hit collider") collider.enabled=false;
            Box(origin + new Vector3(-1.5f,10f,6), new Vector3(1.2f,25f,0.4f), worldLayer);
            Physics.SyncTransforms();
            await Task.Delay(100);
            Check((bool)typeof(PlayerShockwaveAbility).GetMethod("IsObstructed",Private).Invoke(wave,
                new object[]{runner.GetPhysicsScene(),origin+Vector3.up,blocked.transform.position+Vector3.up,blocked}),
                "fixture target is fully occluded before firing");
            float nearHp=near.Health.CurrentHealth, farHp=far.Health.CurrentHealth;
            float outsideHp=outside.Health.CurrentHealth, blockedHp=blocked.Health.CurrentHealth;
            Press(wave);
            Check(wave.IsWaveRunning, "wave starts; no instant full-sector damage");
            Check(Near(near.Health.CurrentHealth, nearHp) && Near(far.Health.CurrentHealth, farHp), "near and far HP unchanged at launch");
            int sequence = wave.ActivationSequence; Press(wave);
            Check(wave.ActivationSequence == sequence, "zero cooldown cannot overwrite a live wave");
            Set(wave, "radius", 8f); Set(wave, "travelDistance", 30f); Set(wave, "waveHeight", 9f);
            Check(wave.CurrentWaveRadius == 3f && wave.CurrentWaveRange == 12f && wave.CurrentWaveHeight == 3f,
                "changing Inspector fields cannot resize an in-flight snapshot");
            await Until(() => wave.CurrentWaveFront > 2f, "front two metres");
            Check(Near(near.Health.CurrentHealth,nearHp) && Near(far.Health.CurrentHealth,farHp), "no premature damage");
            await Until(() => wave.CurrentWaveFront > 5.5f, "front passes near target");
            Check(Near(near.Health.CurrentHealth,nearHp-7f), "near enemy receives one damage request on arrival; before="+nearHp+" after="+near.Health.CurrentHealth+" enemy="+near.transform.position+" origin="+wave.CurrentWaveOrigin+" front="+wave.CurrentWaveFront+" alive="+near.IsAlive);
            Check(Near(far.Health.CurrentHealth,farHp), "far enemy still unharmed");
            Check(Resources.FindObjectsOfTypeAll<PlayerShockwaveVisual>().Count(v => v.gameObject.scene.IsValid() && v.gameObject.scene.isLoaded) == 1, "one local wave presentation");
            Check(wave.CurrentWaveRange == 12f && wave.CurrentWaveRadius == 3f && wave.CurrentWaveHeight == 3f && wave.CurrentWaveThickness == 1f, "visual snapshot carries edited geometry");
            await Until(() => !wave.IsWaveRunning, "wave tail finishes");
            await Task.Delay(100);
            Check(Near(far.Health.CurrentHealth,farHp-7f), "far enemy hit on later wave arrival");
            Check(Near(near.Health.CurrentHealth,nearHp-7f), "multiple colliders and ticks do not duplicate damage");
            Check(Near(outside.Health.CurrentHealth,outsideHp), "outside angle is not hit");
            Check(Near(blocked.Health.CurrentHealth,blockedHp), "wall occlusion retained");
            Check(Resources.FindObjectsOfTypeAll<PlayerShockwaveVisual>().Count(v => v.gameObject.scene.IsValid() && v.gameObject.scene.isLoaded) == 0, "finished wave visual cleaned up");

            // 第二波測試低頻 Tick／高速跨越：靜止目標仍須被掃掠命中。
            var fast = Enemy(origin + new Vector3(0,.5f,10), true); float fastHp=fast.Health.CurrentHealth;
            Set(wave,"radius",3f); Set(wave,"travelDistance",12f); Set(wave,"waveHeight",3f);
            Set(wave,"waveSpeed",1000f); Physics.SyncTransforms(); Press(wave);
            await Until(() => !wave.IsWaveRunning, "fast wave finishes");
            Check(Near(fast.Health.CurrentHealth,fastHp-7f), "fast wave sweep cannot tunnel past target");

            // 空中施放使用實際 KCC 施放位置；之後落地／轉向不應拖動已發出的波牆。
            player.Movement.KCC.SetPosition(origin + Vector3.up * 8f);
            player.Movement.KCC.SetLookRotation(0,0);
            var airborne = Enemy(origin + new Vector3(0,9f,5f), true);
            var below = Enemy(origin + new Vector3(0,.5f,5f), true);
            float airHp=airborne.Health.CurrentHealth, belowHp=below.Health.CurrentHealth;
            Set(wave,"waveSpeed",6f); Physics.SyncTransforms(); Press(wave);
            Vector3 airOrigin=wave.CurrentWaveOrigin, airDirection=wave.CurrentWaveDirection;
            Check(airOrigin.y > origin.y+7f, "airborne cast captures player height without ground snapping");
            player.Movement.KCC.SetPosition(origin);
            player.Movement.KCC.SetLookRotation(0,90);
            await Until(() => wave.CurrentWaveFront > 1f, "airborne wave in progress");
            Check(wave.CurrentWaveOrigin==airOrigin && wave.CurrentWaveDirection==airDirection,
                "landing and turning do not drag the airborne wave");
            await Until(() => !wave.IsWaveRunning, "airborne wave finishes");
            Check(Near(airborne.Health.CurrentHealth,airHp-7f), "airborne target is hit inside fixed wall height");
            Check(Near(below.Health.CurrentHealth,belowHp), "ground target below airborne wall is not hit");


            // 前搖中改位置與 Pitch/Yaw；出手後再轉向，已發射的座標仍須固定。
            Set(wave,"castDelaySeconds",.35f,typeof(PlayerActiveAbilityBase));
            var offset=new Vector3(.4f,1.5f,.6f);Set(wave,"originOffset",offset);
            foreach(float pitch in new[]{-35f,35f})
            {
                var castPosition=origin+new Vector3(15,8,0);
                Pose(player,castPosition,0,0);Press(wave);
                Check(wave.Phase==PlayerActiveAbilityPhase.Casting && !wave.IsWaveRunning,"windup does not launch immediately");
                await Task.Delay(70);
                Pose(player,castPosition,pitch,90);
                Quaternion expected=Quaternion.Euler(pitch,90,0);
                await Until(()=>{if(wave.IsWaveRunning)return true;Pose(player,castPosition,pitch,90);return false;},"aimed release");
                var shotOrigin=wave.CurrentWaveOrigin;var shotRotation=wave.CurrentWaveRotation;
                Check(Quaternion.Angle(shotRotation,expected)<.1f,"release captures latest Pitch/Yaw "+pitch);
                Check(Vector3.Distance(shotOrigin,castPosition+expected*offset)<.15f,"release captures rotated XYZ offset "+pitch);
                var aimed=Enemy(shotOrigin+expected*new Vector3(0,1,7),true);
                float aimedHp=aimed.Health.CurrentHealth;
                Physics.SyncTransforms();Pose(player,origin,0,-90);
                await Until(()=>wave.CurrentWaveFront>1f,"aimed flight");
                Check(Vector3.Distance(wave.CurrentWaveOrigin,shotOrigin)<.001f &&
                    Quaternion.Angle(wave.CurrentWaveRotation,shotRotation)<.1f,"turning after release cannot steer the wave");
                await Until(()=>!wave.IsWaveRunning,"aimed wave finishes");
                Check(Near(aimed.Health.CurrentHealth,aimedHp-7),"tilted authoritative sweep hits target "+pitch);
            }
            Set(wave,"castDelaySeconds",0f,typeof(PlayerActiveAbilityBase));
            Set(wave,"originOffset",Vector3.zero);Pose(player,origin,0,0);
            // 使用原場景回報的相對姿態：腳底在傾斜波牆下方，身體 Collider 中心在牆內。
            Pose(player,origin+new Vector3(15,0,0),-15,90);
            Set(wave,"radius",8f);Set(wave,"travelDistance",20f);Set(wave,"waveHeight",5f);Set(wave,"waveSpeed",60f);
            Vector3 regressionOrigin=player.Movement.KCC.Data.TargetPosition;
            Quaternion regressionRotation=Quaternion.Euler(-15,90,0);
            var bodyOnly=Enemy(regressionOrigin+regressionRotation*new Vector3(.2f,-4.47f,21.66f),true);
            foreach(var c in bodyOnly.GetComponentsInChildren<Collider>())c.enabled=false;
            var bodyProbe=bodyOnly.transform.Find("controlled hit collider").GetComponent<SphereCollider>();
            bodyProbe.enabled=true;bodyProbe.gameObject.layer=9;
            bodyProbe.transform.position=regressionOrigin+regressionRotation*new Vector3(.2f,2.37f,23.35f);
            // 真正命中只允許正式 Enemy Hitbox Layer，不能靠測試新增的 Default Layer 混過。
            Set(wave,"hitMask",(LayerMask)(1<<9));
            var secondBody=UnityEngine.Object.Instantiate(bodyProbe.gameObject,bodyProbe.transform.parent);
            secondBody.transform.position=bodyProbe.transform.position;
            var rootOnly=Enemy(regressionOrigin+regressionRotation*new Vector3(-.2f,1,21),true);
            foreach(var c in rootOnly.GetComponentsInChildren<Collider>())c.enabled=false;
            var outsideProbe=rootOnly.transform.Find("controlled hit collider").GetComponent<SphereCollider>();
            outsideProbe.enabled=true;outsideProbe.gameObject.layer=9;
            outsideProbe.transform.position=regressionOrigin+regressionRotation*new Vector3(-.2f,10,21);
            float outsideBodyHp=rootOnly.Health.CurrentHealth;
            float bodyHp=bodyOnly.Health.CurrentHealth;Physics.SyncTransforms();
            Check(!ShockwavePropagationRules.ContainsSweptBand(bodyOnly.transform.position-regressionOrigin,
                regressionRotation,0,20,8,60,1,20,5),"regression fixture root is outside tilted wave");
            Check(ShockwavePropagationRules.ContainsSweptBand(bodyProbe.bounds.center-regressionOrigin,
                regressionRotation,0,20,8,60,1,20,5),"regression fixture body center is inside tilted wave");
            Press(wave);await Until(()=>!wave.IsWaveRunning,"body hitbox wave");
            Check(Near(bodyOnly.Health.CurrentHealth,bodyHp-7f),"body hitbox receives damage when root lies outside tilted wave");
            Check(Near(rootOnly.Health.CurrentHealth,outsideBodyHp),"root inside cannot hit body outside the wave");
            Pose(player,origin,0,0);
            Set(wave,"waveSpeed",2f); Press(wave);
            await Until(() => wave.IsWaveRunning && wave.CurrentWaveFront > 0.2f, "third wave");
            player.Health.ReceiveDamage(new DamageRequest { RequestedDamage=100000f, BaseDamage=100000f, DamageType=DamageType.Environment });
            await Task.Delay(200);
            Check(!wave.IsWaveRunning, "owner death cancels live wave");
            Check(Resources.FindObjectsOfTypeAll<PlayerShockwaveVisual>().Count(v => v.gameObject.scene.IsValid() && v.gameObject.scene.isLoaded) == 0, "death clears presentation");
            Status="通過";
        }
        catch (Exception ex) { Status="失敗"; report.Add("FAIL " + ex); Debug.LogError("[ShockwaveVerification] " + ex); }
        finally
        {
            if(runner!=null) { try { await runner.Shutdown(); } catch(Exception ex) { report.Add("cleanup " + ex); } runner=null; }
            foreach(var obj in temporary) if(obj!=null) UnityEngine.Object.Destroy(obj);
            temporary.Clear();
            Directory.CreateDirectory("Temp/ShockwaveVfxValidation");
            File.WriteAllLines("Temp/ShockwaveVfxValidation/single-runner.txt", report);
            Debug.Log("[ShockwaveVerification] " + Status);
        }
    }

    private static void Pose(Player player,Vector3 position,float pitch,float yaw)
    {
        var kcc=player.Movement.KCC;kcc.SetPosition(position);kcc.SetLookRotation(pitch,yaw);
        kcc.FixedData.BasePosition=kcc.FixedData.DesiredPosition=kcc.FixedData.TargetPosition=position;
        kcc.FixedData.LookPitch=pitch;kcc.FixedData.LookYaw=yaw;kcc.FixedData.DynamicVelocity=Vector3.zero;
        Physics.SyncTransforms();
    }
    private static EnemyActor Enemy(Vector3 position, bool preventKnockback)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
        var obj=runner.Spawn(prefab.GetComponent<NetworkObject>(),position,Quaternion.identity,onBeforeSpawned:(r,o)=>{
            foreach(var b in o.GetComponents<MonoBehaviour>())
                if(b is EnemyIdlePatrolBrain || b is EnemyAwarenessBrain || b is EnemyPerceptionController ||
                   b is EnemyCombatDecisionController || b is EnemyChaseBrain || b is EnemyPatrolNavigator) b.enabled=false;
        });
        if(preventKnockback) obj.gameObject.AddComponent<ActiveAbilityVerificationKnockbackBlocker>();
        var probe=new GameObject("controlled hit collider"); probe.transform.SetParent(obj.transform,false);
        probe.transform.position=position+Vector3.up;
        Vector3 inherited=obj.transform.lossyScale;
        probe.transform.localScale=new Vector3(1f/inherited.x,1f/inherited.y,1f/inherited.z);
        probe.AddComponent<SphereCollider>().radius=0.35f;
        return obj.GetComponent<EnemyActor>();
    }
    private static GameObject Box(Vector3 position,Vector3 scale,int layer)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name="Shockwave verification obstacle";
        box.layer=layer; box.transform.position=position; box.transform.localScale=scale; temporary.Add(box); return box;
    }
    private static void Set(object target,string name,object value,Type type=null) =>
        (type??target.GetType()).GetField(name,Private).SetValue(target,value);
    private static void Press(PlayerShockwaveAbility wave)
    { var input=new NetInput(); input.Buttons.Set(InputButton.Ability1,true); wave.SimulateAbility(input,default); }
    private static bool Near(float a,float b) => Mathf.Abs(a-b)<0.01f;
    private static void Check(bool value,string message)
    { if(!value) throw new InvalidOperationException(message); report.Add("PASS "+message); }
    private static async Task Until(Func<bool> condition,string label)
    {
        float deadline=Time.realtimeSinceStartup+15f;
        while(!condition()) { if(Time.realtimeSinceStartup>deadline) throw new TimeoutException(label); await Task.Delay(20); }
    }
}
#endif
