#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ScreenEdgeEffectsVerification
{
    public static string RenderAll()
    {
        var settings=AssetDatabase.LoadAssetAtPath<ScreenEdgeEffectsSettings>(ScreenEdgeEffectsSetup.SettingsPath);
        string folder=ScreenEdgeEffectsSetup.Evidence;
        Directory.CreateDirectory(folder);
        var weights=new[]{Vector3.zero,new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,1),ScreenEdgePulse.Prioritize(1,1,1,settings.prioritySuppression)};
        var names=new[]{"before","healing","damage","momentum","combined"};
        for(int i=0;i<weights.Length;i++)
        {
            var w=weights[i];
            string result=BattleHudReferenceLayout.RenderPreview(false,1920,1080,100,false,-1,(player,stage)=>
            {
                var parent=player.transform.parent;
                parent.GetComponent<Canvas>().worldCamera.backgroundColor=new Color(.14f,.17f,.19f);
                var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenEdgeEffectsSetup.PrefabPath),parent);
                root.GetComponent<LocalPlayerScreenEdgeEffects>().enabled=false;
                var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var layer=root.GetComponent<Canvas>();layer.overrideSorting=true;layer.sortingOrder=-5;
                root.GetComponent<ScreenEdgeGraphic>().Present(settings,w,1.3f);
            },"screen-edge-"+names[i]);
            File.Copy(result,folder+"/"+names[i]+".png",true);
        }
        return Path.GetFullPath(folder);
    }
}
#endif
