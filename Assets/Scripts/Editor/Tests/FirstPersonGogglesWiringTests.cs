using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class FirstPersonGogglesWiringTests
{
    [Test] public void PrefabStretchesAndCarriesItsRendererWithoutInputOrNetworkComponents()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(FirstPersonGogglesSetup.PrefabPath);
        Assert.That(asset, Is.Not.Null);
        var parent = new GameObject("isolated goggles canvas", typeof(RectTransform), typeof(Canvas));
        try
        {
            parent.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)parent.transform).sizeDelta = new Vector2(2560, 1080);
            var copy = Object.Instantiate(asset, parent.transform);
            var rect = copy.GetComponent<RectTransform>();
            Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(rect.rect.size, Is.EqualTo(new Vector2(2560, 1080)));
            Assert.That(copy.GetComponent<CanvasRenderer>(), Is.Not.Null);
            Assert.That(copy.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(copy.GetComponent<Fusion.NetworkObject>(), Is.Null);
            Assert.That(copy.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(copy.GetComponent<GoggleRimGraphic>().raycastTarget, Is.False);
            Assert.That(copy.GetComponent<Canvas>().overrideSorting, Is.True);
            Assert.That(copy.GetComponent<Canvas>().sortingOrder, Is.LessThan(parent.GetComponent<Canvas>().sortingOrder));
            Assert.That(copy.GetComponent<GoggleRimGraphic>().enabled, Is.False, "編輯器預覽預設關閉。");
        }
        finally { Object.DestroyImmediate(parent); }
    }
    [Test] public void MenuHasOneFullScreenGoggleBelowExistingUi()
    {
        var scene = SceneManager.GetSceneByPath(FirstPersonGogglesSetup.MenuPath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(FirstPersonGogglesSetup.MenuPath, OpenSceneMode.Additive);
        try
        {
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LocalFirstPersonGoggles>(true)).ToArray();
            Assert.That(all.Length, Is.EqualTo(1));
            var effect = all[0]; var canvas = effect.GetComponent<Canvas>();
            Assert.That(effect.transform.parent.name, Is.EqualTo("PlayerHUDCanvas"));
            Assert.That(effect.transform.GetSiblingIndex(), Is.Zero);
            Assert.That(effect.GetComponent<RectTransform>().rect.width, Is.GreaterThan(0));
            Assert.That(effect.GetComponent<CanvasRenderer>(), Is.Not.Null);
            Assert.That(canvas.overrideSorting, Is.True);
            Assert.That(canvas.sortingOrder, Is.LessThan(canvas.rootCanvas.sortingOrder));
            Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(effect.gameObject), Is.Zero);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
}
