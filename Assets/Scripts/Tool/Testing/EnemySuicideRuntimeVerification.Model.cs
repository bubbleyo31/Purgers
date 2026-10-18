#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>沿用隔離 Runner 工具，驗證實際模型、權威幀時序與 Proxy 姿勢；不保存 Scene。</summary>
public static partial class EnemySuicideRuntimeVerification
{
    public static async void BeginModel(bool multiplayer = false)
    {
        if (!Application.isPlaying || host != null || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要沒有現存 Runner 的 Play Mode。");
        Status = "執行中"; report.Clear(); network = multiplayer; session = "suicide-model-" + Guid.NewGuid().ToString("N");
        try
        {
            host = await Connect(true);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(floor); host.MoveToRunnerScene(floor);
            floor.transform.position = Origin - Vector3.up * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
            var areaGo = new GameObject("Model test patrol area"); temporary.Add(areaGo); host.MoveToRunnerScene(areaGo);
            areaGo.transform.position = Origin; area = areaGo.AddComponent<EnemyPatrolArea>();
            var points = new GameObject("Points").transform; points.SetParent(areaGo.transform, false);
            new GameObject("A").transform.SetParent(points, false); area.ConfigureRuntime("suicide-model-test", points, 0);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab").GetComponent<NetworkObject>();
            var player = host.Spawn(playerPrefab, Origin, Quaternion.identity, host.LocalPlayer); host.SetPlayerObject(host.LocalPlayer, player);
            await Task.Delay(200); player.GetComponent<Player>().enabled = false;
            player.GetComponent<Fusion.Addons.KCC.KCC>().Collider.gameObject.layer = LayerMask.NameToLayer("Hit Mask");
            var enemy = SpawnEnemy(Origin + Vector3.forward * 12);
            var attack = enemy.GetComponent<EnemySuicideAttack>();
            Set(attack, "requireNavMeshLanding", false); Set(attack, "centerDamage", 0f); Set(attack, "edgeDamage", 0f);
            var perception = enemy.GetComponent<EnemyPerceptionController>();
            typeof(EnemyPerceptionController).GetProperty("CurrentTarget").SetValue(perception, player);
            typeof(EnemyPerceptionController).GetProperty("HasDirectSight").SetValue(perception, true);
            var combat = enemy.GetComponent<EnemyCombatDecisionController>();
            var context = new EnemyCombatContext(combat, enemy.GetComponent<EnemyActor>(), perception, player, Origin, 12f, host.DeltaTime);
            Stage = "frame eight takeoff";
            Invoke(combat, "StartOption", attack, context);
            Check(attack.TryGetPounceAnimationTime(out var first) && first < .15f, "startup reads synchronized timer near frame zero");
            int started = host.Tick.Raw;
            await Task.Delay(150);
            attack.TickOption(context);
            Check(!attack.HasExploded && !attack.IsAirborne && Mathf.Abs(enemy.transform.position.y - Origin.y) < .01f, "body stays grounded before frame eight");
            var animator = enemy.GetComponentInChildren<Animator>();
            Check(animator != null && !animator.applyRootMotion && animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Pounce"), "real Animator enters Pounce with root motion off");
            await Until(() => {attack.TickOption(context); return attack.IsAirborne;}, "takeoff");
            float preElapsed = (host.Tick.Raw - started) * host.DeltaTime;
            Check(Mathf.Abs(preElapsed - 8f / 24f) < .12f, "takeoff occurs at frame eight; observed seconds=" + preElapsed);
            int airborneTick = host.Tick.Raw;
            await Task.Delay(275); attack.TickOption(context);
            Check(!attack.HasExploded && attack.TryGetPounceAnimationTime(out var tail) && tail > .999f, "tail pose reached before flight deadline");
            await Task.Delay(40);
            Check(animator.GetFloat("PounceNormalizedTime") > .999f, "presentation driver holds final normalized pose");
            var body = animator.transform.Find("物体/body");
            Vector3 heldBody = body.localPosition;
            var bone = animator.transform.Find("物体/body").GetChild(0);
            Quaternion heldRotation = bone.localRotation;

            if (network)
            {
                Stage = "late join at held tail";
                // 只在隔離測試暫停攻擊計時，以留出 Photon 連線時間；不修改正式 0.6 秒設定。
                int pausedTick = host.Tick.Raw;
                var joining = Connect(false);
                while (!joining.IsCompleted)
                {
                    int now = host.Tick.Raw;
                    for (int tick = pausedTick; tick < now; tick++) attack.PauseSimulationTick();
                    pausedTick = now;
                    await Task.Delay(5);
                }
                client = await joining;
                NetworkObject proxy = null;
                await Until(() =>
                {
                    int now = host.Tick.Raw;
                    for (int tick = pausedTick; tick < now; tick++) attack.PauseSimulationTick();
                    pausedTick = now;
                    proxy = client.FindObject(enemy.Id);
                    return proxy != null && proxy.GetComponentInChildren<Animator>() != null &&
                        proxy.GetComponentInChildren<Animator>().GetFloat("PounceNormalizedTime") > .999f &&
                        proxy.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Pounce");
                }, "late join receives airborne pose");
                var remote = proxy.GetComponent<EnemySuicideAttack>();
                Check(!proxy.HasStateAuthority && remote.IsAirborne && remote.TryGetPounceAnimationTime(out var remoteTime) && remoteTime > .999f,
                    "late join proxy restores held tail from authoritative snapshot");
                Check(proxy.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Pounce"), "late join Animator is in Pounce");
            }
            else
            {
                await Task.Delay(100); attack.TickOption(context);
                Check(!attack.HasExploded && Vector3.Distance(body.localPosition, heldBody) < .00001f &&
                    Quaternion.Angle(bone.localRotation, heldRotation) < .01f, "airborne bones remain at final pose");
            }
            await Until(() => {attack.TickOption(context); return attack.HasExploded;}, "flight expires");
            if (!network)
            {
                float elapsed = (host.Tick.Raw - airborneTick) * host.DeltaTime;
                Check(Mathf.Abs(elapsed - .6f) < .12f, "flight deadline remains 0.6 seconds; observed=" + elapsed);
            }
            Check(enemy.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled || !r.gameObject.activeInHierarchy), "explosion hides all 22 model renderers");
            if (network)
            {
                await Until(() => {var proxy = client.FindObject(enemy.Id); return proxy == null ||
                    proxy.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled || !r.gameObject.activeInHierarchy);}, "proxy death presentation");
                Check(true, "proxy hides model after authoritative explosion");
            }
            Check(enemy.GetComponent<CapsuleCollider>().height == 2.1f, "runtime collision capsule preserves 2.1 meter height");
            Status = "通過";
        }
        catch (Exception ex) {Status = "失敗"; report.Add("FAIL " + Stage + ": " + ex); Debug.LogError("[Suicide Model Verification] " + ex);}
        finally
        {
            if (client != null) {await client.Shutdown(); client = null;}
            if (host != null) {await host.Shutdown(); host = null;}
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj);
            temporary.Clear();
            Directory.CreateDirectory("Temp/SuicideModel");
            File.WriteAllLines("Temp/SuicideModel/" + (network ? "host-client" : "single") + ".txt", report);
        }
    }
}
#endif
