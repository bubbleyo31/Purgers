using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>檢查實際 UI 網格：置中、等比取樣、填滿及框內裁切。</summary>
[Category("PurgersRegression")]
public sealed class AbilityIconFrameFitTests
{
    [TestCase(256, 64, 0)]
    [TestCase(64, 256, 0)]
    [TestCase(128, 128, 0)]
    [TestCase(256, 64, 1)]
    [TestCase(64, 256, 1)]
    [TestCase(128, 128, 1)]
    public void MeshFillsFrameWithoutStretchingOrEscaping(int width, int height, int shape)
    {
        Type type = typeof(LocalPlayerRewardHUD).Assembly.GetType("AbilityIconFrameFit");
        Assert.That(type, Is.Not.Null, "尚未提供共用圖示填框與輪廓裁切元件");
        var root = new GameObject("Test", typeof(RectTransform));
        var frameGo = new GameObject("Frame", typeof(RectTransform));
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var texture = new Texture2D(width, height);
        var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f);
        var mesh = new Mesh();
        try
        {
            frameGo.transform.SetParent(root.transform, false);
            iconGo.transform.SetParent(root.transform, false);
            var frame = (RectTransform)frameGo.transform;
            frame.sizeDelta = new Vector2(72, 84);
            frame.pivot = new Vector2(0, 1);
            frame.anchoredPosition = new Vector2(113, -217);
            var icon = iconGo.GetComponent<Image>();
            icon.rectTransform.pivot = new Vector2(0, 1);
            icon.rectTransform.anchoredPosition = new Vector2(-31, 49);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            var effect = (BaseMeshEffect)iconGo.AddComponent(type);
            var so = new SerializedObject(effect);
            so.FindProperty("frame").objectReferenceValue = frame;
            so.FindProperty("shape").enumValueIndex = shape;
            so.FindProperty("inset").floatValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            using (var vh = new VertexHelper())
            {
                typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(icon, new object[] { vh });
                effect.ModifyMesh(vh);
                vh.FillMesh(mesh);
            }
            var positions = mesh.vertices.Select(v => (Vector2)frame.InverseTransformPoint(icon.transform.TransformPoint(v))).ToArray();
            var min = new Vector2(positions.Min(p => p.x), positions.Min(p => p.y));
            var max = new Vector2(positions.Max(p => p.x), positions.Max(p => p.y));
            Assert.That(Vector2.Distance((min + max) / 2, frame.rect.center), Is.LessThan(.001f));
            Assert.That(Vector2.Distance(max - min, new Vector2(68, 80)), Is.LessThan(.001f));
            var uv = mesh.uv;
            Assert.That(uv.All(p => p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1), Is.True);
            float sx = (max.x - min.x) / ((uv.Max(p => p.x) - uv.Min(p => p.x)) * width);
            float sy = (max.y - min.y) / ((uv.Max(p => p.y) - uv.Min(p => p.y)) * height);
            Assert.That(sx, Is.EqualTo(sy).Within(.001f), "圖示不可被拉寬或壓扁");
            foreach (Vector2 p in positions)
            {
                var normalized = new Vector2((p.x - min.x) / 68, (p.y - min.y) / 80);
                if (shape == 1)
                    Assert.That(Mathf.Abs(normalized.y - .5f) + .5f * Mathf.Abs(normalized.x - .5f),
                        Is.LessThanOrEqualTo(.5001f), "頂點不得超出六角斜邊");
            }
            Assert.That(mesh.vertexCount, Is.EqualTo(shape == 1 ? 7 : 5));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(sprite);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void EveryRewardCardAndSquareTemplateUseTheirOwnFrame()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var slots = prefab.transform.Find("ReferenceFrame/BattleAbilityHUD/AbilitySlots/AbilitySlotTemplate");
        AssertFrame(slots.Find("Icon"), slots, 0);
        foreach (string cardName in new[] { "LeftCard", "MiddleCard", "RightCard" })
        {
            var card = prefab.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow/" + cardName);
            AssertFrame(card.Find("Icon"), card.Find("IconPlate"), 1);
        }
    }

    private static void AssertFrame(Transform icon, Transform frame, int shape)
    {
        var component = icon.GetComponent("AbilityIconFrameFit");
        Assert.That(component, Is.Not.Null, icon.name + " 缺少填框元件");
        var so = new SerializedObject(component);
        Assert.That(so.FindProperty("frame").objectReferenceValue, Is.EqualTo(frame));
        Assert.That(so.FindProperty("shape").enumValueIndex, Is.EqualTo(shape));
    }
}

