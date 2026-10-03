using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class AbilitySlotCenteringTests
{
    [TestCase("AerialSlowTight")]
    [TestCase("AirDashTight")]
    [TestCase("GrappleGatherTight")]
    public void DifferentAspectRatiosStayCenteredInTheSlot(string spriteName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var template = prefab.transform.Find("ReferenceFrame/BattleAbilityHUD/AbilitySlots/AbilitySlotTemplate");
        var clone = Object.Instantiate(template.gameObject);
        var mesh = new Mesh();
        try
        {
            var icon = clone.transform.Find("Icon").GetComponent<Image>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(
                "Assets/_Project_Assets/UI/BattleHUD/Layout/" + spriteName + ".png"))
                if (asset is Sprite sprite) icon.sprite = sprite;
            Assert.That(icon.sprite, Is.Not.Null);
            using (var vh = new VertexHelper())
            {
                typeof(Image).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(VertexHelper) }, null).Invoke(icon, new object[] { vh });
                vh.FillMesh(mesh);
            }
            var center = clone.transform.InverseTransformPoint(icon.transform.TransformPoint(mesh.bounds.center));
            var expected = ((RectTransform)clone.transform).rect.center;
            Assert.That(Vector2.Distance(center, expected), Is.LessThan(.01f), spriteName);
            Assert.That(mesh.bounds.size.x / mesh.bounds.size.y,
                Is.EqualTo(icon.sprite.rect.width / icon.sprite.rect.height).Within(.001f));
        }
        finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(clone); }
    }
}
