using System;
using System.IO;
using MultiClimb.Menu;
using NUnit.Framework;
using Purgers.Progression;
using UnityEngine;
using Fusion.Menu;

[Category("PurgersRegression")]
public sealed class MenuSaveFlowTests
{
    [Test]
    public void ScenePolicyFallsBackFromStalePlayerPrefsSelection()
    {
        var stale = new PhotonMenuSceneInfo
        {
            Name = "Game",
            ScenePath = "Assets/Scenes/Game.unity"
        };
        var safeHouse = new PhotonMenuSceneInfo
        {
            Name = "SafeHouse",
            ScenePath = "Assets/Scenes/SafeHouse.unity"
        };

        bool success = MenuSceneLaunchPolicy.TryResolve(
            stale,
            new[] { safeHouse },
            out PhotonMenuSceneInfo resolved);

        Assert.That(success, Is.True);
        Assert.That(resolved.ScenePath, Is.EqualTo(safeHouse.ScenePath));
        Assert.That(resolved.Name, Is.EqualTo(safeHouse.Name));
    }

    [TestCase(
        false,
        null,
        false,
        MenuSaveLaunchKind.QuickPlayNewHost)]
    [TestCase(
        false,
        "ABCD",
        false,
        MenuSaveLaunchKind.ClientJoin)]
    [TestCase(
        true,
        null,
        false,
        MenuSaveLaunchKind.FreshHost)]
    [TestCase(
        true,
        null,
        true,
        MenuSaveLaunchKind.ContinueHost)]
    public void LaunchPolicySeparatesQuickPlayContinueAndClient(
        bool creating,
        string sessionName,
        bool hasWritableHostSave,
        MenuSaveLaunchKind expected)
    {
        Assert.That(
            MenuSaveLaunchPolicy.Resolve(
                creating,
                sessionName,
                hasWritableHostSave),
            Is.EqualTo(expected));
    }

    [Test]
    public void ClientReadOnlyContextCannotWriteHostSave()
    {
        var contextObject = new GameObject("ClientSaveContext");

        try
        {
            GameSaveRuntimeContext context =
                contextObject.AddComponent<GameSaveRuntimeContext>();
            context.InitializeClientReadOnly();

            Assert.That(
                context.AccessMode,
                Is.EqualTo(GameSaveAccessMode.ClientReadOnly));
            Assert.That(context.ActiveSave, Is.Null);
            Assert.That(context.HasWritableHostSave, Is.False);

            GameSaveRepositoryResult<GameSaveData> writeResult =
                context.WriteActiveSave();
            Assert.That(writeResult.Success, Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(contextObject);
        }
    }

    [Test]
    public void HostAndClientRunnerContextsRemainIndependent()
    {
        var hostObject = new GameObject("HostRunner");
        var clientObject = new GameObject("ClientRunner");

        try
        {
            GameSaveRuntimeContext hostContext =
                hostObject.AddComponent<GameSaveRuntimeContext>();
            GameSaveRuntimeContext clientContext =
                clientObject.AddComponent<GameSaveRuntimeContext>();
            var repository = new JsonGameSaveRepository(
                Path.Combine(
                    Path.GetTempPath(),
                    "PurgersMenuContextTests",
                    Guid.NewGuid().ToString("N")));
            GameSaveData save = GameSaveData.CreateNew(
                Guid.NewGuid().ToString("N"),
                "Host",
                DateTime.UtcNow);

            hostContext.InitializeHost(repository, save);
            clientContext.InitializeClientReadOnly();

            Assert.That(hostContext.HasWritableHostSave, Is.True);
            Assert.That(hostContext.ActiveSave, Is.SameAs(save));
            Assert.That(
                clientContext.AccessMode,
                Is.EqualTo(GameSaveAccessMode.ClientReadOnly));
            Assert.That(clientContext.ActiveSave, Is.Null);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(hostObject);
            UnityEngine.Object.DestroyImmediate(clientObject);
        }
    }
}
