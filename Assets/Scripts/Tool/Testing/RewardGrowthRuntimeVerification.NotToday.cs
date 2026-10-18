#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class RewardGrowthRuntimeVerification
{
    private static PlayerAbilityColorGrading survivalCompositor;
    private static Camera survivalCamera;
    private static Vector4 survivalGamma, survivalBaseline;
    private static bool survivalPending, survivalPeakCaptured, survivalTroughCaptured;
    private static int survivalFrames;
    private static float survivalMinimum, survivalMaximum;
    private static string survivalRenderFailure;
    private static T SurvivalField<T>(object source, string name) => (T)source.GetType().GetField(name, Private).GetValue(source);

    public static async void BeginNotToday()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無 Runner 的 Play Mode");
        Status = "執行中"; report.Clear(); holdE = holdHostE = false;
        session = "not-today-" + Guid.NewGuid().ToString("N");
        survivalFrames = 0; survivalMinimum = float.PositiveInfinity; survivalMaximum = 0;
        survivalRenderFailure = null; survivalPending = survivalPeakCaptured = survivalTroughCaptured = false;
        Directory.CreateDirectory("Temp/NotTodayShieldVerification");
        var menu = UnityEngine.Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.name == "FusionMenuViewMainMenu");
        var hud = UnityEngine.Object.FindObjectOfType<LocalPlayerHealthSlider>();
        var previousHudRunner = hud != null ? SurvivalField<NetworkRunner>(hud, "runner") : null;
        try
        {
            if (menu != null) menu.gameObject.SetActive(false);
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            var logic = CreateLogic(host);
            client = await Connect(false); CreateLogic(client);
            await Until(() => host.TryGetPlayerObject(client.LocalPlayer, out _) && client.TryGetPlayerObject(client.LocalPlayer, out _), "client owner replicas");
            var owner = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            var replica = PlayerAbilityQualification.FindPlayer(client, client.LocalPlayer);
            await Until(() => owner.AbilityRuntimeManager.LoadoutRevision > 0 && owner.ProfessionRuntimeManager.CurrentRuntimeObject != null &&
                replica.GetComponent<PlayerAbilityColorGrading>() != null, "loadout and natural local presentation bind");
            Check(hud != null, "scene contains production local health HUD");
            Field(hud, "runner", client);
            await Until(() => SurvivalField<PlayerHealth>(hud, "boundHealth") == replica.Health, "HUD bound to client replica");
            await driver.AtTick(() => {
                owner.Movement.KCC.SetPosition(origin);
                PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer).Movement.KCC.SetPosition(origin + Vector3.right * 20);
            });
            var gate = owner.GetComponent<PlayerActionGate>(); var replicaGate = replica.GetComponent<PlayerActionGate>();
            survivalCompositor = replica.GetComponent<PlayerAbilityColorGrading>();
            Check(SurvivalField<Player>(survivalCompositor, "owner") == replica, "yellow effect uses naturally bound client Input Authority owner");
            RenderPipelineManager.beginCameraRendering += ObserveSurvivalBegin;
            RenderPipelineManager.endCameraRendering += ObserveSurvivalEnd;
            await driver.AtTick(() => {
                Grant(logic, owner, PlayerGrowthKind.NotToday);
                gate.SetBlocks(PlayerActionBlockSource.StatusEffect, PlayerActionBlockMask.Movement | PlayerActionBlockMask.Fire);
                gate.SetBlocks(PlayerActionBlockSource.System, PlayerActionBlockMask.Aim);
                gate.ApplyExplosionStatus(10f, .5f, 10f);
                Check(gate.TryBeginCapture(logic.Object), "capture accepted before immunity");
                Check(gate.IsExplosionGrappleBlocked && gate.ExplosionGroundSpeedMultiplier < 1f, "explosion restrictions active before lethal hit");
                Fatal(owner);
                Check(owner.Health.IsAlive && owner.Health.IsNotTodayInvulnerable && gate.IsCrowdControlImmune, "lethal interception grants invulnerability and control immunity");
                Check(!gate.IsCaptured && !gate.IsExplosionGrappleBlocked && gate.ExplosionGroundSpeedMultiplier == 1f, "trigger immediately clears capture grapple lock and slow");
                Check(!gate.IsBlocked(PlayerActionBlockMask.Movement | PlayerActionBlockMask.Fire) && gate.IsBlocked(PlayerActionBlockMask.Aim), "negative locks cleared while System lock is preserved");
                gate.SetBlocks(PlayerActionBlockSource.StatusEffect, PlayerActionBlockMask.Movement);
                gate.ApplyExplosionStatus(10f, .8f, 10f);
                Check(!gate.TryBeginCapture(logic.Object), "capture rejected throughout immunity");
                Check(!gate.IsBlocked(PlayerActionBlockMask.Movement) && !gate.IsExplosionGrappleBlocked && gate.ExplosionGroundSpeedMultiplier == 1f, "new status masks and explosion debuffs rejected");
                gate.ClearBlocks(PlayerActionBlockSource.System);
            });
            await Until(() => replica.Health.IsNotTodayInvulnerable && replicaGate.IsCrowdControlImmune && !replicaGate.IsCaptured, "client immunity replication", 2f);
            Check(replica.Health.NotTodayRemainingSeconds > 0f && replica.Health.NotTodayRemainingSeconds <= 3f, "client effect reads synchronized remaining lifetime");
            float hp = owner.Health.CurrentHealth;
            await driver.AtTick(() => Fatal(owner));
            Check(owner.Health.CurrentHealth == hp, "damage blocked during the same protection window");
            await Until(() => !owner.Health.IsNotTodayInvulnerable && !replica.Health.IsNotTodayInvulnerable, "natural three second expiry", 5f);
            Check(survivalRenderFailure == null, "camera render callbacks valid: " + survivalRenderFailure);
            Check(survivalFrames > 2 && survivalMaximum > .1f && survivalMinimum < .02f,
                "actual client URP stack pulses yellow, frames=" + survivalFrames + " redDeltaRange=" + survivalMinimum + ".." + survivalMaximum);
            await Task.Delay(150);
            var volume = SurvivalField<Volume>(survivalCompositor, "runtimeVolume");
            Check(volume != null && !volume.enabled && replica.Health.NotTodayRemainingSeconds == 0f, "expiry leaves no active yellow volume or timer");
            Check(!gate.IsCaptured && !gate.IsBlocked(PlayerActionBlockMask.Movement) && gate.ExplosionGroundSpeedMultiplier == 1f, "cleansed effects do not resume after immunity expires");
            await driver.AtTick(() => {
                gate.ApplyExplosionStatus(5f, .5f, 5f);
                gate.SetBlocks(PlayerActionBlockSource.StatusEffect, PlayerActionBlockMask.Fire);
                Check(gate.IsExplosionGrappleBlocked && gate.IsBlocked(PlayerActionBlockMask.Fire), "new debuffs accepted after expiry");
                Check(gate.TryBeginCapture(logic.Object), "new capture accepted after expiry");
            });
            await Until(() => replicaGate.IsCaptured, "post-expiry capture replication");
            replicaGate.ClearNegativeControlEffects();
            Check(replicaGate.IsCaptured && gate.IsCaptured, "client cannot perform authoritative cleanse");
            await driver.AtTick(() => gate.ClearNegativeControlEffects());
            Check(!gate.IsCaptured, "authority cleanup releases captured player");

            var shieldDefinition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/Shield.asset");
            await driver.AtTick(() => {
                owner.Health.RestoreHealth(owner.Health.MaximumHealth, out _);
                Check(owner.AbilityRuntimeManager.TryEquipRewardAbilityStateAuthority(shieldDefinition, out _), "shield equipped");
            });
            var view = SurvivalField<LocalHealthSegmentView>(hud, "healthSegmentView");
            float maximumBeforeShield = owner.Health.MaximumHealth;
            int expectedHealthCells = LocalHealthSegmentView.SegmentCountFor(maximumBeforeShield, SurvivalField<float>(view, "healthPerSegment"));
            await Until(() => view.VisibleHealthCells == expectedHealthCells, "restored health reaches HUD before shield");
            PlayerShieldAbility shield = null;
            await Until(() => (shield = PlayerAbilityQualification.GetModule<PlayerShieldAbility>(owner)) != null && shield.IsUsable, "shield ready");
            await driver.AtTick(() => {
                var input = new NetInput(); input.Buttons.Set(InputButton.Ability1, true);
                shield.SimulateAbility(input, default);
                Check(shield.CurrentShield > 0f, "shield activates through normal ability input path");
            });
            await Until(() => view.VisibleShieldCells > 0, "real local HUD shows client shield", 2f);
            ScreenCapture.CaptureScreenshot("Temp/NotTodayShieldVerification/shield-active.png");
            Check(SurvivalField<float>(view, "shieldCapacity") > 0f, "active shield adds temporary capacity");
            await Until(() => shield.CurrentShield == 0f && view.VisibleShieldCells == 0, "shield naturally expires on HUD", 3f);
            Check(SurvivalField<float>(view, "shieldCapacity") == 0f && view.VisibleHealthCells == expectedHealthCells && owner.Health.MaximumHealth == maximumBeforeShield,
                "expired shield removes empty extra cells: capacity=" + SurvivalField<float>(view, "shieldCapacity") +
                " healthCells=" + view.VisibleHealthCells + "/" + expectedHealthCells + " maxHP=" + owner.Health.MaximumHealth + "/" + maximumBeforeShield);
            ScreenCapture.CaptureScreenshot("Temp/NotTodayShieldVerification/shield-expired.png");
            await Task.Delay(150);
            Status = "通過 " + report.Count;
        }
        catch (Exception ex) { Status = "失敗 " + ex.Message; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= ObserveSurvivalBegin;
            RenderPipelineManager.endCameraRendering -= ObserveSurvivalEnd;
            survivalCompositor = null; survivalPending = false; survivalCamera = null;
            if (hud != null) Field(hud, "runner", previousHudRunner);
            if (menu != null) menu.gameObject.SetActive(true);
            if (client != null && client.IsRunning) await client.Shutdown();
            if (host != null && host.IsRunning) await host.Shutdown();
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj); temporary.Clear();
            Directory.CreateDirectory("Temp/NotTodayShieldVerification");
            File.WriteAllLines("Temp/NotTodayShieldVerification/host-client.txt", report);
        }
    }

    private static void ObserveSurvivalBegin(ScriptableRenderContext context, Camera camera)
    {
        if (survivalCompositor == null || camera.cameraType != CameraType.Game) return;
        var volume = SurvivalField<Volume>(survivalCompositor, "runtimeVolume");
        if (volume == null || !volume.enabled || SurvivalField<Camera>(survivalCompositor, "activeRenderCamera") != camera) return;
        var owner = SurvivalField<Player>(survivalCompositor, "owner");
        if (owner == null || !owner.Health.IsNotTodayInvulnerable) return;
        survivalCamera = camera;
        survivalGamma = SurvivalField<LiftGammaGain>(survivalCompositor, "grading").gamma.value;
        survivalBaseline = SurvivalField<VolumeStack>(survivalCompositor, "baselineStack").GetComponent<LiftGammaGain>().gamma.value;
        survivalPending = true;
    }
    private static void ObserveSurvivalEnd(ScriptableRenderContext context, Camera camera)
    {
        if (!survivalPending || camera != survivalCamera || survivalCompositor == null) return;
        survivalPending = false;
        var actual = VolumeManager.instance.stack.GetComponent<LiftGammaGain>();
        if (actual == null || Vector4.Distance(actual.gamma.value, survivalGamma) > .001f)
        { survivalRenderFailure = "yellow grading did not reach rendered URP stack"; return; }
        if (SurvivalField<Volume>(survivalCompositor, "runtimeVolume").enabled)
        { survivalRenderFailure = "temporary volume not restored after rendering"; return; }
        var delta = actual.gamma.value - survivalBaseline;
        if (delta.x < -.001f || delta.y < -.001f || delta.z > .001f)
        { survivalRenderFailure = "rendered tint is not yellow"; return; }
        if (!survivalPeakCaptured && delta.x > .15f)
        { ScreenCapture.CaptureScreenshot("Temp/NotTodayShieldVerification/yellow-peak.png"); survivalPeakCaptured = true; }
        else if (survivalPeakCaptured && !survivalTroughCaptured && delta.x < .01f)
        { ScreenCapture.CaptureScreenshot("Temp/NotTodayShieldVerification/yellow-trough.png"); survivalTroughCaptured = true; }
        survivalFrames++; survivalMinimum = Mathf.Min(survivalMinimum, delta.x); survivalMaximum = Mathf.Max(survivalMaximum, delta.x);
    }
}
#endif
