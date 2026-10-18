using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using Fusion;
using Fusion.Photon.Realtime;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;
using Fusion.Menu;
using MultiClimb.Menu;
using NUnit.Framework;
using Purgers.Progression;
using UnityEditor;

[Category("PurgersRegression")]
public sealed class MenuConnectionRecoveryTests
{
    [Test]
    public void AsiaIsAppendedWithoutChangingExistingCodeIndices()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        CollectionAssert.AreEqual(new[] { "", "us", "usw", "eu", "sa" }, config.AvailableRegions.Take(5));
        Assert.That(config.AvailableRegions.IndexOf("asia"), Is.GreaterThanOrEqualTo(5));
    }
    [Test]
    public void PartyRegionCatalogPreservesConfiguredIndices()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        var connection = new MenuConnection(config, null, new JsonGameSaveRepository());
        var regions = connection.RequestAvailableOnlineRegionsAsync(new FusionMenuConnectArgs());
        Assert.That(regions.IsCompleted, Is.True);
        CollectionAssert.AreEqual(config.AvailableRegions, regions.Result.Select(r => r.Code));
    }
    [Test]
    public void CancelBeforeConnectCompletesCannotBecomeConnected()
    {
        var session = new MenuSessionEndState();
        session.MarkLocalExit();
        session.MarkConnected(true);
        Assert.That(session.HasEnded, Is.True);
        Assert.That(session.TryEndRemotely(), Is.False);
    }

    [TestCase("asia")]
    [TestCase("jp")]
    [TestCase("us")]
    [TestCase("eu")]
    public void ActualRegionRoundTripsRegardlessOfClientPreference(string region)
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        string code = MenuRegionRouting.Encode(config, config.CodeGenerator.Create(), region.ToUpperInvariant() + "/cluster");
        Assert.That(MenuRegionRouting.Decode(config, code), Is.EqualTo(region));
    }

    [Test]
    public void LegacyBestAndOutOfRangeCodesFailBeforeStartingConnection()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        string code = config.CodeGenerator.Create();
        Assert.Throws<InvalidOperationException>(() => MenuRegionRouting.Decode(config, config.CodeGenerator.EncodeRegion(code, 0)));
        Assert.Throws<InvalidOperationException>(() => MenuRegionRouting.Decode(config, config.CodeGenerator.EncodeRegion(code, config.AvailableRegions.Count)));
        Assert.Throws<InvalidOperationException>(() => MenuRegionRouting.Encode(config, code, string.Empty));
    }

    [UnityTest] public IEnumerator CancelEndsWaitEvenIfUnderlyingWorkNeverCompletes() => Run(CancelWaitAsync());
    private async Task CancelWaitAsync()
    {
        var work = new TaskCompletionSource<int>();
        using (var attempt = new MenuConnectionAttempt(TimeSpan.FromMinutes(1)))
        {
            var waiting = attempt.WaitAsync(work.Task);
            attempt.CancelByUser();
            Assert.That(await Task.WhenAny(waiting, Task.Delay(1000)), Is.SameAs(waiting));
            try { await waiting; Assert.Fail("Cancelled work returned a result."); }
            catch (OperationCanceledException) { }
            Assert.That(attempt.CancelledByUser, Is.True);
            work.SetResult(123); // 遲到成功不能恢復已取消的等待。
            Assert.That(waiting.IsCanceled, Is.True);
        }
        using (var retry = new MenuConnectionAttempt(TimeSpan.FromSeconds(1)))
            Assert.That(await retry.WaitAsync(Task.FromResult(456)), Is.EqualTo(456));
    }

    [UnityTest] public IEnumerator TimeoutEndsWaitWithoutUserCancellation() => Run(TimeoutWaitAsync());
    private async Task TimeoutWaitAsync()
    {
        using (var attempt = new MenuConnectionAttempt(TimeSpan.FromMilliseconds(30)))
        {
            var waiting = attempt.WaitAsync(new TaskCompletionSource<int>().Task);
            Assert.That(await Task.WhenAny(waiting, Task.Delay(1000)), Is.SameAs(waiting));
            try { await waiting; Assert.Fail("Timeout did not cancel."); }
            catch (OperationCanceledException) { }
            Assert.That(attempt.CancelledByUser, Is.False);
        }
    }

    [UnityTest] public IEnumerator CancelStartupCleansRunnerAndSaveThenAllowsRetry() => Run(CancelAndRetryAsync(false));
    [UnityTest] public IEnumerator CancelBestRegionCleansRunnerAndSaveThenAllowsRetry() => Run(CancelAndRetryAsync(true));
    private async Task CancelAndRetryAsync(bool bestRegion)
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        string path = Path.GetFullPath(Path.Combine("Temp", "ConnectionRecovery-" + Guid.NewGuid().ToString("N")));
        var repository = new JsonGameSaveRepository(path);
        var connection = new ControlledConnection(config, repository, 30f) { HoldLobby = bestRegion };
        try
        {
            var first = connection.ConnectAsync(new FusionMenuConnectArgs { Region = bestRegion ? "" : "asia" });
            Assert.That(first.IsCompleted, Is.False);
            var firstRunner = connection.LastRunner;
            await connection.DisconnectAsync(ConnectFailReason.UserRequest);
            var result = await first;
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailReason, Is.EqualTo(ConnectFailReason.UserRequest));
            Assert.That(firstRunner == null, Is.True, "The initializing Runner must also be destroyed.");
            Assert.That(repository.List().Summaries, Is.Empty, "Failed new host must roll back its new save.");
            Assert.That(connection.Shutdowns, Is.EqualTo(1));

            connection.HoldLobby = false;
            var second = connection.ConnectAsync(new FusionMenuConnectArgs { Region = "asia" });
            Assert.That(second.IsCompleted, Is.False, "Retry must reach the transport instead of the busy guard.");
            var secondRunner = connection.LastRunner;
            // 舊工作遲到成功，不能清理或改動新 Runner。
            if (bestRegion) connection.Lobby.TrySetResult(SuccessResult());
            else connection.Starts[0].TrySetResult(SuccessResult());
            await Task.Yield();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(secondRunner != null, Is.True);
            await connection.DisconnectAsync(ConnectFailReason.UserRequest);
            await second;
            Assert.That(repository.List().Summaries, Is.Empty);
        }
        finally
        {
            await connection.DisconnectAsync(ConnectFailReason.UserRequest);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [UnityTest] public IEnumerator BestHostEncodesLobbyRegionBeforeStartingGame() => Run(BestHostAsync());
    private async Task BestHostAsync()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        string path = Path.GetFullPath(Path.Combine("Temp", "ConnectionRecovery-" + Guid.NewGuid().ToString("N")));
        var connection = new ControlledConnection(config, new JsonGameSaveRepository(path), 30f);
        try
        {
            var pending = connection.ConnectAsync(new FusionMenuConnectArgs { Region = "" });
            Assert.That(connection.LastArgs.CustomPhotonAppSettings.FixedRegion, Is.EqualTo("jp"));
            Assert.That(MenuRegionRouting.Decode(config, connection.LastArgs.SessionName), Is.EqualTo("jp"));
            await connection.DisconnectAsync(ConnectFailReason.UserRequest);
            await pending;
        }
        finally
        {
            await connection.DisconnectAsync(ConnectFailReason.UserRequest);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    [UnityTest] public IEnumerator ClientUsesCodeRegionAndTimeoutReleasesBusyGuard() => Run(ClientTimeoutAsync());
    private async Task ClientTimeoutAsync()
    {
        var config = AssetDatabase.LoadAssetAtPath<FusionMenuConfig>("Assets/Settings/MenuConfig.asset");
        var connection = new ControlledConnection(config, new JsonGameSaveRepository(), 1f);
        string code = MenuRegionRouting.Encode(config, config.CodeGenerator.Create(), "asia");
        var pending = connection.ConnectAsync(new FusionMenuConnectArgs { Region = "eu", Session = code });
        Assert.That(connection.LastArgs.CustomPhotonAppSettings.FixedRegion, Is.EqualTo("asia"));
        var result = await pending;
        Assert.That(result.Success, Is.False);
        StringAssert.Contains("逾時", result.DebugMessage);
        Assert.That(connection.LastRunner == null, Is.True);
        var retry = connection.ConnectAsync(new FusionMenuConnectArgs { Region = "us", Session = code });
        Assert.That(retry.IsCompleted, Is.False);
        await connection.DisconnectAsync(ConnectFailReason.UserRequest);
        await retry;
    }

    private static IEnumerator Run(Task task)
    {
        double deadline = UnityEditor.EditorApplication.timeSinceStartup + 12;
        while (!task.IsCompleted && UnityEditor.EditorApplication.timeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "Connection test exceeded its bounded deadline.");
        if (task.IsFaulted) throw task.Exception.GetBaseException();
        Assert.That(task.IsCanceled, Is.False);
    }

    private static StartGameResult SuccessResult() => (StartGameResult)Activator.CreateInstance(
        typeof(StartGameResult), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic, null, new object[] { ShutdownReason.Ok, null, null }, null);

    private sealed class ControlledConnection : MenuConnection
    {
        public readonly List<TaskCompletionSource<StartGameResult>> Starts = new List<TaskCompletionSource<StartGameResult>>();
        public readonly TaskCompletionSource<StartGameResult> Lobby = new TaskCompletionSource<StartGameResult>();
        public bool HoldLobby;
        public int Shutdowns;
        public NetworkRunner LastRunner;
        public StartGameArgs LastArgs;
        public ControlledConnection(FusionMenuConfig config, IGameSaveRepository repository, float timeout)
            : base(config, null, repository, connectionTimeoutSeconds: timeout) { }
        protected override NetworkRunner CreateRunner() => LastRunner = new GameObject("Connection recovery test").AddComponent<NetworkRunner>();
        protected override string GetLobbyRegion(NetworkRunner runner) => "jp";
        protected override Task<StartGameResult> JoinLobbyAsync(NetworkRunner runner, GameMode mode,
            FusionAppSettings settings, CancellationToken token) => HoldLobby ? Lobby.Task : Task.FromResult(SuccessResult());
        protected override Task<StartGameResult> StartRunnerAsync(NetworkRunner runner, StartGameArgs args)
        {
            LastArgs = args;
            var source = new TaskCompletionSource<StartGameResult>();
            Starts.Add(source);
            return source.Task;
        }
        protected override Task ShutdownRunnerAsync(NetworkRunner runner, int reason)
        {
            Shutdowns++;
            return Task.CompletedTask;
        }
    }
}
