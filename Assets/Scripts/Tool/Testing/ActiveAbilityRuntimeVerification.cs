#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>明確呼叫的單 Runner 權威煙霧驗證；不改 Scene／Prefab，不能當作獨立多人證據。</summary>
public static class ActiveAbilityRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private static NetworkRunner runner;
    private static readonly List<GameObject> world = new List<GameObject>();
    private static readonly List<string> report = new List<string>();
    private static string OutputPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ActiveAbilityRuntimeVerification.txt"));

    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null) throw new InvalidOperationException("只在 Play Mode 且未驗證時執行。");
        Status = "執行中"; report.Clear();
        try
        {
            runner = new GameObject("ActiveAbility verification runner").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var (id, source) in NetworkProjectConfig.Global.PrefabTable.GetEntries())
                config.PrefabTable.AddSource(source);
            var sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            var started = await runner.StartGame(new StartGameArgs { GameMode = GameMode.Single,
                Config = config, SceneManager = sceneManager });
            Check(started.Ok, "Single Runner started");
            await Until(() => !runner.SceneManager.IsBusy, 10f, "runner scene manager");
            await Task.Delay(100);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var playerObject = runner.Spawn(prefab.GetComponent<NetworkObject>(),
                new Vector3(1000f, 100f, 1000f), Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, playerObject);
            Player player = playerObject.GetComponent<Player>();
            int worldLayer = 0;
            for (int layer = 0; layer < 32; layer++)
                if ((player.Movement.KCC.Settings.CollisionLayerMask.value & (1 << layer)) != 0 && layer != player.gameObject.layer)
                { worldLayer = layer; break; }
            Box(new Vector3(1000f, 99.5f, 1000f), new Vector3(30f, 1f, 30f), worldLayer);
            await Until(() => player.AbilityRuntimeManager.LoadoutRevision > 0, 10f, "initial loadout");
            await Until(() => PlayerAbilityQualification.HasRangedWeapon(player), 10f, "ranged profession");
            Check(player.Health.IsAlive, "Player health spawned");
            foreach (string key in new[] { "Shield", "Experience", "Blink", "BulletTime", "Grenade", "HealingPack", "Ricochet", "PiercingCannon", "Shockwave", "PrecisionLock" })
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(
                    "Assets/_Project_Assets/Data/PlayerAbility/Active/" + key + ".asset");
                Check(player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(def, out string error), key + " equip: " + error);
                await Task.Delay(100);
                var skill = PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(player);
                Check(skill != null && skill.IsUsable, key + " usable");
                player.Health.RestoreHealth(player.Health.MaximumHealth);
                float hp = player.Health.CurrentHealth;
                Press(skill);
                Check(skill.ActivationSequence == 1, key + " E accepted exactly once");
                if (key == "Shield")
                {
                    await Task.Delay(70);
                    float shield = player.Health.MaximumHealth * .25f;
                    Apply(player, 10f, false);
                    Check(Mathf.Abs(player.Health.CurrentHealth - hp) < .01f, "shield absorbs before health");
                    Apply(player, shield, false);
                    Check(Mathf.Abs(player.Health.CurrentHealth - (hp - 10f)) < .01f, "shield overflow preserves exact damage");
                }
                else if (key == "Experience")
                {
                    Apply(player, 10f, false);
                    Check(Mathf.Abs(player.Health.CurrentHealth - (hp - 20f)) < .01f, "experience buff doubles received damage");
                    Check(((IPlayerExperienceMultiplier)skill).ExperienceMultiplier == 2f, "personal experience multiplier active");
                    Check(skill.CooldownRemainingSeconds == 0f, "experience cooldown waits for end");
                }
                else if (key == "Blink")
                {
                    Check(skill.Phase == PlayerActiveAbilityPhase.Armed && skill.CooldownRemainingSeconds == 0f, "blink armed without cooldown");
                    player.Movement.KCC.SetPosition(new Vector3(1000f,100.03f,1000f));
                    player.Movement.KCC.SetLookRotation(0f, 0f);
                    GameObject wall = Box(new Vector3(1000f, 102f, 1004f), new Vector3(10f, 5f, .3f), worldLayer);
                    Physics.SyncTransforms();
                    Press(skill);
                    Check(skill.Phase == PlayerActiveAbilityPhase.Dashing && skill.CooldownRemainingSeconds > 0f, "second E dashes and starts cooldown");
                    var kcc = player.Movement.KCC;
                    var probe = new Fusion.Addons.KCC.KCCShapeCastInfo();
                    kcc.CapsuleCast(probe, kcc.FixedData.TargetPosition, kcc.Settings.Radius, kcc.Settings.Height,
                        Vector3.forward, 1f, QueryTriggerInteraction.Ignore, true);
                    for (int h=0; h<probe.ColliderHitCount; h++) {
                        var hit=probe.ColliderHits[h].RaycastHit;
                        report.Add("BLINK initial hit " + hit.collider.name + " distance=" + hit.distance + " normal=" + hit.normal + " point=" + hit.point);
                    }
                    report.Add("BLINK fixed=" + kcc.FixedData.TargetPosition + " deltaTime=" + kcc.FixedData.DeltaTime + " active=" + kcc.FixedData.IsActive);
                    await Task.Delay(900);
                    report.Add("BLINK end=" + kcc.FixedData.TargetPosition + " phase=" + skill.Phase + " velocity=" + kcc.FixedData.DesiredVelocity);
                    float traveled = player.Movement.KCC.Data.TargetPosition.z - 1000f;
                    Check(traveled > 1f && traveled < 4f, "grounded blink travels and stops before wall: " + traveled);
                    UnityEngine.Object.Destroy(wall);
                }
                else if (key == "BulletTime")
                {
                    DamageResult result = Apply(player, 10f, true);
                    Check(!result.Accepted && Math.Abs(player.Health.CurrentHealth - hp) < .01f, "bullet time does not apply damage early");
                    await Task.Delay(2350);
                    Check(Math.Abs(player.Health.CurrentHealth - (hp - 10f)) < .01f, "bullet time settles exactly once");
                }
                else if (key == "HealingPack")
                {
                    await Task.Delay(400);
                    Check(skill.Phase == PlayerActiveAbilityPhase.AwaitingPickup && skill.CooldownRemainingSeconds == 0f, "healing pack waits for pickup");
                }
                else if (key == "PrecisionLock")
                {
                    Check(skill.Phase == PlayerActiveAbilityPhase.Active && skill.ActiveRemainingSeconds > 1f, "precision lock active for two seconds");
                }
                else
                {
                    await Task.Delay(400);
                    Check(skill.CooldownRemainingSeconds > 0f, key + " launched and starts cooldown");
                }
            }
            VerifyCloseWallRicochet(player, worldLayer);
            await VerifyEnemyControl(player, worldLayer);
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + ex); Debug.LogError("[ActiveAbilityVerification] " + ex); }
        finally
        {
            if (runner != null)
            {
                try { await runner.Shutdown(); } catch (Exception ex) { report.Add("Cleanup: " + ex.Message); }
                runner = null;
            }
            foreach (var item in world) if (item != null) UnityEngine.Object.Destroy(item);
            world.Clear();
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + Status + " (controlled Single Runner, not independent multiplayer)");
            File.WriteAllLines(OutputPath, report);
            Debug.Log("[ActiveAbilityVerification] " + Status + " " + OutputPath);
        }
    }
    private static GameObject Box(Vector3 position, Vector3 scale, int layer)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = "ActiveAbility verification obstacle"; obj.layer = layer;
        obj.transform.position = position; obj.transform.localScale = scale;
        world.Add(obj); return obj;
    }

    private static void VerifyCloseWallRicochet(Player player, int worldLayer)
    {
        player.Movement.KCC.SetPosition(new Vector3(1000f, 100f, 1000f));
        player.Health.RestoreHealth(player.Health.MaximumHealth);
        Vector3 origin = player.transform.position + Vector3.up;
        GameObject wall = Box(origin + Vector3.forward * .8f, new Vector3(3f, 3f, .1f), worldLayer);
        Physics.SyncTransforms();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/AbilityRuntime/Active/RicochetProjectile.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), origin, Quaternion.identity,
            player.Object.InputAuthority, (r, o) => o.GetComponent<PlayerAbilityProjectile>().Initialize(r,
                player.Object, null, PlayerAbilityProjectileKind.Ricochet, Vector3.forward * 60f,
                0f, .1f, ~0, 10f, 4f, .4f, 8f, 1, 1));
        float before = player.Health.CurrentHealth;
        typeof(PlayerAbilityProjectile).GetMethod("Sweep",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(obj.GetComponent<PlayerAbilityProjectile>(), new object[] { runner.GetPhysicsScene(), Vector3.forward * 1.3f });
        Check(Mathf.Abs(player.Health.CurrentHealth - (before - 20f)) < .01f,
            "same-tick wall ricochet deals doubled full self damage");
        if (obj.IsValid) runner.Despawn(obj);
        UnityEngine.Object.Destroy(wall);
    }

    private static async Task VerifyEnemyControl(Player player, int worldLayer)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Variants/Enemy_Melee_A.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1100f, 100f, 1000f),
            Quaternion.identity, onBeforeSpawned: (r, o) => {
                foreach (var behaviour in o.GetComponents<MonoBehaviour>())
                    if (behaviour is EnemyIdlePatrolBrain || behaviour is EnemyAwarenessBrain ||
                        behaviour is EnemyPerceptionController || behaviour is EnemyCombatDecisionController ||
                        behaviour is EnemyChaseBrain || behaviour is EnemyPatrolNavigator) behaviour.enabled = false;
            });
        var enemy = obj.GetComponent<EnemyActor>();
        Box(new Vector3(1100f,102f,1003.5f), new Vector3(10f,5f,.3f), worldLayer);
        Physics.SyncTransforms();
        await Task.Delay(100);
        float before = enemy.Health.CurrentHealth;
        var wallDamage = new DamageRequest { RequestedDamage=5f, BaseDamage=5f,
            DamageType=DamageType.Ability, Attacker=player.Object.InputAuthority,
            SourceNetworkObject=player.Object, SourceObject=player.gameObject };
        Check(enemy.GetComponent<CapsuleCollider>() == null, "production enemy uses collider-independent knockback shape");
        Check(enemy.StateController.TryBeginAbilityKnockback(Vector3.forward, 4f, 12f, 1f, wallDamage),
            "production enemy accepts knockback without CapsuleCollider");
        await Task.Delay(500);
        Check(enemy.transform.position.z > 1001f && enemy.transform.position.z < 1003.5f, "enemy stops before wall");
        Check(Mathf.Abs(enemy.Health.CurrentHealth - (before - 5f)) < .01f, "wall extra damage applied once");
        Check(enemy.StateController.IsAbilityStunned, "wall hit stuns enemy");
        await Task.Delay(1200);
        Check(!enemy.StateController.IsAbilityStunned && Mathf.Abs(enemy.Health.CurrentHealth-(before-5f)) < .01f,
            "stun expires without duplicate wall damage");
        var blocker = enemy.gameObject.AddComponent<ActiveAbilityVerificationKnockbackBlocker>();
        Check(!enemy.StateController.TryBeginAbilityKnockback(Vector3.forward, 4f, 12f, 1f, wallDamage),
            "optional enemy component can reject knockback");
        UnityEngine.Object.Destroy(blocker);
        runner.Despawn(obj);
    }

    private static void Press(PlayerActiveAbilityBase skill)
    {
        var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
        skill.SimulateAbility(input, default);
    }
    private static DamageResult Apply(Player player, float amount, bool playerSource)
    {
        var request = new DamageRequest {
            RequestedDamage = amount, BaseDamage = amount, DamageType = DamageType.Environment,
            Attacker = playerSource ? player.Object.InputAuthority : PlayerRef.None,
            SourceObject = playerSource ? player.gameObject : null,
            SourceNetworkObject = playerSource ? player.Object : null,
            HitPoint = player.transform.position
        };
        DamageReceiverUtility.TryApplyDamage(player.gameObject, request, out DamageResult result);
        return result;
    }
    private static async Task Until(Func<bool> condition, float seconds, string label)
    {
        float timeout = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > timeout) throw new TimeoutException(label);
            await Task.Delay(50);
        }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        report.Add("PASS " + message);
    }
}
public sealed class ActiveAbilityVerificationKnockbackBlocker : MonoBehaviour, IEnemyAbilityKnockbackCondition
{
    public bool CanBeKnockedBack(EnemyStateController enemy) => false;
}
#endif


