using System;
using Fusion;
using UnityEngine;

/// <summary>只供各端呈現；不決定射線、傷害、移動或網路階段。</summary>
[Serializable]
public sealed class BeamPresentationSettings
{
    [Header("細線瞄準／中線蓄力／正式發射")]
    [Min(.001f), Tooltip("細線寬度，公尺。")] public float aimWidth = .025f;
    [Min(.001f), Tooltip("中線寬度，公尺。敵人鎖定後使用；玩家仍跟隨準星。")] public float chargeWidth = .075f;
    [Min(.001f), Tooltip("正式發射最大寬度，公尺；只改外觀，不改命中粗細。")] public float shotWidth = .3f;
    [ColorUsage(true, true)] public Color aimColor = new Color(.55f, .85f, .8f, .6f);
    [ColorUsage(true, true)] public Color chargeColor = new Color(.02f, .45f, .34f, 1f);
    [ColorUsage(true, true)] public Color shotColor = new Color(.12f, 2f, 1.6f, 1f);
    [ColorUsage(true, true)] public Color coreColor = new Color(2f, 2.5f, 2.3f, 1f);
    [Range(0f, 1f), Tooltip("正式發射亮核占外層寬度比例；0 不顯示。")] public float coreWidthRatio = .28f;
    [Tooltip("X：發射後進度 0～1；Y：寬度倍率 0～1。到期強制為零。")]
    public AnimationCurve collapse = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [Tooltip("支援頂點色的專用光束材質；不修改共享材質。")] public Material lineMaterial;
    public BeamParticleSettings particles = new BeamParticleSettings();
    public BeamAudioSettings audio = new BeamAudioSettings();
}

[Serializable]
public sealed class BeamParticleSettings
{
    public bool enabled = true;
    [Tooltip("可替換純視覺 ParticleSystem Prefab；空白使用內建一次性火花。不得包含物理或網路元件。")]
    public ParticleSystem prefab;
    public Material material;
    [Range(0, 128)] public int muzzleCount = 18;
    [Range(0, 256)] public int beamCount = 48;
    [Range(0, 128)] public int endCount = 22;
    [Min(.01f), Tooltip("粒子最大存活秒數；不延長光束或傷害。")] public float lifetime = .18f;
    [Min(.001f)] public float size = .045f;
    [Min(0f)] public float speed = 2.5f;
    [Min(0f), Tooltip("光束周圍粒子的散布半徑，公尺。")] public float radius = .12f;
    [ColorUsage(true, true)] public Color color = new Color(.2f, 2f, 1.65f, 1f);
}

[Serializable]
public sealed class BeamAudioSlot
{
    [Tooltip("沿 GameplayAudioService 播放 3D 世界聲。留空安靜；使用指定 Clip 索引與 Pitch，各端一致，不另做隨機。")]
    public GameplayAudioCue cue;
    [Range(0, 255)] public int clipIndex;
    [Range(.01f, 3f)] public float pitch = 1f;
    [Range(0f, 1f)] public float volume = 1f;
}

[Serializable]
public sealed class BeamAudioSettings
{
    [Tooltip("接受本次攻擊時的一次聲；通常與細線進入同時，可只填其中一個。")]
    public BeamAudioSlot prepare = new BeamAudioSlot();
    public BeamAudioSlot aim = new BeamAudioSlot();
    public BeamAudioSlot charge = new BeamAudioSlot();
    public BeamAudioSlot fire = new BeamAudioSlot();
    [Tooltip("細線聲循環到離開該階段；取消、死亡與卸下立即停止。")] public bool loopAim = true;
    [Tooltip("中線聲循環到離開該階段；發射或取消立即停止。")] public bool loopCharge = true;
}

public enum BeamPresentationStage { Hidden, Aim, Charge, Fired }

public static class BeamPresentationRules
{
    public static float ShotScale(float remaining, float duration, AnimationCurve curve)
    {
        if (remaining <= 0f || duration <= 0f) return 0f;
        float progress = Mathf.Clamp01(1f - remaining / duration);
        return Mathf.Clamp01(curve != null ? curve.Evaluate(progress) : 1f - Mathf.SmoothStep(0, 1, progress));
    }

    public static bool IsCharging(float remaining, float duration, float aimRatio) =>
        duration > 0f && 1f - Mathf.Clamp01(remaining / duration) >= Mathf.Clamp01(aimRatio);

    // Object.RenderTime is Fusion's interpolation clock: no per-frame local timer restart or late-join replay.
    public static float Remaining(TickTimer timer, NetworkObject obj, NetworkRunner runner) =>
        timer.TargetTick.HasValue && obj != null && obj.IsValid && runner != null
            ? Mathf.Max(0f, (float)(timer.TargetTick.Value * (double)runner.DeltaTime - obj.RenderTime)) : 0f;
}

/// <summary>第一份快照只恢復持續狀態；序號內的瞬間事件只播放一次，回滾不重播。</summary>
public sealed class BeamPresentationEventGate
{
    private bool initialized;
    private int sequence, seen;
    public bool NewAttack { get; private set; }
    public bool Observe(int nextSequence, int stage)
    {
        NewAttack = false;
        if (!initialized)
        {
            initialized = true; sequence = nextSequence;
            seen = stage > 0 ? (1 << (stage + 1)) - 1 : 0;
            return false;
        }
        int delta = unchecked(nextSequence - sequence);
        if (delta < 0) return false;
        if (delta > 0) { sequence = nextSequence; seen = 0; }
        if (stage <= 0 || (seen & (1 << stage)) != 0) return false;
        NewAttack = seen == 0;
        seen |= (1 << (stage + 1)) - 1;
        return true;
    }
}

