using System;
using UnityEngine;

/// <summary>共用動畫幀座標換算；不讀 Animator、不決定技能或敵人的傷害權限。</summary>
[Serializable]
public sealed class AnimationFrameTiming
{
    [Min(.001f), Tooltip("原始動畫在 1 倍速下的總秒數，必須大於 0。用來換算總幀數與動畫位置；不是加速後秒數。")]
    public float ClipDuration = 1f;
    [Min(.001f), Tooltip("來源動畫每秒幀數（FPS），必須大於 0，例如 24、30、60。指定幀由 0 起算，不是遊戲畫面 FPS。")]
    public float FramesPerSecond = 24f;
    [Min(.001f), Tooltip("實際播放倍率，必須大於 0。2 代表兩倍速；事件秒數 = 指定幀 ÷ FPS ÷ 倍率。Tank 的幀模式由程式取樣動畫，不再疊乘 Controller 的 State Speed。")]
    public float PlaybackSpeed = 1f;

    public bool IsValid => IsPositiveFinite(ClipDuration) && IsPositiveFinite(FramesPerSecond) &&
        IsPositiveFinite(PlaybackSpeed) && IsPositiveFinite(TotalFrames) && IsPositiveFinite(DurationSeconds);
    public float TotalFrames => ClipDuration * FramesPerSecond;
    public float DurationSeconds => ClipDuration / PlaybackSpeed;
    public bool ContainsFrame(float frame) => IsValid && !float.IsNaN(frame) && !float.IsInfinity(frame) &&
        frame >= 0f && frame <= TotalFrames;
    public float FrameToSeconds(float frame) => IsValid ? Mathf.Clamp(frame, 0f, TotalFrames) / FramesPerSecond / PlaybackSpeed : 0f;
    public float FrameToNormalized(float frame) => IsValid ? Mathf.Clamp01(frame / TotalFrames) : 0f;
    private static bool IsPositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
