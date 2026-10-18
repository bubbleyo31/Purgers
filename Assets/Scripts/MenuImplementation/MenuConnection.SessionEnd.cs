using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Menu;
using Purgers.Progression;
using Purgers.GameFlow.Transition;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiClimb.Menu
{
    public partial class MenuConnection
    {
        public event Action RoomClosed;
        private MenuSessionEndState _sessionEnd;
        private NetworkDelegates _sessionCallbacks;
        private Task _sessionCleanup = Task.CompletedTask;
        private bool _roomClosedAwaitingAcknowledgement;
        private bool _multiplePeerSession;
        private readonly HashSet<int> _scenesBeforeSession = new HashSet<int>();

        public void AcknowledgeRoomClosed() => _roomClosedAwaitingAcknowledgement = false;
        public void SuppressSessionEndNotice() => _sessionEnd?.MarkLocalExit();

        private void ObserveSession(NetworkRunner runner)
        {
            _sessionEnd = new MenuSessionEndState();
            _multiplePeerSession = runner.Config?.PeerMode == NetworkProjectConfig.PeerModes.Multiple;
            _scenesBeforeSession.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                _scenesBeforeSession.Add(SceneManager.GetSceneAt(i).handle);

            var session = _sessionEnd;
            _sessionCallbacks = new NetworkDelegates
            {
                OnShutdown = (source, reason) => OnRemoteSessionEnded(source, session),
                OnDisconnectedFromServer = (source, reason) => OnRemoteSessionEnded(source, session)
            };
            runner.AddCallbacks(_sessionCallbacks);
        }

        private void OnRemoteSessionEnded(NetworkRunner runner, MenuSessionEndState session)
        {
            if (!ReferenceEquals(_runner, runner) || !session.TryEndRemotely())
                return;

            _roomClosedAwaitingAcknowledgement = true;
            _sessionCleanup = EndSessionAsync(runner, ConnectFailReason.Disconnect, true);
        }

        private async Task EndSessionAsync(NetworkRunner runner, int reason, bool showRoomClosed)
        {
            // 所有清理都固定在本次 Runner／場景快照；不得在 await 後讀下一次 Session 的資料。
            var callbacks = _sessionCallbacks;
            bool multiplePeer = _multiplePeerSession;
            var previousScenes = new HashSet<int>(_scenesBeforeSession);
            var deadline = Task.Delay(5000);
            // 離開 Fusion callback stack 後才 Shutdown，避免重入或等待自己。
            await Task.Yield();
            try
            {
                GameSaveRuntimeContext saveContext = null;
                if (runner) runner.TryGetComponent(out saveContext);
                if (saveContext) saveContext.Clear();
                if (runner && runner.TryGetComponent(out LocalSceneTransition transition)) transition.Cancel();
                if (runner)
                {
                    // Lobby／StartGame 期間 IsRunning 仍可能為 false，也必須關閉。
                    var shutdown = ShutdownRunnerAsync(runner, reason);
                    MenuConnectionAttempt.ObserveFault(shutdown);
                    if (await Task.WhenAny(shutdown, deadline) == shutdown) await shutdown;
                    else Debug.LogWarning("[MenuConnection] 清理超過 5 秒，隔離並銷毀本次 Runner。");
                }

                // Multiple Peer 的隔離場景由 Fusion scene manager 自行卸載。
                // Single Peer 只清理本次 Session 載入的場景，保留進房前的選單／工具場景。
                if (!multiplePeer)
                {
                    var scenes = new List<Scene>();
                    for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isLoaded && !previousScenes.Contains(scene.handle))
                            scenes.Add(scene);
                    }
                    foreach (var scene in scenes)
                    {
                        if (!scene.IsValid() || !scene.isLoaded) continue;
                        var operation = SceneManager.UnloadSceneAsync(scene);
                        while (operation != null && !operation.isDone && !deadline.IsCompleted)
                            await Task.Yield();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (runner)
                {
                    if (callbacks != null) runner.RemoveCallbacks(callbacks);
                    if (runner.TryGetComponent(out LocalSceneTransition transition)) transition.Cancel();
                    if (runner.TryGetComponent(out NetworkSceneManagerDefault manager) && manager.Runner != null)
                        manager.Shutdown();
                    runner.gameObject.SetActive(false);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(runner.gameObject);
                    else UnityEngine.Object.DestroyImmediate(runner.gameObject);
                }
                if (ReferenceEquals(_runner, runner))
                {
                    _runner = null;
                    _sessionCallbacks = null;
                    _selectedHostSave = null;
                    Usernames = null;
                }
                if (showRoomClosed) RoomClosed?.Invoke();
            }
        }

        protected virtual Task ShutdownRunnerAsync(NetworkRunner runner, int reason) =>
            runner.Shutdown(shutdownReason: ResolveShutdownReason(reason), forceShutdownProcedure: true);
    }
}
