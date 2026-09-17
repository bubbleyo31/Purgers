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
        [Header("Start Terminal")]
        [Tooltip(
            "Host 必須靠近這個位置才能按 E 發起 Ready Check。" +
            "State Authority 會再次驗證距離，Client 無法只靠 RPC 繞過。")]
        [SerializeField] private Transform startTerminalAnchor;

        [Tooltip("允許 Host 發起 Ready Check 的水平與垂直直線距離。")]
        [SerializeField, Min(0.5f)] private float interactionDistance = 3.5f;

        [Header("Stage Scene")]
        [Tooltip(
            "全員準備後由 Scene Authority 載入的 Build Settings 場景名稱。" +
            "Phase 2 預設為 Game。")]
        [SerializeField] private string gameplaySceneName = "Game";

        [Header("Diagnostics")]
        [Tooltip("輸出 Ready Check 開啟、玩家確認與場景載入紀錄。")]
        [SerializeField] private bool debugFlow = true;

        [Networked]
        public SafeHousePhase Phase { get; private set; }

        [Networked]
        public int StageLevel { get; private set; }

        [Networked, Capacity(12)]
        private NetworkDictionary<PlayerRef, NetworkBool>
            ReadyByPlayer => default;

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

                int count = 0;

                foreach (
                    KeyValuePair<PlayerRef, NetworkBool> pair
                        in ReadyByPlayer)
                {
                    if (pair.Value)
                        count++;
                }

                return count;
            }
        }

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
            StageLevel = ResolveHostStageLevel();
            ReconcileReadyPlayers();

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] SafeHouse ready. StageLevel={StageLevel}",
                    this);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
                return;

            ReconcileReadyPlayers();

            if (Phase != SafeHousePhase.ReadyCheck)
                return;

            int connectedCount = connectedPlayers.Count;
            int readyCount = CountConnectedReadyPlayers();

            if (!SafeHouseReadyRules.AreAllPlayersReady(
                    connectedCount,
                    readyCount))
            {
                return;
            }

            BeginStageLoad();
        }

        public void PlayerJoined(PlayerRef player)
        {
            if (!Object.HasStateAuthority)
                return;

            ReadyByPlayer.Set(player, false);

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

        public void RequestLocalReady()
        {
            if (!IsNetworkReady ||
                Phase != SafeHousePhase.ReadyCheck ||
                IsLocalPlayerReady)
            {
                return;
            }

            if (Object.HasStateAuthority)
            {
                TryMarkPlayerReady(Runner.LocalPlayer);
                return;
            }

            RPC_RequestReady();
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
        private void RPC_RequestReady(
            RpcInfo info = default)
        {
            TryMarkPlayerReady(info.Source);
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

            Phase = SafeHousePhase.ReadyCheck;

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Ready Check opened by Host {requester}.",
                    this);
            }
        }

        private void TryMarkPlayerReady(PlayerRef requester)
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

            ReadyByPlayer.Set(requester, true);

            if (debugFlow)
            {
                Debug.Log(
                    $"[SafeHouseFlow] Player ready: {requester}",
                    this);
            }
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
