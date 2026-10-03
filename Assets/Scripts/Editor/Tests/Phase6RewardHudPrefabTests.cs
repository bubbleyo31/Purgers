using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class Phase6RewardHudPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/UI/StageHUD.prefab";

    [Test]
    public void StageHudContainsWiredPhase6RewardHud()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);

        Transform phase6 = prefab.transform.Find("ReferenceFrame/Phase6RewardHud");
        Assert.That(phase6, Is.Not.Null,
            "StageHUD must contain the Phase 6 local presentation root.");
        LocalPlayerRewardHUD hud = phase6.GetComponent<LocalPlayerRewardHUD>();
        Assert.That(hud, Is.Not.Null);

        SerializedObject serialized = new SerializedObject(hud);
        string[] requiredReferences =
        {
            "rewardCatalog", "experienceGroup", "experienceBar",
            "experienceFill", "experienceGaugeRoot", "levelLabel",
            "experienceLabel", "altPrompt", "altPromptLabel",
            "choiceWindow", "choiceWindowGroup", "backgroundDim",
            "windowTitle"
        };
        foreach (string propertyName in requiredReferences)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            Assert.That(property.objectReferenceValue, Is.Not.Null, propertyName);
        }

        foreach (string cardName in new[] { "leftCard", "middleCard", "rightCard" })
        {
            SerializedProperty card = serialized.FindProperty(cardName);
            Assert.That(card, Is.Not.Null, cardName);
            foreach (string field in new[] { "root", "title", "description", "mouseButton" })
            {
                SerializedProperty reference = card.FindPropertyRelative(field);
                Assert.That(reference, Is.Not.Null, cardName + "." + field);
                Assert.That(reference.objectReferenceValue, Is.Not.Null,
                    cardName + "." + field);
            }
        }

        RectTransform choiceWindow =
            serialized.FindProperty("choiceWindow").objectReferenceValue as RectTransform;
        Assert.That(choiceWindow.anchorMin, Is.EqualTo(Vector2.zero));
        Assert.That(choiceWindow.anchorMax, Is.EqualTo(Vector2.one));

        CanvasGroup dim = serialized.FindProperty("backgroundDim")
            .objectReferenceValue as CanvasGroup;
        Assert.That(dim.blocksRaycasts, Is.False);
        Assert.That(prefab.GetComponent<CanvasScaler>().referenceResolution,
            Is.EqualTo(new Vector2(1920f, 1080f)));
    }
}
