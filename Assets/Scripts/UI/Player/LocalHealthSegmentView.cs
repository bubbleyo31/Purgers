using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads the existing local health Slider and draws segmented presentation only.
/// PlayerHealth and its authority remain owned by LocalPlayerHealthSlider.
/// </summary>
[ExecuteAlways, RequireComponent(typeof(CanvasRenderer))]
public sealed class LocalHealthSegmentView : MaskableGraphic
{
    [SerializeField] private Slider healthSource;
    [SerializeField, Min(1f)] private float healthPerSegment = 20f;
    [SerializeField, Min(1f)] private float segmentWidth = 49f;
    [SerializeField, Min(0f)] private float segmentGap = 5f;
    [SerializeField, Min(0f)] private float segmentSlant = 15f;
    [SerializeField, Min(0f)] private float cornerRadius = 2f;
    [SerializeField] private Vector2[] segmentOutline;
    [SerializeField] private float segmentRise;
    [SerializeField, Range(0.5f, 2f)] private float edgeSoftnessPixels = 1f;
    private readonly List<Vector2> outlineBuffer = new List<Vector2>(256);
    private readonly List<Vector2> clippedBuffer = new List<Vector2>(256);
    private float lastPixelScale;
    [SerializeField] private Color healthyColor = new Color(0.18f, 0.62f, 1f, 0.92f);
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.22f, 0.24f, 1f);
    [SerializeField, Range(0f, 1f)] private float lowHealthThreshold = 0.25f;

    private struct Shard
    {
        public int Segment;
        public float Age;
        public bool Upper;
    }

    private readonly List<Shard> shards = new List<Shard>();
    private float displayedHealth = -1f;
    private float displayedMaximum = -1f;

    public static int SegmentCountFor(float maximumHealth, float perSegment)
    {
        if (maximumHealth <= 0f) return 0;
        return Mathf.CeilToInt(maximumHealth / Mathf.Max(1f, perSegment));
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    private void Update()
    {
        float pixelScale = ScreenPixelsPerUnit();
        if (!Mathf.Approximately(lastPixelScale, pixelScale))
        {
            lastPixelScale = pixelScale;
            SetVerticesDirty();
        }
        if (healthSource == null) return;

        float maximum = Mathf.Max(0f, healthSource.maxValue);
        float current = Mathf.Clamp(healthSource.value, 0f, maximum);
        if (!Mathf.Approximately(maximum, displayedMaximum))
        {
            displayedMaximum = maximum;
            displayedHealth = current;
            shards.Clear();
            int count = SegmentCountFor(maximum, healthPerSegment);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                count * segmentWidth + Mathf.Max(0, count - 1) * segmentGap + segmentSlant);
            SetVerticesDirty();
        }
        else if (!Mathf.Approximately(current, displayedHealth))
        {
            if (current < displayedHealth)
            {
                int first = Mathf.FloorToInt(current / healthPerSegment);
                int last = Mathf.CeilToInt(displayedHealth / healthPerSegment) - 1;
                for (int i = first; i <= last && i < 64; i++)
                {
                    if (i < 0) continue;
                    shards.Add(new Shard { Segment = i, Upper = true });
                    shards.Add(new Shard { Segment = i, Upper = false });
                }
            }
            displayedHealth = current;
            SetVerticesDirty();
        }

        if (shards.Count > 0)
        {
            float delta = Time.unscaledDeltaTime;
            for (int i = shards.Count - 1; i >= 0; i--)
            {
                Shard shard = shards[i];
                shard.Age += delta;
                if (shard.Age >= 0.32f) shards.RemoveAt(i);
                else shards[i] = shard;
            }
            SetVerticesDirty();
        }
        if (maximum > 0f && current / maximum <= lowHealthThreshold)
            SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (displayedMaximum <= 0f) return;

        int count = SegmentCountFor(displayedMaximum, healthPerSegment);
        float x0 = rectTransform.rect.xMin;
        float y0 = rectTransform.rect.yMin;
        float y1 = rectTransform.rect.yMax;
        bool low = displayedHealth / displayedMaximum <= lowHealthThreshold;
        Color fillColor = low ? lowHealthColor : healthyColor;
        float shake = low ? Mathf.Sin(Time.unscaledTime * 36f) * 1.5f : 0f;

        for (int i = 0; i < count; i++)
        {
            float amount = Mathf.Clamp01((displayedHealth - i * healthPerSegment) /
                Mathf.Min(healthPerSegment, displayedMaximum - i * healthPerSegment));
            if (amount <= 0f) continue;
            float left = x0 + i * (segmentWidth + segmentGap) + shake;
            AddSlantedQuad(vh, left, y0 + i * segmentRise, segmentWidth, y1 - y0,
                fillColor, amount);
        }

        foreach (Shard shard in shards)
        {
            float fade = 1f - shard.Age / 0.32f;
            Color color = healthyColor;
            color.a *= fade;
            float left = x0 + shard.Segment * (segmentWidth + segmentGap) + 5f;
            float y = (shard.Upper ? y1 + shard.Age * 80f : y0 - 5f - shard.Age * 80f) + shard.Segment * segmentRise;
            AddSlantedQuad(vh, left, y, segmentWidth - 10f, 4f, color);
        }
    }

    private float ScreenPixelsPerUnit()
    {
        if (canvas == null) return 1f;
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.zero));
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.right));
        return Mathf.Max(.001f, Vector2.Distance(a, b));
    }

    private void AddSlantedQuad(VertexHelper vh, float left, float bottom,
        float width, float height, Color tint, float amount = 1f)
    {
        if (width <= 0f || height <= 0f || amount <= 0f) return;
        outlineBuffer.Clear();
        if (segmentOutline != null && segmentOutline.Length >= 3)
        {
            foreach (var point in segmentOutline)
                AppendDistinct(outlineBuffer, new Vector2(left + point.x * (width + segmentSlant), bottom + point.y * height));
        }
        else
        {
            Vector2[] corners = { new Vector2(left, bottom), new Vector2(left + width, bottom),
                new Vector2(left + width + segmentSlant, bottom + height), new Vector2(left + segmentSlant, bottom + height) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = corners[i], prev = corners[(i + 3) % 4], next = corners[(i + 1) % 4];
                float radius = Mathf.Min(cornerRadius, width * .2f, height * .25f);
                Vector2 a = p + (prev - p).normalized * radius, b = p + (next - p).normalized * radius;
                for (int j = 0; j <= 8; j++)
                {
                    float t = j / 8f;
                    AppendDistinct(outlineBuffer, (1-t)*(1-t)*a + 2*(1-t)*t*p + t*t*b);
                }
            }
        }
        RemoveClosingDuplicate(outlineBuffer);
        List<Vector2> polygon = outlineBuffer;
        if (amount < 1f)
        {
            // Clip the unchanged cell: scaling its x coordinates rotates both sloping edges.
            clippedBuffer.Clear();
            float limit = left + (width + segmentSlant) * amount;
            Vector2 previous = outlineBuffer[outlineBuffer.Count - 1];
            foreach (Vector2 current in outlineBuffer)
            {
                bool previousInside = previous.x <= limit, currentInside = current.x <= limit;
                if (previousInside != currentInside)
                    AppendDistinct(clippedBuffer, Vector2.Lerp(previous, current, (limit - previous.x) / (current.x - previous.x)));
                if (currentInside) AppendDistinct(clippedBuffer, current);
                previous = current;
            }
            RemoveClosingDuplicate(clippedBuffer);
            polygon = clippedBuffer;
        }
        AddAntialiasedPolygon(vh, polygon, tint);
    }

    private static void AppendDistinct(List<Vector2> points, Vector2 point)
    {
        if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude > .000001f)
            points.Add(point);
    }

    private static void RemoveClosingDuplicate(List<Vector2> points)
    {
        if (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude < .000001f)
            points.RemoveAt(points.Count - 1);
    }

    private void AddAntialiasedPolygon(VertexHelper vh, List<Vector2> points, Color tint)
    {
        int count = points.Count;
        if (count < 3) return;
        Vector2 centre = Vector2.zero;
        float area = 0f;
        for (int i = 0; i < count; i++)
        {
            centre += points[i];
            var next = points[(i + 1) % count];
            area += points[i].x * next.y - next.x * points[i].y;
        }
        if (Mathf.Abs(area) < .00001f) return;
        centre /= count;
        float winding = Mathf.Sign(area), feather = edgeSoftnessPixels / ScreenPixelsPerUnit();
        int start = vh.currentVertCount;
        var vertex = UIVertex.simpleVert;
        vertex.color = tint; vertex.position = centre; vh.AddVert(vertex);
        foreach (Vector2 point in points) { vertex.position = point; vh.AddVert(vertex); }
        for (int i = 0; i < count; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);

        // A one-screen-pixel alpha fringe works on Screen Space Overlay, without an MSAA render target.
        Color clear = tint; clear.a = 0f; vertex.color = clear;
        for (int i = 0; i < count; i++)
        {
            Vector2 incoming = (points[i] - points[(i + count - 1) % count]).normalized;
            Vector2 outgoing = (points[(i + 1) % count] - points[i]).normalized;
            Vector2 n1 = new Vector2(incoming.y, -incoming.x) * winding;
            Vector2 n2 = new Vector2(outgoing.y, -outgoing.x) * winding;
            Vector2 normal = (n1 + n2).normalized;
            float distance = feather / Mathf.Max(.25f, Vector2.Dot(normal, n2));
            vertex.position = points[i] + normal * distance; vh.AddVert(vertex);
        }
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            vh.AddTriangle(start + 1 + i, start + 1 + count + i, start + 1 + count + next);
            vh.AddTriangle(start + 1 + i, start + 1 + count + next, start + 1 + next);
        }
    }
}
