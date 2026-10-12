using System;
using System.Globalization;
using UnityEngine;

/// <summary>跳字純呈現規則；位置快照不追蹤 Transform，不做遮擋查詢或傷害判定。</summary>
public static class DamageNumberRules
{
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    public static bool ShouldDisplay(CombatHitFeedbackData hit) => hit.IsEnemyTarget && hit.TargetKey != 0 &&
        hit.HasEffectiveDamage && IsFinite(hit.AppliedDamage) && hit.AppliedDamage > 0f && IsFinite(hit.HitPoint);
    public static bool IsHeadHit(CombatHitFeedbackData hit) => hit.HitZone == DamageHitZoneType.Head;
    public static bool ShouldMerge(DamageType type, bool individualHits, bool mergeContinuousDamage) =>
        type == DamageType.DamageOverTime ? mergeContinuousDamage : !individualHits;
    public static float Alpha(float age, float lifetime, float holdFraction)
    {
        float duration = Mathf.Clamp(lifetime, .1f, 3f);
        float hold = duration * Mathf.Clamp(holdFraction, 0f, .9f);
        return age < 0f ? 0f : 1f - Mathf.Clamp01((age - hold) / (duration - hold));
    }
    /// <summary>投影世界命中點；後方／畫面外不夾到邊緣，也不依距離裁掉有效回饋。</summary>
    public static bool TryProject(Camera camera, Vector3 point, out Vector2 screen)
    {
        screen = default;
        if (camera == null || !IsFinite(point)) return false;
        Vector3 viewport = camera.WorldToViewportPoint(point);
        if (!IsFinite(viewport) || viewport.z <= camera.nearClipPlane ||
            viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;
        screen = camera.ViewportToScreenPoint(viewport);
        return true;
    }
    /// <summary>顯示實際扣血量；小數保留一位，正數小於 0.1 顯示 &lt;0.1，不假裝扣了 0。</summary>
    public static string FormatAmount(double amount)
    {
        if (amount > 0d && amount < .1d) return "<0.1";
        return amount.ToString("0.#", CultureInfo.InvariantCulture);
    }
}

/// <summary>有容量與時間上限的本地跳字緩衝；同目標合併仍保留第一筆命中位置。</summary>
public sealed class DamageNumberBuffer
{
    public sealed class Entry
    {
        public bool Active;
        public CombatHitFeedbackData Hit;
        public double Amount;
        public float Started;
        public int Revision;
        public int Lane;
    }
    public const int Capacity = 128;
    private readonly Entry[] entries = new Entry[Capacity];
    public Entry this[int index] => entries[index];
    public DamageNumberBuffer()
    {
        for (int i = 0; i < entries.Length; i++) entries[i] = new Entry();
    }
    public void Clear()
    {
        foreach (var entry in entries) entry.Active = false;
    }
    public void Expire(float now, DamageNumberSettings settings)
    {
        int limit = Mathf.Clamp(settings.maximumNumbers, 1, Capacity);
        float duration = Mathf.Clamp(settings.lifetime, .1f, 3f);
        for (int i = 0; i < entries.Length; i++)
            if (i >= limit || now - entries[i].Started >= duration || now < entries[i].Started)
                entries[i].Active = false;
    }
    /// <returns>使用的顯示槽；拒絕無效回饋時回傳 -1。</returns>
    public int Add(CombatHitFeedbackData hit, float now, DamageNumberSettings settings)
    {
        if (settings == null || !settings.showNumbers || !DamageNumberRules.IsFinite(now) || !DamageNumberRules.ShouldDisplay(hit)) return -1;
        Expire(now, settings);
        int limit = Mathf.Clamp(settings.maximumNumbers, 1, Capacity);
        bool merge = DamageNumberRules.ShouldMerge(hit.DamageType, settings.individualHits, settings.mergeContinuousDamage);
        int free = -1, oldest = 0, nearby = 0;
        for (int i = 0; i < limit; i++)
        {
            var entry = entries[i];
            if (!entry.Active) { if (free < 0) free = i; continue; }
            if (entry.Started < entries[oldest].Started) oldest = i;
            if (entry.Hit.TargetKey == hit.TargetKey) nearby++;
            if (!merge || entry.Hit.TargetKey != hit.TargetKey || entry.Hit.SourceKey != hit.SourceKey ||
                entry.Hit.DamageType != hit.DamageType || entry.Hit.FeedbackId != hit.FeedbackId ||
                DamageNumberRules.IsHeadHit(entry.Hit) != DamageNumberRules.IsHeadHit(hit) ||
                now - entry.Started > Mathf.Clamp(settings.mergeWindow, .02f, 1f)) continue;
            entry.Amount += hit.AppliedDamage;
            entry.Revision++;
            return i;
        }
        int index = free >= 0 ? free : oldest;
        var selected = entries[index];
        selected.Active = true;
        selected.Hit = hit;
        selected.Amount = hit.AppliedDamage;
        selected.Started = now;
        selected.Lane = nearby % 3;
        selected.Revision++;
        return index;
    }
}
