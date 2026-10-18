using UnityEngine;

/// <summary>只計算隊友顯示幾何與辨識色；不判定傷害、陣營或生命。</summary>
public static class TeammateDisplayRules
{
    public const int Capacity = 12; // Matches the existing GameLogic dictionaries.
    public static int FirstFreeSlot(int occupied)
    {
        for(int i=0;i<Capacity;i++) if((occupied & (1<<i))==0)return i;
        return -1;
    }
    private static readonly Color[] basis={new Color32(239,102,96,255),new Color32(91,153,255,255),new Color32(135,221,105,255),new Color32(249,214,86,255)};
    public static Color IdentityColor(int slot)
    {
        slot=Mathf.Clamp(slot,0,Capacity-1);
        Color color=basis[slot%4];
        if(slot/4==1)color=Color.Lerp(color,Color.white,.48f);
        else if(slot/4==2)color=Color.Lerp(color,Color.black,.30f);
        return color;
    }
    public static int CellCount(float value,float unit)=>LocalHealthSegmentView.SegmentCountFor(value,unit);
    public static bool IsLowHealth(float current,float maximum,float threshold)=>current>0 && maximum>0 && current/maximum<=Mathf.Clamp01(threshold);
    public static Vector2 EdgeDirection(Vector3 local,float verticalFov,float aspect)
    {
        float horizontal=Mathf.Max(.0001f,new Vector2(local.x,local.z).magnitude);
        float sine=local.x/horizontal, cosine=local.z/horizontal;
        float tangent=Mathf.Tan(Mathf.Clamp(verticalFov,1,179)*.5f*Mathf.Deg2Rad);
        Vector2 d=new Vector2(sine/Mathf.Max(.01f,aspect*tangent),local.y/horizontal/Mathf.Max(.01f,tangent));
        // Behind-camera hints go around the lower edge without mirrored projection.
        d.y-=Mathf.Max(0,-cosine)*2f;
        return d.sqrMagnitude>.000001f?d.normalized:Vector2.down;
    }
    public static Vector2 EdgePoint(Rect viewport,Vector2 direction,float inset)
    {
        Vector2 half=viewport.size*.5f-Vector2.one*Mathf.Clamp(inset,0,Mathf.Min(viewport.width,viewport.height)*.49f);
        if(direction.sqrMagnitude<.000001f)direction=Vector2.down;
        float t=Mathf.Min(half.x/Mathf.Max(.0001f,Mathf.Abs(direction.x)),half.y/Mathf.Max(.0001f,Mathf.Abs(direction.y)));
        return viewport.center+direction*t;
    }

    public static Vector2 SeparateEdgePoint(Rect viewport,Vector2 desired,float inset,float spacing,System.Collections.Generic.IList<Vector2> occupied)
    {
        inset=Mathf.Clamp(inset,0,Mathf.Min(viewport.width,viewport.height)*.49f);
        var r=new Rect(viewport.xMin+inset,viewport.yMin+inset,viewport.width-2*inset,viewport.height-2*inset);
        float perimeter=2*(r.width+r.height),start;
        if(Mathf.Abs(desired.y-r.yMax)<.1f)start=desired.x-r.xMin;
        else if(Mathf.Abs(desired.x-r.xMax)<.1f)start=r.width+r.yMax-desired.y;
        else if(Mathf.Abs(desired.y-r.yMin)<.1f)start=r.width+r.height+r.xMax-desired.x;
        else start=2*r.width+r.height+desired.y-r.yMin;
        for(int i=0;i<Capacity*4;i++)
        {
            float offset=i==0?0:((i+1)/2)*spacing*(i%2==1?1:-1);
            float t=Mathf.Repeat(start+offset,perimeter);Vector2 candidate;
            if(t<r.width)candidate=new Vector2(r.xMin+t,r.yMax);
            else if((t-=r.width)<r.height)candidate=new Vector2(r.xMax,r.yMax-t);
            else if((t-=r.height)<r.width)candidate=new Vector2(r.xMax-t,r.yMin);
            else candidate=new Vector2(r.xMin,r.yMin+t-r.width);
            bool free=true;foreach(var p in occupied)if(Vector2.Distance(p,candidate)<spacing*.9f){free=false;break;}
            if(free)return candidate;
        }
        return desired;
    }
}
