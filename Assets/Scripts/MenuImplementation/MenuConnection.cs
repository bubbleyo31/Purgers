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
    public class MenuConnection : IFusionMenuConnection
    {
        public MenuConnection(
            IFusionMenuConfig config,
            NetworkRunner runnerPrefab,
            IGameSaveRepository saveRepository)
        {
            _config = config;
            _runnerPrefab = runnerPrefab;
            _saveRepository = saveRepository ??
                throw new ArgumentNullException(nameof(saveRepository));
        }

        public string SessionName { get; private set; }
        public int MaxPlayerCount { get; private set; }
        public string Region { get; private set; }
        public string AppVersion { get; private set; }
        public List<string> Usernames { get; private set; }
        public bool IsConnected => _runner && _runner.IsRunning;
        public int Ping => (int)(IsConnected ? _runner.GetPlayerRtt(_runner.LocalPlayer) * 1000 : 0);

        private NetworkRunner _runnerPrefab;
        private NetworkRunner _runner;
        private IFusionMenuConfig _config;
        private readonly IGameSaveRepository _saveRepository;
        private GameSaveData _selectedHostSave;
        private bool _connectingSafeCheck;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;

        public async Task<ConnectResult> ConnectAsync(IFusionMenuConnectArgs connectArgs)
        {
            if (_connectingSafeCheck)
            {
                return CreateFailedResult(
                    "目前已有連線流程正在執行。",
                    ConnectFailReason.None,
                    true);
            }

            _connectingSafeCheck = true;
            string newlyCreatedSaveId = null;

            try
            {
                if (!TryResolveLaunchScene(connectArgs, out string sceneError))
                    return CreateFailedResult(sceneError);

                if (!TryPrepareSaveContext(
                        connectArgs,
                        out bool forceHost,
                        out GameSaveAccessMode accessMode,
                        out GameSaveData launchSave,
                        out newlyCreatedSaveId,
                        out string saveError))
                {
                    return CreateFailedResult(saveError);
                }

                if (_runner && _runner.IsRunning)
                    await _runner.Shutdown();

                _runner = CreateRunner();
                GameSaveRuntimeContext saveContext =
                    GetOrAddSaveContext(_runner);

                if (accessMode == GameSaveAccessMode.HostWritable)
                    saveContext.InitializeHost(_saveRepository, launchSave);
                else
                    saveContext.InitializeClientReadOnly();

                var sceneManager =
                    _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
                sceneManager.IsSceneTakeOverEnabled = false;

                FusionAppSettings appSettings = CopyAppSettings(connectArgs);

                var args = new StartGameArgs
                {
                    CustomPhotonAppSettings = appSettings,
                    GameMode = ResolveGameMode(connectArgs, forceHost),
                    SessionName = SessionName = connectArgs.Session,
                    PlayerCount = MaxPlayerCount = connectArgs.MaxPlayerCount
                };

                var sceneInfo = new NetworkSceneInfo();
                sceneInfo.AddSceneRef(
                    sceneManager.GetSceneRef(connectArgs.Scene.ScenePath),
                    LoadSceneMode.Additive);
                args.Scene = sceneInfo;

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                _cancellationToken = _cancellationTokenSource.Token;
                args.StartGameCancellationToken = _cancellationToken;

                int regionIndex =
                    _config.AvailableRegions.IndexOf(connectArgs.Region);
                args.SessionNameGenerator = () =>
                    _config.CodeGenerator.EncodeRegion(
                        _config.CodeGenerator.Create(),
                        regionIndex);

                StartGameResult startGameResult =
                    await _runner.StartGame(args);
                var connectResult = new ConnectResult
                {
                    Success = startGameResult.Ok,
                    FailReason = ResolveConnectFailReason(
                        startGameResult.ShutdownReason),
                    DebugMessage = startGameResult.Ok
                        ? string.Empty
                        : $"Fusion 啟動失敗：{startGameResult.ShutdownReason}"
                };

                if (connectResult.Success)
                {
                    SessionName = _runner.SessionInfo.Name;
                    _selectedHostSave = null;
                }
                else
                {
                    RollbackSaveLaunch(newlyCreatedSaveId);
                }

                return connectResult;
            }
            catch (Exception exception)
            {
                RollbackSaveLaunch(newlyCreatedSaveId);
                Debug.LogException(exception);
                return CreateFailedResult(
                    $"連線流程發生例外：{exception.Message}");
            }
            finally
            {
                _connectingSafeCheck = false;
            }
        }

        public async Task DisconnectAsync(int reason)
        {
            if (!_runner)
            {
                _selectedHostSave = null;
                return;
            }

            var peerMode = _runner.Config?.PeerMode;
            _cancellationTokenSource?.Cancel();

            if (_runner.IsRunning)
            {
                await _runner.Shutdown(
                    shutdownReason: ResolveShutdownReason(reason));
            }

            if (_runner.TryGetComponent(
                    out GameSaveRuntimeContext saveContext))
            {
                saveContext.Clear();
            }

            _selectedHostSave = null;

            if (peerMode is NetworkProjectConfig.PeerModes.Multiple) return;

            for (int i = SceneManager.sceneCount - 1; i > 0; i--)
            {
                SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
            }
        }

        public Task<List<FusionMenuOnlineRegion>> RequestAvailableOnlineRegionsAsync(IFusionMenuConnectArgs connectArgs)
        {
            // Force best region
            return Task.FromResult(new List<FusionMenuOnlineRegion>() { new FusionMenuOnlineRegion() { Code = string.Empty, Ping = 0 } });
        }

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
                _saveRepository.CreateNew(string.Empty);

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

        private NetworkRunner CreateRunner()
        {
            if (_runnerPrefab)
                return UnityEngine.Object.Instantiate(_runnerPrefab);

            var runnerObject = new GameObject("NetworkRunner");
            return runnerObject.AddComponent<NetworkRunner>();
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
