using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;
using Purgers.Progression;

[Category("PurgersRegression")]
public sealed class GrowthIconIntegrationTests
{
    [Test]
    public void AllApprovedGrowthRewardsHaveIconsAndGunMasterName()
    {
        for (int i = 1; i <= 15; i++)
        {
            var kind = (PlayerGrowthKind)i;
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                "Assets/_Project_Assets/Data/Progression/Growth/Reward_" + kind + ".asset");
            Assert.That(reward, Is.Not.Null, kind.ToString());
            Assert.That(reward.HudIcon, Is.Not.Null, kind.ToString());
            if (kind == PlayerGrowthKind.JohnWick) Assert.That(reward.DisplayName, Is.EqualTo("槍械高手"));
        }
    }
    [Test]
    public void RankPresentationSupportsEveryTierAndHidesForPassives()
    {
        var type = typeof(LocalPlayerRewardHUD).Assembly.GetType("GrowthRewardRankView");
        Assert.That(type, Is.Not.Null, "Missing rank presentation");
        var method = type.GetMethod("RankLabel");
        Assert.That(method, Is.Not.Null);
        string[] expected = { "I", "II", "III", "IV", "V" };
        for (int kind = 1; kind <= 9; kind++)
            for (int rank = 1; rank <= PlayerGrowthRules.MaximumRank((PlayerGrowthKind)kind); rank++)
                Assert.That(method.Invoke(null, new object[] { (PlayerGrowthKind)kind, rank }), Is.EqualTo(expected[rank - 1]));
        Assert.That(method.Invoke(null, new object[] { PlayerGrowthKind.NotToday, 1 }), Is.EqualTo(string.Empty));
        Assert.That(method.Invoke(null, new object[] { PlayerGrowthKind.ReloadSpeed, 99 }), Is.EqualTo("IV"));
    }

    [Test]
    public void SerializedCardsSwitchRankAndRestoreOrdinaryIconFraming()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(GrowthIconSetup.StagePath);
        foreach (string name in new[] { "LeftCard", "MiddleCard", "RightCard" })
        {
            var source = prefab.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow/" + name);
            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            try
            {
                var icon = clone.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();
                var view = icon.GetComponent<GrowthRewardRankView>();
                Assert.That(view, Is.Not.Null);
                var label = icon.transform.Find("GrowthRank").GetComponent<TMPro.TMP_Text>();
                var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                    "Assets/_Project_Assets/Data/Progression/Growth/Reward_MaximumHealth.asset");
                view.Present(reward, 5);
                Assert.That(label.gameObject.activeSelf, Is.True);
                Assert.That(label.text, Is.EqualTo("V"));
                Assert.That(icon.sprite, Is.EqualTo(reward.GrowthRankBackground));
                var fit = icon.GetComponent<AbilityIconFrameFit>();
                Assert.That(fit.FitWholeSprite, Is.True);
                using (var vertices = new UnityEngine.UI.VertexHelper())
                {
                    fit.ModifyMesh(vertices);
                    var mesh = new UnityEngine.Mesh();
                    try
                    {
                        vertices.FillMesh(mesh);
                        Assert.That(mesh.vertexCount, Is.EqualTo(4));
                        var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(icon.sprite);
                        Assert.That(mesh.uv, Has.Some.EqualTo(new UnityEngine.Vector2(uv.x, uv.y)));
                        Assert.That(mesh.uv, Has.Some.EqualTo(new UnityEngine.Vector2(uv.z, uv.w)));
                        var mapped = fit.SpritePointToLocal(UnityEngine.Vector2.zero);
                        Assert.That(UnityEngine.Vector3.Distance(mesh.vertices[0], mapped), Is.LessThan(.001f));
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                view.Present(null, 1);
                Assert.That(label.gameObject.activeSelf, Is.False);
                Assert.That(fit.FitWholeSprite, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
    }

    [Test]
    public void ApprovedAttributesUseFormalSizeAndCompleteUvs()
    {
        float[] sides = { 0, 68.858f, 66.602f, 69.614f, 64.429f, 62.555f, 71.519f, 62.868f, 64.487f, 63.850f };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrowthIconSetup.StagePath);
        for (int kind = 1; kind <= 9; kind++)
        {
            var card = Object.Instantiate(prefab.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow/LeftCard").gameObject);
            var mesh = new Mesh();
            try
            {
                var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                    "Assets/_Project_Assets/Data/Progression/Growth/Reward_" + (PlayerGrowthKind)kind + ".asset");
                var icon = card.transform.Find("Icon").GetComponent<Image>();
                icon.GetComponent<GrowthRewardRankView>().Present(reward, 1);
                using (var vh = new VertexHelper())
                {
                    icon.GetComponent<AbilityIconFrameFit>().ModifyMesh(vh);
                    vh.FillMesh(mesh);
                }
                Assert.That(mesh.bounds.size.x, Is.EqualTo(sides[kind]).Within(.01f), reward.DisplayName);
                Assert.That(mesh.bounds.size.y, Is.EqualTo(sides[kind]).Within(.01f), "Must retain source aspect");
                Assert.That(mesh.vertexCount, Is.EqualTo(4));
                var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(icon.sprite);
                Assert.That(mesh.uv, Has.Some.EqualTo(new Vector2(uv.x, uv.y)));
                Assert.That(mesh.uv, Has.Some.EqualTo(new Vector2(uv.z, uv.w)));
                icon.GetComponent<GrowthRewardRankView>().Present(null, 1);
                using (var vh = new VertexHelper())
                {
                    icon.GetComponent<AbilityIconFrameFit>().ModifyMesh(vh);
                    mesh.Clear();
                    vh.FillMesh(mesh);
                }
                Assert.That(mesh.bounds.size.x, Is.EqualTo(67.28525f).Within(.01f), "Return to ordinary Cover");
            }
            finally { Object.DestroyImmediate(card); Object.DestroyImmediate(mesh); }
        }
    }

    [Test]
    public void ApprovedSlabRanksFitEachTierAndKeepInkCentres()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrowthIconSetup.StagePath);
        var root = new GameObject("RankCanvas", typeof(Canvas));
        try
        {
            var card = Object.Instantiate(prefab.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow/LeftCard").gameObject, root.transform);
            card.SetActive(true);
            var icon = card.transform.Find("Icon").GetComponent<Image>();
            var label = icon.transform.Find("GrowthRank").GetComponent<TMP_Text>();
            var view = icon.GetComponent<GrowthRewardRankView>();
            for (int kind = 1; kind <= 9; kind++)
            {
                var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                    "Assets/_Project_Assets/Data/Progression/Growth/Reward_" + (PlayerGrowthKind)kind + ".asset");
                Vector2 centre = reward.GrowthRankCenter;
                if (kind == 2) centre.x = 0.4932217f;
                if (kind == 4) centre.x = 0.5219298f;

                for (int rank = 1; rank <= PlayerGrowthRules.MaximumRank((PlayerGrowthKind)kind); rank++)
                {
                    view.Present(reward, rank);
                    label.ForceMeshUpdate(true);
                    Bounds ink = RankInk(label);
                    Vector3 actual = label.rectTransform.localPosition + ink.center;
                    Vector3 expected = icon.GetComponent<AbilityIconFrameFit>().SpritePointToLocal(centre);
                    Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.002f), kind + "/" + rank);
                    Assert.That(AssetDatabase.GetAssetPath(label.font), Is.EqualTo("Assets/_Project_Assets/UI/GrowthIcons/Fonts/PurgersRankSlab SDF.asset"));
                    float fill = Mathf.Max(ink.size.x / label.rectTransform.rect.width, ink.size.y / label.rectTransform.rect.height);
                    Assert.That(fill, Is.EqualTo(.94f).Within(.002f), "Each tier should fill its own box without stretching");
                    Assert.That(label.textInfo.characterCount, Is.EqualTo(label.text.Length));
                    Assert.That(ink.size.x, Is.LessThanOrEqualTo(label.rectTransform.rect.width + .001f));
                    Assert.That(ink.size.y, Is.LessThanOrEqualTo(label.rectTransform.rect.height + .001f));
                }
            }
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void ApprovedAttributeSamplingUsesMipmapsWithoutCompression()
    {
        for (int kind = 1; kind <= 9; kind++)
        {
            var reward = AssetDatabase.LoadAssetAtPath<PlayerRewardDefinition>(
                "Assets/_Project_Assets/Data/Progression/Growth/Reward_" + (PlayerGrowthKind)kind + ".asset");
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(reward.GrowthRankBackground));
            Assert.That(importer.mipmapEnabled, Is.True, reward.DisplayName);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(importer.mipMapBias, Is.EqualTo(-.5f).Within(.001f));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
        }
    }

    private static Bounds RankInk(TMP_Text text)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            var c = text.textInfo.characterInfo[i];
            if (!c.isVisible) continue;
            var m = c.textElement.glyph.metrics;
            Vector2 low = new Vector2(c.origin + m.horizontalBearingX * c.scale,
                c.baseLine + (m.horizontalBearingY - m.height) * c.scale);
            min = Vector2.Min(min, low);
            max = Vector2.Max(max, low + new Vector2(m.width, m.height) * c.scale);
        }
        return new Bounds((min + max) * .5f, max - min);
    }
}