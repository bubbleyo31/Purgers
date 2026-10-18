using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

/// <summary>只裁透明留白的 UI UV；完整保留可見武器輪廓與來源 Sprite。</summary>
[RequireComponent(typeof(Image))]
public sealed class TeamInfoSpriteCrop : BaseMeshEffect
{
    public TeamInfoPanelSettings Settings;
    public override void ModifyMesh(VertexHelper vertices)
    {
        var image=graphic as Image;
        if (image==null || image.sprite==null || image.sprite.packed || Settings==null) return;
        foreach(var entry in Settings.weaponContent)
        {
            if(entry.sprite!=image.sprite || entry.contentUV.width<=0 || entry.contentUV.height<=0) continue;
            var rect=image.rectTransform.rect; var crop=entry.contentUV; var source=Vector2.Scale(image.sprite.rect.size,crop.size);
            float scale=Mathf.Min(rect.width/source.x,rect.height/source.y); var size=source*scale;
            var sr=image.sprite.rect; var texture=image.sprite.texture;
            var uv=new Vector4(sr.xMin/texture.width,sr.yMin/texture.height,sr.xMax/texture.width,sr.yMax/texture.height); vertices.Clear();
            for(int i=0;i<4;i++)
            {
                var corner=new Vector2(i>=2?1:0,i==1||i==2?1:0); var point=crop.min+Vector2.Scale(corner,crop.size);
                var vertex=UIVertex.simpleVert; vertex.color=image.color;
                vertex.position=rect.center+Vector2.Scale(corner-Vector2.one*.5f,size);
                vertex.uv0=new Vector2(Mathf.Lerp(uv.x,uv.z,point.x),Mathf.Lerp(uv.y,uv.w,point.y)); vertices.AddVert(vertex);
            }
            vertices.AddTriangle(0,1,2); vertices.AddTriangle(2,3,0); return;
        }
    }
}
