using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Purgers.Progression;
using TMPro;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class MenuSaveSummaryViewTests
{
    GameObject root, empty, details;
    Component view;
    TMP_Text stage, name, level, date, count;
    [SetUp] public void SetUp()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MultiClimb.Menu.MenuSaveSummaryView")).FirstOrDefault(t => t != null);
        Assert.That(type, Is.Not.Null, "缺少存檔摘要顯示元件。");
        root = new GameObject("SummaryTest");
        view = root.AddComponent(type);
        empty = Child("Empty"); details = Child("Details");
        stage = Label("Stage"); name = Label("Name"); level = Label("Level"); date = Label("Date"); count = Label("Count");
        Set("emptyState", empty); Set("detailsRoot", details); Set("stageLabel", stage); Set("nameLabel", name);
        Set("levelLabel", level); Set("dateLabel", date); Set("countLabel", count);
    }
    [TearDown] public void TearDown() { if(root) UnityEngine.Object.DestroyImmediate(root); }
    GameObject Child(string n) { var g = new GameObject(n, typeof(RectTransform)); g.transform.SetParent(root.transform); return g; }
    TMP_Text Label(string n) => Child(n).AddComponent<TextMeshProUGUI>();
    void Set(string n, object value) => view.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, value);
    void Show(GameSaveSummary s) => view.GetType().GetMethod("Show").Invoke(view, new object[] { s });
    [Test] public void MissingSelectionClearsStaleDetails()
    {
        stage.text = "99"; name.text = "old";
        Show(null);
        Assert.That(empty.activeSelf, Is.True); Assert.That(details.activeSelf, Is.False);
        Assert.That(stage.text, Is.Empty); Assert.That(name.text, Is.Empty);
    }
    [Test] public void CountHandlesEmptyCatalogWithoutInventingRows()
    {
        view.GetType().GetMethod("SetCount").Invoke(view, new object[] { 0 });
        Assert.That(count.text, Is.EqualTo("0 份存檔"));
        view.GetType().GetMethod("SetCount").Invoke(view, new object[] { 7 });
        Assert.That(count.text, Is.EqualTo("7 份存檔"));
    }
    [Test] public void RealSummaryIsDisplayedAndClearedWithoutModifyingData()
    {
        var summary = new GameSaveSummary { DisplayName = "原始存檔", StageLevel = 12, HostPlayerLevel = 8, LastPlayedUtc = "2026-10-03T10:00:00Z" };
        Show(summary);
        Assert.That(details.activeSelf, Is.True); Assert.That(empty.activeSelf, Is.False);
        Assert.That(stage.text, Is.EqualTo("12")); Assert.That(name.text, Is.EqualTo("原始存檔"));
        Assert.That(level.text, Is.EqualTo("Lv. 8")); Assert.That(date.text, Is.Not.Empty);
        Show(null); Assert.That(details.activeSelf, Is.False); Assert.That(date.text, Is.Empty);
        Assert.That(summary.StageLevel, Is.EqualTo(12)); Assert.That(summary.DisplayName, Is.EqualTo("原始存檔"));
    }
    [Test] public void OptionalReferencesCanRemainUnassigned()
    {
        foreach(var n in new[]{"emptyState","detailsRoot","stageLabel","nameLabel","levelLabel","dateLabel","countLabel"}) Set(n,null);
        Assert.DoesNotThrow(() => Show(null));
    }
}
