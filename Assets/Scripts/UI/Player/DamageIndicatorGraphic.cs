using UnityEngine;
using UnityEngine.UI;

/// <summary>預設弧段／倒三角幾何；自訂 Sprite 沿用 Image，不建立材質實例。</summary>
public sealed class DamageIndicatorGraphic : Image
{
    public enum Geometry { Arc, Triangle }
    private Geometry geometry;
    private float radius, degrees, thickness, lineWidth;
    private int signature;
    public void Configure(Geometry shape, DamageIndicatorSettings settings, float scale)
    {
        float r=settings.arcRadius*scale, d=settings.arcDegrees, t=settings.arcThickness*scale, l=settings.triangleLineWidth*scale;
        int hash=shape.GetHashCode() ^ r.GetHashCode() ^ d.GetHashCode() ^ t.GetHashCode() ^ l.GetHashCode();
        if(signature==hash && geometry==shape) return;
        signature=hash; geometry=shape; radius=r;degrees=d;thickness=t;lineWidth=l;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        if(sprite!=null) {base.OnPopulateMesh(vh);return;}
        vh.Clear();
        if(geometry==Geometry.Triangle) Triangle(vh); else Arc(vh);
    }
    private void Arc(VertexHelper vh)
    {
        const int segments=24;
        float angle=Mathf.Clamp(degrees,1,180)*Mathf.Deg2Rad;
        for(int i=0;i<=segments;i++)
        {
            float u=i/(float)segments;
            float a=Mathf.PI*.5f+(u-.5f)*angle;
            Vector2 dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            float half=Mathf.Min(radius,Mathf.Max(.1f,thickness))*.5f*Mathf.Sin(u*Mathf.PI);
            vh.AddVert(dir*(radius-half),color,Vector2.zero);
            vh.AddVert(dir*(radius+half),color,Vector2.zero);
            if(i>0){int k=(i-1)*2;vh.AddTriangle(k,k+1,k+3);vh.AddTriangle(k,k+3,k+2);}
        }
    }
    private void Triangle(VertexHelper vh)
    {
        Rect rect=GetPixelAdjustedRect();
        float w=rect.width,h=rect.height;
        if(w<=0 || h<=0)return;
        // 等距內縮三角形，線寬超出可用範圍時限制，保留中心空白。
        float edge=Mathf.Sqrt(w*w*.25f+h*h);
        float inradius=w*h/(w+2*edge);
        float line=Mathf.Min(Mathf.Max(.1f,lineWidth),inradius*.95f);
        Vector2 a=new Vector2(rect.xMin,rect.yMax), b=new Vector2(rect.xMax,rect.yMax), c=new Vector2(rect.center.x,rect.yMin);
        Vector2 center=new Vector2(rect.center.x,rect.yMax-inradius);
        float inner=1-line/inradius;
        vh.AddVert(a,color,Vector2.zero);vh.AddVert(b,color,Vector2.zero);vh.AddVert(c,color,Vector2.zero);
        vh.AddVert(center+(a-center)*inner,color,Vector2.zero);
        vh.AddVert(center+(b-center)*inner,color,Vector2.zero);
        vh.AddVert(center+(c-center)*inner,color,Vector2.zero);
        for(int i=0;i<3;i++){int j=(i+1)%3;vh.AddTriangle(i,j,j+3);vh.AddTriangle(i,j+3,i+3);}
    }
}
