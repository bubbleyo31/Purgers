using Assert = NUnit.Framework.Assert;
using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

[Category("PurgersRegression")]
public sealed class PlayerSettingsRuntimeTests
{
    NetworkRunner runner;
    [UnityTest] public IEnumerator MenuKeepsSlideMomentumAndPerInputGrappleModeOverridesPrefab()
    {
        yield return new EnterPlayMode();
        runner = new GameObject("Settings runtime test").AddComponent<NetworkRunner>();
        var config = NetworkProjectConfig.Deserialize(NetworkProjectConfig.Serialize(NetworkProjectConfig.Global));
        config.PeerMode = NetworkProjectConfig.PeerModes.Multiple;
        foreach (var entry in NetworkProjectConfig.Global.PrefabTable.GetEntries()) config.PrefabTable.AddSource(entry.Item2);
        var manager = runner.gameObject.AddComponent<GrappleVisualVerificationSceneManager>(); manager.IsSceneTakeOverEnabled = false;
        var start = runner.StartGame(new StartGameArgs { GameMode = GameMode.Single, Config = config, SceneManager = manager });
        float until = Time.realtimeSinceStartup + 30;
        while ((!start.IsCompleted || runner.Tick.Raw <= 0 || runner.SceneManager.IsBusy) && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(start.IsCompleted && start.Result.Ok && runner.Tick.Raw > 0);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KCC_Player.prefab");
        var obj = runner.Spawn(prefab.GetComponent<NetworkObject>(), new Vector3(4000, 5000, 4000), Quaternion.identity, runner.LocalPlayer);
        obj.GetComponent<Player>().enabled = false;
        var grapple = obj.GetComponent<PlayerGrapple>();
        typeof(PlayerGrapple).GetField("useHoldToMaintainGrappleInput", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(grapple, false);
        Set(grapple, "CurrentPhase", GrapplePhase.PreFire); Set(grapple, "PhaseTimer", TickTimer.CreateFromSeconds(runner, 1000));
        grapple.Simulate(false, false, false, false, Vector2.zero, 1, true);
        Assert.AreNotEqual(GrapplePhase.PreFire, grapple.CurrentPhase, "Client hold release overrides prefab toggle");
        Set(grapple, "CurrentPhase", GrapplePhase.PreFire); Set(grapple, "PhaseTimer", TickTimer.CreateFromSeconds(runner, 1000));
        grapple.Simulate(false, false, false, false, Vector2.zero, 1, false);
        Assert.AreEqual(GrapplePhase.PreFire, grapple.CurrentPhase, "Toggle release preserves tether");
        grapple.CancelFromSpecialAbility(false, false);
        var slide = obj.GetComponent<PlayerSlideController>();
        Set(slide, "IsSliding", (NetworkBool)true); Set(slide, "IsCrouched", (NetworkBool)true); Set(slide, "SlideVelocity", Vector3.forward * 12);
        var input = new NetInput { SettingsMenuOpen = true }; input.Buttons.Set(InputButton.Crouch, true);
        slide.Simulate(input, false, false, false, false);
        Assert.IsFalse(slide.IsSliding); Assert.IsTrue(slide.IsCrouched);
        Assert.That(obj.GetComponent<Fusion.Addons.KCC.KCC>().Data.KinematicVelocity.z, Is.GreaterThan(11), "Menu may release slide ownership but cannot brake existing momentum");
        runner.Despawn(obj);
    }
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (runner) { var close = runner.Shutdown(); float deadline = Time.realtimeSinceStartup + 15; while (!close.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null; }
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
