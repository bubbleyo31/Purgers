using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class BattleHudReferenceLayoutTests
{
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    [TestCase(2560, 1080)]
    public void ScreenChangesKeepCirclesRoundAndTheDrawingInsideTheViewport(int width, int height)
    {
        var parent = new GameObject("Viewport", typeof(RectTransform));
        try
        {
            var rect = (RectTransform)parent.transform;
            rect.sizeDelta = new Vector2(width, height);
            var child = new GameObject("Frame", typeof(RectTransform));
            child.transform.SetParent(rect, false);
            var frame = child.AddComponent<BattleHudReferenceFrame>();
            frame.Refresh();
            var scale = child.transform.localScale;
            Assert.That(scale.x, Is.EqualTo(scale.y).Within(.00001f));
            Assert.That(1920 * scale.x, Is.LessThanOrEqualTo(width + .01f));
            Assert.That(1080 * scale.y, Is.LessThanOrEqualTo(height + .01f));
            Assert.That(Mathf.Max(1920 * scale.x / width, 1080 * scale.y / height), Is.EqualTo(1).Within(.00001f));
        }
        finally { Object.DestroyImmediate(parent); }
    }
    [Test]
    public void StageHudSharesTheIllustratedReferenceFrame()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var frame = asset.transform.Find("ReferenceFrame") as RectTransform;
        Assert.That(frame, Is.Not.Null, "The HUD must share one undistorted reference frame.");
        Assert.That(frame.sizeDelta, Is.EqualTo(new Vector2(1920, 1080)));
        var ring = frame.Find("BattleAbilityHUD/GrappleFill") as RectTransform;
        Assert.That(ring.sizeDelta.x, Is.EqualTo(109.6234f).Within(.001f));
        Assert.That(ring.sizeDelta.y, Is.EqualTo(109.6235f).Within(.001f));
        var icon = frame.Find("BattleAbilityHUD/AbilitySlots/AbilitySlotTemplate").GetComponent<UnityEngine.UI.Image>();
        Assert.That(icon.sprite, Is.Not.Null, "Skill slots need the rounded slanted silhouette.");
    }
}
