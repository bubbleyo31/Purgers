using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class BattleHudReadabilityTests
{
    [TestCase(10f)]
    [TestCase(15f)]
    public void RemainingFractionKeepsTheEntireCellOutline(float health)
    {
        WithMesh(health, partial => WithMesh(20, full => {
            Assert.That(partial.vertexCount, Is.EqualTo(full.vertexCount));
            Assert.That(partial.vertices, Is.EqualTo(full.vertices));
        }));
    }
    [Test]
    public void HealthEdgesHaveTransparentCoverageFringeWithoutMsaa()
    {
        WithMesh(20, mesh =>
        {
            Assert.That(mesh.colors32.Any(c => c.a == 0), Is.True,
                "Overlay UI needs its own edge coverage; MSAA does not smooth these triangles.");
            Assert.That(mesh.colors32.Any(c => c.a == 255), Is.True);
        });
    }

    [Test]
    public void RewardBackdropIsDisabledAndStageTextUsesNotoBold()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var frame = prefab.transform.Find("ReferenceFrame");
        Assert.That(frame.Find("Phase6RewardHud/BackgroundDim").gameObject.activeSelf, Is.False);
        foreach (var text in frame.GetComponentsInChildren<TMPro.TMP_Text>(true))
            Assert.That(text.font.sourceFontFile.name, Is.EqualTo("NotoSansTC-Bold"), text.name);
    }

    private static void WithMesh(float health, System.Action<Mesh> check)
    {
        var go = new GameObject("Health outline", typeof(RectTransform), typeof(LocalHealthSegmentView));
        var mesh = new Mesh();
        try
        {
            var view = go.GetComponent<LocalHealthSegmentView>();
            view.rectTransform.pivot = Vector2.zero;
            view.rectTransform.sizeDelta = new Vector2(120, 40);
            Set(view, "segmentWidth", 100f); Set(view, "segmentSlant", 20f);
            Set(view, "segmentOutline", new[] { new Vector2(0, 0), new Vector2(1, .25f), new Vector2(1, 1), new Vector2(.2f, .75f) });
            Set(view, "healthyColor", Color.white);
            var slider = go.AddComponent<Slider>(); slider.maxValue = 20; slider.value = health; Set(view, "healthSource", slider);
            typeof(LocalHealthSegmentView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
            using (var vh = new VertexHelper())
            {
                typeof(LocalHealthSegmentView).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(view, new object[] { vh });
                vh.FillMesh(mesh);
            }
            check(mesh);
        }
        finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(go); }
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}

