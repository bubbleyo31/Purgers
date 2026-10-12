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

/// <summary>明確啟動的真實 Fusion 單 Runner 驗證；受控位置／輸入不等同獨立 Host/Client。</summary>
public static class ActiveAbilityReleaseVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static NetworkRunner runner;
    private static readonly List<GameObject> world = new List<GameObject>();
    private static readonly List<string> report = new List<string>();
    private static int failures;
    private static readonly Vector3 Start = new Vector3(1200, 100, 1200);

    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null || UnityEngine.Object.FindObjectsOfType<NetworkRunner>().Any(r => r.IsRunning))
            throw new InvalidOperationException("僅在 Play Mode 且沒有其他運行中 Runner 時執行。");
        Status = "執行中"; report.Clear(); world.Clear(); failures = 0;
        try
        {
            runner = new GameObject("Release safety verification runner").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var (id, source) in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(source);
            var result = await runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config,
                SceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>() });
            if (!result.Ok) throw new InvalidOperationException(result.ToString());
            await Until(() => !runner.SceneManager.IsBusy, "scene ready");
            await Until(() => runner.Tick.Raw > 0, "first simulation tick");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var ownerObject = runner.Spawn(prefab.GetComponent<NetworkObject>(), Start, Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, ownerObject);
            var owner = ownerObject.GetComponent<Player>();
            int layer = 0;
            for (int i = 0; i < 32; i++)
                if ((owner.Movement.KCC.Settings.CollisionLayerMask.value & (1 << i)) != 0 && i != owner.gameObject.layer) { layer = i; break; }
            Box(Start + Vector3.down * .5f, new Vector3(40, 1, 40), layer);
            await Until(() => owner.AbilityRuntimeManager.LoadoutRevision > 0 && PlayerAbilityQualification.HasRangedWeapon(owner), "owner ready");
            foreach (string key in new[] { "Grenade", "HealingPack", "PiercingCannon" }) await VerifyRelease(owner, key);
            var otherObject = runner.Spawn(prefab.GetComponent<NetworkObject>(), Start + Vector3.forward * 3, Quaternion.identity, PlayerRef.None);
            var other = otherObject.GetComponent<Player>();
            VerifyGrenadeContacts(owner, other, layer);
        }
        catch (Exception ex) { failures++; report.Add("FAIL exception " + ex); }
        finally
        {
            if (runner != null) { try { await runner.Shutdown(); } catch (Exception ex) { failures++; report.Add("FAIL cleanup " + ex); } runner = null; }
            foreach (var obj in world) if (obj != null) UnityEngine.Object.Destroy(obj);
            world.Clear();
            Status = failures == 0 ? "通過" : "失敗 " + failures;
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + Status + " controlled Single Runner; not independent multiplayer");
            File.WriteAllLines(Path.GetFullPath("Temp/ActiveAbilityReleaseVerification.txt"), report);
            Debug.Log("[ActiveAbilityReleaseVerification] " + Status);
        }
    }

    private static async Task VerifyRelease(Player owner, string key)
    {
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/" + key + ".asset");
        if (!owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition, out string error)) throw new InvalidOperationException(error);
        await Task.Delay(100);
        var skill = PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(owner);
        // 只改測試用實例；讓觀測位置不受首幀投射距離干擾，不保存資產。
        typeof(PlayerActiveAbilityBase).GetField("castDelaySeconds", Private).SetValue(skill, .3f);
        if (skill is PlayerProjectileAbility)
        {
            typeof(PlayerProjectileAbility).GetField("projectileSpeed", Private).SetValue(skill, .1f);
            typeof(PlayerProjectileAbility).GetField("gravity", Private).SetValue(skill, 0f);
        }
        Pose(owner, Start, 0);
        var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
        skill.SimulateAbility(input, default);
        Check(skill.Phase == PlayerActiveAbilityPhase.Casting, key + " enters windup");
        await Task.Delay(70);
        Check(skill.Phase == PlayerActiveAbilityPhase.Casting && Projectile(skill) == null, key + " does not release during windup");
        Vector3 moved = Start + Vector3.forward * 4;
        Pose(owner, moved, 90);
        await Until(() => skill.Phase != PlayerActiveAbilityPhase.Casting, key + " releases");
        if (skill is PlayerPiercingCannonAbility)
        {
            Vector3 origin = (Vector3)typeof(PlayerPiercingCannonAbility).GetProperty("BeamStart", Private).GetValue(skill);
            Vector3 end = (Vector3)typeof(PlayerPiercingCannonAbility).GetProperty("BeamEnd", Private).GetValue(skill);
            Check(Vector3.Distance(origin, moved + Vector3.up * 1.5f) < .15f, key + " uses release position: " + origin);
            Check(Vector3.Dot((end - origin).normalized, Vector3.right) > .99f, key + " uses release aim");
        }
        else
        {
            var projectile = Projectile(skill);
            Check(projectile != null, key + " spawns projectile");
            if (projectile != null)
            {
                Check(Vector3.Distance(projectile.transform.position, moved + Vector3.up * 1.5f) < .15f, key + " uses release position: " + projectile.transform.position);
                Check(Vector3.Dot(projectile.Velocity.normalized, Vector3.right) > .99f, key + " uses release aim");
                Check(!projectile.HasImpacted && !projectile.IsResting, key + " is not caught by caster at release");
                runner.Despawn(projectile.Object);
            }
        }
    }

    private static PlayerAbilityProjectile Projectile(PlayerActiveAbilityBase skill) => UnityEngine.Object.FindObjectsOfType<PlayerAbilityProjectile>()
        .FirstOrDefault(p => p.Object != null && p.Object.IsValid && p.Runner == runner && p.LauncherObject == skill.Object);

    private static void VerifyGrenadeContacts(Player owner, Player other, int layer)
    {
        Pose(owner, Start, 0); Pose(other, Start + Vector3.forward * 3, 0);
        owner.Health.RestoreHealth(owner.Health.MaximumHealth); other.Health.RestoreHealth(other.Health.MaximumHealth);
        Vector3 origin = Start + Vector3.up + Vector3.back * 2;
        var grenade = SpawnGrenade(owner, origin);
        float ownerHp = owner.Health.CurrentHealth, otherHp = other.Health.CurrentHealth;
        Sweep(grenade, Vector3.forward * 7);
        Check(!grenade.HasImpacted && Vector3.Distance(grenade.transform.position, origin + Vector3.forward * 7) < .01f,
            "grenade crosses caster and teammate after owner clearance");
        Check(owner.Health.CurrentHealth == ownerHp && other.Health.CurrentHealth == otherHp, "flight crossing causes no explosion damage");
        if (grenade.Object.IsValid) runner.Despawn(grenade.Object);

        grenade = SpawnGrenade(owner, Start + Vector3.forward * 3 + Vector3.up);
        Sweep(grenade, Vector3.forward);
        Check(!grenade.HasImpacted, "grenade initial overlap with teammate is ignored");
        if (grenade.Object.IsValid) runner.Despawn(grenade.Object);

        // 實體牆必須仍引爆；爆炸不沿用飛行玩家排除。
        var wall = Box(Start + Vector3.forward * 5, new Vector3(4, 4, .1f), layer);
        grenade = SpawnGrenade(owner, Start + Vector3.forward * 4 + Vector3.up);
        Sweep(grenade, Vector3.forward * 2);
        Check(grenade.HasImpacted, "world contact still detonates");
        Check(Mathf.Abs(owner.Health.CurrentHealth - (ownerHp - 10)) < .01f, "explosion keeps full self damage");
        Check(Mathf.Abs(other.Health.CurrentHealth - (otherHp - 3.5f)) < .01f, "explosion keeps 35 percent teammate damage");
        if (grenade.Object.IsValid) runner.Despawn(grenade.Object);
        // 玩家爆炸倍率可獨立歸零；接觸世界仍必須產生爆炸事件。
        owner.Health.RestoreHealth(owner.Health.MaximumHealth); other.Health.RestoreHealth(other.Health.MaximumHealth);
        ownerHp = owner.Health.CurrentHealth; otherHp = other.Health.CurrentHealth;
        grenade = SpawnGrenade(owner, Start + Vector3.forward * 4 + Vector3.up);
        typeof(PlayerAbilityProjectile).GetField("grenadeSelfDamageMultiplier", Private).SetValue(grenade, 0f);
        typeof(PlayerAbilityProjectile).GetField("grenadeOtherPlayerDamageMultiplier", Private).SetValue(grenade, 0f);
        Sweep(grenade, Vector3.forward * 2);
        Check(grenade.HasImpacted && owner.Health.CurrentHealth == ownerHp && other.Health.CurrentHealth == otherHp,
            "zero player multipliers preserve explosion without self or teammate damage");
        if (grenade.Object.IsValid) runner.Despawn(grenade.Object);
        wall.SetActive(false);

        grenade = SpawnGrenade(owner, origin);
        typeof(PlayerAbilityProjectile).GetField("grenadeIgnoresPlayers", Private).SetValue(grenade, false);
        Sweep(grenade, Vector3.forward * 4);
        Check(grenade.HasImpacted, "disabled option restores player contact detonation");
        if (grenade.Object.IsValid) runner.Despawn(grenade.Object);
    }
    private static PlayerAbilityProjectile SpawnGrenade(Player owner, Vector3 origin)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/Active/GrenadeProjectile.prefab");
        return runner.Spawn(prefab.GetComponent<NetworkObject>(), origin, Quaternion.identity, runner.LocalPlayer,
            (r, o) => o.GetComponent<PlayerAbilityProjectile>().Initialize(r, owner.Object, null, PlayerAbilityProjectileKind.Grenade,
                Vector3.forward * 18, 0, .1f, ~0, 10, 10, .4f, 8, 1, 1)).GetComponent<PlayerAbilityProjectile>();
    }
    private static void Sweep(PlayerAbilityProjectile projectile, Vector3 delta) => typeof(PlayerAbilityProjectile)
        .GetMethod("Sweep", Private).Invoke(projectile, new object[] { runner.GetPhysicsScene(), delta });
    private static void Pose(Player player, Vector3 position, float yaw)
    {
        var kcc = player.Movement.KCC;
        kcc.SetPosition(position); kcc.SetLookRotation(0, yaw);
        // 受控測試於主執行緒注入 Fixed/Render；不是正式移動實作。
        kcc.FixedData.BasePosition = kcc.FixedData.DesiredPosition = kcc.FixedData.TargetPosition = position;
        kcc.FixedData.LookPitch = 0; kcc.FixedData.LookYaw = yaw;
        kcc.FixedData.DynamicVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }
    private static GameObject Box(Vector3 position, Vector3 scale, int layer)
    {
        var obj = new GameObject("Release safety test wall"); world.Add(obj);
        obj.layer = layer; obj.transform.position = position; obj.transform.localScale = scale;
        obj.AddComponent<BoxCollider>(); Physics.SyncTransforms(); return obj;
    }
    private static void Check(bool value, string message) { report.Add((value ? "PASS " : "FAIL ") + message); if (!value) failures++; }
    private static async Task Until(Func<bool> predicate, string label)
    {
        float start = Time.realtimeSinceStartup;
        while (!predicate()) { if (Time.realtimeSinceStartup - start > 12) throw new TimeoutException(label); await Task.Delay(10); }
    }
}
#endif
