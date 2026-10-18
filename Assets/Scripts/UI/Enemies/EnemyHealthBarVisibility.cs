using UnityEngine;

/// <summary>重用緩衝的本機可見性查詢；裁切主要身體盒後取九個螢幕採樣，最多九次物理查詢。</summary>
public sealed class EnemyHealthBarVisibility
{
    private readonly Vector3[] corners = new Vector3[8];
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private static readonly int[] edges = { 0,1, 2,3, 4,5, 6,7, 0,2, 1,3, 4,6, 5,7, 0,4, 1,5, 2,6, 3,7 };
    private static readonly float[] samples = { .5f, .08f, .92f };

    /// <summary>先裁切近／遠平面，避免頭部或盒角在鏡頭後方產生反向投影。</summary>
    public bool TryProject(Camera camera, Transform owner, Bounds localBody, out Rect rect)
    {
        rect = default;
        if (camera == null || owner == null) return false;
        Vector3 min = localBody.min, max = localBody.max;
        float near = camera.nearClipPlane, far = camera.farClipPlane;
        Vector2 low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 high = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            Vector3 local = new Vector3((i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            corners[i] = camera.transform.InverseTransformPoint(owner.TransformPoint(local));
            if (corners[i].z >= near && corners[i].z <= far) Include(camera, corners[i], ref low, ref high);
        }
        for (int i = 0; i < edges.Length; i += 2)
        {
            Vector3 a = corners[edges[i]], b = corners[edges[i + 1]];
            IncludeIntersection(camera, a, b, near, ref low, ref high);
            IncludeIntersection(camera, a, b, far, ref low, ref high);
        }
        if (float.IsInfinity(low.x)) return false;
        Rect viewport = camera.pixelRect;
        rect = Rect.MinMaxRect(Mathf.Max(viewport.xMin, low.x), Mathf.Max(viewport.yMin, low.y),
            Mathf.Min(viewport.xMax, high.x), Mathf.Min(viewport.yMax, high.y));
        return rect.width > .01f && rect.height > .01f;
    }

    private static void IncludeIntersection(Camera camera, Vector3 a, Vector3 b, float depth, ref Vector2 low, ref Vector2 high)
    {
        if ((a.z < depth && b.z > depth) || (b.z < depth && a.z > depth))
            Include(camera, Vector3.LerpUnclamped(a, b, (depth - a.z) / (b.z - a.z)), ref low, ref high);
    }

    private static void Include(Camera camera, Vector3 local, ref Vector2 low, ref Vector2 high)
    {
        Vector2 screen = camera.WorldToScreenPoint(camera.transform.TransformPoint(local));
        low = Vector2.Min(low, screen); high = Vector2.Max(high, screen);
    }

    /// <summary>只測試在視口內且確實穿過主要身體盒的射線；緩衝滿時保守隱藏。</summary>
    public bool TryVisibleRegion(Camera camera, Transform owner, Bounds body, Rect projected,
        PhysicsScene physics, int mask, Transform observer, out Rect visible)
    {
        visible = default;
        if (!physics.IsValid()) return false;
        bool found = false;
        Vector2 low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 high = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (float y in samples)
        foreach (float x in samples)
        {
            Vector2 screen = new Vector2(Mathf.Lerp(projected.xMin, projected.xMax, x),
                Mathf.Lerp(projected.yMin, projected.yMax, y));
            Ray ray = camera.ScreenPointToRay(screen);
            Ray localRay = new Ray(owner.InverseTransformPoint(ray.origin), owner.InverseTransformVector(ray.direction));
            // Ray 會正規化方向，因此以本機交點轉回世界後再求物理距離。
            if (!body.IntersectRay(localRay, out float along)) continue;
            Vector3 surface = owner.TransformPoint(localRay.GetPoint(Mathf.Max(0, along)));
            float distance = Vector3.Distance(ray.origin, surface);
            int count = physics.Raycast(ray.origin, ray.direction, hits, distance + .001f, mask, QueryTriggerInteraction.Ignore);
            bool blocked = count == hits.Length;
            for (int i = 0; i < count && !blocked; i++)
            {
                Transform hit = hits[i].collider.transform;
                if (hit.IsChildOf(owner) || (observer != null && hit.IsChildOf(observer))) continue;
                blocked = true;
            }
            if (blocked) continue;
            found = true; low = Vector2.Min(low, screen); high = Vector2.Max(high, screen);
        }
        if (!found) return false;
        // 單一可見樣本仍有有效面積，不能把部分露出的敵人誤當完全離屏。
        visible = Rect.MinMaxRect(low.x - .01f, low.y - .01f, high.x + .01f, high.y + .01f);
        return true;
    }
}

