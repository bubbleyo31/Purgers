using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class FocusRewardPromptTests
{
    UnityEngine.SceneManagement.Scene scene;
    TMP_Text host;

    [SetUp] public void Open()
    {
        scene = EditorSceneManager.NewPreviewScene();
        var canvas = new GameObject("Prompt test", typeof(RectTransform), typeof(Canvas));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvas, scene);
        var go = new GameObject("PromptLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(canvas.transform, false);
        host = go.GetComponent<TMP_Text>();
        host.font = HudKeyPromptStyle.Shared.font;
        host.fontSharedMaterial = HudKeyPromptStyle.Shared.fontMaterial;
        host.color = Color.black;
        host.rectTransform.sizeDelta = new Vector2(376, 24);
    }

    [TearDown] public void Close() => EditorSceneManager.ClosePreviewScene(scene);

    void Refresh(HudKeyPromptRow row, KeyCode binding, bool compact)
    {
        var method = typeof(HudKeyPromptRow).GetMethod("RefreshWithPrefix", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "Approved prompt must support separate Description1 / keycap / Description2.");
        method.Invoke(row, new object[] { binding, "按住", compact ? "+ 技能鍵 丟棄" : "選擇升級獎勵", false, compact ? 14f : 0f, compact });
        Canvas.ForceUpdateCanvases();
        foreach (var text in host.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
    }

    [Test] public void RewardHasPrefixThenKeycapThenDescriptionAndPreservesHost()
    {
        host.rectTransform.anchoredPosition = new Vector2(131, -1050);
        Refresh(new HudKeyPromptRow(host), KeyCode.LeftAlt, false);
        var row = host.transform.Find("SharedPromptRow");
        var prefix = row.Find("Description1").GetComponent<TMP_Text>();
        var suffix = row.Find("Description2").GetComponent<TMP_Text>();
        var key = (RectTransform)row.Find("Key");
        Assert.AreEqual("按住", prefix.text);
        Assert.AreEqual("選擇升級獎勵", suffix.text);
        Assert.Less(prefix.rectTransform.anchoredPosition.x, key.anchoredPosition.x);
        Assert.Greater(suffix.rectTransform.anchoredPosition.x, key.anchoredPosition.x + key.rect.width * key.localScale.x);
        Assert.AreEqual(new Vector2(131, -1050), host.rectTransform.anchoredPosition);
        Assert.AreEqual(24, key.rect.height * key.localScale.y * row.localScale.y, .01f);
        Assert.AreEqual(Color.black, suffix.color);
    }

    [TestCase(KeyCode.LeftAlt)] [TestCase(KeyCode.RightControl)]
    public void CompactKeycapMatchesVisibleGlyphHeightAndRetainsArtworkAfterRebinding(KeyCode binding)
    {
        host.color = new Color32(120, 238, 35, 255);
        host.rectTransform.sizeDelta = new Vector2(320, 26);
        var prompt = new HudKeyPromptRow(host, true);
        Refresh(prompt, KeyCode.LeftAlt, true);
        Refresh(prompt, binding, true);
        var row = host.transform.Find("SharedPromptRow");
        var prefix = row.Find("Description1").GetComponent<TMP_Text>();
        var key = (RectTransform)row.Find("Key");
        float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
        for (int i = 0; i < prefix.textInfo.characterCount; i++)
        {
            var c = prefix.textInfo.characterInfo[i];
            if (!c.isVisible) continue;
            var metrics = c.textElement.glyph.metrics;
            top = Mathf.Max(top, c.baseLine + metrics.horizontalBearingY * c.scale);
            bottom = Mathf.Min(bottom, c.baseLine + (metrics.horizontalBearingY - metrics.height) * c.scale);
        }
        Assert.AreEqual(top - bottom, key.rect.height * key.localScale.y, .01f);
        Assert.AreEqual((top + bottom) / 2, key.anchoredPosition.y, .01f);
        Assert.AreEqual(1, row.localScale.x, .01f);
        Assert.AreEqual(host.color, prefix.color);
        var background = key.Find("SharedKeycap").GetComponent<Image>();
        var label = background.GetComponentInChildren<TMP_Text>();
        Assert.AreSame(HudKeyPromptStyle.Shared.background, background.sprite);
        Assert.AreEqual(Purgers.Settings.PlayerSettingsData.KeyLabel(binding), label.text);
        Assert.IsFalse(label.isTextOverflowing);
    }

    [Test] public void RuntimeReusesAuthoredDescriptionsWithoutDuplicatingChildren()
    {
        Refresh(new HudKeyPromptRow(host), KeyCode.LeftAlt, false);
        Refresh(new HudKeyPromptRow(host), KeyCode.RightControl, false);
        var row = host.transform.Find("SharedPromptRow");
        Assert.AreEqual(3, row.childCount);
        Assert.IsNull(row.Find("Description"));
        Assert.AreEqual("按住", row.Find("Description1").GetComponent<TMP_Text>().text);
        Assert.AreEqual("選擇升級獎勵", row.Find("Description2").GetComponent<TMP_Text>().text);
    }
}
