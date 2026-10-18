using MultiClimb.Menu;
using NUnit.Framework;
using Fusion.Menu;
using Purgers.Progression;
using UnityEngine.SceneManagement;

[Category("PurgersRegression")]
public sealed class MenuSessionEndTests
{
    [Test]
    public void DisconnectWithoutRunnerDoesNotUnloadMenuScenes()
    {
        var connection = new MenuConnection(null, null, new JsonGameSaveRepository());
        int sceneCount = SceneManager.sceneCount;
        var completion = connection.DisconnectAsync(ConnectFailReason.UserRequest);
        Assert.That(completion.IsCompleted, Is.True);
        Assert.That(completion.IsFaulted, Is.False);
        Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount));
    }
    [Test]
    public void EstablishedClientReportsRemoteEndOnlyOnce()
    {
        var session = new MenuSessionEndState();
        session.MarkConnected(true);
        Assert.That(session.TryEndRemotely(), Is.True);
        Assert.That(session.TryEndRemotely(), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LocalExitSuppressesRoomClosed(bool connected)
    {
        var session = new MenuSessionEndState();
        if (connected) session.MarkConnected(true);
        session.MarkLocalExit();
        Assert.That(session.TryEndRemotely(), Is.False);
    }

    [Test]
    public void HostAndFailedJoinNeverShowRoomClosed()
    {
        var host = new MenuSessionEndState();
        host.MarkConnected(false);
        Assert.That(host.TryEndRemotely(), Is.False);
        Assert.That(new MenuSessionEndState().TryEndRemotely(), Is.False);
    }

    [Test]
    public void LateConnectCompletionCannotReviveEndedSession()
    {
        var session = new MenuSessionEndState();
        Assert.That(session.TryEndRemotely(), Is.False);
        session.MarkConnected(true);
        Assert.That(session.TryEndRemotely(), Is.False);
    }

    [Test]
    public void OldSessionCallbackCannotConsumeNewSessionNotice()
    {
        var oldSession = new MenuSessionEndState();
        oldSession.MarkConnected(true);
        oldSession.MarkLocalExit();
        var nextSession = new MenuSessionEndState();
        nextSession.MarkConnected(true);
        Assert.That(oldSession.TryEndRemotely(), Is.False);
        Assert.That(nextSession.TryEndRemotely(), Is.True);
    }
}
