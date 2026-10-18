using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Purgers.Progression;

/// <summary>正式元件離屏驗圖；只填示例資料，不接觸遊戲狀態或 Editor 焦點。</summary>
public static class TeamInfoVisualVerification
{
    public static string Capture()
    {
        if (Application.isPlaying) throw new InvalidOperationException("需 Edit Mode");
        var scene=EditorSceneManager.NewPreviewScene(); var root=new GameObject("Team info offscreen preview");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        var cameraObject=new GameObject("Offscreen camera",typeof(Camera)); cameraObject.transform.SetParent(root.transform);
        var camera=cameraObject.GetComponent<Camera>(); camera.scene=scene; camera.enabled=false;
        camera.transform.position=new Vector3(0,0,-10); camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color32(23,29,22,255); camera.nearClipPlane=.01f; camera.farClipPlane=100;
        var texture=new RenderTexture(1920,1080,24); texture.Create(); camera.targetTexture=texture;
        var panel=root.AddComponent<LocalTeamInfoPanel>(); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var settings=TeamInfoPanelSettings.Load();
        try
        {
            typeof(LocalTeamInfoPanel).GetField("settings",flags).SetValue(panel,settings);
            typeof(LocalTeamInfoPanel).GetMethod("Build",flags).Invoke(panel,null);
            var canvas=root.GetComponentInChildren<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var frame=canvas.transform.Find("Fixed four player frame/Reference plane");
            frame.Find("Count").GetComponent<TMP_Text>().text="3 / 4 人";
            var definitions=settings.catalog.Rewards.Where(d=>d!=null&&d.IsImplementedAbilityReward).ToArray();
            var growth=settings.catalog.Rewards.Where(d=>d!=null&&d.IsImplementedGrowthReward).OrderBy(d=>(int)d.GrowthKind).ToArray();
            var weapons=settings.catalog.Rewards.Where(d=>d!=null&&d.IsImplementedWeaponReward).ToArray();
            for(int rowIndex=0;rowIndex<3;rowIndex++)
            {
                var row=frame.Find("Player row "+(rowIndex+1));
                row.Find("Player name").GetComponent<TMP_Text>().text=rowIndex==0?"你  · 自己":rowIndex==1?"隊友 A":"隊友 B";
                var portrait=row.Find("Portrait").GetComponent<Image>();portrait.sprite=settings.portraits.GetPortrait(null,default);portrait.enabled=true;
                row.Find("Identity stripe").GetComponent<Image>().color=TeammateDisplayRules.IdentityColor(rowIndex);
                var hp=row.Find("Health segments").GetComponent<LocalHealthSegmentView>();hp.gameObject.SetActive(true);hp.ApplyHealthSnapshot(rowIndex==1?60:100,100);
                row.Find("HP and shield").GetComponent<TMP_Text>().text=rowIndex==1?"60 / 100":"100 / 100";
                var weapon=row.Find("Current weapon").GetComponent<Image>();weapon.sprite=weapons[rowIndex%weapons.Length].HudIcon;weapon.enabled=true;
                row.Find("Weapon name").GetComponent<TMP_Text>().text=weapons[rowIndex%weapons.Length].DisplayName;
                var slots=row.Find("Skills");
                for(int i=0;i<2;i++)
                {
                    var slot=slots.GetChild(i);var icon=slot.Find("Project icon").GetComponent<Image>(); icon.sprite=definitions[(rowIndex*2+i)%definitions.Length].HudIcon;icon.enabled=true;slot.Find("Slot background").GetComponent<Image>().color=new Color32(242,238,219,255);
                    if(i==1)
                    {
                        var shade=slot.Find("Full tile radial cooldown").GetComponent<Image>();shade.gameObject.SetActive(true);shade.fillAmount=new[]{.65f,.25f,.8f}[rowIndex];
                        slot.Find("Cooldown number").GetComponent<TMP_Text>().text=new[]{"8","3","12"}[rowIndex];
                    }
                }
                int count=new[]{1,2,8}[rowIndex];var content=(RectTransform)row.Find("Growth viewport/Owned growth");
                var grid=new TeamInfoGrid(count,304,settings.preferredIconSize,settings.minimumIconSize,settings.iconGap);
                for(int i=0;i<count;i++)
                {
                    var view=typeof(LocalTeamInfoPanel).GetMethod("CreateIcon",flags).Invoke(panel,new object[]{content,true});var type=view.GetType();
                    var icon=(Image)type.GetField("Icon").GetValue(view);var rect=(RectTransform)type.GetField("Root").GetValue(view);
                    rect.anchoredPosition=new Vector2(grid.X(i),-grid.Y(i)-Mathf.Max(0,(122-grid.Height)*.5f));rect.localScale=Vector3.one*(grid.Size/60);
                    icon.sprite=growth[i].HudIcon;icon.enabled=true;((GrowthRewardRankView)type.GetField("Rank").GetValue(view)).Present(growth[i],i%3+1);
                }
            }
            Canvas.ForceUpdateCanvases();foreach(var text in root.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true);
            camera.Render();var old=RenderTexture.active;var image=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            try { RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1920,1080),0,0);image.Apply();Directory.CreateDirectory("Temp/TeamInfo");File.WriteAllBytes("Temp/TeamInfo/ui-preview.png",image.EncodeToPNG()); }
            finally { RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image); }
            return "Temp/TeamInfo/ui-preview.png";
        }
        finally { UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);texture.Release();UnityEngine.Object.DestroyImmediate(texture); }
    }
}
