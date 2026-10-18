using System.Collections.Generic;
using UnityEngine;

public partial class GameplayAudioService
{
    [Header("世界循環音效集區")]
    [SerializeField, Min(1), Tooltip("同時存在的 3D 世界循環聲上限，預設 32。滿載時拒絕新聲音；不搶占仍在播放的聲音。")]
    private int maximumWorldLoopVoices = 32;

    private sealed class ActiveWorldLoop
    {
        public AudioSource Source;
        public Transform Target;
        public float FadeDuration;
        public float FadeElapsed;
        public float FadeStartVolume;
    }
    private readonly List<AudioSource> worldLoopVoices = new List<AudioSource>();
    private readonly Dictionary<int, ActiveWorldLoop> activeWorldLoops = new Dictionary<int, ActiveWorldLoop>();
    private readonly List<int> expiredWorldLoops = new List<int>();
    private int nextWorldLoopHandle;

    /// <summary>播放已由權威選定變體的世界循環聲。呼叫端持有 handle 並在同步狀態結束時停止。</summary>
    public int StartWorldLoop(GameplayAudioCue cue, byte variant, float pitch, Transform target) =>
        StartWorldLoopWithVolume(cue, variant, pitch, target, 1f);

    /// <summary>沿同一循環聲集區加入呼叫端音量；保留原四參數介面及反射相容性。</summary>
    public int StartWorldLoopWithVolume(GameplayAudioCue cue, byte variant, float pitch, Transform target, float volumeScale)
    {
        if (!isActiveAndEnabled || cue == null || target == null || !target.gameObject.activeInHierarchy ||
            pitch <= 0f || cue.GetClip(variant) == null) return -1;
        AudioSource source = null;
        foreach (var voice in worldLoopVoices)
        {
            if (voice == null) continue;
            bool used = false;
            foreach (var active in activeWorldLoops.Values)
                if (active.Source == voice) { used = true; break; }
            if (!used) { source = voice; break; }
        }
        if (source == null)
        {
            if (worldLoopVoices.Count >= Mathf.Max(1, maximumWorldLoopVoices)) return -1;
            source = CreateAudioSource("WorldLoopVoice_" + worldLoopVoices.Count.ToString("00"));
            worldLoopVoices.Add(source);
        }
        ResetAudioSource(source);
        ConfigureSource(source, cue, cue.GetClip(variant), target.position, false, true, pitch, volumeScale);
        do
        {
            nextWorldLoopHandle = nextWorldLoopHandle == int.MaxValue ? 1 : nextWorldLoopHandle + 1;
        } while (activeWorldLoops.ContainsKey(nextWorldLoopHandle));
        activeWorldLoops.Add(nextWorldLoopHandle, new ActiveWorldLoop { Source = source, Target = target });
        source.Play();
        return nextWorldLoopHandle;
    }

    /// <summary>以持有權為準；暫停音訊不應被當成需要重播。</summary>
    public bool HasWorldLoop(int handle) => activeWorldLoops.ContainsKey(handle);

    public void StopWorldLoop(int handle)
    {
        if (!activeWorldLoops.TryGetValue(handle, out var active)) return;
        ResetAudioSource(active.Source);
        activeWorldLoops.Remove(handle);
    }

    /// <summary>開始線性淡出；重複要求不延長尾音，handle 保留到結束，可隨時 StopWorldLoop 強制中斷。</summary>
    public void FadeOutWorldLoop(int handle, float seconds)
    {
        if (!activeWorldLoops.TryGetValue(handle, out var active)) return;
        if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
        { StopWorldLoop(handle); return; }
        if (active.FadeDuration > 0f) return;
        if (active.Source == null) { StopWorldLoop(handle); return; }
        active.FadeDuration = seconds;
        active.FadeStartVolume = active.Source.volume;
    }

    private void UpdateWorldLoops() => AdvanceWorldLoops(Time.unscaledDeltaTime);

    private void AdvanceWorldLoops(float deltaTime)
    {
        expiredWorldLoops.Clear();
        foreach (var pair in activeWorldLoops)
        {
            var active = pair.Value;
            if (active.Source == null || active.Target == null || !active.Target.gameObject.activeInHierarchy)
                expiredWorldLoops.Add(pair.Key);
            else
            {
                active.Source.transform.position = active.Target.position;
                if (active.FadeDuration <= 0f) continue;
                active.FadeElapsed += Mathf.Max(0f, deltaTime);
                active.Source.volume = active.FadeStartVolume * Mathf.Clamp01(1f - active.FadeElapsed / active.FadeDuration);
                if (active.FadeElapsed >= active.FadeDuration) expiredWorldLoops.Add(pair.Key);
            }
        }
        foreach (int handle in expiredWorldLoops) StopWorldLoop(handle);
    }

    private void StopAllWorldLoops()
    {
        foreach (var active in activeWorldLoops.Values) ResetAudioSource(active.Source);
        activeWorldLoops.Clear();
    }

    private void OnDisable() => StopAllWorldLoops();
}
