using System.Collections.Generic;
using Fusion;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Purgers.GameFlow.SafeHouse
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SafeHouseFlowController :
        NetworkBehaviour,
        IPlayerJoined,
        IPlayerLeft
    {
        [Header("開始遊戲終端機")]
        [Tooltip(
            "Host 必須靠近這個位置才能按 E 發起 Ready Check。" +
            "State Authority 會再次驗證距離，Client 無法只靠 RPC 繞過。")]
        [SerializeField] private Transform startTerminalAnchor;

        [Tooltip("允許 Host 發起 Ready Check 的水平與垂直直線距離。")]
        [SerializeField, Min(0.5f)] private float interactionDistance = 3.5f;

        [Header("關卡場景")]
        [Tooltip(
            "全員準備後由 Scene Authority 載入的 Build Settings 場景名稱。" +
            "Phase 2 預設為 Game。")]
        [SerializeField] private string gameplaySceneName = "Game";

        [Tooltip("全員準備後，Host 等待多久才切入關卡。")]
        [SerializeField, Min(0.1f)] private float countdownDurationSeconds = 3f;

        [Tooltip("Host 開啟投票後，超過這段時間仍未進場就自動取消。")]
        [SerializeField, Min(0.1f)] private float readyCheckTimeoutSeconds = 7f;

        [Header("除錯資訊")]
        [Tooltip("輸出 Ready Check 開啟、玩家確認與場景載入紀錄。")]
        [SerializeField] private bool debugFlow = true;

        [Networked]
        public SafeHousePhase Phase { get; private set; }

        [Networked]
        public int StageLevel { get; private set; }

        [Networked]
        public int CycleLength { get; private set; }

        [Networked]
        public int CycleStage { get; private set; }

        [Networked]
        public NetworkBool IsBossStage { get; private set; }

        [Networked, Capacity(12)]
        private NetworkDictionary<PlayerRef, NetworkBool>
            ReadyByPlayer => default;

        [Networked]
        private TickTimer StageStartCountdown { get; set; }

        [Networked]
        private TickTimer ReadyCheckTimeout { get; set; }

        private readonly List<PlayerRef> connectedPlayers =
            new List<PlayerRef>(12);

        private readonly List<PlayerRef> disconnectedPlayers =
            new List<PlayerRef>(12);

        public bool IsNetworkReady =>
            Object != null &&
            Object.IsValid &&
            Runner != null &&
            Runner.IsRunning;

        public bool IsLocalHost =>
            IsNetworkReady &&
            Runner.IsServer;

        public int ConnectedPlayerCount
        {
            get
            {
                if (!IsNetworkReady)
                    return 0;

                CollectConnectedPlayers();
                return connectedPlayers.Count;
            }
        }

        public int ReadyPlayerCount
        {
            get
            {
                if (!IsNetworkReady)
                    return 0;

                CollectConnectedPlayers();
                return CountConnectedReadyPlayers();
            }
        }

        public int UnreadyPlayerCount =>
            Mathf.Max(0, ConnectedPlayerCount - ReadyPlayerCount);

        public float RemainingCountdownSeconds =>
            IsNetworkReady && StageStartCountdown.IsRunning
                ? Mathf.Max(
                    0f,
                    StageStartCountdown.RemainingTime(Runner) ?? 0f)
                : 0f;

        public float RemainingReadyCheckSeconds =>
            IsNetworkReady && ReadyCheckTimeout.IsRunning
                ? Mathf.Max(
                    0f,
                    ReadyCheckTimeout.RemainingTime(Runner) ?? 0f)
                : 0f;

        public bool IsLocalPlayerReady
        {
            get
            {
                return IsNetworkReady &&
                       ReadyByPlayer.TryGet(
                           Runner.LocalPlayer,
                           out NetworkBool ready) &&
                       ready;
            }
        }

        public override void Spawned()
        {
            if (!Object.HasStateAuthority)
                return;

            Phase = SafeHousePhase.WaitingForHost;
            Purgers.GameFlow.Stage.StageRuntimePlan runtimePlan =
                Purgers.GameFlow.Stage.StageRules.ResolveRuntimePlan(
                    ResolveHostStageLevel(),
                    ResolveCycleLength());
            StageLevel = runtimePlan.StageLevel;
            CycleLength = runtimePlan.CycleLength;
            CycleStage = runtimePlan.CycleStage;
            IsBossStage = runtimePlan.IsBossStage;
            StageStartCountdown = TickTimer.None;
            ReadyCheckTimeout = TickTimer.None;
            ReconcileReadyPlayers();

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] SafeHouse ready. StageLevel={StageLevel}, " +
                    $"CycleStage={CycleStage}/{CycleLength}, Boss={IsBossStage}",
                    this);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
                return;

            ReconcileReadyPlayers();

            if (Phase != SafeHousePhase.ReadyCheck &&
                Phase != SafeHousePhase.Countdown)
            {
                return;
            }

            if (SafeHouseReadyRules.ShouldCancelExpiredReadyCheck(
                    Phase,
                    ReadyCheckTimeout.Expired(Runner)))
            {
                CancelReadyCheck("投票逾時");
                return;
            }

            int connectedCount = connectedPlayers.Count;
            int readyCount = CountConnectedReadyPlayers();

            SafeHouseReadyCountdownDecision decision =
                SafeHouseReadyRules.EvaluateCountdown(
                    Phase,
                    connectedCount,
                    readyCount,
                    StageStartCountdown.Expired(Runner));

            switch (decision)
            {
                case SafeHouseReadyCountdownDecision.Start:
                    StartCountdown();
                    break;
                case SafeHouseReadyCountdownDecision.Cancel:
                    CancelCountdown();
                    break;
                case SafeHouseReadyCountdownDecision.Load:
                    BeginStageLoad();
                    break;
            }
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (!Object.HasStateAuthority)
                return;

            ReadyByPlayer.Set(player, false);

            if (Phase == SafeHousePhase.Countdown)
                CancelCountdown();

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Player joined as unready: {player}",
                    this);
            }
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (!Object.HasStateAuthority)
                return;

            ReadyByPlayer.Remove(player);

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Removed disconnected player: {player}",
                    this);
            }
        }

        public bool CanLocalHostOpenReadyCheck()
        {
            if (Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked)
                return false;
            if (!IsNetworkReady)
                return false;

            bool inRange = TryGetLocalPlayerTransform(
                out Transform playerTransform) &&
                IsWithinInteractionRange(playerTransform.position);

            return SafeHouseReadyRules.CanOpenReadyCheck(
                Phase,
                IsLocalHost,
                inRange);
        }

        public bool TryGetLocalPlayerTransform(out Transform playerTransform)
        {
            playerTransform = null;

            if (Runner == null ||
                !Runner.IsRunning ||
                !Runner.TryGetPlayerObject(
                    Runner.LocalPlayer,
                    out NetworkObject playerObject) ||
                playerObject == null ||
                !playerObject.IsValid)
            {
                return false;
            }

            playerTransform = playerObject.transform;
            return true;
        }

        public void RequestOpenReadyCheck()
        {
            if (!CanLocalHostOpenReadyCheck())
                return;

            if (Object.HasStateAuthority)
            {
                TryOpenReadyCheck(Runner.LocalPlayer);
                return;
            }

            RPC_RequestOpenReadyCheck();
        }

        public void RequestToggleLocalReady()
        {
            if (Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked)
                return;
            if (!IsNetworkReady ||
                !SafeHouseReadyRules.CanAcceptReady(
                    Phase,
                    IsLocalPlayerReady))
            {
                return;
            }

            if (Object.HasStateAuthority)
            {
                TryTogglePlayerReady(Runner.LocalPlayer);
                return;
            }

            RPC_RequestToggleReady();
        }

        public void RequestCancelLocalReadyCheck()
        {
            if (Purgers.GameFlow.Control.LocalPlayerControl.AllInputBlocked ||
                !IsNetworkReady)
            {
                return;
            }

            if (Object.HasStateAuthority)
            {
                TryCancelReadyCheck(Runner.LocalPlayer);
                return;
            }

            RPC_RequestCancelReadyCheck();
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            Channel = RpcChannel.Reliable,
            TickAligned = false)]
        private void RPC_RequestOpenReadyCheck(
            RpcInfo info = default)
        {
            TryOpenReadyCheck(info.Source);
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            Channel = RpcChannel.Reliable,
            TickAligned = false)]
        private void RPC_RequestToggleReady(
            RpcInfo info = default)
        {
            TryTogglePlayerReady(info.Source);
        }

        [Rpc(
            RpcSources.All,
            RpcTargets.StateAuthority,
            Channel = RpcChannel.Reliable,
            TickAligned = false)]
        private void RPC_RequestCancelReadyCheck(
            RpcInfo info = default)
        {
            TryCancelReadyCheck(info.Source);
        }

        private void TryOpenReadyCheck(PlayerRef requester)
        {
            if (!Object.HasStateAuthority)
                return;

            bool requesterIsHost =
                Runner.IsServer && requester == Runner.LocalPlayer;
            bool requesterInRange =
                TryGetPlayerPosition(requester, out Vector3 position) &&
                IsWithinInteractionRange(position);

            if (!SafeHouseReadyRules.CanOpenReadyCheck(
                    Phase,
                    requesterIsHost,
                    requesterInRange))
            {
                return;
            }

            ReconcileReadyPlayers();

            for (int i = 0; i < connectedPlayers.Count; i++)
                ReadyByPlayer.Set(connectedPlayers[i], false);

            StageStartCountdown = TickTimer.None;
            ReadyCheckTimeout = TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(0.1f, readyCheckTimeoutSeconds));
            Phase = SafeHousePhase.ReadyCheck;

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Ready Check opened by Host {requester}.",
                    this);
            }
        }

        private void TryTogglePlayerReady(PlayerRef requester)
        {
            if (!Object.HasStateAuthority ||
                !IsConnectedPlayer(requester))
            {
                return;
            }

            bool alreadyReady =
                ReadyByPlayer.TryGet(
                    requester,
                    out NetworkBool ready) &&
                ready;

            if (!SafeHouseReadyRules.CanAcceptReady(
                    Phase,
                    alreadyReady))
            {
                return;
            }

            bool nextReady = !alreadyReady;
            ReadyByPlayer.Set(requester, nextReady);

            if (!nextReady && Phase == SafeHousePhase.Countdown)
                CancelCountdown();

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Player ready changed: {requester}={nextReady}",
                    this);
            }
        }

        private void TryCancelReadyCheck(PlayerRef requester)
        {
            if (!Object.HasStateAuthority)
                return;

            bool requesterIsConnected = IsConnectedPlayer(requester);

            if (!SafeHouseReadyRules.CanCancelReadyCheck(
                    Phase,
                    requesterIsConnected))
            {
                return;
            }

            CancelReadyCheck($"玩家 {requester} 取消");
        }

        private void CancelReadyCheck(string reason)
        {
            if (!Object.HasStateAuthority ||
                (Phase != SafeHousePhase.ReadyCheck &&
                 Phase != SafeHousePhase.Countdown))
            {
                return;
            }

            ReconcileReadyPlayers();

            for (int i = 0; i < connectedPlayers.Count; i++)
                ReadyByPlayer.Set(connectedPlayers[i], false);

            StageStartCountdown = TickTimer.None;
            ReadyCheckTimeout = TickTimer.None;
            Phase = SafeHousePhase.WaitingForHost;

            if (debugFlow)
                Debug.Log($"[SafeHouseFlow] Ready vote cancelled: {reason}.", this);
        }

        private void StartCountdown()
        {
            if (!Object.HasStateAuthority || !Runner.IsServer)
                return;

            Phase = SafeHousePhase.Countdown;
            StageStartCountdown = TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(0.1f, countdownDurationSeconds));

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] All players ready. " +
                    $"Starting {countdownDurationSeconds:0.0}s countdown.",
                    this);
            }
        }

        private void CancelCountdown()
        {
            if (!Object.HasStateAuthority || Phase != SafeHousePhase.Countdown)
                return;

            StageStartCountdown = TickTimer.None;
            Phase = SafeHousePhase.ReadyCheck;

            if (debugFlow)
                Debug.Log("[SafeHouseFlow] Ready countdown cancelled.", this);
        }

        private void BeginStageLoad()
        {
            if (Phase == SafeHousePhase.LoadingStage ||
                !Runner.IsSceneAuthority ||
                Runner.IsSceneManagerBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(gameplaySceneName))
            {
                Debug.LogError(
                    "[SafeHouseFlow] Gameplay Scene Name is empty.",
                    this);
                return;
            }

            Phase = SafeHousePhase.LoadingStage;
            StageStartCountdown = TickTimer.None;
            ReadyCheckTimeout = TickTimer.None;

            // Use the existing lifecycle hook for both directions of travel.
            GameLogic gameLogic = FindObjectOfType<GameLogic>();
            if (gameLogic != null)
                gameLogic.PrepareForAuthoritativeSceneTransition();

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] All players ready. Loading '{gameplaySceneName}'.",
                    this);
            }

            Runner.LoadScene(
                gameplaySceneName,
                LoadSceneMode.Single,
                LocalPhysicsMode.None,
                true);
        }

        private int ResolveHostStageLevel()
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();

            if (context != null &&
                context.ActiveSave?.RunProgression != null)
            {
                return Mathf.Max(
                    1,
                    context.ActiveSave.RunProgression.StageLevel);
            }

            return 1;
        }

        private int ResolveCycleLength()
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();

            return context?.ActiveSave != null
                ? Mathf.Max(1, context.ActiveSave.CycleLengthSnapshot)
                : GameSaveSchema.DefaultCycleLength;
        }

        private void ReconcileReadyPlayers()
        {
            CollectConnectedPlayers();

            for (int i = 0; i < connectedPlayers.Count; i++)
            {
                PlayerRef player = connectedPlayers[i];

                if (!ReadyByPlayer.ContainsKey(player))
                    ReadyByPlayer.Add(player, false);
            }

            disconnectedPlayers.Clear();

            foreach (
                KeyValuePair<PlayerRef, NetworkBool> pair
                    in ReadyByPlayer)
            {
                if (!connectedPlayers.Contains(pair.Key))
                    disconnectedPlayers.Add(pair.Key);
            }

            for (int i = 0; i < disconnectedPlayers.Count; i++)
                ReadyByPlayer.Remove(disconnectedPlayers[i]);
        }

        private void CollectConnectedPlayers()
        {
            connectedPlayers.Clear();

            if (Runner == null)
                return;

            foreach (PlayerRef player in Runner.ActivePlayers)
                connectedPlayers.Add(player);
        }

        private int CountConnectedReadyPlayers()
        {
            int count = 0;

            for (int i = 0; i < connectedPlayers.Count; i++)
            {
                if (ReadyByPlayer.TryGet(
                        connectedPlayers[i],
                        out NetworkBool ready) &&
                    ready)
                {
                    count++;
                }
            }

            return count;
        }

        public void CopyConnectedReadyStates(
            List<SafeHousePlayerReadyState> destination)
        {
            if (destination == null)
                throw new System.ArgumentNullException(nameof(destination));

            destination.Clear();

            if (!IsNetworkReady)
                return;

            CollectConnectedPlayers();
            connectedPlayers.Sort(
                (left, right) => left.AsIndex.CompareTo(right.AsIndex));

            for (int i = 0; i < connectedPlayers.Count; i++)
            {
                PlayerRef player = connectedPlayers[i];
                bool isReady = ReadyByPlayer.TryGet(
                    player,
                    out NetworkBool ready) && ready;
                destination.Add(
                    new SafeHousePlayerReadyState(player, isReady));
            }
        }

        private bool IsConnectedPlayer(PlayerRef player)
        {
            CollectConnectedPlayers();
            return connectedPlayers.Contains(player);
        }

        private bool TryGetPlayerPosition(
            PlayerRef player,
            out Vector3 position)
        {
            position = default;

            if (!Runner.TryGetPlayerObject(
                    player,
                    out NetworkObject playerObject) ||
                playerObject == null ||
                !playerObject.IsValid)
            {
                return false;
            }

            position = playerObject.transform.position;
            return true;
        }

        private bool IsWithinInteractionRange(Vector3 playerPosition)
        {
            if (!startTerminalAnchor)
                return false;

            float maximumDistance = Mathf.Max(0.5f, interactionDistance);
            return (playerPosition - startTerminalAnchor.position)
                .sqrMagnitude <= maximumDistance * maximumDistance;
        }
    }
}
