#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Fusion.Menu;
using MultiClimb.Menu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>同 Editor 隔離三 Runner 驗證；不建立存檔、不保存場景。</summary>
public static class RoomClosureRuntimeVerification
{
    public static string Status { get; private set; } = "Not started";
    private static readonly List<string> Report = new List<string>();
    private static readonly List<NetworkRunner> Runners = new List<NetworkRunner>();
    private static readonly List<MenuConnection> Connections = new List<MenuConnection>();
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static string session;

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception(label);
        Report.Add("PASS " + label);
    }

    private static async Task Until(Func<bool> predicate, string label)
    {
        float end = Time.realtimeSinceStartup + 40;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup > end) throw new TimeoutException(label);
            await Task.Delay(50);
        }
    }

    private static void Field(object target, string name, object value)
        => target.GetType().GetField(name, Private).SetValue(target, value);

    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("Requires Play Mode with no running Runner.");
        Status = "Running";
        Report.Clear(); Runners.Clear(); Connections.Clear();
        session = "room-close-" + Guid.NewGuid().ToString("N");
        const string nameKey = "Photon.Menu.Username";
        bool hadName = PlayerPrefs.HasKey(nameKey);
        string originalName = PlayerPrefs.GetString(nameKey);
        try
        {
            var owner = UnityEngine.Object.FindObjectOfType<MenuConnectionBehaviour>(true);
            Check(owner != null, "existing menu connection owner");
            var host = await Connect(true);
            var hostChat = CreateChat(host, "Host");
            var a = await Connect(false);
            var aChat = CreateChat(a, "離開玩家");
            var aConnection = Attach(owner, a, false);
            int voluntaryPopupCount = 0;
            aConnection.RoomClosed += () => voluntaryPopupCount++;
            var b = await Connect(false);
            var bChat = CreateChat(b, "留下玩家");
            var bConnection = Attach(owner, b, true);
            int roomClosedCount = 0;
            bConnection.RoomClosed += () => roomClosedCount++;
            await Until(() => hostChat.LocalHistory.Any(m => m.SenderName == "離開玩家") &&
                hostChat.LocalHistory.Any(m => m.SenderName == "留下玩家"), "name registration RPCs");
            Check(true, "Host receives validated names from both clients");

            await aConnection.DisconnectAsync(ConnectFailReason.UserRequest);
            await Until(() => hostChat.LocalHistory.Any(m => m.Kind == LocalChatMessageKind.PlayerLeft) &&
                bChat.LocalHistory.Any(m => m.Kind == LocalChatMessageKind.PlayerLeft), "departure broadcast");
            Check(hostChat.LocalHistory.Count(m => m.Kind == LocalChatMessageKind.PlayerLeft) == 1,
                "Host receives exactly one departure");
            Check(bChat.LocalHistory.Single(m => m.Kind == LocalChatMessageKind.PlayerLeft).Text == "離開玩家 離開了遊戲",
                "remaining Client receives reliable departure with accepted name");
            Check(voluntaryPopupCount == 0 && b.IsRunning, "voluntary Client exit leaves room running without popup");

            var main = UnityEngine.Object.FindObjectOfType<FusionMenuUIMain>(true);
            main.Controller.Show<FusionMenuUIGameplay>();
            var popup = main.Controller.Get<FusionMenuUIPopup>();
            var button = popup.transform.Find("PoUp/ButtonContainer/PrimaryButton").GetComponent<Button>();
            string previousCaption = button.GetComponentInChildren<TMP_Text>(true).text;
            Vector2 previousPosition = ((RectTransform)button.transform).anchoredPosition;
            await host.Shutdown();
            await Until(() => roomClosedCount == 1 && popup.IsShowing, "remote close popup");
            Check(!bConnection.IsConnected, "Client exits network session before modal");
            Check(popup.transform.Find("PoUp/PopUpText").GetComponent<TMP_Text>().text == "房間已關閉",
                "exact room closed message");
            Check(button.GetComponentInChildren<TMP_Text>(true).text == "回到主畫面", "exact return button caption");
            Check(popup.GetComponentsInChildren<Button>(true).Count(x => x.isActiveAndEnabled && x.interactable) == 1,
                "only one enabled popup action");
            Check(Mathf.Abs(((RectTransform)popup.transform.Find("PoUp")).anchoredPosition.x) < 1 &&
                Mathf.Abs(((RectTransform)button.transform).anchoredPosition.x) < 1, "centered pane and return action");
            Check(Cursor.visible && Cursor.lockState == CursorLockMode.None, "cursor released for popup");
            Directory.CreateDirectory("Temp/RoomClosure");
            ScreenCapture.CaptureScreenshot("Temp/RoomClosure/popup.png");
            await Task.Delay(600);
            button.onClick.Invoke();
            await Until(() => main.IsShowing && !popup.gameObject.activeSelf, "return to main menu");
            Check(button.GetComponentInChildren<TMP_Text>(true).text == previousCaption &&
                ((RectTransform)button.transform).anchoredPosition == previousPosition, "normal popup presentation restored");
            Check(roomClosedCount == 1, "disconnect and shutdown callbacks produce one modal");
            Status = "Passed " + Report.Count;
        }
        catch (Exception exception)
        {
            Status = "Failed: " + exception;
            Report.Add(Status); Debug.LogError(Status);
        }
        finally
        {
            foreach (var connection in Connections) connection.SuppressSessionEndNotice();
            foreach (var runner in Runners)
                if (runner && runner.IsRunning) await runner.Shutdown();
            if (hadName) PlayerPrefs.SetString(nameKey, originalName); else PlayerPrefs.DeleteKey(nameKey);
            Directory.CreateDirectory("Temp/RoomClosure");
            File.WriteAllLines("Temp/RoomClosure/runtime.txt", Report);
        }
    }

    private static MenuConnection Attach(MenuConnectionBehaviour owner, NetworkRunner runner, bool withUi)
    {
        var connection = (MenuConnection)owner.Create();
        if (!withUi) typeof(MenuConnection).GetField("RoomClosed", Private).SetValue(connection, null);
        Field(connection, "_runner", runner);
        typeof(MenuConnection).GetMethod("ObserveSession", Private).Invoke(connection, new object[] { runner });
        ((MenuSessionEndState)typeof(MenuConnection).GetField("_sessionEnd", Private).GetValue(connection)).MarkConnected(true);
        Connections.Add(connection);
        return connection;
    }

    private static NetworkChatSystem CreateChat(NetworkRunner runner, string playerName)
    {
        PlayerPrefs.SetString("Photon.Menu.Username", playerName);
        // 正式遊戲每程序只有一份 local singleton。受控 Multi Peer 先讓各自 Spawned 註冊；
        // 此測試不聲稱驗證 LocalChatPanel 在同程序多個本機玩家的綁定。
        typeof(NetworkChatSystem).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
        var root = new GameObject("Temporary chat", typeof(NetworkObject));
        runner.MoveToRunnerScene(root);
        var chat = root.AddComponent<NetworkChatSystem>();
        new NetworkObjectBaker().Bake(root);
        runner.RegisterSceneObjects(SceneRef.FromIndex(0), new[] { root.GetComponent<NetworkObject>() }, default);
        return chat;
    }

    private static async Task<NetworkRunner> Connect(bool server)
    {
        var runner = new GameObject(server ? "Room host" : "Room client").AddComponent<NetworkRunner>();
        Runners.Add(runner);
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        var manager = runner.gameObject.AddComponent<ShockwaveVerificationSceneManager>();
        manager.IsSceneTakeOverEnabled = false;
        var app = new Fusion.Photon.Realtime.FusionAppSettings();
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.CopyTo(app); app.FixedRegion = "asia";
        var result = await runner.StartGame(new StartGameArgs {
            GameMode = server ? GameMode.Host : GameMode.Client, SessionName = session,
            Config = config, SceneManager = manager, CustomPhotonAppSettings = app, PlayerCount = 4, IsVisible = false });
        Check(result.Ok, "Runner starts: " + result.ShutdownReason);
        await Until(() => runner.IsRunning && runner.Tick.Raw > 0, "runner tick");
        return runner;
    }
}
#endif
