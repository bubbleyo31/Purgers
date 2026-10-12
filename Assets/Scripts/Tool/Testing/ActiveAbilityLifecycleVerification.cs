#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>真實 Fusion 物件的受控生命週期回歸；狀態注入不等同獨立多人封包驗證。</summary>
public static class ActiveAbilityLifecycleVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> report = new List<string>();
    private static NetworkRunner runner;
    private static int failures;

    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null) throw new InvalidOperationException("需要 Play Mode 且尚未執行。");
        Status = "執行中"; report.Clear(); failures = 0;
        GameObject visualTemplate = null;
        try
        {
            runner = new GameObject("Active ability lifecycle verification").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var (id, source) in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(source);
            var started = await runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config,
                SceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>() });
            if (!started.Ok) throw new InvalidOperationException(started.ToString());
            await Until(() => !runner.SceneManager.IsBusy, "scene ready");
            await Task.Delay(100);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000, 100, 1000), Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, obj);
            var player = obj.GetComponent<Player>();
            await Until(() => player.AbilityRuntimeManager.LoadoutRevision > 0 && PlayerAbilityQualification.HasRangedWeapon(player), "player ready");

            // 模擬 Owner 比 Runtime 晚一個 snapshot 可用；不透過 Manager/HUD 查詢偷偷重綁。
            var delayed = runner.Spawn(Definition("Shield").RuntimePrefab, obj.transform.position, Quaternion.identity, runner.LocalPlayer,
                (r, o) => o.GetComponent<PlayerAbilityRuntime>().InitializeBeforeSpawn(null, 0));
            await Task.Delay(100);
            delayed.GetComponent<PlayerAbilityRuntime>().InitializeBeforeSpawn(obj, 0);
            await Task.Delay(200);
            Check(delayed.GetComponent<PlayerShieldAbility>().IsUsable, "late owner reference binds without input or HUD query");
            runner.Despawn(delayed);

            // 第一次呈現的命中狀態已過期，不可再建立完整壽命的爆炸特效。
            var pp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/Active/GrenadeProjectile.prefab");
            var po = runner.Spawn(pp.GetComponent<NetworkObject>(), obj.transform.position + Vector3.up * 10,
                Quaternion.identity, runner.LocalPlayer, (r, o) => o.GetComponent<PlayerAbilityProjectile>().Initialize(r, obj, null,
                    PlayerAbilityProjectileKind.Grenade, Vector3.zero, 0, .1f, 0, 0, 4, .4f, 100, 1, 1));
            var projectile = po.GetComponent<PlayerAbilityProjectile>();
            visualTemplate = new GameObject("LifecycleImpactTemplate");
            var slot = new PlayerAbilityLocalVfxSlot();
            typeof(PlayerAbilityLocalVfxSlot).GetField("prefab", Private).SetValue(slot, visualTemplate);
            typeof(PlayerAbilityProjectile).GetField("impactVfx", Private).SetValue(projectile, slot);
            typeof(PlayerAbilityProjectile).GetMethod("Finish", Private).Invoke(projectile, new object[] { true, projectile.transform.position, 4f });
            typeof(PlayerAbilityProjectile).GetProperty("ImpactTimer", Private).SetValue(projectile, TickTimer.None);
            projectile.Render();
            var replay = GameObject.Find("LifecycleImpactTemplate (Ability Local VFX)");
            Check(replay == null, "expired first impact snapshot does not replay local VFX");
            if (replay != null) UnityEngine.Object.Destroy(replay);
            runner.Despawn(po);

            await Equip(player, "BulletTime");
            var time = PlayerAbilityQualification.GetModule<PlayerBulletTimeAbility>(player);
            Press(time);
            float hp = player.Health.CurrentHealth;
            var request = new DamageRequest { BaseDamage = 7, RequestedDamage = 7, DamageType = DamageType.Ability,
                Attacker = runner.LocalPlayer, SourceObject = obj.gameObject, SourceNetworkObject = obj };
            DamageReceiverUtility.TryApplyDamage(obj.gameObject, request, out var queued);
            Check(queued.Deferred && Near(player.Health.CurrentHealth, hp), "bullet time queues damage without early health change");
            await Equip(player, "Shield");
            Check(Near(player.Health.CurrentHealth, hp - 7), "unequip flushes pending damage exactly once");
            Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(player), "unequip releases player slowdown registry");
            await Task.Delay(200);
            Check(Near(player.Health.CurrentHealth, hp - 7), "old session cannot settle twice");
            var shield = PlayerAbilityQualification.GetModule<PlayerShieldAbility>(player);
            Press(shield);
            Check(shield.CurrentShield > 0, "shield active before death");
            player.Health.ReceiveDamage(new DamageRequest { BaseDamage = 100000, RequestedDamage = 100000, DamageType = DamageType.Environment });
            await Task.Delay(100);
            Check(!player.Health.IsAlive && shield.CurrentShield == 0, "death clears shield through ability tick");
            runner.Despawn(obj);
            Check(!PlayerBulletTimeSessionRegistry.IsPlayerAffected(player), "despawn leaves no affected player entry");
        }
        catch (Exception ex) { failures++; report.Add("FAIL exception " + ex); }
        finally
        {
            if (runner != null) { try { await runner.Shutdown(); } catch (Exception ex) { failures++; report.Add("FAIL shutdown " + ex); } runner = null; }
            if (visualTemplate != null) UnityEngine.Object.Destroy(visualTemplate);
            Status = failures == 0 ? "通過" : "失敗 " + failures;
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + Status + " controlled Single Runner lifecycle verification");
            File.WriteAllLines(Path.GetFullPath("Temp/ActiveAbilityLifecycleVerification.txt"), report);
            Debug.Log("[ActiveAbilityLifecycleVerification] " + Status);
        }
    }
    private static PlayerAbilityDefinition Definition(string key) => AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/" + key + ".asset");
    private static async Task Equip(Player player, string key)
    {
        if (!player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(Definition(key), out string error)) throw new InvalidOperationException(error);
        await Task.Delay(100);
    }
    private static void Press(PlayerActiveAbilityBase module)
    {
        var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
        module.SimulateAbility(input, default);
    }
    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    private static void Check(bool passed, string message) { report.Add((passed ? "PASS " : "FAIL ") + message); if (!passed) failures++; }
    private static async Task Until(Func<bool> predicate, string label)
    {
        float start = Time.realtimeSinceStartup;
        while (!predicate()) { if (Time.realtimeSinceStartup - start > 12f) throw new TimeoutException(label); await Task.Delay(30); }
    }
}
#endif
