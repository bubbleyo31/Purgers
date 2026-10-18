using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[Category("PurgersRegression")]
public sealed class TeamInfoViewTests
{
    [Test] public void DestroyedWeaponSourceIsUnavailableInsteadOfThrowingAfterDeath()
    {
        var root=new GameObject("Destroyed weapon presentation");
        IPlayerWeaponHUDSource source=root.AddComponent<SupportSMGWeaponHUDSource>();
        Object.DestroyImmediate(root);
        var method=typeof(LocalTeamInfoPanel).GetMethod("TryReadWeaponSnapshot",BindingFlags.Static|BindingFlags.NonPublic);
        var args=new object[]{source,null};
        Assert.IsFalse((bool)method.Invoke(null,args));
    }
    [Test] public void FourEqualRowsEmptySkillsAndFullTileCooldownAreBuiltFromExistingAssets()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            var root=new GameObject("Team information test",typeof(LocalTeamInfoPanel));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
            var panel=root.GetComponent<LocalTeamInfoPanel>(); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var settings=AssetDatabase.LoadAssetAtPath<TeamInfoPanelSettings>(TeamInfoPanelSetup.SettingsPath);
            Assert.IsNotNull(settings); Assert.IsNotNull(settings.font); Assert.IsNotNull(settings.portraits);
            typeof(LocalTeamInfoPanel).GetField("settings",flags).SetValue(panel,settings);
            typeof(LocalTeamInfoPanel).GetMethod("Build",flags).Invoke(panel,null);
            var rows=root.GetComponentsInChildren<RectTransform>(true).Where(r=>r.name.StartsWith("Player row ")).ToArray();
            Assert.AreEqual(4,rows.Length); Assert.IsTrue(rows.All(r=>r.sizeDelta==new Vector2(1072,164)));
            foreach(var row in rows)
            {
                Assert.AreEqual(4,row.Find("Skills").Cast<Transform>().Count(t=>t.gameObject.activeSelf));
                Assert.IsFalse(row.Find("Current weapon").GetComponent<Image>().enabled);
                Assert.IsFalse(row.Find("Portrait").GetComponent<Image>().enabled);
            }
            var shade=root.GetComponentsInChildren<Image>(true).First(i=>i.name=="Full tile radial cooldown");
            Assert.AreEqual(Image.FillMethod.Radial360,shade.fillMethod); Assert.AreEqual((int)Image.Origin360.Top,shade.fillOrigin);
            Assert.IsFalse(shade.fillClockwise,"Remaining mask recedes clockwise from top"); Assert.IsNotNull(shade.sprite);
            var number=shade.transform.parent.Find("Cooldown number");
            Assert.AreEqual(shade.rectTransform.rect,((RectTransform)number).rect);
            Assert.IsNull(number.GetComponent<Image>(),"Number must never gain a separate background");
            Assert.IsTrue(root.GetComponentsInChildren<TMP_Text>(true).All(t=>!t.text.Contains("TAB 按住")));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
