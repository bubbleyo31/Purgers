using System.Reflection;
using NUnit.Framework;
using Purgers.Progression;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class GuardGrowthIntegrationTests
{
    [Test]
    public void ApprovedAssetsUseGuardDefaultsAndThreeSeparateRewardRanks()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(GuardGrowthSetup.RuntimePath);
        var guard=new SerializedObject(prefab.GetComponent<TankGuardAbility>());
        Assert.That(guard.FindProperty("guardDamageReduction").floatValue,Is.EqualTo(.4f).Within(.0001f));
        Assert.That(guard.FindProperty("brokenCooldownDuration").floatValue,Is.EqualTo(5));
        var catalog=AssetDatabase.LoadAssetAtPath<PlayerRewardCatalog>(GuardGrowthSetup.CatalogPath);
        for(int i=16;i<=18;i++)
        {
            var kind=(PlayerGrowthKind)i; var reward=catalog.FindGrowth(kind);
            Assert.That(reward,Is.Not.Null,kind.ToString());
            Assert.That(reward.IsImplementedGrowthReward,Is.True);
            for(int rank=1;rank<=PlayerGrowthRules.MaximumRank(kind);rank++)
                Assert.That(reward.GrowthValue(rank),Is.EqualTo(PlayerGrowthRules.Value(kind,rank)).Within(.0001f));
        }
    }
    [Test]
    public void GuardPublicPresentationIsSafeBeforeSpawn()
    {
        var root=new GameObject("Unspawned guard");
        try
        {
            var guard=root.AddComponent<TankGuardAbility>();
            Assert.That(guard.IsNetworkReady,Is.False);
            Assert.That(guard.IsGuarding,Is.False); Assert.That(guard.IsBrokenCooldown,Is.False);
            Assert.That(guard.StaminaNormalized,Is.Zero); Assert.That(guard.RecoveryDelayRemainingSeconds,Is.Zero);
            Assert.That(guard.GetMovementInputMultiplier(default),Is.EqualTo(1));
            var input=default(NetInput); input.Buttons.Set(InputButton.Aim,true);
            Assert.That(guard.GetMovementInputMultiplier(input),Is.EqualTo(1));
        }
        finally {Object.DestroyImmediate(root);}
    }
    [Test]
    public void GuardAndStatusesShareAlignmentAndCollapseWhenFull()
    {
        var root=new GameObject("HUD test",typeof(RectTransform));
        var settings=ScriptableObject.CreateInstance<StatusEffectHudSettings>();
        try
        {
            var anchor=(RectTransform)root.transform;anchor.sizeDelta=new Vector2(1920,1080);
            var view=LocalPlayerGuardHUD.Create(anchor);
            var statuses=StatusEffectListView.Create(anchor);
            float leading=view.Present(new GuardHudSnapshot(true,true,false,70,100,0,0),settings,280);
            statuses.Present(new StatusEffectSnapshot[0],0,settings,280,true,leading);
            Assert.That(leading,Is.EqualTo(43));
            Assert.That(((RectTransform)view.transform).anchoredPosition.y,Is.EqualTo(-80));
            Assert.That(((RectTransform)statuses.transform).anchoredPosition.y,Is.EqualTo(-123));
            Assert.That(LocalPlayerGuardHUD.ResolveCrosshairOffset(settings,leading,4,-216),Is.EqualTo(57),"four status rows clear cast title by eight units");
            Assert.That(LocalPlayerGuardHUD.ResolveCrosshairOffset(settings,leading,3,-216),Is.EqualTo(80),"ordinary layout retains approved position");
            Assert.That(LocalPlayerGuardHUD.ResolveCrosshairOffset(settings,leading,4,float.NegativeInfinity),Is.EqualTo(80),"hidden cast needs no reserved space");
            float hidden=view.Present(new GuardHudSnapshot(true,false,false,100,100,0,0),settings,280);
            statuses.Present(new StatusEffectSnapshot[0],0,settings,280,true,hidden);
            Assert.That(view.gameObject.activeSelf,Is.False); Assert.That(hidden,Is.Zero);
            Assert.That(((RectTransform)statuses.transform).anchoredPosition.y,Is.EqualTo(-80));
            view.Present(new GuardHudSnapshot(true,false,true,0,400,0,3.5f),settings,280);
            var fill=view.transform.Find("Remaining").GetComponent<GuardBarGraphic>();
            Assert.That(fill.GetComponent<CanvasRenderer>(),Is.Not.Null,"runtime bar must have a renderer");
            using(var vertices=new VertexHelper())
            {
                typeof(GuardBarGraphic).GetMethod("OnPopulateMesh",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Invoke(fill,new object[]{vertices});
                Assert.That(vertices.currentVertCount,Is.Zero,"broken must stay empty");
            }
            view.Clear(); Assert.That(view.gameObject.activeSelf,Is.False);
        }
        finally {Object.DestroyImmediate(root);Object.DestroyImmediate(settings);}
    }
}
