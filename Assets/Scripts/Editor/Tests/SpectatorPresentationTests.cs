using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>觀戰子物件不能將位移回授給父相機；重生後必須恢復正常跟隨。</summary>
[Category("PurgersRegression")]
public sealed class SpectatorPresentationTests
{
    private GameObject rig;
    private GameObject anchor;
    private CameraFollow follow;
    [SetUp] public void SetUp()
    {
        rig = new GameObject("isolated spectator camera");
        rig.SetActive(false);
        follow = rig.AddComponent<CameraFollow>();
        anchor = new GameObject("spectator controller child");
        anchor.transform.SetParent(rig.transform, false);
        anchor.transform.localPosition = new Vector3(2, 3, 4);
    }
    [TearDown] public void TearDown() => Object.DestroyImmediate(rig);
    private void SetPose(Vector3 position, Quaternion rotation)
    {
        var method = typeof(CameraFollow).GetMethod("SetWorldPose");
        Assert.That(method, Is.Not.Null, "觀戰必須提交世界座標快照，不能追隨相機自己的子物件。");
        method.Invoke(follow, new object[] { anchor.transform, position, rotation });
    }
    private void Tick() => typeof(CameraFollow).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(follow, null);
    [Test] public void ChildOwnerDoesNotFeedBackIntoCameraPose()
    {
        var position = new Vector3(12, 7, -9);
        var rotation = Quaternion.Euler(-42, 125, 0);
        SetPose(position, rotation);
        for (int i = 0; i < 12; i++) Tick();
        Assert.That(Vector3.Distance(rig.transform.position, position), Is.LessThan(.0001f));
        Assert.That(Quaternion.Angle(rig.transform.rotation, rotation), Is.LessThan(.01f));
        Assert.That(anchor.transform.localPosition, Is.EqualTo(new Vector3(2, 3, 4)));
    }
    [Test] public void RespawnTargetReplacesSpectatorSnapshot()
    {
        SetPose(new Vector3(5, 6, 7), Quaternion.identity);
        var local = new GameObject("respawn target");
        try
        {
            local.transform.SetPositionAndRotation(new Vector3(-10, 2, 20), Quaternion.Euler(30, 90, 0));
            follow.SetTarget(local.transform);
            follow.ClearTarget(anchor.transform);
            Tick();
            Assert.That(rig.transform.position, Is.EqualTo(local.transform.position));
            Assert.That(Quaternion.Angle(rig.transform.rotation, local.transform.rotation), Is.LessThan(.01f));
        }
        finally { Object.DestroyImmediate(local); }
    }
    [Test] public void ClearingOwnerStopsApplyingOldSnapshot()
    {
        SetPose(Vector3.one, Quaternion.identity);
        follow.ClearTarget(anchor.transform);
        rig.transform.position = Vector3.one * 8;
        Tick();
        Assert.That(rig.transform.position, Is.EqualTo(Vector3.one * 8));
    }

    [Test] public void ObservedBodyKeepsShadowsAndRestoresOriginalModes()
    {
        var controller = anchor.AddComponent<LocalPlayerSpectatorController>();
        var body = anchor.AddComponent<MeshRenderer>();
        body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(LocalPlayerSpectatorController).GetMethod("HideObservedBody", flags)
            .Invoke(controller, new object[] { new Renderer[] { body, body, null } });
        Assert.That(body.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly));
        Assert.That(body.enabled, Is.True);
        typeof(LocalPlayerSpectatorController).GetMethod("RestoreObservedBody", flags).Invoke(controller, null);
        Assert.That(body.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.TwoSided));
    }

    [Test] public void MissingRunnerCannotExposeAStaleObservedPlayer()
    {
        Assert.That(LocalPlayerSpectatorController.TryGetViewedPlayerObject(null, out var viewed), Is.False);
        Assert.That(viewed, Is.Null);
        Assert.That(LocalPlayerSpectatorController.IsViewedObject(null), Is.False);
        Assert.That(LocalPlayerSpectatorController.ViewedPlayer(null), Is.EqualTo(Fusion.PlayerRef.None));
    }
}
