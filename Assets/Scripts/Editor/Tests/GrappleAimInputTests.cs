using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>有效 Spawned 生命週期的鈎索輸入回歸；不保存 Scene 或 Prefab。</summary>
[Category("PurgersRegression")]
public sealed class GrappleAimInputTests
{
    private NetworkRunner runner;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [UnityTest]
    public IEnumerator RightMousePreservesGrappleAndManualQStillReleases()
    {
        SessionState.SetBool(GrappleAimTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        runner = new GameObject("Grapple Aim Input Test").AddComponent<NetworkRunner>();
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries())
            config.PrefabTable.AddSource(entry.Item2);
        var sceneManager = runner.gameObject.AddComponent<GrappleVisualVerificationSceneManager>();
        sceneManager.IsSceneTakeOverEnabled = false;
        var start = runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Single, Config = config, SceneManager = sceneManager
        });
        float deadline = Time.realtimeSinceStartup + 30f;
        while (!start.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(start.IsCompleted, Is.True, "Single Runner 啟動逾時");
        Assert.That(start.GetAwaiter().GetResult().Ok, Is.True);
        int initialTick = runner.Tick.Raw;
        while ((runner.SceneManager.IsBusy || runner.Tick.Raw == initialTick) && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(runner.SceneManager.IsBusy, Is.False);
        Assert.That(runner.Tick.Raw, Is.Not.EqualTo(initialTick));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(1000f, 100f, 1000f),
            Quaternion.identity, runner.LocalPlayer);
        // 停止自然輸入，僅提交測試的 Simulate；保留實際 KCC、StateMachine 與能量元件。
        obj.GetComponent<Player>().enabled = false;
        var grapple = obj.GetComponent<PlayerGrapple>();
        SetField(grapple, "debugGrapple", false);
        // 正式 Prefab 的倍率可為 0；僅測試實例啟用 Momentum，以驗證 Q 路徑。
        SetField(grapple, "releaseSpeedMultiplier", 1f);
        SetField(grapple, "grappleOcclusionObstacleMask", (LayerMask)0);

        foreach (bool holdMode in new[] { false, true })
        {
            SetField(grapple, "useHoldToMaintainGrappleInput", holdMode);
            foreach (var phase in new[] { GrapplePhase.PreFire, GrapplePhase.Shooting, GrapplePhase.Attached })
            {
                Prepare(grapple, phase);
                // 新按下、持續按住、放開均不應收繩或消耗 Aim。
                for (int input = 0; input < 3; input++)
                {
                    grapple.Simulate(false, true, input == 0, input < 2, Vector2.zero, 1f);
                    Assert.That(grapple.CurrentPhase, Is.EqualTo(phase), holdMode + " / " + phase + " / Aim " + input);
                    Assert.That(grapple.BlocksProfessionAimInput, Is.False, "右鍵不可被斷繩消耗");
                }
            }
            Prepare(grapple, GrapplePhase.Attached);
            var kcc = obj.GetComponent<Fusion.Addons.KCC.KCC>();
            kcc.Data.IsGrounded = false;
            kcc.SetDynamicVelocity(Vector3.right * 30f);
            // 右鍵與 Q 取消同 Tick，Q 仍可收繩，Aim 不得被吃掉。
            grapple.Simulate(!holdMode, !holdMode, true, true, Vector2.zero, 1f);
            Assert.That(grapple.CurrentPhase, Is.EqualTo(GrapplePhase.Retracting));
            Assert.That(grapple.IsReleaseMomentumActive, Is.True);
            Assert.That(grapple.BlocksProfessionAimInput, Is.False);
        }
        Prepare(grapple, GrapplePhase.Attached);
        Set(grapple, "AimConsumedUntilReleased", (NetworkBool)true);
        grapple.Simulate(false, true, false, true, Vector2.zero, 1f);
        Assert.That(grapple.BlocksProfessionAimInput, Is.False, "舊快照消耗鎖不得殘留");
        runner.Despawn(obj);
    }

    private static void Prepare(PlayerGrapple grapple, GrapplePhase phase)
    {
        typeof(PlayerGrapple).GetProperty("CurrentPhase").SetValue(grapple, phase);
        Set(grapple, "GrappleWorldPoint", grapple.transform.position + Vector3.forward * 40f + Vector3.up * 10f);
        Set(grapple, "TrackingDynamicAnchor", (NetworkBool)false);
        Set(grapple, "SupportInteractionTetherActive", (NetworkBool)false);
        Set(grapple, "ReleaseMomentumActive", (NetworkBool)false);
        Set(grapple, "HasAppliedAttachedPull", (NetworkBool)false);
        Set(grapple, "HasBeenAirborneDuringAttached", (NetworkBool)false);
        Set(grapple, "AttachedStartupGroundedTimer", TickTimer.None);
        Set(grapple, "PhaseTimer", TickTimer.CreateFromSeconds(grapple.Runner, 1000f));
        Set(grapple, "CurrentShootDuration", 1000f);
        Set(grapple, "AimConsumedUntilReleased", (NetworkBool)false);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (runner != null)
        {
            var shutdown = runner.Shutdown();
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!shutdown.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            if (runner != null) UnityEngine.Object.Destroy(runner.gameObject);
            runner = null;
        }
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private static void Set(PlayerGrapple grapple, string name, object value) =>
        typeof(PlayerGrapple).GetProperty(name, Private).SetValue(grapple, value);

    private static void SetField(PlayerGrapple grapple, string name, object value) =>
        typeof(PlayerGrapple).GetField(name, Private).SetValue(grapple, value);
}

// EnterPlayMode/ExitPlayMode 會重載 Domain，重新註冊結果回呼以保留 NUnit 證據。
[InitializeOnLoad]
internal sealed class GrappleAimTestResults : UnityEditor.TestTools.TestRunner.Api.ICallbacks
{
    internal const string ActiveKey = "Purgers.GrappleAimTestResults.Active";
    static GrappleAimTestResults()
    {
        if (SessionState.GetBool(ActiveKey, false))
            ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>()
                .RegisterCallbacks(new GrappleAimTestResults());
    }
    public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor tests) { }
    public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }
    public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result) { }
    public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
    {
        System.IO.Directory.CreateDirectory("Temp/GrappleAimFix");
        var lines = new System.Collections.Generic.List<string>();
        Append(result, lines);
        System.IO.File.WriteAllLines("Temp/GrappleAimFix/latest-tests.txt", lines);
        SessionState.SetBool(ActiveKey, false);
    }
    private static void Append(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result,
        System.Collections.Generic.List<string> lines)
    {
        lines.Add(result.FullName + " | " + result.ResultState + " | passed=" + result.PassCount +
            " failed=" + result.FailCount + " skipped=" + result.SkipCount + " | " + result.Message + " | " + result.StackTrace);
        foreach (var child in result.Children) Append(child, lines);
    }
}