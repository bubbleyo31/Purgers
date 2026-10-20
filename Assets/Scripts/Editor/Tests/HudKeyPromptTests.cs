using System.Reflection;
using NUnit.Framework;
using Purgers.Settings;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class HudKeyPromptTests
{
    const string PrefabPath = "Assets/Prefabs/UI/StageHUD.prefab";
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    UnityEngine.SceneManagement.Scene scene;
    GameObject root;
    LocalPlayerBattleAbilityHUD hud;

    [SetUp] public void Open()
    {
        scene = EditorSceneManager.NewPreviewScene();
        root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        hud = root.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
        hud.enabled = false;
        var slotTemplate = (GameObject)typeof(LocalPlayerBattleAbilityHUD).GetField("slotTemplate", Private).GetValue(hud);
        slotTemplate.transform.Find("Fallback").gameObject.SetActive(false);
    }
    [TearDown] public void Close() { EditorSceneManager.ClosePreviewScene(scene); }
    T Field<T>(string name) => (T)typeof(LocalPlayerBattleAbilityHUD).GetField(name, Private).GetValue(hud);

    [Test] public void ProductionEnergyKeyRetainsVisibleBackgroundReference()
    {
        var image = Field<Image>("grappleKeyBackground");
        Assert.IsNotNull(image, "Energy key background must remain wired for remapping and tinting");
        Assert.IsNotNull(image.sprite); Assert.Greater(image.color.a, .9f);
    }

    [Test] public void EnergyAndAbilityUseTheSameKeycapAppearance()
    {
        var energy = Field<Image>("grappleKeyBackground");
        var energyLabel = Field<TMP_Text>("grappleKeyLabel");
        var template = Field<GameObject>("slotTemplate");
        var ability = template.transform.Find("Key").GetComponent<Image>();
        var abilityLabel = ability.GetComponentInChildren<TMP_Text>(true);
        new HudKeyPromptLayout(energyLabel, energy, null).Refresh(KeyCode.Q);
        new HudKeyPromptLayout(abilityLabel, ability, null).Refresh(KeyCode.Q);
        Assert.AreSame(ability.sprite, energy.sprite, "Energy must use the common keycap artwork");
        Assert.AreEqual(ability.color, energy.color);
        Assert.AreSame(abilityLabel.font, energyLabel.font);
        Assert.AreEqual(abilityLabel.alignment, energyLabel.alignment);
        Assert.AreEqual(abilityLabel.fontStyle, energyLabel.fontStyle);
        Assert.AreEqual(abilityLabel.rectTransform.offsetMin, energyLabel.rectTransform.offsetMin);
        Assert.AreEqual(abilityLabel.rectTransform.offsetMax, energyLabel.rectTransform.offsetMax);
    }

    [Test] public void InactiveKeycapsMeasureFullSizeBeforeFirstEnable()
    {
        var template = Field<GameObject>("slotTemplate");
        template.SetActive(false);
        var image = template.transform.Find("Key").GetComponent<Image>();
        var label = image.GetComponentInChildren<TMP_Text>(true);
        label.isOrthographic = false;
        var layout = new HudKeyPromptLayout(label, image, null);
        layout.Refresh(KeyCode.Tab);
        Assert.Greater(layout.Width, 45, "Inactive uGUI must not measure with the TMP 3D 0.1 scale");
        template.SetActive(true);
        label.ForceMeshUpdate();
        Assert.IsFalse(label.isTextOverflowing);
        Assert.Greater(label.fontSize, 18, "First activation must retain a readable key label");
    }

    [Test] public void PrefabZeroScaleDoesNotCollapseRelativePlacement()
    {
        root.transform.localScale = Vector3.zero;
        var template = Field<GameObject>("slotTemplate");
        var image = template.transform.Find("Key").GetComponent<Image>();
        new HudKeyPromptLayout(image.GetComponentInChildren<TMP_Text>(true), image, template.transform as RectTransform).Refresh(KeyCode.E);
        Assert.AreEqual(0, image.rectTransform.anchoredPosition.x, .01f);
        Assert.AreEqual(-99, image.rectTransform.anchoredPosition.y, .01f);
    }

    [Test] public void ChangingSharedStyleRefreshesExistingKeycapsWithoutRebinding()
    {
        var style = Object.Instantiate(HudKeyPromptStyle.Shared);
        try
        {
            var energy = Field<Image>("grappleKeyBackground");
            var template = Field<GameObject>("slotTemplate");
            var ability = template.transform.Find("Key").GetComponent<Image>();
            var a = new HudKeyPromptLayout(Field<TMP_Text>("grappleKeyLabel"), energy, null, style);
            var b = new HudKeyPromptLayout(ability.GetComponentInChildren<TMP_Text>(true), ability, null, style);
            a.Refresh(KeyCode.Q); b.Refresh(KeyCode.Q);
            style.background = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/UI/BattleHUD/Layout/Key.png");
            style.minimumWidth = 80;
            style.readyColor = Color.cyan;
            typeof(HudKeyPromptStyle).GetMethod("OnValidate", Private).Invoke(style, null);
            a.Refresh(KeyCode.Q); b.Refresh(KeyCode.Q);
            Assert.AreSame(style.background, energy.sprite); Assert.AreSame(energy.sprite, ability.sprite);
            Assert.AreEqual(80, a.Width); Assert.AreEqual(a.Width, b.Width);
            Assert.AreEqual(Color.cyan, energy.color); Assert.AreEqual(energy.color, ability.color);
        }
        finally { Object.DestroyImmediate(style); }
    }

    [TestCase(false)] [TestCase(true)]
    public void RebindingPreservesArtworkFitsTextAndRestoresSingleLetter(bool energy)
    {
        var template = Field<GameObject>("slotTemplate"); template.SetActive(true);
        var above = energy ? Field<Image>("grappleFill").rectTransform : (RectTransform)template.transform;
        var key = energy ? (RectTransform)Field<TMP_Text>("grappleKeyLabel").transform.parent : (RectTransform)template.transform.Find("Key");
        var image = key.GetComponent<Image>(); var label = key.GetComponentInChildren<TMP_Text>(true);
        var sprite = image.sprite; var originalAbove = above.anchoredPosition; var originalSize = above.sizeDelta;
        image.color = new Color32(241, 237, 217, 255);
        var layout = new HudKeyPromptLayout(label, image, above);
        float singleWidth = 0;
        foreach (var binding in new[] { KeyCode.Q, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.PageDown, KeyCode.Mouse0, KeyCode.Mouse3, KeyCode.None, KeyCode.Q })
        {
            layout.Refresh(binding); Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate(true);
            Assert.AreEqual(PlayerSettingsData.KeyLabel(binding), label.text);
            Assert.AreSame(sprite, image.sprite); Assert.IsTrue(image.enabled); Assert.Greater(image.color.a, .9f);
            Assert.That(key.rect.width, Is.InRange(32f, 104f)); Assert.AreEqual(32f, key.rect.height, .01f);
            Assert.AreEqual(8f, label.rectTransform.offsetMin.x, .01f); Assert.AreEqual(-8f, label.rectTransform.offsetMax.x, .01f);
            Assert.IsFalse(label.isTextOverflowing, binding.ToString()); Assert.AreEqual(1, label.textInfo.lineCount);
            Assert.LessOrEqual(label.textBounds.size.x, label.rectTransform.rect.width + .5f, binding.ToString());
            var bottom = key.parent.InverseTransformPoint(above.TransformPoint(new Vector3(above.rect.center.x, above.rect.yMin)));
            var top = key.parent.InverseTransformPoint(key.TransformPoint(new Vector3(key.rect.center.x, key.rect.yMax)));
            Assert.AreEqual(bottom.x, top.x, .01f); Assert.AreEqual(19f, bottom.y - top.y, .01f);
            if (binding == KeyCode.Q) { if (singleWidth == 0) singleWidth = key.rect.width; else Assert.AreEqual(singleWidth, key.rect.width, .01f); }
            else if (binding != KeyCode.None) Assert.Greater(key.rect.width, singleWidth);
        }
        Assert.AreEqual(originalAbove, above.anchoredPosition); Assert.AreEqual(originalSize, above.sizeDelta);
    }
}

