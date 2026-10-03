using UnityEngine;

/// <summary>
/// 單一火焰的播放狀態：只接受前進的成功射擊序號，不排隊、不因預測回捲重播。
/// 綁定／失效後第一筆只建立基準；同一畫面內多次射擊合併為最新一次爆發。
/// </summary>
public sealed class FirstPersonMuzzleFlashPlayback
{
    public const int FrameCount = 12;
    public bool IsPlaying { get; private set; }
    public bool ReleaseRequested { get; private set; }
    private bool initialized;
    private bool wasEligible;
    private int lastSequence;
    private int lastState;
    private float elapsed;

    public bool Observe(int sequence, int state, bool eligible)
    {
        ReleaseRequested = false;
        if (!initialized)
        {
            initialized = true;
            lastSequence = sequence;
            lastState = state;
            wasEligible = eligible;
            return false;
        }

        if (state != lastState || (wasEligible && !eligible))
        {
            IsPlaying = false;
            ReleaseRequested = true;
        }
        lastState = state;
        wasEligible = eligible;

        // 模數比較可跨 int 正負邊界；回捲不降低已呈現過的最高序號。
        if (unchecked(sequence - lastSequence) <= 0)
            return false;
        lastSequence = sequence;
        if (!eligible)
            return false;
        elapsed = 0f;
        IsPlaying = true;
        return true;
    }

    public int Advance(float deltaSeconds, float framesPerSecond)
    {
        if (!IsPlaying) return -1;
        elapsed += Mathf.Max(0f, deltaSeconds);
        int frame = Mathf.FloorToInt(elapsed * Mathf.Clamp(framesPerSecond, 1f, 120f));
        if (frame < FrameCount) return frame;
        IsPlaying = false;
        return -1;
    }

    public void Invalidate()
    {
        initialized = false;
        IsPlaying = false;
        ReleaseRequested = true;
        elapsed = 0f;
    }
}
