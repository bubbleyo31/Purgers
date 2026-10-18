using System.Linq;
using Fusion;
using Assert = NUnit.Framework.Assert;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class StatusEffectPresentationTests
{
    private GameObject root;
    private Texture2D texture;
    private Sprite icon;
    private StatusEffectHudSettings settings;
    private StatusEffectListView view;
    [SetUp] public void Setup()
    {
        root = new GameObject("Status test", typeof(RectTransform));
        texture = new Texture2D(2,2); icon = Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);
        settings = ScriptableObject.CreateInstance<StatusEffectHudSettings>();
        settings.styles = System.Enum.GetValues(typeof(StatusEffectKind)).Cast<StatusEffectKind>().Select(k =>
            new StatusEffectHudSettings.Style { kind=k,icon=icon,barColor=Color.magenta }).ToArray();
        view = StatusEffectListView.Create(root.GetComponent<RectTransform>());
    }
    [TearDown] public void Cleanup()
    {
        Object.DestroyImmediate(root); Object.DestroyImmediate(settings); Object.DestroyImmediate(icon); Object.DestroyImmediate(texture);
    }
    private static StatusEffectSnapshot S(StatusEffectKind kind,float fraction) => StatusEffectSnapshot.FromProgress(kind,fraction);
    [Test] public void AllEightRowsAreVisibleWithoutTextOrClickTargets()
    {
        var values=System.Enum.GetValues(typeof(StatusEffectKind)).Cast<StatusEffectKind>().Select(k=>S(k,.5f)).ToArray();
        view.Present(values,values.Length,settings,118,false);
        Assert.That(view.VisibleCount,Is.EqualTo(8));
        Assert.That(root.GetComponentsInChildren<Text>(true),Is.Empty);
        Assert.That(root.GetComponentsInChildren<TMPro.TMP_Text>(true),Is.Empty);
        Assert.That(root.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget),Is.True);
    }
    [Test] public void ThreeConcurrentRowsKeepIndependentProgressAndVerticalOrder()
    {
        var values=new[]{S(StatusEffectKind.Stun,.8f),S(StatusEffectKind.BulletTime,.5f),S(StatusEffectKind.Mark,.2f)};
        view.Present(values,3,settings,118,false);
        float previous=float.PositiveInfinity;
        for(int i=0;i<3;i++)
        {
            var row=(RectTransform)view.transform.GetChild(i);
            Assert.That(row.anchoredPosition.y,Is.LessThan(previous));previous=row.anchoredPosition.y;
            var fill=(RectTransform)row.Find("Remaining duration");var empty=(RectTransform)row.Find("Duration capacity");
            Assert.That(fill.rect.width/empty.rect.width,Is.EqualTo(values[i].Ratio).Within(.001));
        }
    }
    [Test] public void ExpiredRowsCompactAndUnusedRowsHide()
    {
        var values=new[]{S(StatusEffectKind.Stun,1),S(StatusEffectKind.BulletTime,1),S(StatusEffectKind.Mark,1)};
        view.Present(values,3,settings,118,false);
        values[0]=default;view.Present(values,3,settings,118,false);
        Assert.That(view.VisibleCount,Is.EqualTo(2));
        Assert.That(((RectTransform)view.transform.GetChild(0)).anchoredPosition.y,Is.Zero);
        Assert.That(view.transform.GetChild(2).gameObject.activeSelf,Is.False);
    }
    [Test] public void HideAndRestoreUsesAuthoritativeRatio()
    {
        var values=new[]{S(StatusEffectKind.Slow,.7f)};
        view.Present(values,1,settings,118,false);view.Clear();
        values[0]=S(StatusEffectKind.Slow,.2f);view.Present(values,1,settings,118,false);
        var row=view.transform.GetChild(0);
        Assert.That(((RectTransform)row.Find("Remaining duration")).rect.width/((RectTransform)row.Find("Duration capacity")).rect.width,Is.EqualTo(.2f).Within(.001));
        Assert.That(view.transform.childCount,Is.EqualTo(1),"重新顯示沿用物件，不建立第二個倒數");
    }
    [Test] public void WorldRowsStayAboveHealthAndCompactWithoutAnEmptyBottomRow()
    {
        var health = (RectTransform)root.transform;
        health.sizeDelta = new Vector2(118,9);
        var values = new[]{S(StatusEffectKind.Stun,1),S(StatusEffectKind.BulletTime,.5f),S(StatusEffectKind.Mark,.2f)};
        for(int count=3;count>=1;count--)
        {
            if(count<3) values[count]=default;
            view.Present(values,3,settings,118,false);
            var corners = new Vector3[4];
            ((RectTransform)view.transform.GetChild(count-1)).GetWorldCorners(corners);
            Assert.That(health.InverseTransformPoint(corners[0]).y,
                Is.EqualTo(health.rect.yMax+settings.healthGap).Within(.01),"Bottom row must remain above health without gaps");
        }
    }
    [Test] public void SelfRowsStartBelowScreenCenterIndependentOfHealthWidth()
    {
        var frame=(RectTransform)root.transform;
        frame.sizeDelta=new Vector2(1920,1080);
        view.Present(new[]{S(StatusEffectKind.Slow,.5f)},1,settings,280,true);
        var corners=new Vector3[4];((RectTransform)view.transform).GetWorldCorners(corners);
        Assert.That(frame.InverseTransformPoint((corners[1]+corners[2])*.5f).x,Is.EqualTo(frame.rect.center.x).Within(.01));
        Assert.That(frame.InverseTransformPoint(corners[1]).y,Is.EqualTo(frame.rect.center.y-80).Within(.01));
    }
    [Test] public void SourcesBeforeSpawnNeverReadNetworkedState()
    {
        var buffer=new StatusEffectSnapshot[StatusEffectSources.Capacity];
        root.AddComponent<NetworkObject>();root.AddComponent<PlayerActionGate>();
        Assert.DoesNotThrow(()=>Assert.That(new StatusEffectSources(root).Read(PlayerRef.None,buffer),Is.Zero));
        var mark=root.AddComponent<AttackGrappleMarkState>();
        Assert.That(mark.GetMarkStatus(PlayerRef.None).IsVisible,Is.False);
    }
    [Test] public void StatusDoesNotRestartOriginalHealthActivityWindow()
    {
        var state=new EnemyHealthBarActivity();state.Observe(80,100,0);
        Assert.That(StatusEffectRules.ShowEnemy(true,state.IsVisible(11),true),Is.True);
        Assert.That(StatusEffectRules.ShowEnemy(true,state.IsVisible(12),false),Is.False);
    }
    [Test] public void ApprovedAssetContainsEveryIconAndPurpleBulletTime()
    {
        var asset=UnityEditor.AssetDatabase.LoadAssetAtPath<StatusEffectHudSettings>(StatusEffectHudSetup.SettingsPath);
        Assert.That(asset,Is.Not.Null);
        foreach(StatusEffectKind kind in System.Enum.GetValues(typeof(StatusEffectKind))) Assert.That(asset.TryGetStyle(kind,out _),Is.True,kind.ToString());
        asset.TryGetStyle(StatusEffectKind.BulletTime,out var bullet);
        Assert.That(bullet.barColor.b,Is.GreaterThan(bullet.barColor.g));
        Assert.That(bullet.barColor.r,Is.GreaterThan(bullet.barColor.g));
        Assert.That(UnityEditor.AssetDatabase.GetAssetPath(bullet.icon),Is.EqualTo(StatusEffectHudSetup.ArtRoot+"BulletTime.png"));
        asset.TryGetStyle(StatusEffectKind.GrappleBlocked,out var grapple);
        Assert.That(UnityEditor.AssetDatabase.GetAssetPath(grapple.icon),Is.EqualTo(StatusEffectHudSetup.ArtRoot+"GrappleBlocked.png"));
    }
}
