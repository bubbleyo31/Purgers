using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion.Menu;
using MultiClimb.Menu;
using NUnit.Framework;
using Purgers.GameFlow.Transition;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class ConnectionLoadingSortingTests
{
    private GameObject root;
    private MenuUIController controller;
    private FusionMenuUILoading loading;
    private Canvas loadingCanvas;

    [SetUp]
    public void SetUp()
    {
        // 隔離選單呈現測試，不啟動連線、讀寫存檔或執行場景上的其他元件。
        root = new GameObject("Menu test", typeof(RectTransform), typeof(Canvas));

        controller = root.AddComponent<MenuUIController>();
        var screen = new GameObject("Loading", typeof(RectTransform), typeof(Canvas));
        screen.transform.SetParent(root.transform, false);
        loading = screen.AddComponent<FusionMenuUILoading>();
        loadingCanvas = screen.GetComponent<Canvas>();
        screen.SetActive(false);
        typeof(FusionMenuUIScreen).GetField("_plugins", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(loading, new List<FusionMenuScreenPlugin>());
        typeof(FusionMenuUIController<FusionMenuConnectArgs>)
            .GetField("_screenLookup", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(controller, new Dictionary<Type, FusionMenuUIScreen>
            {
                { typeof(FusionMenuUILoading), loading }
            });
    }

    [TearDown]
    public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

    [TestCase(0)]
    [TestCase(100)]
    [TestCase(200)]
    public void ShowLoadingRendersAboveGameplayAndContinueUi(int otherOrder)
    {
        var otherCanvas = root.GetComponent<Canvas>();
        otherCanvas.sortingOrder = otherOrder;
        controller.Show<FusionMenuUILoading>();
        Assert.That(loadingCanvas.overrideSorting, Is.True,
            "Loading must not inherit the gameplay Canvas sorting order.");
        Assert.That(loadingCanvas.sortingOrder, Is.GreaterThan(otherOrder));
        foreach (var layer in SortingLayer.layers)
            Assert.That(SortingLayer.GetLayerValueFromID(loadingCanvas.sortingLayerID),
                Is.GreaterThanOrEqualTo(layer.value));
        Assert.That(otherCanvas.sortingOrder, Is.EqualTo(otherOrder));
        Assert.That(loading.IsShowing, Is.True);
    }

    [Test]
    public void RepeatedLoadingKeepsSortingAndNormalHideLifecycle()
    {
        controller.Show<FusionMenuUILoading>();
        int order = loadingCanvas.sortingOrder;
        loading.Hide();
        Assert.That(loading.gameObject.activeSelf, Is.False);
        typeof(FusionMenuUIController<FusionMenuConnectArgs>)
            .GetField("_activeScreen", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(controller, null);
        controller.Show<FusionMenuUILoading>();
        Assert.That(loading.gameObject.activeSelf, Is.True);
        Assert.That(loadingCanvas.overrideSorting, Is.True);
        Assert.That(loadingCanvas.sortingOrder, Is.EqualTo(order));
        Assert.That(loading.GetComponents<Canvas>().Length, Is.EqualTo(1));
    }

    [Test]
    public void SceneFadeRemainsAboveConnectionLoading()
    {
        var fadeRoot = new GameObject("Fade test");
        try
        {
            var fade = fadeRoot.AddComponent<ScreenFadeLayer>();
            // EditMode 不自動呼叫一般 MonoBehaviour.Awake。
            if (fadeRoot.GetComponentInChildren<Canvas>() == null)
                typeof(ScreenFadeLayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(fade, null);
            fade.SetImmediate(1f);
            controller.Show<FusionMenuUILoading>();
            var fadeCanvas = fadeRoot.GetComponentInChildren<Canvas>();
            Assert.That(loadingCanvas.overrideSorting, Is.True);
            Assert.That(loadingCanvas.sortingLayerID, Is.EqualTo(fadeCanvas.sortingLayerID));
            Assert.That(loadingCanvas.sortingOrder, Is.LessThan(fadeCanvas.sortingOrder));
        }
        finally { UnityEngine.Object.DestroyImmediate(fadeRoot); }
    }
}
