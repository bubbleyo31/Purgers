using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemyJellyfishCollisionSetupTests
{
    [TestCase("Enemy_Ranged_A")]
    [TestCase("Enemy_Ranged_B")]
    public void MigrationAlignsBothQueriesHeadZonesAndDeathCleanupAndCanRepeat(string prefabName)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject(prefabName);
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var parts = Fixture(root);
            var model = root.transform.Find(EnemyJellyfishCollisionSetup.ModelPath);
            var renderer = model.gameObject.AddComponent<SkinnedMeshRenderer>();
            var originalTransforms = model.GetComponentsInChildren<Transform>(true)
                .ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
            var envelope = new EnemyCollisionEnvelope(4, 20, -4);
            var bounds = new Bounds(new Vector3(0, 6, 0), new Vector3(6, 12, 6));
            for (int pass = 0; pass < 2; pass++)
            {
                EnemyJellyfishCollisionSetup.ConfigureContents(root, parts, envelope, .1f, 1, bounds);
                Assert.That(renderer.quality, Is.EqualTo(SkinQuality.Bone4), "畫質切換不可改變模型與四權重受擊點的對應");
                var colliders = root.GetComponentsInChildren<Collider>(true);
                var hitRoot = root.GetComponent<HitboxRoot>();
                Assert.That(colliders.Length, Is.EqualTo(8));
                Assert.That(hitRoot.Hitboxes.Length, Is.EqualTo(8));
                Assert.That(root.transform.Find("Body"), Is.Not.Null, "保留舊物件身分");
                foreach (var hitbox in hitRoot.Hitboxes)
                {
                    var part = parts.Single(p => hitbox.name == EnemyJellyfishCollisionSetup.GeneratedPrefix + p.Id);
                    Assert.That(hitbox.Root, Is.SameAs(hitRoot));
                    Assert.That(hitbox.transform.lossyScale, Is.EqualTo(Vector3.one));
                    var zone = hitbox.GetComponent<WeaponHitZone>();
                    Assert.That(zone.IsHead, Is.EqualTo(part.BodyRegion == EnemyJellyfishCollisionSetup.Region.SmallBell));
                    Assert.That(zone.DamageMultiplier, Is.EqualTo(1));
                    var capsule = hitbox.GetComponent<CapsuleCollider>();
                    if (capsule != null)
                    {
                        Assert.That(hitbox.CapsuleRadius, Is.EqualTo(capsule.radius));
                        Assert.That(hitbox.CapsuleExtents, Is.EqualTo(capsule.height));
                    }
                    else Assert.That(hitbox.BoxExtents * 2, Is.EqualTo(hitbox.GetComponent<BoxCollider>().size));
                }
                var death = new SerializedObject(root.GetComponent<CombatDeathHandler>()).FindProperty("collidersToDisableOnDeath");
                Assert.That(death.arraySize, Is.EqualTo(colliders.Length));
                for (int i = 0; i < death.arraySize; i++) Assert.That(death.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
                foreach (var original in originalTransforms)
                {
                    Assert.That(original.Key.localPosition, Is.EqualTo(original.Value.Item1));
                    Assert.That(original.Key.localRotation, Is.EqualTo(original.Value.Item2));
                    Assert.That(original.Key.localScale, Is.EqualTo(original.Value.Item3));
                }
                foreach (var motor in new Component[] {root.GetComponent<EnemyFlyingPatrolNavigator>(), root.GetComponent<EnemyFlyingChaseMotor>()})
                {
                    var so = new SerializedObject(motor);
                    Assert.That(so.FindProperty("bodyRadius").floatValue, Is.EqualTo(envelope.Radius));
                    Assert.That(so.FindProperty("bodyHeight").floatValue, Is.EqualTo(envelope.Height));
                    Assert.That(so.FindProperty("bodyBaseOffset").floatValue, Is.EqualTo(envelope.BaseOffset));
                    Assert.That(so.FindProperty("collisionSkin").floatValue, Is.EqualTo(.1f));
                }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [Test]
    public void UnexpectedColliderAbortsBeforeRemovingOriginalComponents()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Enemy_Ranged_A"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var parts = Fixture(root);
            var unexpected = new GameObject("UserAddedCollider"); unexpected.transform.SetParent(root.transform, false);
            unexpected.layer = 8; unexpected.AddComponent<BoxCollider>();
            var original = root.transform.Find("Body").GetComponent<Collider>();
            Assert.Throws<InvalidOperationException>(() => EnemyJellyfishCollisionSetup.ConfigureContents(root, parts,
                new EnemyCollisionEnvelope(4, 20, -4), .1f, 1, new Bounds(Vector3.zero, Vector3.one * 20)));
            Assert.That(original != null, Is.True);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(EnemyJellyfishCollisionSetup.GeneratedPrefix)), Is.False);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    // 僅測試用的合成骨架／尺寸，絕不可作為正式模型配置或儲存至 Prefab。
    private static EnemyJellyfishCollisionSetup.Part[] Fixture(GameObject root)
    {
        root.AddComponent<HitboxRoot>(); root.AddComponent<CombatDeathHandler>();
        root.AddComponent<EnemyFlyingPatrolNavigator>(); root.AddComponent<EnemyFlyingChaseMotor>();
        var old = new GameObject("Body"); old.transform.SetParent(root.transform, false); old.layer = 8;
        old.AddComponent<BoxCollider>(); old.AddComponent<Hitbox>(); old.AddComponent<WeaponHitZone>();
        string basePath = EnemyJellyfishCollisionSetup.ModelPath + "/骨架/body_01";
        var list = new List<EnemyJellyfishCollisionSetup.Part>();
        Add(root, list, EnemyJellyfishCollisionSetup.Region.SmallBell, basePath + "/neck_01/head_01", 1);
        Add(root, list, EnemyJellyfishCollisionSetup.Region.NeckAndShortTentacles, basePath + "/neck_01", 1);
        Add(root, list, EnemyJellyfishCollisionSetup.Region.MainBell, basePath, 1);
        Add(root, list, EnemyJellyfishCollisionSetup.Region.Skirt, basePath, 1);
        foreach (var direction in new[] { "back", "forward", "Left", "right" })
        {
            Add(root, list, EnemyJellyfishCollisionSetup.Region.LongTentacle, basePath, 1);
            list[list.Count - 1].Id = "LongTentacles_" + direction;
        }
        return list.ToArray();
    }

    private static void Add(GameObject root, List<EnemyJellyfishCollisionSetup.Part> parts,
        EnemyJellyfishCollisionSetup.Region region, string path, int count)
    {
        var parent = root.transform;
        foreach (var name in path.Split('/'))
        {
            var child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            parent = child;
        }
        for (int i = 0; i < count; i++) parts.Add(new EnemyJellyfishCollisionSetup.Part {
            Id = region + "_" + parts.Count, BodyRegion = region, BonePath = path,
            Geometry = region == EnemyJellyfishCollisionSetup.Region.LongTentacle
                ? EnemyJellyfishCollisionSetup.Shape.Capsule : EnemyJellyfishCollisionSetup.Shape.Box,
            Size = Vector3.one, Radius = .1f, Height = 1
        });
    }
}
