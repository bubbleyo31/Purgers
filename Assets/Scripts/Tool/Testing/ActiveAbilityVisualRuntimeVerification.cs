#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 明確呼叫的 Single Runner 呈現驗證。只觀察真正的相機渲染，從不手動呼叫調色 Begin／Bind。
/// 不改 authored Scene／Prefab；結果不代表獨立 Host／Client、晚加入或畫面美術驗收。
/// </summary>
public static class ActiveAbilityVisualRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private static NetworkRunner runner;
    private static GameObject runnerRoot;
    private static Player player;
    private static PlayerAbilityColorGrading compositor;
    private static Camera postCamera;
    private static CameraFollow cameraFollow;
    private static Transform oldCameraTarget;
    private static Vector3 oldCameraPosition;
    private static Quaternion oldCameraRotation;
    private static string observing;
    private static string callbackFailure;
    private static int renderedFrames, completedGameFrames, healingEvents;
    private static float healedAmount;
    private static RenderSample pendingSample;
    private static readonly List<GameObject> world = new List<GameObject>();
    private static readonly List<string> report = new List<string>();
    private static readonly Dictionary<Camera, bool> originalPost = new Dictionary<Camera, bool>();
    private static string OutputPath => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../Temp/ActiveAbilityVisualRuntimeVerification.txt"));

    private sealed class RenderSample
    {
        public Camera Camera;
        public int Frame;
        public string Label;
        public Vector4 Lift, Gamma, Gain, BaselineGamma;
    }

    public static async void Begin()
    {
        if (!Application.isPlaying || runner != null || Status == "執行中")
            throw new InvalidOperationException("只在 Play Mode 且未驗證時執行。");
        Status = "執行中";
        report.Clear(); world.Clear(); originalPost.Clear();
        observing = callbackFailure = null;
        pendingSample = null;
        renderedFrames = completedGameFrames = healingEvents = 0;
        healedAmount = 0f;
        float startedAt = Time.realtimeSinceStartup;
        try
        {
            foreach (var existing in UnityEngine.Object.FindObjectsOfType<NetworkRunner>())
                Check(!existing.IsRunning, "沒有其他運作中的 Runner，避免接管既有遊戲畫面");
            cameraFollow = CameraFollow.Singleton;
            Check(cameraFollow != null, "既有 CameraFollow 存在");
            oldCameraTarget = Read<Transform>(cameraFollow, "target");
            oldCameraPosition = cameraFollow.transform.position;
            oldCameraRotation = cameraFollow.transform.rotation;
            postCamera = ResolvePostCamera(cameraFollow);
            Check(postCamera != null, "既有 Game camera stack 有有效末端相機");
            foreach (Camera camera in cameraFollow.GetComponentsInChildren<Camera>(true))
            {
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) originalPost[camera] = data.renderPostProcessing;
            }
            report.Add("CAMERA observed final stack camera=" + postCamera.name);

            runnerRoot = new GameObject("ActiveAbility visual verification runner");
            runner = runnerRoot.AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var (id, source) in NetworkProjectConfig.Global.PrefabTable.GetEntries())
                config.PrefabTable.AddSource(source);
            var sceneManager = runnerRoot.AddComponent<NetworkSceneManagerDefault>();
            var started = await runner.StartGame(new StartGameArgs {
                GameMode = GameMode.Single, Config = config, SceneManager = sceneManager });
            Check(started.Ok, "Single Runner started");
            await Until(() => !runner.SceneManager.IsBusy, 4f, "runner scene manager ready");
            // SceneManager 就緒早於首個 Fusion state；首輪模擬後才生成 KCC。
            await Task.Delay(100);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            Check(prefab != null, "KCC_Player prefab exists");
            var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000f, 100f, 1000f),
                Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, obj);
            player = obj.GetComponent<Player>();
            Check(obj.HasStateAuthority && obj.HasInputAuthority, "受控玩家具 State/Input Authority");
            int worldLayer = 0;
            for (int layer = 0; layer < 32; layer++)
                if ((player.Movement.KCC.Settings.CollisionLayerMask.value & (1 << layer)) != 0 && layer != player.gameObject.layer)
                { worldLayer = layer; break; }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ActiveAbility visual verification floor";
            floor.layer = worldLayer;
            floor.transform.position = new Vector3(1000f, 99.5f, 1000f);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            world.Add(floor);
            Physics.SyncTransforms();
            await Until(() => player.AbilityRuntimeManager.LoadoutRevision > 0 &&
                PlayerAbilityQualification.HasRangedWeapon(player), 4f, "initial loadout and ranged profession");
            await Until(() => player.GetComponent<PlayerAbilityColorGrading>() != null, 1f,
                "PlayerLocalView naturally binds PlayerAbilityColorGrading");
            compositor = player.GetComponent<PlayerAbilityColorGrading>();
            Check(Read<Player>(compositor, "owner") == player && Read<Transform>(cameraFollow, "target") == player.Movement.CamTarget,
                "調色及 CameraFollow 由正式本機玩家流程綁定");
            // 必須在正式 Bind 訂閱之後觀察，End 中會看見該相機已完成的 URP stack。
            RenderPipelineManager.beginCameraRendering += ObserveBeginCamera;
            RenderPipelineManager.endCameraRendering += ObserveEndCamera;
            player.Health.LocalHealingReceived += ObserveHealing;
            await Until(() => completedGameFrames > 0, 1.5f, "既有末端 Game camera 真正完成渲染");
            Check(healingEvents == 0 && float.IsNegativeInfinity(Read<float>(compositor, "lastHealTime")),
                "出生滿血不產生治療脈衝");

            var shield = await Equip("Shield");
            Press(shield);
            await Until(() => shield.Phase == PlayerActiveAbilityPhase.Active, 0.8f, "shield active");
            AssertHud(shield, PlayerActiveAbilityPhase.Active, 1.5f, "Shield");
            await Until(() => shield.Phase == PlayerActiveAbilityPhase.Ready, 2f, "shield naturally expires");
            Check(!shield.TimedHudState.IsVisible && shield.CooldownRemainingSeconds > 0f,
                "Shield 到期後隱藏時間條並進入冷卻");

            var experience = await Equip("Experience");
            Press(experience);
            await Until(() => experience.Phase == PlayerActiveAbilityPhase.Active, 0.8f, "experience active");
            AssertHud(experience, PlayerActiveAbilityPhase.Active, 5f, "Experience");
            await ObserveTint("Experience", "experienceWeight");
            float remaining = experience.TimedHudState.RemainingSeconds;
            Check(remaining < 5f && remaining > 0f && experience.CooldownRemainingSeconds == 0f,
                "Experience HUD 倒數中且尚未開始冷卻");

            var blink = await Equip("Blink");
            await Until(() => Read<float>(compositor, "experienceWeight") <= 0.001f, 0.8f,
                "卸下 Experience 後既有調色自然淡出");
            observing = null;
            Press(blink);
            AssertHud(blink, PlayerActiveAbilityPhase.Armed, 5f, "Blink");
            Press(blink);
            Check(blink.ActivationSequence == 1 && !blink.TimedHudState.IsVisible && blink.CooldownRemainingSeconds > 0f,
                "Blink 二次 E 啟動衝刺、隱藏待命倒數且不重複建立施放序號");
            await Until(() => blink.Phase == PlayerActiveAbilityPhase.Ready, 1f, "blink dash completes");

            var bullet = await Equip("BulletTime");
            Press(bullet);
            await Until(() => bullet.Phase == PlayerActiveAbilityPhase.Active, 0.8f, "bullet time active");
            AssertHud(bullet, PlayerActiveAbilityPhase.Active, 2f, "BulletTime");
            await ObserveTint("BulletTime", "bulletWeight");
            await Until(() => bullet.Phase == PlayerActiveAbilityPhase.Ready, 2.5f, "bullet time naturally expires");
            await Until(() => Read<float>(compositor, "bulletWeight") <= 0.001f, 0.8f, "bullet time tint fades out");
            Check(!bullet.TimedHudState.IsVisible, "BulletTime 到期後隱藏時間條");

            var precision = await Equip("PrecisionLock");
            Press(precision);
            await Until(() => precision.Phase == PlayerActiveAbilityPhase.Active, 0.8f, "precision active");
            AssertHud(precision, PlayerActiveAbilityPhase.Active, 2f, "PrecisionLock");
            await ObserveTint("PrecisionLock", "precisionWeight");
            await Until(() => precision.Phase == PlayerActiveAbilityPhase.Ready, 2.5f, "precision naturally expires");
            await Until(() => Read<float>(compositor, "precisionWeight") <= 0.001f, 0.8f, "precision tint fades out");
            Check(!precision.TimedHudState.IsVisible, "PrecisionLock 到期後隱藏時間條");
            observing = null;

            Check(!player.Health.RestoreHealth(10f, out float fullApplied) && fullApplied == 0f,
                "滿血 RestoreHealth 正式拒絕且回復量為 0");
            int frameBefore = completedGameFrames;
            await Until(() => completedGameFrames >= frameBefore + 2, 0.8f, "full-health render frames");
            Check(healingEvents == 0 && float.IsNegativeInfinity(Read<float>(compositor, "lastHealTime")),
                "滿血不送 LocalHealingReceived RPC 回饋、不觸發調色脈衝");
            float beforeDamage = player.Health.CurrentHealth;
            var request = new DamageRequest { RequestedDamage = 20f, BaseDamage = 20f,
                DamageType = DamageType.Environment, Attacker = PlayerRef.None, HitPoint = player.transform.position };
            Check(DamageReceiverUtility.TryApplyDamage(player.gameObject, request, out DamageResult result) && result.HasEffectiveDamage &&
                player.Health.CurrentHealth < beforeDamage, "正式傷害入口建立可回復血量");
            observing = "Healing";
            int beforeHealingFrames = renderedFrames;
            Check(player.Health.RestoreHealth(10f, out float actualHeal) && Mathf.Abs(actualHeal - 10f) < 0.001f,
                "正式 RestoreHealth 實際回復 10 HP");
            await Until(() => healingEvents == 1 && renderedFrames > beforeHealingFrames, 0.8f,
                "成功回血 RPC 導致一次本機事件及真實淺綠 Volume 渲染");
            Check(Mathf.Abs(healedAmount - actualHeal) < 0.001f, "治療事件量等於實際回復量");
            float healingTime = Read<float>(compositor, "lastHealTime");
            var settings = Read<AbilityColorGradingSettings>(compositor, "settings");
            await Until(() => Time.unscaledTime - healingTime > settings.healingPulseSeconds + 0.05f, 1.2f,
                "healing pulse naturally expires");
            int beforeQuiet = renderedFrames;
            frameBefore = completedGameFrames;
            await Until(() => completedGameFrames >= frameBefore + 2, 0.8f, "healing ended render frames");
            Check(healingEvents == 1 && Read<float>(compositor, "lastHealTime") == healingTime && renderedFrames == beforeQuiet,
                "治療脈衝到期後不重播、不重複事件、不再注入技能 Volume");
            Check(callbackFailure == null, "所有觀測相機的 Volume stack 與後製還原一致");
            report.Add("Elapsed gameplay/setup seconds=" + (Time.realtimeSinceStartup - startedAt).ToString("F2"));
            Status = "通過";
        }
        catch (Exception ex)
        {
            Status = "失敗";
            report.Add("FAIL " + ex);
            Debug.LogError("[ActiveAbilityVisualVerification] " + ex);
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= ObserveBeginCamera;
            RenderPipelineManager.endCameraRendering -= ObserveEndCamera;
            if (player != null && player.Health != null) player.Health.LocalHealingReceived -= ObserveHealing;
            if (runner != null)
            {
                try { await runner.Shutdown(); }
                catch (Exception ex) { Status = "失敗"; report.Add("FAIL cleanup runner: " + ex); }
            }
            if (runnerRoot != null) UnityEngine.Object.Destroy(runnerRoot);
            runnerRoot = null; runner = null; player = null; compositor = null;
            foreach (var item in world) if (item != null) UnityEngine.Object.Destroy(item);
            world.Clear();
            foreach (var pair in originalPost)
            {
                if (pair.Key == null) continue;
                var data = pair.Key.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = pair.Value;
            }
            originalPost.Clear();
            if (cameraFollow != null)
            {
                cameraFollow.SetTarget(oldCameraTarget);
                cameraFollow.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
            }
            cameraFollow = null; postCamera = null; oldCameraTarget = null; pendingSample = null;
            report.Insert(0, DateTime.UtcNow.ToString("O") + " " + Status +
                " (controlled Single Runner; not independent multiplayer or screenshot acceptance)");
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllLines(OutputPath, report);
            Debug.Log("[ActiveAbilityVisualVerification] " + Status + " " + OutputPath);
        }
    }

    private static async Task<PlayerActiveAbilityBase> Equip(string key)
    {
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(
            "Assets/_Project_Assets/Data/PlayerAbility/Active/" + key + ".asset");
        Check(definition != null && player.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(definition, out _), key + " equipped");
        PlayerActiveAbilityBase skill = null;
        await Until(() => {
            skill = PlayerAbilityQualification.GetModule<PlayerActiveAbilityBase>(player);
            return skill != null && skill.GetType().Name == "Player" + key + "Ability" && skill.IsUsable;
        }, 0.8f, key + " runtime ready");
        return skill;
    }

    private static void Press(PlayerActiveAbilityBase skill)
    {
        var input = new NetInput();
        input.Buttons.Set(InputButton.Ability1, true);
        skill.SimulateAbility(input, default);
        Check(skill.ActivationSequence == 1, skill.GetType().Name + " E accepted once through SimulateAbility");
    }

    private static void AssertHud(PlayerActiveAbilityBase skill, PlayerActiveAbilityPhase phase, float duration, string label)
    {
        var state = skill.TimedHudState;
        Check(state.Phase == phase && state.IsVisible && Mathf.Abs(state.DurationSeconds - duration) < 0.01f &&
            state.RemainingSeconds > 0f && state.RemainingSeconds <= duration + 0.01f && state.NormalizedRemaining > 0f,
            label + " TimedHudState phase=" + state.Phase + " duration=" + state.DurationSeconds + " remaining=" + state.RemainingSeconds);
    }

    private static async Task ObserveTint(string label, string weightField)
    {
        observing = label;
        int before = renderedFrames;
        await Until(() => Read<float>(compositor, weightField) >= 0.5f && renderedFrames > before, 1f,
            label + " 真實相機完成技能 Volume 渲染");
        Check(true, label + " local weight=" + Read<float>(compositor, weightField).ToString("F3") + " rendered frames=" + (renderedFrames - before));
    }

    private static void ObserveHealing(float amount) { healingEvents++; healedAmount += amount; }

    private static void ObserveBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (compositor == null || camera != postCamera || camera.cameraType != CameraType.Game) return;
        try
        {
            var volume = Read<Volume>(compositor, "runtimeVolume");
            if (volume == null || !volume.enabled || Read<Camera>(compositor, "activeRenderCamera") != camera) return;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (data == null || !data.renderPostProcessing) throw new InvalidOperationException("技能 Volume 已開啟但末端相機未啟用後製。");
            var grading = Read<LiftGammaGain>(compositor, "grading");
            var baseline = Read<VolumeStack>(compositor, "baselineStack").GetComponent<LiftGammaGain>();
            pendingSample = new RenderSample { Camera = camera, Frame = Time.frameCount, Label = observing,
                Lift = grading.lift.value, Gamma = grading.gamma.value, Gain = grading.gain.value, BaselineGamma = baseline.gamma.value };
        }
        catch (Exception ex) { callbackFailure = ex.ToString(); }
    }

    private static void ObserveEndCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera != postCamera || camera.cameraType != CameraType.Game) return;
        completedGameFrames++;
        try
        {
            var sample = pendingSample;
            pendingSample = null;
            if (sample == null || sample.Camera != camera || sample.Frame != Time.frameCount || compositor == null) return;
            var actual = VolumeManager.instance.stack.GetComponent<LiftGammaGain>();
            if (actual == null || !Near(actual.lift.value, sample.Lift) || !Near(actual.gamma.value, sample.Gamma) || !Near(actual.gain.value, sample.Gain))
                throw new InvalidOperationException("技能 LGG 未進入實際完成渲染相機的 Volume stack；不得以私有權重非零判作呈現通過。");
            var volume = Read<Volume>(compositor, "runtimeVolume");
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if ((volume != null && volume.enabled) || data.renderPostProcessing != originalPost[camera])
                throw new InvalidOperationException("相機完成渲染後未還原技能 Volume 或 authored 後製開關。");
            Vector4 tint = sample.Gamma - sample.BaselineGamma;
            if (sample.Label == "Experience" && !(tint.x > 0f && tint.y > 0f && tint.z < 0f))
                throw new InvalidOperationException("Experience 實際 Gamma 色偏不符合黃色。");
            if (sample.Label == "BulletTime" && !(tint.z > tint.x && tint.x > 0f && tint.y < 0f))
                throw new InvalidOperationException("BulletTime 實際 Gamma 色偏不符合紫色。");
            if (sample.Label == "PrecisionLock" && !(tint.y > 0f && tint.z > 0f && tint.x < 0f))
                throw new InvalidOperationException("PrecisionLock 實際 Gamma 色偏不符合藍綠色。");
            if (sample.Label == "Healing" && !(tint.y > 0f && tint.x < 0f && tint.z < 0f))
                throw new InvalidOperationException("Healing 實際 Gamma 色偏不符合淺綠色。");
            if (sample.Label != null) renderedFrames++;
        }
        catch (Exception ex) { callbackFailure = ex.ToString(); }
    }

    private static Camera ResolvePostCamera(CameraFollow follow)
    {
        foreach (Camera camera in follow.GetComponentsInChildren<Camera>(true))
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (!camera.isActiveAndEnabled || data == null || data.renderType != CameraRenderType.Base) continue;
            Camera final = camera;
            foreach (var overlay in data.cameraStack) if (overlay != null && overlay.isActiveAndEnabled) final = overlay;
            return final;
        }
        return null;
    }

    private static T Read<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return (T)field.GetValue(target);
    }
    private static bool Near(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 0.000001f;
    private static async Task Until(Func<bool> condition, float seconds, string label)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (callbackFailure != null) throw new InvalidOperationException(callbackFailure);
            if (!Application.isPlaying || Time.realtimeSinceStartup > deadline)
                throw new TimeoutException(label + "（必須有真實 Game camera 渲染；不會手動呼叫 BeginCamera 補過）。");
            await Task.Delay(20);
        }
        if (callbackFailure != null) throw new InvalidOperationException(callbackFailure);
    }
    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException(label);
        report.Add("PASS " + label);
    }
}
#endif
