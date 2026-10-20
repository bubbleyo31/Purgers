using UnityEngine;
using UnityEngine.UI;

/// <summary>單一不攔點擊的 UGUI 層；沒有相機、RenderTexture 或每幀網格重建。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ScreenEdgeGraphic : MaskableGraphic
{
    private Material ownedMaterial;
    private static readonly int[] HealIds=StyleIds("_Heal"), HurtIds=StyleIds("_Hurt"), BoostIds=StyleIds("_Boost");
    private static int[] StyleIds(string prefix) => new[]{Shader.PropertyToID(prefix+"Tint"),Shader.PropertyToID(prefix+"Shape"),Shader.PropertyToID(prefix+"Symbols")};
    public bool IsReady => ownedMaterial != null && ownedMaterial.shader != null && ownedMaterial.shader.isSupported;
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    public void Configure(ScreenEdgeEffectsSettings settings)
    {
        if (settings == null || settings.shader == null) return;
        if (ownedMaterial != null && ownedMaterial.shader == settings.shader) return;
        DisposeMaterial();
        ownedMaterial = new Material(settings.shader) { hideFlags = HideFlags.HideAndDontSave };
        material = ownedMaterial; raycastTarget = false;
    }
    public void Present(ScreenEdgeEffectsSettings s, Vector3 weights, float clock)
    {
        Configure(s);
        if (!IsReady || s == null) { enabled = false; return; }
        enabled = s.effectsEnabled && weights.sqrMagnitude > .000001f;
        if (!enabled) return;
        ownedMaterial.SetVector("_Weights", weights);
        ownedMaterial.SetFloat("_Clock", clock);
        ownedMaterial.SetFloat("_Aspect", rectTransform.rect.width / Mathf.Max(1,rectTransform.rect.height));
        ownedMaterial.SetFloat("_MotionRate", Mathf.Clamp(s.motionRate,.5f,3));
        ownedMaterial.SetFloat("_MaximumOpacity", Mathf.Clamp01(s.maximumOpacity));
        SetStyle(HealIds,s.healing); SetStyle(HurtIds,s.damage); SetStyle(BoostIds,s.momentum);
    }
    private void SetStyle(int[] ids, ScreenEdgeStyle s)
    {
        ownedMaterial.SetColor(ids[0], QualitySettings.activeColorSpace == ColorSpace.Linear ? s.tint.linear : s.tint);
        ownedMaterial.SetVector(ids[1], new Vector4(Mathf.Clamp01(s.strength),Mathf.Clamp(s.width,.04f,.24f),Mathf.Clamp(s.verticalWidth,.03f,.2f),Mathf.Clamp01(s.corners)));
        ownedMaterial.SetFloat(ids[2], Mathf.Clamp(s.symbols,0,14));
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect;
        // 四片矩形留出中央空洞；上限設定也不會超出這個保守包絡。
        AddQuad(vh,r,0,0,1,.25f); AddQuad(vh,r,0,.75f,1,1);
        AddQuad(vh,r,0,.25f,.40f,.75f); AddQuad(vh,r,.60f,.25f,1,.75f);
    }
    private void AddQuad(VertexHelper vh, Rect r, float x0,float y0,float x1,float y1)
    {
        int n=vh.currentVertCount;
        vh.AddVert(new Vector3(r.xMin+x0*r.width,r.yMin+y0*r.height),color,new Vector2(x0,y0));
        vh.AddVert(new Vector3(r.xMin+x0*r.width,r.yMin+y1*r.height),color,new Vector2(x0,y1));
        vh.AddVert(new Vector3(r.xMin+x1*r.width,r.yMin+y1*r.height),color,new Vector2(x1,y1));
        vh.AddVert(new Vector3(r.xMin+x1*r.width,r.yMin+y0*r.height),color,new Vector2(x1,y0));
        vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n,n+2,n+3);
    }
    public override bool Raycast(Vector2 point,Camera camera) => false;
    private void DisposeMaterial()
    {
        if (ownedMaterial == null) return;
        material = null;
        if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        ownedMaterial = null;
    }
    protected override void OnDestroy() { DisposeMaterial(); base.OnDestroy(); }
}
