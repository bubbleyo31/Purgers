using UnityEngine;

/// <summary>提示資格與投影幾何。沒有 Networked 寫入、命中或位移授權。</summary>
public static class TankPreparedDashIndicatorRules
{
    public static bool CanPresent(bool ownPlayer, bool alive, bool activeWeapon, bool blocked)
        => ownPlayer && alive && activeWeapon && !blocked;

    public static float RingAlpha(bool preparing, bool grounded, bool frozen)
        => preparing && grounded ? (frozen ? 1f : .35f) : 0f;

    public static bool UseHitRegion(bool explicitlyLarge, Bounds whole, float threshold)
        => explicitlyLarge || Mathf.Max(whole.size.x, Mathf.Max(whole.size.y, whole.size.z)) >= Mathf.Max(.1f, threshold);

    public static bool CanJoinGroundSamples(bool first, bool second, Vector3 a, Vector3 b, float maxHeight)
        => first && second && Mathf.Abs(a.y - b.y) <= Mathf.Max(0f, maxHeight);

    static Vector3 Corner(Bounds box, int index) => box.center + Vector3.Scale(box.extents,
        new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1));

    /// <summary>先裁切近裁面再投影；不把相機後方角點鏡射到 HUD，也不把整個大型敵人縮成假部位。</summary>
    public static bool TryProject(Camera camera, Bounds localBounds, Matrix4x4 localToWorld, float padding, out Rect rect)
    {
        rect = default;
        if (camera == null || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return false;
        float near = camera.nearClipPlane + .001f;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        bool found = false;
        for (int i = 0; i < 8; i++)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(Corner(localBounds, i));
            float az = camera.transform.InverseTransformPoint(a).z;
            if (az >= near) Include(camera, a, ref min, ref max, ref found);
            for (int axis = 1; axis <= 4; axis <<= 1)
            {
                if ((i & axis) != 0) continue;
                Vector3 b = localToWorld.MultiplyPoint3x4(Corner(localBounds, i | axis));
                float bz = camera.transform.InverseTransformPoint(b).z;
                if ((az < near) == (bz < near)) continue;
                Include(camera, Vector3.Lerp(a, b, (near - az) / (bz - az)), ref min, ref max, ref found);
            }
        }
        if (!found) return false;
        Rect viewport = camera.pixelRect;
        // Reject off-screen geometry before adding padding; never invent a screen-edge target.
        if (max.x <= viewport.xMin || min.x >= viewport.xMax || max.y <= viewport.yMin || min.y >= viewport.yMax) return false;
        padding = Mathf.Max(0f, padding);
        rect = Rect.MinMaxRect(Mathf.Max(viewport.xMin, min.x - padding), Mathf.Max(viewport.yMin, min.y - padding),
            Mathf.Min(viewport.xMax, max.x + padding), Mathf.Min(viewport.yMax, max.y + padding));
        return rect.width > 0f && rect.height > 0f;
    }

    static void Include(Camera camera, Vector3 point, ref Vector2 min, ref Vector2 max, ref bool found)
    {
        Vector3 screen = camera.WorldToScreenPoint(point);
        if (float.IsNaN(screen.x) || float.IsInfinity(screen.x) || float.IsNaN(screen.y) || float.IsInfinity(screen.y)) return;
        min = Vector2.Min(min, screen); max = Vector2.Max(max, screen); found = true;
    }
}
