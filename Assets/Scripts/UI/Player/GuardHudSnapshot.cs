using System;

/// <summary>格檔 HUD 的唯讀數值；不計時、不提交玩法，破防期限與耐力比例分開。</summary>
public readonly struct GuardHudSnapshot
{
    public readonly bool Visible, Guarding, Broken, Recovering;
    public readonly float Current, Maximum, Ratio, WaitingSeconds, BrokenSeconds;
    public GuardHudSnapshot(bool ready, bool guarding, bool broken, float current, float maximum,
        float waitingSeconds, float brokenSeconds)
    {
        Maximum = Math.Max(1, maximum);
        Current = Math.Max(0, Math.Min(Maximum, current));
        Guarding = guarding; Broken = broken;
        WaitingSeconds = Math.Max(0, waitingSeconds); BrokenSeconds = Math.Max(0, brokenSeconds);
        Recovering = !guarding && !broken && WaitingSeconds <= 0 && Current < Maximum;
        Visible = ready && (guarding || broken || Current < Maximum);
        Ratio = broken ? 0 : Current / Maximum;
    }
}
