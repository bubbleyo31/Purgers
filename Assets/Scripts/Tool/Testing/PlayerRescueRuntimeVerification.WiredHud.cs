#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>檢查已保存的正式 Canvas 接線；不建立替代 UI，不保存 Play Mode 場景。</summary>
public static partial class PlayerRescueRuntimeVerification
{
    public static async void BeginWiredHud()
    {
        if (!Application.isPlaying || NetworkRunner.Instances.Any(r => r.IsRunning))
            throw new InvalidOperationException("需要無其他 Runner 的 Play Mode。");
        var hud = UnityEngine.Object.FindObjectsOfType<LocalPlayerRescueHUD>(true).Single();
        if (hud.transform.parent.GetComponent<BattleHudReferenceFrame>() == null)
            throw new InvalidOperationException("必須沿用正式 HUD ReferenceFrame。");
        var content = hud.transform.Find("Content").gameObject;
        var ring = content.transform.Find("ProgressRing").GetComponent<Image>();
        var label = content.transform.Find("Status").GetComponent<TMP_Text>();
        var menu = GameObject.Find("GameplayHUD Canvas/Menu");
        bool menuActive = menu != null && menu.activeSelf;
        Status = "執行中"; report.Clear(); holdE = false; holdHostE = false;
        session = "rescue-ui-" + Guid.NewGuid().ToString("N");
        const string output = "Temp/RescueHudWiring";
        Directory.CreateDirectory(output);
        try
        {
            await Task.Delay(250);
            Check(!content.activeSelf, "formal HUD hidden with no Runner in main menu");
            host = await Connect(true);
            var driver = host.gameObject.AddComponent<StageTimerPauseVerificationDriver>(); host.AddGlobal(driver);
            CreateLogic(host);
            await Until(() => host.TryGetPlayerObject(host.LocalPlayer, out _), "host player");
            client = await Connect(false); CreateLogic(client);
            await Until(() => client.TryGetPlayerObject(host.LocalPlayer, out _) &&
                host.TryGetPlayerObject(client.LocalPlayer, out _) && client.TryGetPlayerObject(client.LocalPlayer, out _), "replicas");
            // Multi-Peer 共用相機，固定 Client 畫面；正式單 Runner 保持自動解析。
            foreach (var p in UnityEngine.Object.FindObjectsOfType<Player>())
                if (p.Runner == host)
                {
                    p.GetComponent<PlayerLocalView>().enabled = false;
                    foreach (var a in p.GetComponentsInChildren<PlayerAimController>()) a.enabled = false;
                    // Runner 物理場景隔離不等於相機渲染隔離；隱藏另一端同座標模型。
                    foreach (var renderer in p.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                }
            Field(hud, "runner", client);
            foreach (var view in UnityEngine.Object.FindObjectsOfType<LocalPlayerHealthSlider>(true)) Field(view, "runner", client);
            foreach (var view in UnityEngine.Object.FindObjectsOfType<LocalPlayerWeaponHUD>(true)) Field(view, "runner", client);
            var spectator = UnityEngine.Object.FindObjectOfType<LocalPlayerSpectatorController>();
            if (spectator != null) Field(spectator, "runner", client);
            if (menu != null) menu.SetActive(false);
            var victim = PlayerAbilityQualification.FindPlayer(host, host.LocalPlayer);
            var helper = PlayerAbilityQualification.FindPlayer(host, client.LocalPlayer);
            Field(victim.Health, "debugHealth", false); Field(helper.Health, "debugHealth", false);
            await driver.AtTick(() => { victim.Movement.KCC.SetPosition(origin); helper.Movement.KCC.SetPosition(origin + Vector3.right * 10); });
            await Task.Delay(750);
            Check(hud.gameObject.activeInHierarchy && !content.activeSelf, "formal Canvas stays active, healthy player has no rescue prompt");
            await driver.AtTick(() => Fatal(victim));
            await Task.Delay(200);
            Check(!content.activeSelf, "out of range has no rescue prompt");
            await driver.AtTick(() => helper.Movement.KCC.SetPosition(origin + Vector3.right * 1.5f));
            await Until(() => content.activeInHierarchy && label.text.Contains("按住"), "nearby prompt");
            Check(content.transform.Find("Key").gameObject.activeSelf && ring.fillAmount == 0 && content.transform.Find("Key/Label").GetComponent<TMP_Text>().text == "E", "nearby E button visible with empty ring");
            await CaptureWiredHud(output + "/nearby.png");
            holdE = true;
            await Until(() => ring.fillAmount >= .35f && ring.fillAmount < .85f, "actual replicated radial progress");
            Check(label.text.Contains("救援中") && ring.fillMethod == Image.FillMethod.Radial360, "rescue status and partial radial ring show on formal Canvas");
            await CaptureWiredHud(output + "/rescuing.png");
            holdE = false;
            await Until(() => ring.fillAmount == 0 && label.text.Contains("按住"), "release resets HUD");
            Check(content.activeInHierarchy, "release returns to E prompt with empty ring");
            await CaptureWiredHud(output + "/released.png");
            holdE = true;
            await Until(() => victim.Health.IsAlive, "five second rescue");
            await Until(() => !content.activeSelf, "revive hides HUD");
            Check(victim.Health.CurrentHealth == 35, "35 percent revived and formal HUD hidden");
            holdE = false;
            await Task.Delay(150);
            await driver.AtTick(() => Fatal(helper));
            await Until(() => content.activeInHierarchy && label.text.Contains("已倒地"), "local downed prompt");
            Check(ring.fillAmount == 0, "local downed shows countdown and empty ring");
            Check(!content.transform.Find("Key").gameObject.activeSelf, "downed player has no E key or square background");
            await CaptureWiredHud(output + "/downed.png");
            holdHostE = true;
            await Until(() => label.text == "正在被救援" && ring.fillAmount > 0, "local player receives rescue");
            Check(!content.transform.Find("Key").gameObject.activeSelf, "being rescued shows status and progress without E key");
            await CaptureWiredHud(output + "/being-rescued.png");
            holdHostE = false;
            await Until(() => label.text.Contains("已倒地"), "interruption returns to waiting prompt");
            await driver.AtTick(() => helper.Health.KillUnrescuable());
            await Until(() => !content.activeSelf, "dead hides rescue HUD");
            Check(true, "death clears rescue prompt during spectator transition");
            Status = "通過 " + report.Count;
        }
        catch (Exception ex) { Status = "失敗 " + ex.Message; report.Add("FAIL " + ex); Debug.LogException(ex); }
        finally
        {
            holdE = false; holdHostE = false;
            if (client != null && client.IsRunning) await client.Shutdown();
            if (host != null && host.IsRunning) await host.Shutdown();
            foreach (var obj in temporary) if (obj != null) UnityEngine.Object.Destroy(obj);
            temporary.Clear(); Field(hud, "runner", null);
            if (menu != null) menu.SetActive(menuActive);
            File.WriteAllLines(output + "/playmode.txt", report);
        }
    }

    private static async Task CaptureWiredHud(string path)
    {
        // 顯示切換後等正常幀完成 TMP rebuild，再擷取穩定畫面。
        await Task.Delay(200);
        Canvas.ForceUpdateCanvases();
        ScreenCapture.CaptureScreenshot(path);
        await Task.Delay(180);
    }
}
#endif
