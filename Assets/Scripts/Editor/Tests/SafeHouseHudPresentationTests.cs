using System.Reflection;
using NUnit.Framework;
using Purgers.GameFlow.SafeHouse;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class SafeHouseHudPresentationTests
{
    GameObject root;
    SafeHouseHudController hud;
    TMP_Text status, timer, prompt;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Isolated SafeHouse HUD", typeof(RectTransform));
        hud = root.AddComponent<SafeHouseHudController>();
        status = Label("Status"); timer = Label("Timer"); prompt = Label("Prompt");
        Set("readyStatusLabel", status); Set("readyButtonLabel", prompt);
        var button = new GameObject("Ready", typeof(RectTransform), typeof(Button));
        button.transform.SetParent(root.transform, false);
        Set("readyButton", button.GetComponent<Button>());
    }

    TMP_Text Label(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(root.transform, false);
        return go.GetComponent<TMP_Text>();
    }
    void Set(string name, Object value)
    {
        var field = typeof(SafeHouseHudController).GetField(name, Private);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(hud, value);
    }
    void Render(SafeHousePhase phase, bool ready, float seconds)
    {
        var method = typeof(SafeHouseHudController).GetMethod("RenderReadyPresentation", Private);
        Assert.That(method, Is.Not.Null, "Split vote presentation must consume a supplied authoritative snapshot.");
        method.Invoke(hud, new object[] { phase, 2, 4, ready, seconds });
    }
    [TearDown] public void TearDown() { Object.DestroyImmediate(root); }

    [Test]
    public void VoteCountdownAndLocalToggleUseSeparateLabels()
    {
        Set("readyTimerLabel", timer);
        Render(SafeHousePhase.ReadyCheck, false, 4.2f);
        Assert.That(status.text, Is.EqualTo("已準備 2　未準備 2"));
        Assert.That(timer.text, Is.EqualTo("投票 <color=#E5C45E>5</color> 秒"));
        Assert.That(prompt.text, Is.EqualTo("準備"));
        Render(SafeHousePhase.ReadyCheck, true, 4.2f);
        Assert.That(prompt.text, Is.EqualTo("取消準備"));
    }

    [Test]
    public void DepartureShowsOnlyDepartureAndClearsOnDisable()
    {
        Set("readyTimerLabel", timer);
        Render(SafeHousePhase.Countdown, true, 0.6f);
        Assert.That(timer.text, Is.EqualTo("出發 <color=#E5C45E>1</color> 秒"));
        Assert.That(status.text, Does.Not.Contain("投票"));
        // EditMode does not dispatch MonoBehaviour lifecycle callbacks for this fixture.
        typeof(SafeHouseHudController).GetMethod("OnDisable", Private).Invoke(hud, null);
        Assert.That(timer.text, Is.Empty);
        Assert.That(status.text, Is.Empty);
        Assert.That(prompt.text, Is.Empty);
    }

    [Test]
    public void UnmigratedHudRetainsCombinedPrompts()
    {
        Render(SafeHousePhase.ReadyCheck, false, 6.1f);
        Assert.That(status.text, Is.EqualTo("已準備 2　未準備 2　投票 7"));
        Assert.That(prompt.text, Is.EqualTo("TAB  準備　ESC  取消投票"));
        Render(SafeHousePhase.Countdown, true, 0.7f);
        Assert.That(status.text, Is.EqualTo("已準備 2　未準備 2　出發 1"));
    }

    [Test]
    public void ExpandedMapLeavesCompactMaskAndReturnsToOriginalContent()
    {
        var canvas = new GameObject("Map canvas", typeof(RectTransform), typeof(Canvas));
        var map = canvas.AddComponent<LocalMinimapController>();
        try
        {
            var slot = new GameObject("Styled map frame", typeof(RectTransform), typeof(Image), typeof(Mask));
            slot.transform.SetParent(canvas.transform, false);
            var content = new GameObject("Original content", typeof(RectTransform));
            content.transform.SetParent(slot.transform, false);
            var viewport = new GameObject("Runtime viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(content.transform, false);
            var type = typeof(LocalMinimapController);
            type.GetField("compactContent", Private).SetValue(map, content.transform);
            type.GetField("viewport", Private).SetValue(map, viewport);
            map.SetExpanded(true);
            Assert.That(viewport.parent, Is.SameAs(canvas.transform));
            Assert.That(viewport.sizeDelta, Is.EqualTo(new Vector2(600, 600)));
            Assert.That(viewport.GetComponentInParent<Mask>(), Is.Null);
            map.SetExpanded(false);
            Assert.That(viewport.parent, Is.SameAs(content.transform));
            Assert.That(viewport.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(viewport.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(viewport.sizeDelta, Is.EqualTo(Vector2.zero));
            type.GetField("viewport", Private).SetValue(map, null);
        }
        finally
        {
            typeof(LocalMinimapController).GetField("viewport", Private).SetValue(map, null);
            Object.DestroyImmediate(canvas);
        }
    }
}
