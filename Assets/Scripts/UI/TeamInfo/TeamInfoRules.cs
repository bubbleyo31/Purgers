using System;

/// <summary>本機共鍵仲裁。投票優先，持續按住跨越模式邊界不產生新操作。</summary>
public sealed class TeamInfoInputState
{
    public bool IsOpen { get; private set; }
    public bool ReadyPressed { get; private set; }
    public bool EscapeClosed { get; private set; }
    bool previousHeld, previousVote, requireRelease;
    public void Close(bool held) { IsOpen = false; requireRelease |= held; ReadyPressed = false; }
    public void Step(bool held, bool down, bool toggle, bool eligible, bool vote, bool escape)
    {
        ReadyPressed = false;
        EscapeClosed = escape && IsOpen && !vote;
        if (!held) requireRelease = false;
        if (vote != previousVote)
        {
            IsOpen = false;
            if (held && previousHeld) requireRelease = true;
        }
        if (!eligible || escape) Close(held);
        else if (vote)
        {
            IsOpen = false;
            ReadyPressed = down && !requireRelease;
        }
        else if (!requireRelease)
        {
            if (toggle) { if (down) IsOpen = !IsOpen; }
            else if (!held) IsOpen = false;
            else if (down) IsOpen = true;
        }
        previousHeld = held;
        previousVote = vote;
    }
}

/// <summary>動態列的幾何：每行左右留白等於圖示間距，末行重新置中；不截斷項目。</summary>
public readonly struct TeamInfoGrid
{
    public readonly int Count, Columns, Rows;
    public readonly float Size, Width, Height, Gap;
    public TeamInfoGrid(int count, float width, float preferred, float minimum, float gap)
    {
        Count = Math.Max(0, count); Width = Math.Max(1, width); Gap = Math.Max(0, gap);
        minimum = Math.Max(1, Math.Min(minimum, preferred));
        int capacity = Math.Max(1, (int)((Width - Gap) / (minimum + Gap)));
        Columns = Math.Max(1, Math.Min(Count, capacity));
        Rows = Count == 0 ? 0 : (Count + Columns - 1) / Columns;
        Size = Math.Max(1, Math.Min(preferred, (Width - Gap * (Columns + 1)) / Columns));
        Height = Rows == 0 ? 0 : Rows * Size + (Rows + 1) * Gap;
    }
    public float X(int index)
    {
        int row = index / Columns;
        int count = Math.Min(Columns, Count - row * Columns);
        float margin = (Width - count * Size) / (count + 1);
        return margin + (index % Columns) * (Size + margin);
    }
    public float Y(int index) => Gap + (index / Columns) * (Size + Gap);
    public static float CooldownFraction(float remaining, float duration) =>
        remaining <= 0 || duration <= 0 ? 0 : Math.Min(1, remaining / duration);
}
