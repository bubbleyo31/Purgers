using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>暫時轉場衰減與使用者 Master 設定合成；不更動偏好或音效播放責任。</summary>
public static class GameplayAudioTransitionGain
{
    private static readonly Dictionary<object, float> gains = new Dictionary<object, float>();
    private static readonly Dictionary<AudioMixer, float> masters = new Dictionary<AudioMixer, float>();
    public static float Gain
    {
        get { float gain = 1f; foreach (float value in gains.Values) gain = Mathf.Min(gain, value); return gain; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { gains.Clear(); masters.Clear(); }
    public static float ComposeMasterDb(float userDb, float gain) => gain <= 0f ? -80f : Mathf.Max(-80f, userDb + 20f * Mathf.Log10(Mathf.Clamp01(gain)));
    public static void ApplyUserMaster(AudioMixer mixer, float userDb)
    {
        if (mixer == null) return;
        masters[mixer] = userDb;
        mixer.SetFloat("PlayerMasterVolume", ComposeMasterDb(userDb, Gain));
    }
    public static void Set(object owner, float gain)
    {
        if (owner == null) return;
        gains[owner] = Mathf.Clamp01(gain); Refresh();
    }
    public static void Release(object owner) { if (owner != null && gains.Remove(owner)) Refresh(); }
    private static void Refresh()
    {
        foreach (var pair in masters)
            if (pair.Key != null) pair.Key.SetFloat("PlayerMasterVolume", ComposeMasterDb(pair.Value, Gain));
    }
}
