using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>玩家碰撞篩選與射出起點須一致，不能藉移除 Player Layer 關掉爆炸傷害。</summary>
[Category("PurgersRegression")]
public sealed class ActiveAbilityPlayerCollisionTests
{
    private Scene scene;
    [SetUp] public void SetUp() => scene = EditorSceneManager.NewPreviewScene();
    [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);
    private GameObject Create(string name)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }
    [TestCase(PlayerAbilityProjectileKind.Grenade, true, true)]
    [TestCase(PlayerAbilityProjectileKind.Grenade, false, false)]
    [TestCase(PlayerAbilityProjectileKind.HealingPack, true, false)]
    [TestCase(PlayerAbilityProjectileKind.Ricochet, true, false)]
    public void FlightPolicyOnlyIgnoresPlayersForConfiguredGrenades(PlayerAbilityProjectileKind kind, bool enabled, bool expected)
    {
        var projectile = Create("Projectile").AddComponent<PlayerAbilityProjectile>();
        var serialized = new SerializedObject(projectile);
        var setting = serialized.FindProperty("grenadeIgnoresPlayers");
        Assert.That(setting, Is.Not.Null, "榴彈飛行碰撞需可配置，且不可改動補包與反彈彈規則。");
        setting.boolValue = enabled;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var method = typeof(PlayerAbilityProjectile).GetMethod("IgnoresPlayers");
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(projectile, new object[] { kind }), Is.EqualTo(expected));
    }
    [Test] public void ExistingGrenadePrefabDefaultsToIgnoringPlayers()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/AbilityRuntime/Active/GrenadeProjectile.prefab");
        var setting = new SerializedObject(prefab.GetComponent<PlayerAbilityProjectile>()).FindProperty("grenadeIgnoresPlayers");
        Assert.That(setting, Is.Not.Null);
        Assert.That(setting.boolValue, Is.True);
    }
    [TestCase(true)] [TestCase(false)]
    public void OriginInsideAnotherPlayerObeysFlightPolicy(bool ignorePlayers)
    {
        var player = Create("Other player");
        player.AddComponent<PlayerHealth>();
        var child = Create("Player collider on Default layer");
        child.transform.SetParent(player.transform);
        child.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var method = typeof(ActiveAbilityPhysics).GetMethod("TryResolveShotOrigin");
        Assert.That(method.GetParameters().Length, Is.EqualTo(8), "射出起點需要與飛行一致的玩家排除規則。");
        object[] args = { scene.GetPhysicsScene(), Vector3.zero, Vector3.up * 1.5f, .1f,
            (LayerMask)(~0), null, null, ignorePlayers };
        Assert.That((bool)method.Invoke(null, args), Is.EqualTo(ignorePlayers));
        if (ignorePlayers) Assert.That((Vector3)args[6], Is.EqualTo(Vector3.up * 1.5f));
    }
    [Test] public void IgnoringPlayersStillStopsAtWorldBehindThem()
    {
        var player = Create("Other player");
        player.AddComponent<PlayerHealth>();
        player.transform.position = Vector3.up * .7f;
        player.AddComponent<BoxCollider>();
        var wall = Create("Ceiling");
        wall.transform.position = Vector3.up * 1.5f;
        wall.transform.localScale = new Vector3(4f, .1f, 4f);
        wall.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        var method = typeof(ActiveAbilityPhysics).GetMethod("TryResolveShotOrigin");
        Assert.That(method.GetParameters().Length, Is.EqualTo(8));
        object[] args = { scene.GetPhysicsScene(), Vector3.zero, Vector3.up * 2f, .1f,
            (LayerMask)(~0), null, null, true };
        Assert.That((bool)method.Invoke(null, args), Is.True);
        Assert.That(((Vector3)args[6]).y, Is.InRange(1.3f, 1.35f));
    }
}
