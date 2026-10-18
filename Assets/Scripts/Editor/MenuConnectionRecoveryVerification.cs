using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Menu;
using Fusion.Photon.Realtime;
using MultiClimb.Menu;
using Purgers.Progression;
using Purgers.GameFlow.Control;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>僅 Editor 的連線修復驗證；存檔全部寫入 Temp，不改 Scene／Prefab。</summary>
[InitializeOnLoad]
public static class MenuConnectionRecoveryVerification
{
    public static string Status = "idle";
    private static Task running;
    private const string ReportPath = "Temp/connection-playmode.txt";
    private static TestRunnerApi testApi;
    static MenuConnectionRecoveryVerification()
    {
        if (SessionState.GetBool("ConnectionRecovery.CaptureTests", false)) CaptureNextRun();
    }
    public static void CaptureNextRun()
    {
        SessionState.SetBool("ConnectionRecovery.CaptureTests", true);
        testApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        testApi.RegisterCallbacks(new ResultCapture(), 1000);
    }
    private sealed class ResultCapture : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool("ConnectionRecovery.CaptureTests", false)) return;
            File.WriteAllText("Temp/connection-regression.xml", result.ToXml().OuterXml);
            SessionState.SetBool("ConnectionRecovery.CaptureTests", false);
        }
    }
    private static void Record(string message)
    {
        Status = message;
        File.AppendAllText(ReportPath, DateTime.UtcNow.ToString("O") + " " + message + "\n");
    }
    public static string Begin()
    {
        if (!Application.isPlaying) return "Play Mode required";
        if (running != null && !running.IsCompleted) return Status;
        File.WriteAllText(ReportPath, "");
        running = VerifyAsync();
        return Status;
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Record("PASS " + message);
    }
    private static async Task VerifyAsync()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        var repository = new JsonGameSaveRepository(Path.GetFullPath("Temp/ConnectionPlayModeSaves"));
        var main = UnityEngine.Object.FindObjectsOfType<FusionMenuUIMain>(true).First(x => x.gameObject.scene.IsValid());
        var behaviour = (MenuConnectionBehaviour)main.Connection;
        var original = behaviour.Connection;
        var failedUi = typeof(MenuConnectionBehaviour).GetMethod("ShowConnectionFailure", BindingFlags.Instance | BindingFlags.NonPublic);
        MenuConnection host = null, client = null, stalled = null;
        try
        {
            Record("UI cancellation");
            stalled = new StalledConnection(config, repository, 10f);
            stalled.ConnectionAttemptFailed += message => failedUi.Invoke(behaviour, new object[] { message });
            behaviour.Connection = stalled;
            main.Controller.Show<FusionMenuUILoading>();
            var pending = stalled.ConnectAsync(new FusionMenuConnectArgs { Region = "", MaxPlayerCount = 2 });
            await Task.Delay(100);
            // 使用真正的取消按鈕事件，而非直接呼叫 Disconnect。
            var loading = main.Controller.Get<FusionMenuUILoading>();
            var button = (UnityEngine.UI.Button)typeof(FusionMenuUILoading).GetField("_disconnectButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loading);
            var rect = (RectTransform)button.transform;
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);
            Require(button.IsInteractable() && hits.Count > 0 && hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>() == button,
                "cancel button receives UI raycast: " + string.Join(",", hits.Select(h => h.gameObject.name)));
            button.onClick.Invoke();
            Require(await Task.WhenAny(pending, Task.Delay(7000)) == pending, "cancel button finishes bounded connection");
            Require(!(await pending).Success, "cancelled attempt never succeeds");
            await Task.Delay(500);
            Require(main.IsShowing && main.gameObject.activeInHierarchy && !loading.gameObject.activeInHierarchy, "cancel returns to real main menu");
            Require(!LocalPlayerControl.AllInputBlocked, "cancel releases input lock");

            Record("UI timeout");
            stalled = new StalledConnection(config, repository, 1f);
            stalled.ConnectionAttemptFailed += message => failedUi.Invoke(behaviour, new object[] { message });
            behaviour.Connection = stalled;
            main.Controller.Show<FusionMenuUILoading>();
            var timedOut = await stalled.ConnectAsync(new FusionMenuConnectArgs { Region = "", MaxPlayerCount = 2 });
            await Task.Delay(500);
            Require(!timedOut.Success && timedOut.DebugMessage.Contains("逾時"), "timeout reports reason");
            Require(main.IsShowing && main.gameObject.activeInHierarchy && !loading.gameObject.activeInHierarchy, "timeout returns to main menu");
            var popup = main.Controller.Get<FusionMenuUIPopup>();
            popup.Hide();
            behaviour.Connection = original;

            Record("Photon Best Host");
            host = new MenuConnection(config, null, repository, connectionTimeoutSeconds: 30f);
            var hostArgs = new FusionMenuConnectArgs { Region = "", MaxPlayerCount = 2, AppVersion = "connection-recovery-verification" };
            var opened = await host.ConnectAsync(hostArgs);
            Require(opened.Success, "Best Host connected: " + opened.DebugMessage);
            Require(MenuRegionRouting.Decode(config, host.SessionName) == host.Region, "room code matches actual Host region=" + host.Region);

            Record("Photon Client Join");
            client = new MenuConnection(config, null, repository, connectionTimeoutSeconds: 30f);
            var clientArgs = new FusionMenuConnectArgs { Region = host.Region == "eu" ? "us" : "eu", Session = host.SessionName, MaxPlayerCount = 2, AppVersion = hostArgs.AppVersion };
            var joined = await client.ConnectAsync(clientArgs);
            Require(joined.Success && client.Region == host.Region, "Client ignores different preference and joins Host: " + joined.DebugMessage);
            await client.DisconnectAsync(ConnectFailReason.UserRequest);
            Require(!client.IsConnected, "Client disconnect cleans Runner");
            joined = await client.ConnectAsync(clientArgs);
            Require(joined.Success, "Client reconnect succeeds: " + joined.DebugMessage);
            var closed = new TaskCompletionSource<bool>();
            client.RoomClosed += () => closed.TrySetResult(true);
            await host.DisconnectAsync(ConnectFailReason.UserRequest);
            Require(await Task.WhenAny(closed.Task, Task.Delay(15000)) == closed.Task, "Host close still delivers RoomClosed");
            client.AcknowledgeRoomClosed();
            Record("COMPLETE");
        }
        catch (Exception exception) { Record("FAIL " + exception); }
        finally
        {
            if (stalled != null) await stalled.DisconnectAsync(ConnectFailReason.UserRequest);
            if (client != null) await client.DisconnectAsync(ConnectFailReason.UserRequest);
            if (host != null) await host.DisconnectAsync(ConnectFailReason.UserRequest);
            behaviour.Connection = original;
            main.Controller.Show<FusionMenuUIMain>();
            File.AppendAllText(ReportPath, "remaining runners=" + NetworkRunner.Instances.Count() + ", inputBlocked=" + LocalPlayerControl.AllInputBlocked + "\n");
        }
    }
    private sealed class StalledConnection : MenuConnection
    {
        public StalledConnection(FusionMenuConfig config, IGameSaveRepository repository, float timeout)
            : base(config, null, repository, connectionTimeoutSeconds: timeout) { }
        protected override Task<StartGameResult> JoinLobbyAsync(NetworkRunner runner, GameMode mode, FusionAppSettings settings, CancellationToken token)
            => new TaskCompletionSource<StartGameResult>().Task;
    }
}
