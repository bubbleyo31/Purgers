using UnityEngine;
using UnityEngine.UI;

/// <summary>本機向量標記；箭頭不含名字、血量或距離，死亡符號不依賴字型字形。</summary>
public sealed class TeammateMarkerGraphic : MaskableGraphic
{
    public enum Shape { Arrow, MapDot, Death }
    private Shape shape;
    public void SetShape(Shape value){if(shape==value)return;shape=value;SetVerticesDirty();}
    protected override void Awake(){base.Awake();raycastTarget=false;}
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if(shape==Shape.Arrow){Tri(vh,new Vector2(.5f,0),new Vector2(-.4f,.4f),new Vector2(-.15f,0),color);Tri(vh,new Vector2(.5f,0),new Vector2(-.15f,0),new Vector2(-.4f,-.4f),color);return;}
        if(shape==Shape.MapDot){Circle(vh,Vector2.zero,.43f,color);return;}
        // Diamond outline and skull; colored perimeter preserves player identity.
        Vector2[] p={new Vector2(0,.49f),new Vector2(.49f,0),new Vector2(0,-.49f),new Vector2(-.49f,0)};
        for(int i=0;i<4;i++){var a=p[i];var b=p[(i+1)%4];Tri(vh,a,b,b*.83f,color);Tri(vh,a,b*.83f,a*.83f,color);}
        Circle(vh,new Vector2(0,.07f),.25f,Color.white);
        Quad(vh,-.16f,-.22f,.16f,.05f,Color.white);
        Circle(vh,new Vector2(-.10f,.06f),.06f,new Color(.08f,.10f,.12f));
        Circle(vh,new Vector2(.10f,.06f),.06f,new Color(.08f,.10f,.12f));
        Quad(vh,-.02f,-.22f,.02f,-.12f,new Color(.08f,.10f,.12f));
    }
    private Vector3 Point(Vector2 p)=>rectTransform.rect.center+Vector2.Scale(p,rectTransform.rect.size);
    private void Tri(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Color tint)
    {int i=vh.currentVertCount;vh.AddVert(Point(a),tint,Vector2.zero);vh.AddVert(Point(b),tint,Vector2.zero);vh.AddVert(Point(c),tint,Vector2.zero);vh.AddTriangle(i,i+1,i+2);}
    private void Quad(VertexHelper vh,float x0,float y0,float x1,float y1,Color tint)
    {Tri(vh,new Vector2(x0,y0),new Vector2(x1,y0),new Vector2(x1,y1),tint);Tri(vh,new Vector2(x0,y0),new Vector2(x1,y1),new Vector2(x0,y1),tint);}
    private void Circle(VertexHelper vh,Vector2 center,float radius,Color tint)
    {for(int i=0;i<16;i++){float a=i*Mathf.PI/8,b=(i+1)*Mathf.PI/8;Tri(vh,center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,tint);}}
}
