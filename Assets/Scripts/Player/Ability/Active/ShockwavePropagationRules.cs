using UnityEngine;

/// <summary>固定尺寸弧形波牆的共用幾何；權威掃掠、Render 與 Gizmo 使用同一個施放座標系。</summary>
public static class ShockwavePropagationRules
{
    /// <summary>方向介面的相容入口；正式施放保留完整 Quaternion，垂直瞄準時仍保有玩家 Yaw。</summary>
    public static Quaternion RotationFromDirection(Vector3 forward)
    {
        if (forward.sqrMagnitude < 0.000001f) return Quaternion.identity;
        forward.Normalize();
        return Quaternion.LookRotation(forward, Mathf.Abs(forward.y) > 0.9999f ? Vector3.forward : Vector3.up);
    }

    /// <summary>公尺偏移：瞄準座標 X 右、Y 上、Z 前，不受 Prefab Scale 影響。</summary>
    public static Vector3 EmissionOrigin(Vector3 position, Quaternion rotation, Vector3 offset)
        => position + rotation * offset;

    public static float GetThickness(float radius, float thickness)
        => Mathf.Clamp(thickness, 0.05f, Mathf.Max(0.1f, radius));

    /// <summary>將波牆最後緣放在施放平面；傳回弧心相對該平面的前後偏移，不是額外位移。</summary>
    public static float GetCenterOffset(float radius, float halfAngle, float thickness)
    {
        radius = Mathf.Max(0.1f, radius);
        float inner = radius - GetThickness(radius, thickness);
        float cosine = Mathf.Cos(Mathf.Clamp(halfAngle, 0f, 180f) * Mathf.Deg2Rad);
        return -(cosine >= 0f ? inner : radius) * cosine;
    }

    /// <summary>弧心座標中的直立圓柱面；任意高度都沿用同一個水平半徑，不往上收尖。</summary>
    public static Vector3 SurfacePoint(float radius, float yawDegrees, float height)
    {
        float yaw = yawDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(yaw) * radius, height, Mathf.Cos(yaw) * radius);
    }

    /// <summary>
    /// 將靜止目標反向移動成弧心座標中的線段，再與環形扇區相交。
    /// 完整覆蓋兩 Tick 之間的平移，無需按速度離散取樣；不回溯移動敵人的歷史軌跡。
    /// </summary>
    public static bool ContainsSweptBand(Vector3 delta, Vector3 forward, float previousFront,
        float currentFront, float radius, float halfAngle, float thickness, float travelDistance, float height)
        => ContainsSweptBand(delta, RotationFromDirection(forward), previousFront, currentFront,
            radius, halfAngle, thickness, travelDistance, height);

    public static bool ContainsSweptBand(Vector3 delta, Quaternion rotation, float previousFront,
        float currentFront, float radius, float halfAngle, float thickness, float travelDistance, float height)
    {
        delta = Quaternion.Inverse(rotation) * delta;
        travelDistance = Mathf.Max(0.1f, travelDistance);
        if (currentFront < previousFront || currentFront < 0f || previousFront > travelDistance ||
            delta.y < -0.0001f || delta.y > Mathf.Max(0.1f, height) + 0.0001f) return false;
        radius = Mathf.Max(0.1f, radius);
        halfAngle = Mathf.Clamp(halfAngle, 0f, 180f);
        if (halfAngle <= 0f) return false;
        float inner = radius - GetThickness(radius, thickness);
        float x = delta.x;
        float z = delta.z - GetCenterOffset(radius, halfAngle, thickness);
        float low = z - Mathf.Clamp(currentFront, 0f, travelDistance);
        float high = z - Mathf.Clamp(previousFront, 0f, travelDistance);
        float cosine = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        if (Inside(x, low, inner, radius, cosine) || Inside(x, high, inner, radius, cosine)) return true;

        // 連續線段若穿入扇區，一定經過內圓、外圓或角度切面之一。
        if (CircleCrossing(x, low, high, radius, inner, radius, cosine) ||
            CircleCrossing(x, low, high, inner, inner, radius, cosine)) return true;
        float sine = Mathf.Sin(halfAngle * Mathf.Deg2Rad);
        if (sine > 0.000001f)
        {
            float cut = Mathf.Abs(x) * cosine / sine;
            if (OnSegment(cut, low, high) && Inside(x, cut, inner, radius, cosine)) return true;
        }
        return false;
    }

    private static bool CircleCrossing(float x, float low, float high, float r, float inner, float outer, float cosine)
    {
        float square = r * r - x * x;
        if (square < 0f) return false;
        float z = Mathf.Sqrt(square);
        return (OnSegment(z, low, high) && Inside(x, z, inner, outer, cosine)) ||
               (OnSegment(-z, low, high) && Inside(x, -z, inner, outer, cosine));
    }

    private static bool OnSegment(float z, float low, float high) => z >= low - 0.0001f && z <= high + 0.0001f;

    private static bool Inside(float x, float z, float inner, float outer, float cosine)
    {
        float squared = x * x + z * z;
        float tolerance = 0.00001f * Mathf.Max(1f, outer * outer);
        return squared >= inner * inner - tolerance && squared <= outer * outer + tolerance &&
               z >= Mathf.Sqrt(squared) * cosine - 0.0001f;
    }
}
