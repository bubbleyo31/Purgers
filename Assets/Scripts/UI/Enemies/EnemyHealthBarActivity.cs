/// <summary>每個敵人的本機生命變化顯示期限；只記錄快照，不裁定生命或同步任何網路資料。</summary>
public struct EnemyHealthBarActivity
{
    public const double VisibleSeconds = 10;
    private bool initialized, hasChange;
    private float previousHealth, previousMaximum;
    private double lastChangeTime;

    public void Observe(float health, float maximum, double now)
    {
        if (float.IsNaN(health) || float.IsInfinity(health) || float.IsNaN(maximum) ||
            float.IsInfinity(maximum) || maximum <= 0 || health <= 0 || double.IsNaN(now) || double.IsInfinity(now))
        { Reset(); return; }

        // 首次滿血不視為變動；晚加入首次讀到殘血則提供本機 10 秒觀察窗。
        // 比較實際生命數值，不能只比比例，也不能以 Approximate 吞掉小額有效傷害。
        bool changed = initialized ? health != previousHealth || maximum != previousMaximum : health < maximum;
        if (changed) { hasChange = true; lastChangeTime = now; }
        previousHealth = health;
        previousMaximum = maximum;
        initialized = true;
    }

    public bool IsVisible(double now) => initialized && hasChange && now >= lastChangeTime &&
        now - lastChangeTime < VisibleSeconds;

    public void Reset() => this = default;
}
