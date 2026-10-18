using System;
using UnityEngine;

/// <summary>既有 Navigator/Motor 共用的垂直膠囊幾何；不持有狀態、不決定移動或傷害。</summary>
public readonly struct EnemyCollisionEnvelope
{
    public readonly float Radius;
    public readonly float Height;
    public readonly float BaseOffset;

    public EnemyCollisionEnvelope(float radius, float height, float baseOffset)
    {
        Radius = radius;
        Height = height;
        BaseOffset = baseOffset;
    }

    public bool IsValid => Finite(Radius) && Finite(Height) && Finite(BaseOffset) &&
        Radius > 0f && Height >= Radius * 2f;

    // 呼叫者指定用途的 Root-local bounds；完整包含 AABB，不能只把寬和高直接當膠囊尺寸。
    public static EnemyCollisionEnvelope Fit(Bounds bounds, float padding)
    {
        var min = bounds.min;
        var max = bounds.max;
        if (!Finite(padding) || padding < 0 || !Finite(min.x) || !Finite(min.y) ||
            !Finite(min.z) || !Finite(max.x) || !Finite(max.y) || !Finite(max.z) ||
            min.x > max.x || min.y > max.y || min.z > max.z)
            throw new ArgumentOutOfRangeException(nameof(bounds), "碰撞取樣與 Padding 必須有限且非負。");
        float x = Math.Max(Math.Abs(min.x), Math.Abs(max.x));
        float z = Math.Max(Math.Abs(min.z), Math.Abs(max.z));
        float radius = Math.Max(.01f, (float)Math.Sqrt((double)x * x + (double)z * z) + padding);
        var result = new EnemyCollisionEnvelope(radius, max.y - min.y + 2 * (radius + padding), min.y - padding - radius);
        if (!result.IsValid) throw new ArgumentOutOfRangeException(nameof(bounds), "碰撞包絡超出有效範圍。");
        return result;
    }

    public void Endpoints(Vector3 root, float skin, out Vector3 bottom, out Vector3 top)
    {
        bottom = root + Vector3.up * (BaseOffset + Radius + skin);
        top = root + Vector3.up * (BaseOffset + Mathf.Max(Radius + skin, Height - Radius));
    }

    public bool Contains(Vector3 rootLocalPoint, float tolerance = 0.0001f)
    {
        float lower = BaseOffset + Radius;
        float upper = BaseOffset + Height - Radius;
        float y = Math.Max(lower, Math.Min(upper, rootLocalPoint.y));
        return (rootLocalPoint - new Vector3(0, y, 0)).sqrMagnitude <=
            (Radius + tolerance) * (Radius + tolerance);
    }

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
