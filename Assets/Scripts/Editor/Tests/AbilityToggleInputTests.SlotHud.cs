using System.Collections;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

public sealed partial class AbilityToggleInputTests
{
    [UnityTest, Explicit("需要 Photon，依 Editor 順位獨立執行技能槽 Host/Client 驗證")]
    public IEnumerator SlotHudHostClientRecastCooldownLateJoinAndReconnect()
    {
        SessionState.SetBool(AbilityToggleTestResults.ActiveKey, true);
        yield return new EnterPlayMode();
        yield return RunSlotHudNetwork();
    }

    private IEnumerator RunSlotHudNetwork()
    {
        System.IO.Directory.CreateDirectory("Temp/AbilityToggle");
        System.IO.File.WriteAllText("Temp/AbilityToggle/network-progress.txt", "Skill slot HUD Host/Client\n");
        string session = "purgers-slot-hud-" + System.Guid.NewGuid().ToString("N");
        yield return Connect(true, session);
        SpawnPlayer(runner.LocalPlayer);
        var blink = Spawn<PlayerBlinkAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/Blink.asset");
        SetField(blink, "primedSeconds", 90f);
        SetField(blink, "cooldownSeconds", 60f);
        blink.SimulateAbility(Press, default);
        Assert.That(blink.Phase, Is.EqualTo(PlayerActiveAbilityPhase.Armed));
        var runtime = blink.GetComponent<PlayerAbilityRuntime>();
        Assert.That(AbilitySlotHudSnapshot.Read(runtime, false).Phase, Is.EqualTo(AbilitySlotVisualPhase.Recast));
        Assert.That(AbilitySlotHudSnapshot.Read(runtime, false).Label, Is.EqualTo("再次施放"));
        Assert.That(AbilitySlotHudSnapshot.Read(runtime, true).Label, Is.EqualTo("待命"));
        Assert.That(blink.CooldownRemainingSeconds, Is.Zero, "待命期間不提前冷卻");

        yield return Connect(false, session);
        yield return Until(() => client.FindObject(blink.Object.Id) != null, "late join Blink");
        var proxy = client.FindObject(blink.Object.Id).GetComponent<PlayerBlinkAbility>();
        var proxyRuntime = proxy.GetComponent<PlayerAbilityRuntime>();
        yield return Until(() => proxy.TimedHudState.IsVisible, "late join armed snapshot");
        var state = AbilitySlotHudSnapshot.Read(proxyRuntime, false);
        Assert.That(state.Phase, Is.EqualTo(AbilitySlotVisualPhase.Recast));
        Assert.That(state.PhaseDuration, Is.EqualTo(90f));
        Assert.That(state.PhaseRemaining, Is.LessThan(state.PhaseDuration));
        Assert.That(state.PhaseRatio, Is.EqualTo(state.PhaseRemaining / 90f).Within(.001f));
        int sequence = blink.ActivationSequence;
        proxy.SimulateAbility(Press, default);
        Assert.That(blink.Phase, Is.EqualTo(PlayerActiveAbilityPhase.Armed), "Client proxy 不得決定衝刺");

        blink.SimulateAbility(Press, default);
        Assert.That(blink.ActivationSequence, Is.EqualTo(sequence));
        Assert.That(blink.CooldownDurationSeconds, Is.EqualTo(60f).Within(runner.DeltaTime));
        yield return Until(() => proxy.CooldownRemainingSeconds > 0f, "cooldown replicated");
        state = AbilitySlotHudSnapshot.Read(proxyRuntime, false);
        Assert.That(state.Phase, Is.EqualTo(AbilitySlotVisualPhase.Cooling), "衝刺安全期限不覆蓋正式冷卻");
        Assert.That(state.CooldownDuration, Is.EqualTo(blink.CooldownDurationSeconds));
        Assert.That(state.CooldownRatio, Is.EqualTo(state.CooldownRemaining / state.CooldownDuration).Within(.001f));

        var close = client.Shutdown();
        while (!close.IsCompleted) yield return null;
        client = null;
        yield return Connect(false, session);
        yield return Until(() => client.FindObject(blink.Object.Id) != null, "reconnect cooldown object");
        proxy = client.FindObject(blink.Object.Id).GetComponent<PlayerBlinkAbility>();
        yield return Until(() => proxy.CooldownDurationSeconds > 0f, "reconnect full cooldown duration");
        state = AbilitySlotHudSnapshot.Read(proxy.GetComponent<PlayerAbilityRuntime>(), false);
        Assert.That(state.CooldownDuration, Is.EqualTo(blink.CooldownDurationSeconds));
        Assert.That(state.CooldownRatio, Is.LessThan(1f), "重連不能重畫全滿冷卻");

        SpawnPlayer(client.LocalPlayer);
        var ownBlink = Spawn<PlayerBlinkAbility>("Assets/_Project_Assets/Data/PlayerAbility/Active/Blink.asset");
        SetField(ownBlink, "primedSeconds", 60f); SetField(ownBlink, "cooldownSeconds", 15f);
        var driver = runner.gameObject.AddComponent<AbilityToggleNetworkDriver>();
        driver.InputPlayer = client.LocalPlayer; driver.Module = ownBlink; runner.AddGlobal(driver);
        yield return Until(() => client.FindObject(ownBlink.Object.Id) != null, "client owned Blink");
        var ownProxy = client.FindObject(ownBlink.Object.Id).GetComponent<PlayerBlinkAbility>();
        clientPress = true;
        yield return Until(() => ownBlink.Phase == PlayerActiveAbilityPhase.Armed && ownProxy.TimedHudState.IsVisible, "Client first press armed");
        Assert.That(ownProxy.Object.HasInputAuthority && !ownProxy.Object.HasStateAuthority, Is.True);
        Assert.That(AbilitySlotHudSnapshot.Read(ownProxy.GetComponent<PlayerAbilityRuntime>(), false).Label, Is.EqualTo("再次施放"));
        clientPress = false;
        yield return Until(() => !driver.PreviousButtons.IsSet(InputButton.Ability1), "Client release");
        clientPress = true;
        yield return Until(() => ownProxy.CooldownRemainingSeconds > 0f, "Client second press cooldown");
        Assert.That(ownProxy.CooldownDurationSeconds, Is.EqualTo(ownBlink.CooldownDurationSeconds));
        Assert.That(AbilitySlotHudSnapshot.Read(ownProxy.GetComponent<PlayerAbilityRuntime>(), false).Phase, Is.EqualTo(AbilitySlotVisualPhase.Cooling));
        clientPress = false; driver.Module = null;
    }
}
