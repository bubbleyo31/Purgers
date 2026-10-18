using Fusion;
using Fusion.Menu;
using Fusion.Photon.Realtime;
using Purgers.Progression;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiClimb.Menu
{
    public partial class MenuConnection : IFusionMenuConnection
    {
        public MenuConnection(
            IFusionMenuConfig config,
            NetworkRunner runnerPrefab,
            IGameSaveRepository saveRepository,
            int newSaveCycleLength = GameSaveSchema.DefaultCycleLength,
            float connectionTimeoutSeconds = 30f)
        {
            _config = config;
            _runnerPrefab = runnerPrefab;
            _saveRepository = saveRepository ??
                throw new ArgumentNullException(nameof(saveRepository));
            _newSaveCycleLength = Math.Max(1, newSaveCycleLength);
            _connectionTimeoutSeconds = Math.Max(1f, connectionTimeoutSeconds);
        }

        public string SessionName { get; private set; }
        public int MaxPlayerCount { get; private set; }
        public string Region { get; private set; }
        public string AppVersion { get; private set; }
        public List<string> Usernames { get; private set; }
        public bool IsConnected => _runner && _runner.IsRunning;
        public NetworkRunner Runner => _runner;
        public int Ping => (int)(IsConnected ? _runner.GetPlayerRtt(_runner.LocalPlayer) * 1000 : 0);

        private NetworkRunner _runnerPrefab;
        private NetworkRunner _runner;
        private IFusionMenuConfig _config;
        private readonly IGameSaveRepository _saveRepository;
        private readonly int _newSaveCycleLength;
        private GameSaveData _selectedHostSave;
        private bool _connectingSafeCheck;
        private readonly float _connectionTimeoutSeconds;
        private MenuConnectionAttempt _attempt;
        private TaskCompletionSource<bool> _connectFinished;
        public event Action<string> ConnectionAttemptFailed;

        public async Task<ConnectResult> ConnectAsync(IFusionMenuConnectArgs connectArgs)
        {
            if (_connectingSafeCheck || IsConnected || !_sessionCleanup.IsCompleted || _roomClosedAwaitingAcknowledgement)
            {
                return CreateFailedResult(
                    "目前已有連線流程正在執行。",
                    ConnectFailReason.None,
                    true);
            }

            _connectingSafeCheck = true;
            _connectFinished = new TaskCompletionSource<bool>();
            var attempt = _attempt = new MenuConnectionAttempt(TimeSpan.FromSeconds(_connectionTimeoutSeconds));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            string newlyCreatedSaveId = null;
            NetworkRunner runner = null;
            ConnectResult result = null;

            try
            {
                if (!TryResolveLaunchScene(connectArgs, out string sceneError))
                    return result = CreateFailedResult(sceneError);

                // Client 只信任代碼；不使用本機 PreferredRegion 或 Best 快取。
                bool joining = !connectArgs.Creating && !string.IsNullOrEmpty(connectArgs.Session);
                connectArgs.Region = joining
                    ? MenuRegionRouting.Decode(_config, connectArgs.Session.Trim().ToUpperInvariant())
                    : MenuRegionRouting.Normalize(connectArgs.Region);
                if (joining) connectArgs.Session = connectArgs.Session.Trim().ToUpperInvariant();

                if (!TryPrepareSaveContext(
                        connectArgs,
                        out bool forceHost,
                        out GameSaveAccessMode accessMode,
                        out GameSaveData launchSave,
                        out newlyCreatedSaveId,
                        out string saveError))
                {
                    return result = CreateFailedResult(saveError);
                }

                runner = _runner = CreateRunner();
                ObserveSession(runner);
                var session = _sessionEnd;
                GameSaveRuntimeContext saveContext =
                    GetOrAddSaveContext(runner);

                if (accessMode == GameSaveAccessMode.HostWritable)
                    saveContext.InitializeHost(_saveRepository, launchSave);
                else
                    saveContext.InitializeClientReadOnly();

                var sceneManager =
                    runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
                sceneManager.IsSceneTakeOverEnabled = false;

                FusionAppSettings appSettings = CopyAppSettings(connectArgs);
                GameMode gameMode = ResolveGameMode(connectArgs, forceHost);

                if (!joining)
                {
                    if (string.IsNullOrEmpty(connectArgs.Region))
                    {
                        LogConnectStage("選擇區域", elapsed.Elapsed.TotalSeconds);
                        // 在同一 Runner 先連上 Lobby；沿用 Photon 的測速／快取與後續連線。
                        var lobby = await attempt.WaitAsync(JoinLobbyAsync(runner, gameMode, appSettings, attempt.Token));
                        if (!lobby.Ok) throw new InvalidOperationException("無法取得伺服器區域：" + lobby.ShutdownReason);
                        connectArgs.Region = MenuRegionRouting.Normalize(GetLobbyRegion(runner));
                        appSettings.FixedRegion = Region = connectArgs.Region;
                    }
                    connectArgs.Session = MenuRegionRouting.Encode(_config, _config.CodeGenerator.Create(), connectArgs.Region);
                }
                attempt.Token.ThrowIfCancellationRequested();

                var args = new StartGameArgs
                {
                    CustomPhotonAppSettings = appSettings,
                    GameMode = gameMode,
                    SessionName = SessionName = connectArgs.Session,
                    PlayerCount = MaxPlayerCount = connectArgs.MaxPlayerCount
                };

                var sceneInfo = new NetworkSceneInfo();
                sceneInfo.AddSceneRef(
                    sceneManager.GetSceneRef(connectArgs.Scene.ScenePath),
                    LoadSceneMode.Additive);
                args.Scene = sceneInfo;

                args.StartGameCancellationToken = attempt.Token;

                LogConnectStage("建立／加入房間 region=" + connectArgs.Region, elapsed.Elapsed.TotalSeconds);
                StartGameResult startGameResult =
                    await attempt.WaitAsync(StartRunnerAsync(runner, args));
                result = new ConnectResult
                {
                    Success = startGameResult.Ok && !session.HasEnded && ReferenceEquals(_runner, runner),
                    FailReason = ResolveConnectFailReason(
                        startGameResult.ShutdownReason),
                    DebugMessage = startGameResult.Ok
                        ? "連線已結束，請重新連線。"
                        : $"Fusion 啟動失敗：{startGameResult.ShutdownReason}"
                };

                if (result.Success)
                {
                    session.MarkConnected(runner.IsClient);
                    _multiplePeerSession = runner.Config.PeerMode == NetworkProjectConfig.PeerModes.Multiple;
                    SessionName = runner.SessionInfo.Name;
                    Region = MenuRegionRouting.Normalize(runner.SessionInfo.Region);
                    _selectedHostSave = null;
                    result.DebugMessage = string.Empty;
                    LogConnectStage("完成 region=" + Region, elapsed.Elapsed.TotalSeconds);
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                return result = CreateFailedResult(attempt.CancelledByUser ? string.Empty :
                    $"連線逾時（{_connectionTimeoutSeconds:0} 秒），請檢查網路或指定伺服器區域後重試。",
                    attempt.CancelledByUser ? ConnectFailReason.UserRequest : ConnectFailReason.Disconnect);
            }
            catch (Exception exception)
            {
                // 網路／代碼錯誤交給既有 Popup；避免正常取消變成 Console 例外。
                return result = CreateFailedResult(
                    $"連線流程發生例外：{exception.Message}");
            }
            finally
            {
                if (result == null || !result.Success)
                {
                    _sessionEnd?.MarkLocalExit();
                    attempt.CancelByUser(); // 停止仍在執行的底層工作；保留上方已判定的失敗原因。
                    if (runner)
                    {
                        if (_sessionCleanup.IsCompleted)
                            _sessionCleanup = EndSessionAsync(runner, ConnectFailReason.UserRequest, false);
                        await _sessionCleanup;
                    }
                    RollbackSaveLaunch(newlyCreatedSaveId);
                    SessionName = Region = null;
                    LogConnectStage("已清理：" + (result?.DebugMessage ?? "取消"), elapsed.Elapsed.TotalSeconds);
                }
                attempt.Dispose();
                _attempt = null;
                _connectingSafeCheck = false;
                _connectFinished.TrySetResult(true);
                if (result != null && !result.Success && ConnectionAttemptFailed != null)
                {
                    result.CustomResultHandling = true;
                    ConnectionAttemptFailed.Invoke(result.DebugMessage);
                }
            }
        }

        public async Task DisconnectAsync(int reason)
        {
            _sessionEnd?.MarkLocalExit();
            if (_attempt != null)
            {
                _attempt.CancelByUser();
                // ConnectAsync 負責本次 Runner 與新存檔清理；不可互相等待 Disconnect。
                await _connectFinished.Task;
                return;
            }
            if (!_sessionCleanup.IsCompleted)
            {
                await _sessionCleanup;
                return;
            }

            var runner = _runner;
            if (!runner)
            {
                _selectedHostSave = null;
                return;
            }
            _sessionCleanup = EndSessionAsync(runner, reason, false);
            await _sessionCleanup;
        }

        public Task<List<FusionMenuOnlineRegion>> RequestAvailableOnlineRegionsAsync(IFusionMenuConnectArgs connectArgs)
        {
            // 保留 SDK Party 的固定索引。真正 Best 選區在可取消的 ConnectAsync 內。
            return Task.FromResult(MenuRegionRouting.CreateCatalog(_config));
        }

        protected virtual Task<StartGameResult> JoinLobbyAsync(NetworkRunner runner, GameMode mode,
            FusionAppSettings settings, CancellationToken token) => runner.JoinSessionLobby(
                mode == GameMode.Shared ? SessionLobby.Shared : SessionLobby.ClientServer,
                customAppSettings: settings, cancellationToken: token, useCachedRegions: true);

        protected virtual string GetLobbyRegion(NetworkRunner runner) => runner.LobbyInfo.Region;
        protected virtual Task<StartGameResult> StartRunnerAsync(NetworkRunner runner, StartGameArgs args) => runner.StartGame(args);

        private static void LogConnectStage(string stage, double seconds) =>
            Debug.Log($"[MenuConnection] {stage} elapsed={seconds:0.00}s");

        public void SetSessionUsernames(List<string> usernames)
        {
            Usernames = usernames;
        }

        public void SelectHostSave(GameSaveData save)
        {
            _selectedHostSave = save ??
                throw new ArgumentNullException(nameof(save));
        }

        public void ClearSelectedHostSave()
        {
            _selectedHostSave = null;
        }

        private bool TryResolveLaunchScene(
            IFusionMenuConnectArgs connectArgs,
            out string error)
        {
            if (!MenuSceneLaunchPolicy.TryResolve(
                    connectArgs.Scene,
                    _config.AvailableScenes,
                    out PhotonMenuSceneInfo resolvedScene))
            {
                error = "Fusion Menu 尚未設定可進入的場景。";
                return false;
            }

            connectArgs.Scene = resolvedScene;
            error = string.Empty;
            return true;
        }

        private GameMode ResolveGameMode(
            IFusionMenuConnectArgs args,
            bool forceHost)
        {
            bool isSharedSession = args.Scene.SceneName.Contains("Shared");
            if (forceHost)
                return isSharedSession ? GameMode.Shared : GameMode.Host;

            if (args.Creating)
            {
                // Create session
                return isSharedSession ? GameMode.Shared : GameMode.Host;
            }

            if (string.IsNullOrEmpty(args.Session))
            {
                // QuickJoin
                return isSharedSession ? GameMode.Shared : GameMode.AutoHostOrClient;
            }

            // Join session
            return isSharedSession ? GameMode.Shared : GameMode.Client;
        }

        private bool TryPrepareSaveContext(
            IFusionMenuConnectArgs connectArgs,
            out bool forceHost,
            out GameSaveAccessMode accessMode,
            out GameSaveData launchSave,
            out string newlyCreatedSaveId,
            out string error)
        {
            forceHost = false;
            accessMode = GameSaveAccessMode.None;
            launchSave = null;
            newlyCreatedSaveId = null;
            error = string.Empty;

            MenuSaveLaunchKind launchKind = MenuSaveLaunchPolicy.Resolve(
                connectArgs.Creating,
                connectArgs.Session,
                _selectedHostSave != null);

            if (launchKind == MenuSaveLaunchKind.ClientJoin)
            {
                accessMode = GameSaveAccessMode.ClientReadOnly;
                _selectedHostSave = null;
                Debug.Log(
                    "[MenuConnection] Prepared ClientJoin with read-only " +
                    "save context.");
                return true;
            }

            forceHost = true;
            if (launchKind == MenuSaveLaunchKind.ContinueHost)
            {
                accessMode = GameSaveAccessMode.HostWritable;
                launchSave = _selectedHostSave;
                Debug.Log(
                    "[MenuConnection] Prepared ContinueHost for save " +
                    $"'{launchSave.SaveId}'.");
                return true;
            }

            GameSaveRepositoryResult<GameSaveData> createResult =
                _saveRepository.CreateNew(
                    string.Empty,
                    _newSaveCycleLength);

            if (!createResult.Success)
            {
                _selectedHostSave = null;
                error = $"建立新存檔失敗：{createResult.Error}";
                return false;
            }

            newlyCreatedSaveId = createResult.Value.SaveId;
            accessMode = GameSaveAccessMode.HostWritable;
            launchSave = createResult.Value;
            Debug.Log(
                $"[MenuConnection] Prepared {launchKind} with new save " +
                $"'{launchSave.SaveId}' in '{_saveRepository.RootDirectory}'.");
            return true;
        }

        private void RollbackSaveLaunch(string newlyCreatedSaveId)
        {
            if (!string.IsNullOrEmpty(newlyCreatedSaveId))
            {
                GameSaveRepositoryResult<bool> deleteResult =
                    _saveRepository.Delete(newlyCreatedSaveId);

                if (!deleteResult.Success)
                {
                    Debug.LogError(
                        "[MenuConnection] 新 Host Session 建立失敗，" +
                        $"且無法刪除未啟用存檔：{deleteResult.Error}");
                }
            }

            if (_runner && _runner.TryGetComponent(
                    out GameSaveRuntimeContext saveContext))
            {
                saveContext.Clear();
            }

            _selectedHostSave = null;
        }

        private static ConnectResult CreateFailedResult(
            string message,
            int failReason = ConnectFailReason.Disconnect,
            bool customHandling = false)
        {
            return new ConnectResult
            {
                Success = false,
                FailReason = failReason,
                DebugMessage = message,
                CustomResultHandling = customHandling
            };
        }

        private ShutdownReason ResolveShutdownReason(int reason)
        {
            switch (reason)
            {
                case ConnectFailReason.UserRequest:
                    return ShutdownReason.Ok;
                case ConnectFailReason.ApplicationQuit:
                    return ShutdownReason.Ok;
                case ConnectFailReason.Disconnect:
                    return ShutdownReason.DisconnectedByPluginLogic;
                default:
                    return ShutdownReason.Error;
            }
        }

        private int ResolveConnectFailReason(ShutdownReason reason)
        {
            switch (reason)
            {
                case ShutdownReason.Ok:
                case ShutdownReason.OperationCanceled:
                    return ConnectFailReason.UserRequest;
                case ShutdownReason.DisconnectedByPluginLogic:
                case ShutdownReason.Error:
                    return ConnectFailReason.Disconnect;
                default:
                    return ConnectFailReason.None;
            }
        }

        protected virtual NetworkRunner CreateRunner()
        {
            if (_runnerPrefab)
                return UnityEngine.Object.Instantiate(_runnerPrefab);

            var runnerObject = new GameObject("NetworkRunner");
            var runner = runnerObject.AddComponent<NetworkRunner>();
            runnerObject.AddComponent<InputManager>();
            return runner;
        }

        private static GameSaveRuntimeContext GetOrAddSaveContext(
            NetworkRunner runner)
        {
            if (runner.TryGetComponent(
                    out GameSaveRuntimeContext existingContext))
            {
                return existingContext;
            }

            return runner.gameObject.AddComponent<GameSaveRuntimeContext>();
        }

        private FusionAppSettings CopyAppSettings(IFusionMenuConnectArgs connectArgs)
        {
            FusionAppSettings appSettings = new FusionAppSettings();
            PhotonAppSettings.Global.AppSettings.CopyTo(appSettings);
            appSettings.FixedRegion = Region = connectArgs.Region;
            appSettings.AppVersion = AppVersion = connectArgs.AppVersion;
            return appSettings;
        }
    }
}
