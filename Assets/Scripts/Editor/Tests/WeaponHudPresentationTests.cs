using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class WeaponHudPresentationTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase("")]
    [TestCase(" / ")]
    public void SplitAmmoAndWeaponIconWorkAfterLegacyTextIsDeleted(string separator)
    {
        WithHud(false, (hud, root, fill, current, capacity, icon) =>
        {
            Set(hud, "valueSeparator", separator);
            Invoke(hud, "ValidateUIReferences");
            Present(hud, false, 0);
            Assert.That(current.text, Is.EqualTo("16"));
            Assert.That(capacity.text, Is.EqualTo("/25"));
            Assert.That(icon.enabled, Is.True);
            Assert.That(icon.sprite, Is.Not.Null);
        });
    }

    [TestCase(0f)]
    [TestCase(.25f)]
    [TestCase(.8f)]
    public void SpriteLessReloadBarRendersActualProgress(float progress)
    {
        WithHud(true, (hud, root, fill, current, capacity, icon) =>
        {
            Present(hud, true, .1f);
            Present(hud, true, progress); // Same ammo count: progress must still refresh.
            var mesh = new Mesh();
            try
            {
                using (var vh = new VertexHelper())
                {
                    typeof(Image).GetMethod("OnPopulateMesh", Flags, null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(fill, new object[] { vh });
                    vh.FillMesh(mesh);
                }
                Assert.That(root.activeSelf, Is.True);
                Assert.That(mesh.vertexCount == 0 ? 0 : mesh.vertices.Max(p => p.x) - mesh.vertices.Min(p => p.x),
                    Is.EqualTo(100 * progress).Within(.01f));
                Present(hud, false, 0); // Completion/cancellation is owned by the weapon snapshot.
                Assert.That(root.activeSelf, Is.False);
                Present(hud, true, .5f);
                Invoke(hud, "ClearDisplayedUI"); // Missing source / profession transition.
                Assert.That(root.activeSelf, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        });
    }

    static void WithHud(bool legacy, Action<LocalPlayerWeaponHUD, GameObject, Image, TMP_Text, TMP_Text, Image> check)
    {
        var owner = new GameObject("Weapon presentation test"); owner.SetActive(false);
        var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero);
        try
        {
            var hud = owner.AddComponent<LocalPlayerWeaponHUD>();
            var visual = Child(owner.transform, "WeaponHUDRoot");
            var icon = Child(visual.transform, "Icon").AddComponent<Image>();
            var current = Child(visual.transform, "Current").AddComponent<TextMeshProUGUI>();
            var capacity = Child(visual.transform, "Capacity").AddComponent<TextMeshProUGUI>();
            var progress = Child(visual.transform, "ReloadProgressRoot");
            ((RectTransform)progress.transform).sizeDelta = new Vector2(100, 10);
            var fill = Child(progress.transform, "Fill").AddComponent<Image>();
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            Set(hud, "weaponHUDVisualRoot", visual); Set(hud, "weaponIconImage", icon);
            Set(hud, "currentValueText", current); Set(hud, "secondaryValueText", capacity);
            Set(hud, "fallbackWeaponIcon", sprite); Set(hud, "valueSeparator", "/");
            Set(hud, "reloadProgressRoot", progress); Set(hud, "reloadProgressFill", fill);
            if (legacy) Set(hud, "weaponValueText", Child(visual.transform, "Legacy").AddComponent<TextMeshProUGUI>());
            check(hud, progress, fill, current, capacity, icon);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(sprite); }
    }

    static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go;
    }
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static void Invoke(object obj, string method) => obj.GetType().GetMethod(method, Flags).Invoke(obj, null);
    static void Present(LocalPlayerWeaponHUD hud, bool reloading, float progress) =>
        typeof(LocalPlayerWeaponHUD).GetMethod("RefreshUIIfChanged", Flags).Invoke(hud, new object[] {
            new PlayerWeaponHUDSnapshot(null, PlayerWeaponHUDValueMode.Ammunition, 16, 25, false, false, false, reloading, progress) });
}
