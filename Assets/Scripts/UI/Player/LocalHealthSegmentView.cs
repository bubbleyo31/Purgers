using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>既有生命 Slider 的本機整格呈現；餘額、動畫都不回寫 PlayerHealth 或護盾。</summary>
[ExecuteAlways, DefaultExecutionOrder(100), RequireComponent(typeof(CanvasRenderer))]
public sealed class LocalHealthSegmentView : MaskableGraphic
{
    [Header("血量來源與整格單位")]
    [SerializeField, Tooltip("既有本機生命 Slider；讀取真實 value/maxValue，不回寫數字。")] private Slider healthSource;
    [SerializeField, Min(1f), Tooltip("每格 HP，至少 1；傷害及治療淨變化累積滿此值才增減整格。")] private float healthPerSegment = 20f;
    [SerializeField, Tooltip("即時生命數字；空白不顯示。")] private TMP_Text healthValueText;
    [SerializeField, Tooltip("即時護盾數字；零護盾自動清空。")] private TMP_Text shieldValueText;

    [Header("血格形狀（UI 參考單位）")]
    [SerializeField, Min(1f), Tooltip("正常格寬，至少 1；高血量時在原 Rect 內等比縮窄，不拉長 UI。")] private float segmentWidth = 18f;
    [SerializeField, Min(0f), Tooltip("格間空隙，至少 0；高血量時隨步距縮窄。")] private float segmentGap = 3f;
    [SerializeField, Min(0f), Tooltip("單格上緣向右的斜角；至少 0，高密度時依格寬限縮。")] private float segmentSlant = 4f;
    [SerializeField, Min(0f), Tooltip("單格圓角半徑；0 為銳角。")] private float cornerRadius;
    [SerializeField, Tooltip("保留舊輪廓引用以相容既有序列化；新版配置清空以採用窄平行四邊形。")] private Vector2[] segmentOutline;
    [SerializeField, HideInInspector] private float segmentRise; // 保留序列化；新版所有格共用水平基線。
    [SerializeField, Range(.5f, 2f), Tooltip("螢幕像素抗鋸齒邊緣；0.5～2 像素。")] private float edgeSoftnessPixels = 1f;
    [Header("血格色彩與整格動畫")]
    [SerializeField, Tooltip("一般生命填色。")] private Color healthyColor = new Color32(241,237,217,255);
    [SerializeField, Tooltip("低血量填色；不影響護盾。")] private Color lowHealthColor = new Color32(228,122,104,255);
    [SerializeField, Range(0f,1f), Tooltip("真實 HP/maxHP 低於此比例時改為警示色。")] private float lowHealthThreshold = .25f;
    [SerializeField, Tooltip("護盾填色；緊接目前可見生命格，共用基線及步距。")] private Color shieldColor = new Color32(213,196,94,255);
    [SerializeField, Tooltip("未填滿容量格的淡底色。")] private Color emptyColor = new Color(.25f,.31f,.29f,.32f);
    [SerializeField, Tooltip("整格受傷殘影。")] private Color damageColor = new Color32(228,122,104,255);
    [SerializeField, Tooltip("整格治療亮回的起始顏色。")] private Color healingColor = new Color32(184,216,173,255);
    [SerializeField, Range(.05f,1f), Tooltip("整格動畫秒數，0.05～1；使用未縮放時間，不切割血格。")] private float animationDuration = .3f;

    // 這是顯示餘額，不是第二套血量；重綁或容量改變時從來源建立快照。
    private sealed class CellDisplay
    {
        public int Count;
        public float Previous = -1, Remainder;
        public void Reset() { Count=0; Previous=-1; Remainder=0; }
        public void Sample(float value, float unit, bool reset, bool full)
        {
            if(reset || Previous < 0) { Count=SegmentCountFor(value,unit); Remainder=0; }
            else {
                Remainder += value-Previous;
                int steps=(int)((Mathf.Abs(Remainder)+.0001f)/unit)* (Remainder>=0?1:-1);
                Count=Mathf.Max(0,Count+steps); Remainder-=steps*unit;
            }
            if(value<=0) { Count=0;Remainder=0; }
            else if(full) { Count=SegmentCountFor(value,unit);Remainder=0; }
            Previous=value;
        }
    }
    private struct CellTransition { public int Index; public float Age; public Color Color; public bool Healing; }
    private readonly CellDisplay healthCells = new CellDisplay(), shieldCells = new CellDisplay();
    private readonly List<CellTransition> transitions = new List<CellTransition>();
    private readonly List<Vector2> outlineBuffer = new List<Vector2>(64);
    private float lastPixelScale, shieldHealth, shieldCapacity, displayedHealth=-1, displayedMaximum=-1, displayedUnit;
    public int VisibleHealthCells => healthCells.Count;
    public int VisibleShieldCells => shieldCells.Count;
    public int ShieldStartCell => healthCells.Count;

    public static int SegmentCountFor(float health, float unit) =>
        health<=0 || float.IsNaN(health) || float.IsInfinity(health) ? 0 : Mathf.CeilToInt(health/Mathf.Max(1,unit));

    /// <summary>解除綁定時清除舊角色餘額、護盾與殘影。</summary>
    public void ResetPresentation()
    {
        healthCells.Reset(); shieldCells.Reset(); transitions.Clear();
        shieldHealth=shieldCapacity=0; displayedHealth=displayedMaximum=-1;
        if(healthValueText!=null) healthValueText.text="";
        if(shieldValueText!=null) shieldValueText.text="";
        SetVerticesDirty();
    }
    public void SetShieldHealth(float value)
    {
        value=float.IsNaN(value)||float.IsInfinity(value)?0:Mathf.Max(0,value);
        if(Mathf.Approximately(shieldHealth,value)) return;
        int old=shieldCells.Count;
        bool grant=shieldHealth<=0 && value>0;
        shieldHealth=value;shieldCapacity=Mathf.Max(shieldCapacity,value);
        shieldCells.Sample(value,Mathf.Max(1,healthPerSegment),grant,false);
        Animate(old,shieldCells.Count,healthCells.Count,shieldColor);
        RefreshNumbers();SetVerticesDirty();
    }
    protected override void Awake(){base.Awake();raycastTarget=false;}
    protected override void OnDisable(){base.OnDisable();ResetPresentation();}
    private void Update()
    {
        if(healthSource==null)return;
        float unit=Mathf.Max(1,healthPerSegment),maximum=Mathf.Max(0,healthSource.maxValue),current=Mathf.Clamp(healthSource.value,0,maximum);
        bool reset=displayedMaximum<0 || !Mathf.Approximately(maximum,displayedMaximum) || !Mathf.Approximately(unit,displayedUnit);
        bool changed=reset || !Mathf.Approximately(current,displayedHealth);
        if(changed){
            int old=healthCells.Count;
            healthCells.Sample(current,unit,reset,current>=maximum);
            if(reset){transitions.Clear();shieldCells.Sample(shieldHealth,unit,true,false);}
            else Animate(old,healthCells.Count,0,healthyColor);
            displayedHealth=current;displayedMaximum=maximum;displayedUnit=unit;
            if(current<=0){shieldHealth=0;shieldCells.Reset();}
            RefreshNumbers();SetVerticesDirty();
        }
        for(int i=transitions.Count-1;i>=0;i--){
            var t=transitions[i];t.Age+=Time.unscaledDeltaTime;
            if(t.Age>=animationDuration)transitions.RemoveAt(i);else transitions[i]=t;
            SetVerticesDirty();
        }
        float scale=ScreenPixelsPerUnit();if(!Mathf.Approximately(lastPixelScale,scale)){lastPixelScale=scale;SetVerticesDirty();}
    }
    private void RefreshNumbers()
    {
        if(healthValueText!=null && healthSource!=null)healthValueText.SetText("{0:0} / {1:0}",healthSource.value,healthSource.maxValue);
        if(shieldValueText!=null){if(shieldHealth>0)shieldValueText.SetText("+ {0:0}",shieldHealth);else shieldValueText.text="";}
    }
    private void Animate(int oldCount,int newCount,int start,Color tint)
    {
        if(!Application.isPlaying || oldCount==newCount)return;
        for(int i=Mathf.Min(oldCount,newCount);i<Mathf.Max(oldCount,newCount);i++){
            int index=start+i;transitions.RemoveAll(t=>t.Index==index);
            transitions.Add(new CellTransition{Index=index,Healing=newCount>oldCount,Color=tint});
        }
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(displayedMaximum<=0)return;
        int capacity=Mathf.Max(1,SegmentCountFor(displayedMaximum,healthPerSegment)+SegmentCountFor(shieldCapacity,healthPerSegment));
        float width=rectTransform.rect.width;
        float step=Mathf.Min(segmentWidth+segmentSlant+segmentGap,width/capacity);
        float gap=Mathf.Min(segmentGap,step*.18f),slant=Mathf.Min(segmentSlant,(step-gap)*.25f),body=step-gap-slant;
        Color health=displayedHealth/displayedMaximum<=lowHealthThreshold?lowHealthColor:healthyColor;
        for(int i=0;i<capacity;i++){
            Color tint=i<healthCells.Count?health:i<healthCells.Count+shieldCells.Count?shieldColor:emptyColor;
            foreach(var t in transitions)if(t.Index==i && t.Healing){
                float a=Mathf.Clamp01(t.Age/Mathf.Max(.05f,animationDuration));
                tint=Color.Lerp(healingColor,tint,a);tint.a*=Mathf.Lerp(.55f,1,a);
            }
            AddSlantedQuad(vh,rectTransform.rect.xMin+i*step,rectTransform.rect.yMin,body,rectTransform.rect.height,tint,slant);
        }
        foreach(var t in transitions)if(!t.Healing && t.Index>=healthCells.Count+shieldCells.Count && t.Index<capacity){
            Color tint=damageColor;tint.a*=1-Mathf.Clamp01((t.Age/Mathf.Max(.05f,animationDuration)-.26f)/.74f);
            AddSlantedQuad(vh,rectTransform.rect.xMin+t.Index*step,rectTransform.rect.yMin,body,rectTransform.rect.height,tint,slant);
        }
    }
    private float ScreenPixelsPerUnit()
    {
        if(canvas==null)return 1;
        Camera camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        Vector2 a=RectTransformUtility.WorldToScreenPoint(camera,rectTransform.TransformPoint(Vector3.zero));
        Vector2 b=RectTransformUtility.WorldToScreenPoint(camera,rectTransform.TransformPoint(Vector3.right));
        return Mathf.Max(.001f,Vector2.Distance(a,b));
    }
    private void AddSlantedQuad(VertexHelper vh,float left,float bottom,float width,float height,Color tint,float slant)
    {
        if(width<=0 || height<=0)return;
        outlineBuffer.Clear();
        if(segmentOutline!=null && segmentOutline.Length>=3){
            foreach(var p in segmentOutline)AppendDistinct(outlineBuffer,new Vector2(left+p.x*(width+slant),bottom+p.y*height));
        }else{
            AppendDistinct(outlineBuffer,new Vector2(left,bottom));
            AppendDistinct(outlineBuffer,new Vector2(left+width,bottom));
            AppendDistinct(outlineBuffer,new Vector2(left+width+slant,bottom+height));
            AppendDistinct(outlineBuffer,new Vector2(left+slant,bottom+height));
        }
        RemoveClosingDuplicate(outlineBuffer);AddAntialiasedPolygon(vh,outlineBuffer,tint);
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
