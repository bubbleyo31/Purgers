using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>
/// 只改既有技能 Image 的顯示網格：依框架中心等比放大，超出四角／六角輪廓的部分不產生三角形。
/// 不搬移階層、不改 Sprite、材質或技能資料；獎勵卡與裝備槽沿用同一個 HudIcon。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
[AddComponentMenu("UI/Ability Icon Frame Fit")]
public sealed class AbilityIconFrameFit : BaseMeshEffect
{
    public enum FrameShape { Rectangle, Hexagon }

    [Header("圖示框架")]
    [Tooltip("指定同一 Canvas 內的底板 RectTransform；空白時使用圖示自身範圍。框架的位置、pivot 與尺寸決定顯示區域，不改動原始 RectTransform。")]
    [SerializeField] private RectTransform frame;

    [Tooltip("四角或上下尖角六角。六角斜邊接在高度 1/4 與 3/4 處，對應目前 RewardHex 底板；不自動辨識任意 Sprite 輪廓。")]
    [SerializeField] private FrameShape shape;

    [Tooltip("從框架的矩形外界向內縮的 UI 單位；至少 0。六角形依縮小後的範圍建立輪廓。過大使寬或高歸零時隱藏圖示。")]
    [SerializeField, Min(0f)] private float inset = 2f;

    [Tooltip("等比填滿後的額外倍率；1～2，1 為剛好覆蓋框架，增大會裁掉更多邊緣。透明留白也屬原圖的一部分；此值不修改圖片資產。")]
    [SerializeField, Range(1f, 2f)] private float zoom = 1f;

    private static readonly Vector2[] Rectangle = {
        new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0)
    };
    private static readonly Vector2[] Hexagon = {
        new Vector2(.5f, 0), new Vector2(0, .25f), new Vector2(0, .75f),
        new Vector2(.5f, 1), new Vector2(1, .75f), new Vector2(1, .25f)
    };

    private Rect lastRect;
    private Matrix4x4 lastRelativeMatrix;
    private bool hasFrameSnapshot;

    // 框架可能是兄弟物件：其排版變動不一定通知 Image，因此只在相對幾何變動時重建。
    private void LateUpdate()
    {
        if (!IsActive() || graphic == null) return;
        RectTransform target = frame != null ? frame : graphic.rectTransform;
        Matrix4x4 relative = transform.worldToLocalMatrix * target.localToWorldMatrix;
        if (!hasFrameSnapshot || target.rect != lastRect || relative != lastRelativeMatrix)
        {
            lastRect = target.rect;
            lastRelativeMatrix = relative;
            hasFrameSnapshot = true;
            graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsActive()) return;
        Image image = graphic as Image;
        if (image == null || image.type != Image.Type.Simple) return;
        Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        vertices.Clear();
        if (sprite == null) return;

        RectTransform target = frame != null ? frame : image.rectTransform;
        Rect bounds = target.rect;
        float padding = Mathf.Max(0f, inset);
        Vector2 size = bounds.size - Vector2.one * (padding * 2f);
        if (size.x <= 0f || size.y <= 0f || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
            return;

        // Cover：兩軸共用較大的縮放倍率；UV 向中心收縮，維持原圖比例並裁除溢出部分。
        float scale = Mathf.Max(size.x / sprite.rect.width, size.y / sprite.rect.height)
            * Mathf.Clamp(zoom, 1f, 2f);
        Vector2 visibleFraction = new Vector2(
            size.x / (sprite.rect.width * scale), size.y / (sprite.rect.height * scale));
        Vector4 outerUv = DataUtility.GetOuterUV(sprite);
        Matrix4x4 relative = image.rectTransform.worldToLocalMatrix * target.localToWorldMatrix;
        Vector2[] outline = shape == FrameShape.Hexagon ? Hexagon : Rectangle;
        AddVertex(vertices, Vector2.one * .5f, bounds.center, size, visibleFraction, outerUv, relative, image.color);
        for (int i = 0; i < outline.Length; i++)
            AddVertex(vertices, outline[i], bounds.center, size, visibleFraction, outerUv, relative, image.color);
        for (int i = 0; i < outline.Length; i++)
            vertices.AddTriangle(0, i + 1, (i + 1) % outline.Length + 1);
    }

    private static void AddVertex(VertexHelper vertices, Vector2 point, Vector2 center,
        Vector2 size, Vector2 visibleFraction, Vector4 outerUv, Matrix4x4 relative, Color color)
    {
        Vector2 offset = point - Vector2.one * .5f;
        Vector2 uv = Vector2.one * .5f + Vector2.Scale(offset, visibleFraction);
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = relative.MultiplyPoint3x4(center + Vector2.Scale(offset, size));
        vertex.color = color;
        vertex.uv0 = new Vector2(Mathf.Lerp(outerUv.x, outerUv.z, uv.x), Mathf.Lerp(outerUv.y, outerUv.w, uv.y));
        vertices.AddVert(vertex);
    }
}

