using System.Linq;
using Fusion;
using Assert = NUnit.Framework.Assert;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[Category("PurgersRegression")]
public sealed class EnemyHealthBarWiringTests
{
    [TestCase("Enemy_Melee_A")] [TestCase("Enemy_Melee_B")] [TestCase("Enemy_Ranged_A")]
    [TestCase("Enemy_Ranged_B")] [TestCase("Enemy_Ranged_C_Beam")] [TestCase("Enemy_Ranged_C_Projectile")]
    [TestCase("Enemy_Ranged_Reflector")] [TestCase("Enemy_Flytrap")] [TestCase("Enemy_Suicide")]
    public void EnemyHasBakedTargetAndInternalAnchor(string name)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyHealthBarWiring.PathFor(name));
        var target = root.GetComponent<EnemyHealthBarTarget>(); Assert.That(target, Is.Not.Null, name + " 尚未接線");
        var data = new SerializedObject(target);
        var anchor = data.FindProperty("headAnchor").objectReferenceValue as Transform;
        Assert.That(anchor, Is.Not.Null); Assert.That(anchor.IsChildOf(root.transform), Is.True);
        Assert.That(target.LocalBody.size.x * target.LocalBody.size.y * target.LocalBody.size.z, Is.GreaterThan(0));
        Assert.That(root.GetComponent<NetworkObject>().NetworkedBehaviours, Has.Member(target));
        var visible = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
        var all = visible[0].bounds; foreach (var r in visible) all.Encapsulate(r.bounds);
        float worldExtent = root.transform.TransformVector(target.LocalBody.size).magnitude;
        Assert.That(worldExtent, Is.LessThan(all.size.magnitude * 2), "身體盒不能因骨架比例換算而異常放大");
        Assert.That(Vector3.Distance(root.transform.TransformPoint(target.LocalBody.center), all.center),
            Is.LessThan(all.size.magnitude), "身體盒必須靠近實際模型");
        Assert.That(root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)), Is.Zero);
    }
    [Test] public void BossAndBaseRemainExcluded()
    {
        foreach (string path in new[] { EnemyHealthBarWiring.PathFor("Enemy_Boss"), "Assets/Prefabs/Enemy/Core/Enemy_Base_Core.prefab" })
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<EnemyHealthBarTarget>(), Is.Null);
    }
    [Test] public void SavedMenuHasExactlyOneConfiguredScreenSpaceHud()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
        if (!alreadyLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity", OpenSceneMode.Additive);
        try
        {
            var huds = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LocalEnemyHealthBarHUD>(true)).ToArray();
            Assert.That(huds.Length, Is.EqualTo(1));
            Assert.That(huds[0].Settings, Is.Not.Null);
            Assert.That(huds[0].transform.parent.name, Is.EqualTo("PlayerHUDCanvas"));
            Assert.That(huds[0].GetComponentInParent<Canvas>().renderMode, Is.Not.EqualTo(RenderMode.WorldSpace));
        }
        finally { if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true); }
    }
}
