using NUnit.Framework;
using UnityEngine;
using UnityEditor;

[Category("PurgersRegression")]
public sealed class ScreenEdgeEffectsTests
{
    [TestCase(0f,0f)] [TestCase(.5f,0f)] [TestCase(.9999f,0f)]
    [TestCase(1f,1f)] [TestCase(1.1f,1f)]
    [TestCase(float.NaN,0f)] [TestCase(float.PositiveInfinity,0f)]
    public void SpeedMomentumBoostAppearsOnlyAtFullGauge(float energy,float expected)
    {
        var rule=typeof(ScreenEdgePulse).GetMethod("FullMomentumWeight");
        Assert.That(rule,Is.Not.Null,"Runtime needs an explicit full-gauge presentation gate.");
        Assert.That((float)rule.Invoke(null,new object[]{energy}),Is.EqualTo(expected));
    }
    [Test]
    public void GameHasRuntimePresenterOutsideEditorAssembly()
    {
        Assert.That(typeof(PlayerHealth).Assembly.GetType("LocalPlayerScreenEdgeEffects"), Is.Not.Null,
            "Approved edge effects must have a runtime presenter, not only an Editor preview.");
    }

    [Test]
    public void MomentumPreviewWaitsForFullGaugeThenFadesAway()
    {
        var s=HealingEdgePreviewWindow.NewSpeedStyle();
        Assert.That(HealingEdgePreviewWindow.SpeedWeight(s,2.5f,false),Is.Zero);
        Assert.That(HealingEdgePreviewWindow.SpeedWeight(s,3.2f,false),Is.EqualTo(1f));
        Assert.That(HealingEdgePreviewWindow.SpeedWeight(s,5.1f,false),Is.Zero);
        Assert.That(HealingEdgePreviewWindow.SpeedWeight(s,1f,true),Is.EqualTo(1f));
    }

    [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
    public void InvalidOrNoAppliedHealthChangeDoesNotTrigger(float amount)
    {
        var s=ScriptableObject.CreateInstance<ScreenEdgeEffectsSettings>();
        try {var pulse=new ScreenEdgePulse();pulse.Trigger(0,s.healing,amount);Assert.That(pulse.Evaluate(.15f,s.healing),Is.Zero);}
        finally {Object.DestroyImmediate(s);}
    }
    [Test]
    public void RepeatedEventsRefreshFromCurrentBrightnessAndEventuallyClear()
    {
        var s=ScriptableObject.CreateInstance<ScreenEdgeEffectsSettings>();
        try
        {
            foreach(var style in new[]{s.healing,s.damage})
            {
                var pulse=new ScreenEdgePulse(); pulse.Trigger(0,style,10);
                Assert.That(pulse.Evaluate(.15f,style),Is.GreaterThan(.8f));
                for(int i=1;i<100;i++)
                {
                    float t=i*.2f, before=pulse.Evaluate(t,style);pulse.Trigger(t,style,1);
                    Assert.That(pulse.Evaluate(t,style),Is.EqualTo(before).Within(.00001f));
                    Assert.That(pulse.Evaluate(t+.05f,style),Is.InRange(0f,1f));
                }
                Assert.That(pulse.Evaluate(22,style),Is.Zero);
                pulse.Trigger(23,style,1);pulse.Clear();Assert.That(pulse.Evaluate(23.1f,style),Is.Zero);
            }
        }
        finally {Object.DestroyImmediate(s);}
    }
    [Test]
    public void DamageTakesPriorityWithoutIncreasingOtherWeights()
    {
        Assert.That(ScreenEdgePulse.Prioritize(1,1,1,.75f),Is.EqualTo(new Vector3(.25f,1,.25f)));
        Assert.That(ScreenEdgePulse.Prioritize(1,0,1,.75f),Is.EqualTo(new Vector3(1,0,.25f)));
        Assert.That(ScreenEdgePulse.Prioritize(float.NaN,0,float.PositiveInfinity,.75f),Is.EqualTo(Vector3.zero));
    }
    [Test]
    public void RuntimeAssetContainsShaderAndHudLayerDoesNotInterceptClicks()
    {
        var settings=AssetDatabase.LoadAssetAtPath<ScreenEdgeEffectsSettings>(ScreenEdgeEffectsSetup.SettingsPath);
        Assert.That(settings,Is.Not.Null);Assert.That(settings.shader,Is.Not.Null);Assert.That(ShaderUtil.ShaderHasError(settings.shader),Is.False);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ScreenEdgeEffectsSetup.PrefabPath);
        Assert.That(prefab,Is.Not.Null);
        Assert.That(prefab.GetComponent<Canvas>().sortingOrder,Is.EqualTo(-5));
        Assert.That(prefab.GetComponent<ScreenEdgeGraphic>().raycastTarget,Is.False);
        Assert.That(prefab.GetComponent<CanvasGroup>().blocksRaycasts,Is.False);
        Assert.That(prefab.GetComponent<UnityEngine.UI.GraphicRaycaster>(),Is.Null);
        Assert.That(prefab.GetComponent<LocalPlayerScreenEdgeEffects>().Settings,Is.EqualTo(settings));
    }
}
