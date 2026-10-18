using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class WorldAudioLoopTests
{
    private GameObject root, targetA, targetB;
    private GameplayAudioService service;
    private GameplayAudioCue cue;
    private AudioClip clip;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SetUp] public void Setup()
    {
        Assert.That(typeof(GameplayAudioService).GetMethod("StartWorldLoop"), Is.Not.Null,
            "既有音效服務必須支援有距離、方向及可清理的世界循環聲。");
        root = new GameObject("World loop test"); root.SetActive(false);
        service = root.AddComponent<GameplayAudioService>();
        typeof(GameplayAudioService).GetField("persistAcrossScenes", Private).SetValue(service, false);
        root.SetActive(true);
        targetA = new GameObject("Emitter A"); targetB = new GameObject("Emitter B");
        cue = ScriptableObject.CreateInstance<GameplayAudioCue>();
        clip = AudioClip.Create("Loop", 4410, 1, 44100, false);
        typeof(GameplayAudioCue).GetField("clips", Private).SetValue(cue, new[] { clip });
    }
    private object Call(string method, params object[] args) => typeof(GameplayAudioService).GetMethod(method).Invoke(service, args);
    private int Start(Transform target) => (int)Call("StartWorldLoop", cue, (byte)0, 1f, target);
    private void Tick() => typeof(GameplayAudioService).GetMethod("UpdateWorldLoops", Private).Invoke(service, null);
    [Test] public void StoppingOnePlayerDoesNotStopAnotherAndVoicesAreReused()
    {
        int a = Start(targetA.transform), b = Start(targetB.transform);
        Assert.That(a, Is.GreaterThan(0)); Assert.That(b, Is.GreaterThan(a));
        Call("StopWorldLoop", a);
        Assert.That(Call("HasWorldLoop", a), Is.False);
        Assert.That(Call("HasWorldLoop", b), Is.True);
        int c = Start(targetA.transform);
        Assert.That(c, Is.GreaterThan(b));
        Assert.That(root.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
    }
    [Test] public void WorldLoopFollowsEmitterAndDisappearsOnDespawn()
    {
        int a = Start(targetA.transform);
        targetA.transform.position = new Vector3(10, 20, 30); Tick();
        var source = root.GetComponentInChildren<AudioSource>();
        Assert.That(source.transform.position, Is.EqualTo(targetA.transform.position));
        Assert.That(source.spatialBlend, Is.EqualTo(1f)); Assert.That(source.loop, Is.True);
        Object.DestroyImmediate(targetA); Tick();
        Assert.That(Call("HasWorldLoop", a), Is.False); Assert.That(source.clip, Is.Null);
    }
    [Test] public void BudgetNeverStealsAnExistingLoopAndDisableCleansUp()
    {
        typeof(GameplayAudioService).GetField("maximumWorldLoopVoices", Private).SetValue(service, 1);
        int a = Start(targetA.transform);
        Assert.That(Start(targetB.transform), Is.EqualTo(-1));
        Assert.That(Call("HasWorldLoop", a), Is.True);
        typeof(GameplayAudioService).GetMethod("OnDisable", Private).Invoke(service, null);
        Assert.That(Call("HasWorldLoop", a), Is.False);
    }
    [Test] public void MissingOrInactiveTargetDoesNotAllocate()
    {
        Assert.That(Start(null), Is.EqualTo(-1)); targetA.SetActive(false);
        Assert.That(Start(targetA.transform), Is.EqualTo(-1));
        Assert.That(root.GetComponentsInChildren<AudioSource>().Length, Is.Zero);
    }
    private void Fade(int handle, float seconds)
    {
        var method = typeof(GameplayAudioService).GetMethod("FadeOutWorldLoop");
        Assert.That(method, Is.Not.Null, "世界循環聲需要可中斷的淡出。");
        method.Invoke(service, new object[] { handle, seconds });
    }
    private void Advance(float seconds) => typeof(GameplayAudioService).GetMethod("AdvanceWorldLoops", Private).Invoke(service, new object[] { seconds });
    [Test] public void FadeUsesOriginalVolumeAndRepeatedRequestsDoNotExtendIt()
    {
        int a = Start(targetA.transform);
        var source = root.GetComponentInChildren<AudioSource>(); source.volume = .6f;
        Fade(a, .15f); Advance(.05f);
        Assert.That(source.volume, Is.EqualTo(.4f).Within(.001f));
        Assert.That(Call("HasWorldLoop", a), Is.True);
        Fade(a, .15f); Advance(.05f);
        Assert.That(source.volume, Is.EqualTo(.2f).Within(.001f));
        Advance(.051f);
        Assert.That(Call("HasWorldLoop", a), Is.False);
        Assert.That(source.clip, Is.Null);
    }
    [Test] public void HardStopInterruptsOnlyOwnedFadeAndReusedVoiceHasFullVolume()
    {
        int a = Start(targetA.transform), b = Start(targetB.transform);
        Fade(a, .15f); Advance(.05f);
        Call("StopWorldLoop", a);
        Assert.That(Call("HasWorldLoop", a), Is.False);
        Assert.That(Call("HasWorldLoop", b), Is.True);
        int c = Start(targetA.transform); Advance(.2f);
        Assert.That(Call("HasWorldLoop", c), Is.True);
        Assert.That(root.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
        Assert.That(root.GetComponentInChildren<AudioSource>().volume, Is.EqualTo(1f));
    }
    [Test] public void ZeroFadeStopsImmediatelyAndDespawnDoesNotWaitForFade()
    {
        int a = Start(targetA.transform); Fade(a, 0f);
        Assert.That(Call("HasWorldLoop", a), Is.False);
        a = Start(targetA.transform); Fade(a, .15f);
        Object.DestroyImmediate(targetA); Advance(.01f);
        Assert.That(Call("HasWorldLoop", a), Is.False);
    }
    [TearDown] public void Cleanup()
    {
        if (root != null) Object.DestroyImmediate(root);
        if (targetA != null) Object.DestroyImmediate(targetA);
        if (targetB != null) Object.DestroyImmediate(targetB);
        if (cue != null) Object.DestroyImmediate(cue);
        if (clip != null) Object.DestroyImmediate(clip);
    }
}
