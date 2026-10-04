using System;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class ActiveAbilityPresentationTests
{
    private Scene scene;
    private GameObject root;
    private Type PresentationType => typeof(Player).Assembly.GetType("PlayerAbilityProjectilePresentation");
    private Type SlotType => typeof(Player).Assembly.GetType("PlayerAbilityLocalVfxSlot");

    [SetUp] public void SetUp()
    {
        scene = EditorSceneManager.NewPreviewScene();
        root = new GameObject("Authoritative projectile root");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetPositionAndRotation(new Vector3(8f, 2f, 6f), Quaternion.Euler(0f, 35f, 0f));
    }
    [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

    private Component AddPresentation(Transform anchor)
    {
        Assert.That(PresentationType, Is.Not.Null, "需要與投射物權威根物件分離的本機外觀元件。");
        var component = root.AddComponent(PresentationType);
        var field = PresentationType.GetField("visualAnchor", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field.SetValue(component, anchor);
        return component;
    }
    private void Present(Component component, PlayerAbilityProjectileKind kind, Vector3 velocity,
        bool resting, float seconds)
    {
        var method = PresentationType.GetMethod("ApplyVisualState");
        Assert.That(method, Is.Not.Null);
        method.Invoke(component, new object[] { kind, velocity, resting, false, seconds, 1f });
    }
    private Transform Child()
    {
        var child = new GameObject("Visual only");
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = Vector3.up * 0.2f;
        return child.transform;
    }

    [Test] public void HealingFloat_ChangesVisualOnlyAndStaysBounded()
    {
        Transform anchor = Child();
        var component = AddPresentation(anchor);
        Vector3 origin = root.transform.position;
        Quaternion rotation = root.transform.rotation;
        Vector3 basePosition = anchor.localPosition;
        Present(component, PlayerAbilityProjectileKind.HealingPack, Vector3.zero, true, 0.25f);
        Assert.That(root.transform.position, Is.EqualTo(origin));
        Assert.That(root.transform.rotation, Is.EqualTo(rotation));
        Assert.That(anchor.localPosition.y, Is.GreaterThan(basePosition.y));
        Assert.That(anchor.localPosition.y - basePosition.y, Is.LessThanOrEqualTo(0.5f));
        Present(component, PlayerAbilityProjectileKind.HealingPack, Vector3.forward, false, 0.5f);
        Assert.That(anchor.localPosition, Is.EqualTo(basePosition), "離開停留狀態時不可累積漂浮偏移。");
    }

    [Test] public void GrenadeNose_FollowsDescendingTrajectoryWithoutRotatingRoot()
    {
        Transform anchor = Child();
        var component = AddPresentation(anchor);
        Quaternion rotation = root.transform.rotation;
        Vector3 direction = new Vector3(1f, -1f, 2f).normalized;
        Present(component, PlayerAbilityProjectileKind.Grenade, direction * 15f, false, 0.5f);
        Assert.That(Vector3.Angle(anchor.forward, direction), Is.LessThan(1f));
        Assert.That(root.transform.rotation, Is.EqualTo(rotation));
    }

    [Test] public void MisconfiguredRootAnchor_CannotMoveAuthority()
    {
        var component = AddPresentation(root.transform);
        Vector3 origin = root.transform.position;
        Quaternion rotation = root.transform.rotation;
        Present(component, PlayerAbilityProjectileKind.Grenade, Vector3.down, false, 0.3f);
        Present(component, PlayerAbilityProjectileKind.HealingPack, Vector3.zero, true, 0.3f);
        Assert.That(root.transform.position, Is.EqualTo(origin));
        Assert.That(root.transform.rotation, Is.EqualTo(rotation));
    }

    [Test] public void VisualSlot_RejectsCollisionAndNetworkComponents()
    {
        Assert.That(SlotType, Is.Not.Null);
        var check = SlotType.GetMethod("IsVisualOnly", BindingFlags.Public | BindingFlags.Static);
        Assert.That(check, Is.Not.Null);
        Assert.That((bool)check.Invoke(null, new object[] { root }), Is.True);
        var collider = root.AddComponent<BoxCollider>();
        Assert.That((bool)check.Invoke(null, new object[] { root }), Is.False);
        UnityEngine.Object.DestroyImmediate(collider);
        root.AddComponent<NetworkObject>();
        Assert.That((bool)check.Invoke(null, new object[] { root }), Is.False);
    }
}
