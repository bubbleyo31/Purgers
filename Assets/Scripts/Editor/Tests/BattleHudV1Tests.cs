using System.Reflection;
using NUnit.Framework;
using Purgers.GameFlow.Stage;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleHudV1Tests
{
    [TestCase(59.99f, true)]
    [TestCase(60f, false)]
    [TestCase(0f, true)]
    public void TimedStageTurnsRedBelowOneMinute(float remaining, bool expected)
    {
        MethodInfo method = typeof(StageHudController).GetMethod(
            "ShouldWarnAboutTime", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { remaining }), Is.EqualTo(expected));
    }

    [Test]
    public void BattleHudUsesASeparateReloadProgressVisual()
    {
        const string scenePath = "Assets/Scenes/_Menu.unity";
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
            scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            LocalPlayerWeaponHUD hud = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                hud = root.GetComponentInChildren<LocalPlayerWeaponHUD>(true);
                if (hud != null) break;
            }
            Assert.That(hud, Is.Not.Null);
            var serialized = new SerializedObject(hud);
            SerializedProperty progress = serialized.FindProperty("reloadProgressFill");
            Assert.That(progress, Is.Not.Null);
            Assert.That(progress.objectReferenceValue, Is.TypeOf<Image>());
            Assert.That(serialized.FindProperty("reloadKeyLabel").objectReferenceValue,
                Is.AssignableTo<TMPro.TMP_Text>());
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void BattleKeysHaveConfigurableDefaultsOnRunner()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Runner.prefab");
        Assert.That(prefab, Is.Not.Null);
        var input = prefab.GetComponent<InputManager>();
        Assert.That(input, Is.Not.Null);
        Assert.That(input.GrappleKey, Is.EqualTo(KeyCode.Q));
        Assert.That(input.GrappleFocusKey, Is.EqualTo(KeyCode.E));
        Assert.That(input.ReloadKey, Is.EqualTo(KeyCode.R));
        Assert.That(input.RewardHoldKey, Is.EqualTo(KeyCode.LeftAlt));
    }

    [TestCase(100f, 20f, 5)]
    [TestCase(200f, 20f, 10)]
    [TestCase(101f, 20f, 6)]
    public void HealthSegmentsFollowMaximumHealth(float maximum, float perSegment, int expected)
    {
        var type = typeof(LocalPlayerHealthSlider).Assembly.GetType("LocalHealthSegmentView");
        Assert.That(type, Is.Not.Null);
        var method = type.GetMethod("SegmentCountFor", BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, new object[] { maximum, perSegment }), Is.EqualTo(expected));
    }

    [Test]
    public void GrappleFocusAcceptsEAbilityInputInsteadOfAimInput()
    {
        var root = new GameObject("FocusInputTest");
        try
        {
            var ability = root.AddComponent<SupportAerialAbility>();
            MethodInfo method = typeof(SupportAerialAbility).GetMethod(
                "IsFocusInputActive", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            var eInput = new NetInput();
            eInput.Buttons.Set(InputButton.Ability1, true);
            Assert.That(method.Invoke(ability, new object[] { eInput }), Is.EqualTo(true));
            var aimInput = new NetInput();
            aimInput.Buttons.Set(InputButton.Aim, true);
            Assert.That(method.Invoke(ability, new object[] { aimInput }), Is.EqualTo(false));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void StageHudIncludesBattleAbilityLayout()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/UI/StageHUD.prefab");
        Assert.That(prefab, Is.Not.Null);
        var hud = prefab.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true);
        Assert.That(hud, Is.Not.Null);
        var serialized = new SerializedObject(hud);
        Assert.That(serialized.FindProperty("slotTemplate").objectReferenceValue,
            Is.Not.Null);
        Assert.That(serialized.FindProperty("grappleFill").objectReferenceValue,
            Is.TypeOf<Image>());
    }

    [TestCase("SupportAerial")]
    [TestCase("TankAirDash")]
    [TestCase("AttackGrappleMark")]
    [TestCase("SupportGrapplePull")]
    [TestCase("TankGrappleGather")]
    public void RewardAbilityUsesSharedHudIcon(string assetName)
    {
        var definition = AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>(
            "Assets/_Project_Assets/Data/PlayerAbility/Definitions/" + assetName + ".asset");
        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.HudIcon, Is.Not.Null);
    }
}
