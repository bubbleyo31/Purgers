#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Purgers.GameFlow.Stage;
using UnityEngine;

/// <summary>手動啟動的 Single Runner 暫停驗證；只建立臨時物件，不寫存檔或 Scene。</summary>
public static class StageTimerPauseRuntimeVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private static bool running;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> report = new List<string>();

    public static async void Begin()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("需在 Play Mode 且無本測試進行。");
        foreach (var existing in NetworkRunner.Instances)
            if (existing != null && existing.IsRunning)
                throw new InvalidOperationException("請先關閉既有遊戲連線。");

        running = true;
        Status = "執行中";
        report.Clear();
        NetworkRunner runner = null;
        GameObject stageRoot = null;
        StageTimerPauseVerificationDriver driver = null;
        try
        {
            runner = new GameObject("Stage timer verification runner").AddComponent<NetworkRunner>();
            var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
            config.PeerMode = NetworkProjectConfig.PeerModes.Single;
            foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
            driver = runner.gameObject.AddComponent<StageTimerPauseVerificationDriver>();
            var manager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            var started = await runner.StartGame(new StartGameArgs
                { GameMode = GameMode.Single, Config = config, SceneManager = manager });
            Check(started.Ok, "Single Runner 啟動");
            runner.AddGlobal(driver);
            await Until(() => runner.Tick.Raw > 0 && !manager.IsBusy, "Runner ready");

            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
            var player = runner.Spawn(playerPrefab.GetComponent<NetworkObject>(),
                new Vector3(1000, 10000, 1000), Quaternion.identity, runner.LocalPlayer);
            runner.SetPlayerObject(runner.LocalPlayer, player);
            await Until(() => player.GetComponent<PlayerHealth>().IsAlive, "living player");
            Check(player.GetComponent<PlayerHealth>().IsAlive, "存在存活玩家，避免全滅規則立即結束關卡");
            stageRoot = new GameObject("Temporary stage timer");
            var stage = stageRoot.AddComponent<StageFlowController>();
            new NetworkObjectBaker().Bake(stageRoot);
            runner.RegisterSceneObjects(SceneRef.FromIndex(0),
                new[] { stage.GetComponent<NetworkObject>() }, default);
            Check(stage.IsNetworkReady, "StageFlow 已 Spawn");
            // 不走地圖、玩家與存檔流程，只啟用現有關卡計時器。
            typeof(StageFlowController).GetField("defaultTimeLimitSeconds", Private).SetValue(stage, 2f);
            typeof(StageFlowController).GetField("debugFlow", Private).SetValue(stage, false);
            await driver.AtTick(() => BeginStage(stage));
            Check(!stage.IsStageTimerPaused && stage.HasTimeLimit, "新關卡啟用有限倒數");
            Check(stage.RequestDevelopmentTimerToggle(), "受理 Host 暫停");
            await Task.Delay(200);
            await Until(() => stage.IsStageTimerPaused, "pause tick");
            float frozen = stage.RemainingStageSeconds;
            int tick = runner.Tick.Raw;
            float gameTime = Time.time;
            float scale = Time.timeScale;
            await driver.AtTick(() => Set(stage, "ExtractionHoldTimer", TickTimer.CreateFromSeconds(runner, 5f)));
            // Boss 分支不會因無撤離候選重設撤離 Timer，方便獨立檢查其時間持續流逝。
            await driver.AtTick(() => Set(stage, "ObjectiveKind", StageObjectiveKind.DefeatBoss));
            float extractionBefore = stage.RemainingExtractionSeconds;
            await Task.Delay(2600);
            Check(stage.Phase == StagePhase.Active, "跨過原始到期點仍保持 Active");
            Check(Mathf.Abs(stage.RemainingStageSeconds - frozen) < 0.0001f, "剩餘秒數精確凍結");
            Check(runner.Tick.Raw > tick + 5 && Time.time > gameTime + 1f && Time.timeScale == scale,
                "Fusion Tick、Unity 時間持續且 timeScale 不變");
            Check(stage.RemainingExtractionSeconds < extractionBefore - 1f, "撤離倒數持續");
            Check(stage.RequestDevelopmentTimerToggle(), "受理恢復");
            await Until(() => !stage.IsStageTimerPaused, "resume tick");
            Check(stage.RemainingStageSeconds <= frozen && stage.RemainingStageSeconds > frozen - 0.2f,
                "恢復沿用剩餘時間，不補扣暫停期間");
            await Task.Delay(400);
            Check(stage.RemainingStageSeconds < frozen - 0.2f, "恢復後正常扣時");
            Check(stage.RequestDevelopmentTimerToggle(), "可再次暫停");
            await Until(() => stage.IsStageTimerPaused, "second pause");
            await driver.AtTick(() => BeginStage(stage));
            Check(!stage.IsStageTimerPaused && stage.RemainingStageSeconds > 1.9f, "新關卡清除暫停");
            await driver.AtTick(() =>
            {
            Set(stage, "Phase", StagePhase.Initializing);
            Check(!stage.RequestDevelopmentTimerToggle(), "載入期間拒收");
            Set(stage, "Phase", StagePhase.Active);
            Set(stage, "HasTimeLimit", (NetworkBool)false);
            Check(!stage.RequestDevelopmentTimerToggle(), "不限時拒收");
            Set(stage, "HasTimeLimit", (NetworkBool)true);
            Set(stage, "StageTimer", TickTimer.CreateFromTicks(runner, 0));
            Check(!stage.RequestDevelopmentTimerToggle(), "到期當 Tick 不可暫停救回");
            // 在下一個 Tick 前結束 fixture，避免觸發沒有存檔 Context 的結果提交。
            Set(stage, "Phase", StagePhase.Completed);
            Check(!stage.RequestDevelopmentTimerToggle(), "已結束拒收");
            });
            Status = "通過";
        }
        catch (Exception error)
        {
            Status = "失敗";
            report.Add(error.ToString());
            Debug.LogException(error);
        }
        finally
        {
            if (runner != null) await runner.Shutdown();
            if (stageRoot != null) UnityEngine.Object.Destroy(stageRoot);
            Directory.CreateDirectory("Temp/StageTimerPauseVerification");
            File.WriteAllLines("Temp/StageTimerPauseVerification/runtime.txt", report);
            running = false;
            Debug.Log("[StageTimerPauseVerification] " + Status + "\n" + string.Join("\n", report));
        }
    }

    private static void BeginStage(StageFlowController stage) =>
        typeof(StageFlowController).GetMethod("BeginActiveStage", Private)
            .Invoke(stage, new object[] { StageRules.ResolveRuntimePlan(1, 4) });

    private static void Set(StageFlowController stage, string name, object value) =>
        typeof(StageFlowController).GetProperty(name, Private | BindingFlags.Public).SetValue(stage, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        report.Add("PASS " + message);
    }

    private static async Task Until(Func<bool> predicate, string label)
    {
        float end = Time.realtimeSinceStartup + 10f;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup > end) throw new TimeoutException(label);
            await Task.Delay(20);
        }
    }
}

/// <summary>測試用：將同步欄位注入安排在 Fusion Tick，避免 Frame 寫入被狀態還原。</summary>
public sealed class StageTimerPauseVerificationDriver : SimulationBehaviour, IBeforeTick
{
    private Action pending;
    public Task AtTick(Action action)
    {
        var completion = new TaskCompletionSource<bool>();
        pending = () =>
        {
            try { action(); completion.SetResult(true); }
            catch (Exception error) { completion.SetException(error); }
        };
        return completion.Task;
    }
    public void BeforeTick()
    {
        var action = pending;
        pending = null;
        action?.Invoke();
    }
}
#endif
