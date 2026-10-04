using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
/// <summary>只驗證本機血格，數字來源仍是原有 Slider。</summary>
[Category("PurgersRegression")]
public sealed class HealthWholeCellTests
{
    GameObject root, source; LocalHealthSegmentView view; Slider slider;
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    [SetUp] public void Setup() {
        root=new GameObject("Whole cells",typeof(RectTransform));
        source=new GameObject("Source",typeof(RectTransform),typeof(Slider));
        view=root.AddComponent<LocalHealthSegmentView>();view.rectTransform.sizeDelta=new Vector2(400,28);
        slider=source.GetComponent<Slider>();slider.maxValue=240;slider.value=240;
        typeof(LocalHealthSegmentView).GetField("healthSource",Private).SetValue(view,slider);Tick();
    }
    [TearDown] public void Cleanup(){Object.DestroyImmediate(root);Object.DestroyImmediate(source);}
    void Tick()=>typeof(LocalHealthSegmentView).GetMethod("Update",Private).Invoke(view,null);
    int Count(string name){var p=typeof(LocalHealthSegmentView).GetProperty(name);Assert.That(p,Is.Not.Null,"需要整格呈現快照");return (int)p.GetValue(view);}
    void Health(float n){slider.value=n;Tick();}
    [Test] public void DamageEightSevenFiveRemovesOnlyOneWholeCell(){
        Health(232);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(12));
        Health(225);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(12));
        Health(220);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(11));Assert.That(slider.value,Is.EqualTo(220));
    }
    [Test] public void HealingEightSevenFiveRestoresOnlyOneWholeCell(){
        Health(200);Health(208);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(10));
        Health(215);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(10));
        Health(220);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(11));
    }
    [Test] public void OppositeChangeCancelsPendingFraction(){
        Health(232);Health(237);Health(222);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(12));
        Health(220);Assert.That(Count("VisibleHealthCells"),Is.EqualTo(11));
    }
    [Test] public void ShieldStartsAfterVisibleHealthAndKeepsWholeTail(){
        Health(120);view.SetShieldHealth(25);Tick();
        Assert.That(Count("ShieldStartCell"),Is.EqualTo(6));Assert.That(Count("VisibleShieldCells"),Is.EqualTo(2));
        view.SetShieldHealth(17);Tick();Assert.That(Count("VisibleShieldCells"),Is.EqualTo(2));
        view.SetShieldHealth(5);Tick();Assert.That(Count("VisibleShieldCells"),Is.EqualTo(1));
        view.SetShieldHealth(0);Tick();Assert.That(Count("VisibleShieldCells"),Is.Zero);
    }
    [Test] public void DeathAndRebindClearPendingCells(){
        Health(8);Health(0);Assert.That(Count("VisibleHealthCells"),Is.Zero);
        root.SetActive(false);root.SetActive(true);Health(240);
        Assert.That(Count("VisibleHealthCells"),Is.EqualTo(12));Assert.That(Count("VisibleShieldCells"),Is.Zero);
    }
    [Test] public void HighCapacityNeverExpandsAuthoredRect(){
        Vector2 size=view.rectTransform.sizeDelta;slider.maxValue=1200;Health(1200);view.SetShieldHealth(400);Tick();
        Assert.That(view.rectTransform.sizeDelta,Is.EqualTo(size));
        Assert.That(Count("VisibleHealthCells"),Is.EqualTo(60));Assert.That(Count("VisibleShieldCells"),Is.EqualTo(20));
    }
}
