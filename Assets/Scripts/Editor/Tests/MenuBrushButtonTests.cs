using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class MenuBrushButtonTests
{
    GameObject root;
    Component presenter;
    Button button;
    Image reveal;
    RectTransform visual;
    TMP_Text label;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SetUp]
    public void SetUp()
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("MultiClimb.Menu.MenuBrushButtonVisual")).FirstOrDefault(t => t != null);
        Assert.That(type, Is.Not.Null, "主選單缺少獨立筆觸視覺元件。");
        root = new GameObject("Menu button test", typeof(RectTransform), typeof(Image), typeof(Button));
        root.SetActive(false);
        button = root.GetComponent<Button>();
        visual = Child("Visual", root.transform);
        reveal = Child("Reveal", visual).gameObject.AddComponent<Image>();
        label = Child("Label", visual).gameObject.AddComponent<TextMeshProUGUI>();
        presenter = root.AddComponent(type);
        Set("button", button); Set("visualRoot", visual); Set("highlight", reveal); Set("label", label);
        Set("labelRestPosition", new Vector2(32, 0));
        root.SetActive(true); presenter.GetType().GetMethod("OnEnable", Private).Invoke(presenter, null);
    }
    [TearDown] public void TearDown() { if (root) UnityEngine.Object.DestroyImmediate(root); }
    [Test]
    public void HoverAndKeyboardSelectionAreVisualOnlyAndPointerExitKeepsSelection()
    {
        int clicks = 0; button.onClick.AddListener(() => clicks++);
        ((IPointerEnterHandler)presenter).OnPointerEnter(null); Step(.4f);
        Assert.That(reveal.fillAmount, Is.EqualTo(1f));
        Assert.That(label.rectTransform.anchoredPosition.x, Is.GreaterThan(32));
        ((ISelectHandler)presenter).OnSelect(null);
        ((IPointerExitHandler)presenter).OnPointerExit(null); Step(.4f);
        Assert.That(reveal.fillAmount, Is.EqualTo(1f));
        ((IDeselectHandler)presenter).OnDeselect(null); Step(.4f);
        Assert.That(reveal.fillAmount, Is.Zero);
        Assert.That(clicks, Is.Zero);
        button.onClick.Invoke(); Assert.That(clicks, Is.EqualTo(1));
    }
    [Test]
    public void DisabledAncestorClearsHighlightAndPressWithoutChangingHitArea()
    {
        ((IPointerEnterHandler)presenter).OnPointerEnter(null);
        ((IPointerDownHandler)presenter).OnPointerDown(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
        Step(.4f);
        Assert.That(visual.localScale.x, Is.LessThan(1));
        Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
        var parent = new GameObject("Disabled group", typeof(RectTransform), typeof(CanvasGroup));
        try
        {
            root.transform.SetParent(parent.transform, false);
            parent.GetComponent<CanvasGroup>().interactable = false;
            Step(.4f);
            Assert.That(reveal.fillAmount, Is.Zero);
            Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
        }
        finally { root.transform.SetParent(null); UnityEngine.Object.DestroyImmediate(parent); }
    }
    [Test]
    public void HideAndReopenDoNotRetainHoverOrPressedScale()
    {
        ((IPointerEnterHandler)presenter).OnPointerEnter(null);
        ((IPointerDownHandler)presenter).OnPointerDown(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
        Step(.4f); presenter.GetType().GetMethod("OnDisable", Private).Invoke(presenter, null); root.SetActive(false); root.SetActive(true); presenter.GetType().GetMethod("OnEnable", Private).Invoke(presenter, null); Step(.4f);
        Assert.That(reveal.fillAmount, Is.Zero);
        Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
        Assert.That(label.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(32, 0)));
    }
    [Test]
    public void RightMouseAndZeroElapsedTimeCannotTriggerPressedPresentation()
    {
        ((IPointerEnterHandler)presenter).OnPointerEnter(null); Step(0);
        Assert.That(reveal.fillAmount, Is.Zero);
        ((IPointerDownHandler)presenter).OnPointerDown(new PointerEventData(null) { button = PointerEventData.InputButton.Right });
        Step(.4f); Assert.That(visual.localScale, Is.EqualTo(Vector3.one));
    }
    void Set(string field, object value) => presenter.GetType().GetField(field, Private).SetValue(presenter, value);
    void Step(float delta) => presenter.GetType().GetMethod("AdvanceVisual", Private).Invoke(presenter, new object[] { delta });
    static RectTransform Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
