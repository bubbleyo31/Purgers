#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>重用已存在的隔離 Host/Client 觀戰流程，驗證正式護目鏡實例；不保存場景或玩家進度。</summary>
public static class FirstPersonGogglesVerification
{
    public static string Status { get; private set; } = "尚未執行";
    private static readonly List<string> report = new List<string>();
    private const string Folder = "Documentation/ProjectArchitecture/Validation/FirstPersonGoggles";
    private static NetworkRunner Runner(string name) => (NetworkRunner)typeof(SpectatorRuntimeVerification)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); report.Add("PASS " + message); }
    public static async void Begin()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要沒有 Runner 的 Play Mode。");
        var goggles = UnityEngine.Object.FindObjectOfType<LocalFirstPersonGoggles>(true);
        if (goggles == null) throw new InvalidOperationException("正式護目鏡未配置。");
        Status = "執行中"; report.Clear(); Directory.CreateDirectory(Folder);
        var graphic = goggles.GetComponent<GoggleRimGraphic>();
        float originalThickness = graphic.Thickness, originalOpacity = graphic.Opacity;
        bool hostShown = false, clientShown = false, observed = false, respawned = false, emptyView = false;
        NetworkRunner bound = null; float end = Time.realtimeSinceStartup + 160;
        try
        {
            Check(!goggles.IsVisible, "No Runner: goggles hidden in menu");
            SpectatorRuntimeVerification.Begin();
            while (SpectatorRuntimeVerification.Status == "執行中")
            {
                if (Time.realtimeSinceStartup > end) throw new TimeoutException("既有觀戰驗證流程逾時");
                var client = Runner("client"); var host = Runner("host");
                var candidate = client != null && client.IsRunning ? client : host;
                if (candidate != null && candidate.IsRunning)
                {
                    if (candidate != bound) { goggles.BindRunner(candidate); bound = candidate; await Task.Delay(100); }
                    if (LocalPlayerSpectatorController.TryGetViewedPlayerObject(candidate, out var view))
                    {
                        var health = view.GetComponent<PlayerHealth>();
                        if (health != null && health.IsRescueReady && !health.IsDead && goggles.ViewedObject == view && goggles.IsVisible)
                        {
                            if (candidate == host) hostShown = true;
                            if (candidate == client && view.InputAuthority == candidate.LocalPlayer)
                            { clientShown = true; if (observed) respawned = true; }
                            if (candidate == client && view.InputAuthority != candidate.LocalPlayer && !observed)
                            {
                                observed = true;
                                ScreenCapture.CaptureScreenshot(Folder + "/playmode-observing.png");
                            }
                        }
                    }
                    else if (observed && !goggles.IsVisible) emptyView = true;
                }
                await Task.Delay(1);
            }
            Check(SpectatorRuntimeVerification.Status == "通過", "Existing Host/Client spectator scenario: " + SpectatorRuntimeVerification.Status);
            Check(hostShown, "Host local first-person shows goggles");
            Check(clientShown, "Late-joining Client local first-person shows goggles");
            Check(observed, "Client death switches goggles source to observed teammate");
            Check(respawned, "Respawn rebinds goggles to own player");
            Check(emptyView, "No surviving target clears goggles");
            while (NetworkRunner.Instances.Any(r => r.IsRunning) && Time.realtimeSinceStartup < end) await Task.Delay(100);
            goggles.BindRunner(null); await Task.Delay(350);
            Check(!goggles.IsVisible && goggles.ViewedObject == null, "Shutdown clears presentation without stale player");
            Check(!graphic.raycastTarget && !goggles.GetComponent<CanvasGroup>().blocksRaycasts, "Effect cannot intercept UI input");
            Check(goggles.GetComponent<Canvas>().sortingOrder == -10, "Runtime stays below existing HUD");
            Status = "通過";
        }
        catch (Exception ex) { Status = "失敗"; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            if (goggles != null) goggles.BindRunner(null);
            if (graphic != null) { graphic.Thickness = originalThickness; graphic.Opacity = originalOpacity; }
            File.WriteAllLines(Folder + "/playmode-host-client.txt", report);
        }
    }
}
#endif

