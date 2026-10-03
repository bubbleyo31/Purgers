using System;
using System.Collections.Generic;
using Fusion;
using Purgers.Progression;
using Purgers.GameFlow.Transition;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace Purgers.GameFlow.Stage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class StageFlowController : NetworkBehaviour, IPlayerLeft
    {
        [Header("地圖")]
        [Tooltip(
            "本場景的入口與地圖抽選器。關卡只會在 Host 完成入口與地圖準備後開始。")]
        [SerializeField] private MapRunSelectionPrototype mapRunSelection;

        [Header("關卡規則")]
        [Tooltip(
            "只有缺少 Host 存檔 Context 時使用的 CycleLength 回退值。" +
            "正式流程讀取新存檔建立時固定的 CycleLengthSnapshot。")]
        [FormerlySerializedAs("cycleLength")]
        [SerializeField, Min(1)] private int fallbackCycleLength =
            GameSaveSchema.DefaultCycleLength;

        [Tooltip("一般關卡的預設時間限制，秒。設為 0 代表不限時。")]
        [SerializeField, Min(0f)] private float defaultTimeLimitSeconds = 600f;

        [Tooltip(
            "Boss 關時間限制，秒。0 代表 Unlimited；第一個 Phase 4-F 切片預設為 0。" +
            "是否採用此值由 StageRuntimePlan 決定，HUD 與 Enemy 不自行判斷。")]
        [SerializeField, Min(0f)] private float bossTimeLimitSeconds;

        [Tooltip("列在此處的 StageLevel 不套用時間限制。")]
        [SerializeField] private int[] unlimitedStageLevels = Array.Empty<int>();

        [Tooltip("所有存活玩家持續留在撤離區多久才完成關卡。")]
        [SerializeField, Min(0.1f)] private float extractionHoldSeconds = 3f;

        [Tooltip(
            "撤離候選與關卡入口投影到地面 NavMesh 時的最大搜尋距離。" +
            "只有 State Authority 會執行完整路徑判定。")]
        [SerializeField, Min(0.01f)]
        private float extractionNavMeshSampleDistance = 8f;

        [SerializeField, Tooltip(
            "Boss 目標的 State Authority owner。只在 StageRuntimePlan 指定 DefeatBoss 時使用。")]
        private BossStageObjectiveController bossObjectiveController;

        [Header("場景轉換")]
        [Tooltip("成功或失敗提交存檔後返回的安全屋場景名稱。")]
        [SerializeField] private string safeHouseSceneName = "SafeHouse";

        [Header("除錯資訊")]
        [Tooltip("輸出關卡開始、撤離、失敗與存檔提交紀錄。")]
        [SerializeField] private bool debugFlow = true;

        [Networked] public StagePhase Phase { get; private set; }
        [Networked] public StageFailureReason FailureReason { get; private set; }
        [Networked] public int StageLevel { get; private set; }
        [Networked] public int CycleLength { get; private set; }
        [Networked] public int CycleStage { get; private set; }
        [Networked] public NetworkBool IsBossStage { get; private set; }
        [Networked] public StageObjectiveKind ObjectiveKind { get; private set; }
        public int SelectedExtractionIndex
        {
            get
            {
                return TryGetComponent(
                           out Purgers.Map.NetworkMapState map)
                    ? map.SelectedExtractionIndex
                    : -1;
            }
        }
        [Networked] public NetworkBool HasTimeLimit { get; private set; }
        [Networked] private TickTimer StageTimer { get; set; }
        [Networked] private TickTimer ExtractionHoldTimer { get; set; }

        [Networked, Capacity(12)]
        private NetworkDictionary<PlayerRef, NetworkId> LoadedPlayers => default;
        private float nextLoadReportTime;

        public int LoadedPlayerCount
        {
            get
            {
                if (!IsNetworkReady) return 0;
                int count = 0;
                foreach (PlayerRef player in Runner.ActivePlayers)
                    if (HasCurrentLoadReport(player)) count++;
                return count;
            }
        }

        private readonly List<StageExtractionPoint> extractionPoints =
            new List<StageExtractionPoint>();

        private bool loggedInitializationFailure;
        private bool extractionSelectionAttempted;
        private int boundExtractionLayoutRevision = -1;

        public bool IsNetworkReady =>
            Object != null &&
            Object.IsValid &&
            Runner != null &&
            Runner.IsRunning;

        public float RemainingStageSeconds =>
            IsNetworkReady && HasTimeLimit
                ? Mathf.Max(0f, StageTimer.RemainingTime(Runner) ?? 0f)
                : 0f;

        public float RemainingExtractionSeconds =>
            IsNetworkReady && ExtractionHoldTimer.IsRunning
                ? Mathf.Max(
                    0f,
                    ExtractionHoldTimer.RemainingTime(Runner) ?? 0f)
                : Mathf.Max(0.1f, extractionHoldSeconds);

        public float ExtractionHoldProgress
        {
            get
            {
                float duration = Mathf.Max(0.1f, extractionHoldSeconds);
                return ExtractionHoldTimer.IsRunning
                    ? Mathf.Clamp01(1f - RemainingExtractionSeconds / duration)
                    : 0f;
            }
        }

        /// <summary>
        /// 集中提供 HUD 與後續任務條件使用的可撤離狀態。
        /// 未來加入任務完成條件時，只需收斂這個入口。
        /// </summary>
        public bool IsExtractionAvailable =>
            IsNetworkReady &&
            Phase == StagePhase.Active &&
            ObjectiveKind == StageObjectiveKind.ReachExtractionAndHold &&
            SelectedExtractionIndex >= 0;

        public bool RequiresExtraction =>
            ObjectiveKind == StageObjectiveKind.ReachExtractionAndHold;

        public override void Spawned()
        {
            RebuildExtractionPointList();
            ApplyExtractionVisuals();

            if (!Object.HasStateAuthority)
                return;

            Phase = StagePhase.Initializing;
            StageRuntimePlan runtimePlan = StageRules.ResolveRuntimePlan(
                ResolveHostStageLevel(),
                ResolveCycleLength());
            StageLevel = runtimePlan.StageLevel;
            CycleLength = runtimePlan.CycleLength;
            CycleStage = runtimePlan.CycleStage;
            IsBossStage = runtimePlan.IsBossStage;
            ObjectiveKind = runtimePlan.ObjectiveKind;
            LoadedPlayers.Clear();
            FailureReason = StageFailureReason.None;
            extractionSelectionAttempted = false;
            StageTimer = TickTimer.None;
            ExtractionHoldTimer = TickTimer.None;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !Runner.IsServer)
                return;

            switch (Phase)
            {
                case StagePhase.Initializing:
                    TryInitializeStage();
                    break;
                case StagePhase.Active:
                    EvaluateActiveStage();
                    break;
            }
        }

        public override void Render()
        {
            if (RequiresExtraction)
                TryBindExtractionPointsToFinalChunk();
            ApplyExtractionVisuals();
            TryReportLocalLoad();
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (Object.HasStateAuthority)
                LoadedPlayers.Remove(player);
        }

        private void TryReportLocalLoad()
        {
            if (!IsNetworkReady || Phase != StagePhase.Initializing ||
                (TryGetComponent(out Purgers.Map.NetworkMapState localMap) && !localMap.IsReady) ||
                Time.unscaledTime < nextLoadReportTime ||
                !Runner.TryGetComponent(out LocalSceneTransition transition) ||
                !transition.IsLocalLoadComplete ||
                !LocalSceneTransition.TryGetReadyLocalPlayer(Runner, out NetworkObject player) ||
                HasCurrentLoadReport(Runner.LocalPlayer))
                return;

            nextLoadReportTime = Time.unscaledTime + 0.5f;
            if (Object.HasStateAuthority)
                AcceptLoadReport(Runner.LocalPlayer, player.Id);
            else
                RPC_ReportLoaded(player.Id);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, Channel = RpcChannel.Reliable,
            TickAligned = false)]
        private void RPC_ReportLoaded(NetworkId playerObjectId, RpcInfo info = default)
        {
            // The sender is derived from Fusion, never a client-provided PlayerRef.
            AcceptLoadReport(info.Source, playerObjectId);
        }

        private void AcceptLoadReport(PlayerRef player, NetworkId playerObjectId)
        {
            if (!Object.HasStateAuthority || !Runner.IsServer ||
                Phase != StagePhase.Initializing || Runner.IsSceneManagerBusy)
                return;
            bool connected = false;
            foreach (PlayerRef active in Runner.ActivePlayers)
                if (active == player) { connected = true; break; }
            if (!connected || !Runner.TryGetPlayerObject(player, out NetworkObject current) ||
                current == null || !current.IsValid || current.Id != playerObjectId)
                return;
            LoadedPlayers.Set(player, playerObjectId);
            if (debugFlow)
                Debug.Log($"[StageFlow] Load acknowledged: {player}, object={playerObjectId}", this);
        }

        private bool HasCurrentLoadReport(PlayerRef player)
        {
            return LoadedPlayers.TryGet(player, out NetworkId id) &&
                Runner.TryGetPlayerObject(player, out NetworkObject current) &&
                current != null && current.IsValid && current.Id == id;
        }

        public bool TryGetSelectedExtraction(
            out StageExtractionPoint extractionPoint)
        {
            if (extractionPoints.Count == 0)
            {
                TryBindExtractionPointsToFinalChunk();
                RebuildExtractionPointList();
            }

            int index = SelectedExtractionIndex;
            if (index < 0 || index >= extractionPoints.Count)
            {
                extractionPoint = null;
                return false;
            }

            extractionPoint = extractionPoints[index];
            return extractionPoint != null;
        }

        public bool IsLocalPlayerInsideExtraction()
        {
            return TryGetSelectedExtraction(out StageExtractionPoint point) &&
                   Runner != null &&
                   Runner.TryGetPlayerObject(
                       Runner.LocalPlayer,
                       out NetworkObject playerObject) &&
                   playerObject != null &&
                   playerObject.IsValid &&
                   point.Contains(playerObject.transform.position);
        }

        private void TryInitializeStage()
        {
            if (!Object.HasStateAuthority ||
                !Runner.IsServer ||
                mapRunSelection == null ||
                !mapRunSelection.IsMapPreparationReady ||
                (TryGetComponent(out Purgers.Map.NetworkMapState map) && !map.IsReady) ||
                Runner.IsSceneManagerBusy || !AreAllConnectedPlayersLoaded())
            {
                return;
            }

            StageRuntimePlan runtimePlan = mapRunSelection.RuntimePlan;
            if (runtimePlan.StageLevel != StageLevel ||
                runtimePlan.CycleLength != CycleLength ||
                runtimePlan.ObjectiveKind != ObjectiveKind)
            {
                LogInitializationFailure(
                    "[StageFlow] 地圖與關卡流程解析到不同的 StageRuntimePlan。");
                return;
            }

            if (runtimePlan.ObjectiveKind == StageObjectiveKind.DefeatBoss)
            {
                if (bossObjectiveController == null)
                    TryGetComponent(out bossObjectiveController);

                if (bossObjectiveController == null ||
                    !map.TryGetFinalChunk(out MapChunk bossChunk) ||
                    !bossObjectiveController.TryPrepare(runtimePlan, bossChunk))
                {
                    LogInitializationFailure(
                        "[StageFlow] Boss 目標、Boss Chunk 或 Boss Prefab 尚未完成設定。");
                    return;
                }

                BeginActiveStage(runtimePlan);

                if (debugFlow)
                {
                    Debug.Log(
                        $"[StageFlow] Boss 關開始。" +
                        $"\nStageLevel: {StageLevel}" +
                        $"\nCycleStage: {CycleStage}/{CycleLength}" +
                        $"\nTimeLimit: {(HasTimeLimit ? RemainingStageSeconds.ToString("F0") + "s" : "Unlimited")}",
                        this);
                }

                return;
            }

            if (!TryBindExtractionPointsToFinalChunk())
                return;

            RebuildExtractionPointList();
            if (extractionSelectionAttempted)
                return;

            extractionSelectionAttempted = true;
            if (extractionPoints.Count == 0)
            {
                if (!loggedInitializationFailure)
                {
                    loggedInitializationFailure = true;
                    Debug.LogError(
                        "[StageFlow] 最後 Chunk 沒有啟用的 StageExtractionPoint，關卡無法開始。",
                        this);
                }

                return;
            }

            if (runtimePlan.ObjectiveKind !=
                    StageObjectiveKind.ReachExtractionAndHold ||
                runtimePlan.Kind != StageRuntimeKind.Ordinary)
            {
                if (!loggedInitializationFailure)
                {
                    loggedInitializationFailure = true;
                    Debug.LogError(
                        "[StageFlow] Boss 或超出目前能力的普通關不可進入一般撤離流程。",
                        this);
                }

                return;
            }

            if (!mapRunSelection.TryGetInitialSpawnPose(
                    Runner,
                    out Pose initialSpawnPose) ||
                mapRunSelection.Alignment == null ||
                mapRunSelection.Alignment.RuntimeNavigation == null)
            {
                if (!loggedInitializationFailure)
                {
                    loggedInitializationFailure = true;
                    Debug.LogError(
                        "[StageFlow] 無法取得 Host 入口或 Runtime Navigation，" +
                        "不能驗證最後 Chunk 撤離點的地面可到達性。",
                        this);
                }

                return;
            }

            var reachableCandidateIndices = new List<int>();
            MapRuntimeNavigationPrototype navigation =
                mapRunSelection.Alignment.RuntimeNavigation;

            for (int index = 0; index < extractionPoints.Count; index++)
            {
                StageExtractionPoint candidate = extractionPoints[index];
                if (candidate != null &&
                    navigation.TryResolveCompleteGroundPath(
                        initialSpawnPose.position,
                        candidate.transform.position,
                        extractionNavMeshSampleDistance,
                        out _))
                {
                    reachableCandidateIndices.Add(index);
                }
            }

            int selectedExtractionIndex =
                StageRules.SelectExtractionCandidateIndex(
                    mapRunSelection.ActiveRunSeed,
                    reachableCandidateIndices);

            if (selectedExtractionIndex < 0)
            {
                if (!loggedInitializationFailure)
                {
                    loggedInitializationFailure = true;
                    Debug.LogError(
                        "[StageFlow] 最後 Chunk 沒有可由關卡入口透過完整地面 NavMesh 路徑抵達的撤離點。",
                        this);
                }

                return;
            }

            if (!map.TryPublishExtractionSelection(
                    selectedExtractionIndex,
                    extractionPoints.Count))
            {
                if (!loggedInitializationFailure)
                {
                    loggedInitializationFailure = true;
                    Debug.LogError(
                        "[StageFlow] NetworkMapState 拒絕發布撤離選擇，關卡無法開始。",
                        this);
                }

                return;
            }

            BeginActiveStage(runtimePlan);

            if (debugFlow)
            {
                string timeLimitLabel = HasTimeLimit
                    ? RemainingStageSeconds.ToString("F0") + "s"
                    : "Unlimited";

                Debug.Log(
                    $"[StageFlow] 關卡開始。" +
                    $"\nStageLevel: {StageLevel}" +
                    $"\nCycleStage: {CycleStage}/{CycleLength}" +
                    $"\nExtraction: {extractionPoints[SelectedExtractionIndex].PointId}" +
                    $"\nFinalChunkIndex: {map.FinalChunkIndex}" +
                    $"\nReachableCandidates: {reachableCandidateIndices.Count}/{extractionPoints.Count}" +
                    $"\nTimeLimit: {timeLimitLabel}",
                    this);
            }
        }

        private void EvaluateActiveStage()
        {
            if (HasTimeLimit && StageTimer.Expired(Runner))
            {
                CompleteStage(false, StageFailureReason.TimeExpired);
                return;
            }

            CollectLivingPlayerState(
                null,
                out int connectedCount,
                out int livingCount,
                out _);

            if (connectedCount > 0 && livingCount == 0)
            {
                CompleteStage(false, StageFailureReason.AllPlayersDead);
                return;
            }

            if (ObjectiveKind == StageObjectiveKind.DefeatBoss)
            {
                if (bossObjectiveController != null &&
                    bossObjectiveController.BossDefeated)
                {
                    CompleteStage(true, StageFailureReason.None);
                }

                return;
            }

            if (!TryGetSelectedExtraction(out StageExtractionPoint point))
                return;

            CollectLivingPlayerState(
                point,
                out connectedCount,
                out livingCount,
                out int livingInsideCount);

            bool allLivingPlayersInside =
                StageRules.AreAllLivingPlayersInside(
                    livingCount,
                    livingInsideCount);

            if (!allLivingPlayersInside)
            {
                ExtractionHoldTimer = TickTimer.None;
                return;
            }

            if (!ExtractionHoldTimer.IsRunning)
            {
                ExtractionHoldTimer = TickTimer.CreateFromSeconds(
                    Runner,
                    Mathf.Max(0.1f, extractionHoldSeconds));
                return;
            }

            if (ExtractionHoldTimer.Expired(Runner))
                CompleteStage(true, StageFailureReason.None);
        }

        private void CompleteStage(
            bool succeeded,
            StageFailureReason failureReason)
        {
            if (Phase != StagePhase.Active)
                return;

            Phase = succeeded
                ? StagePhase.Completed
                : StagePhase.Failed;
            FailureReason = failureReason;
            StageTimer = TickTimer.None;
            ExtractionHoldTimer = TickTimer.None;

            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();

            if (context == null ||
                !context.HasWritableHostSave ||
                context.ActiveSave?.RunProgression == null)
            {
                Phase = StagePhase.CommitFailed;
                Debug.LogError(
                    "[StageFlow] Host Runner 沒有可寫入存檔，關卡結果不會切場。",
                    this);
                return;
            }

            GameLogic progressionOwner = GameLogic.GetPrimaryForRunner(Runner);
            if (progressionOwner == null ||
                (succeeded && !progressionOwner.TryWriteHostExperienceToSave(
                    context.ActiveSave)))
            {
                Phase = StagePhase.CommitFailed;
                Debug.LogError(
                    "[StageFlow] 無法取得 Host 玩家經驗狀態，關卡結果不會切場。",
                    this);
                return;
            }

            if (succeeded)
            {
                context.ActiveSave.RunProgression.StageLevel =
                    Mathf.Max(
                        1,
                        context.ActiveSave.RunProgression.StageLevel) + 1;
            }
            else
            {
                context.ActiveSave.ResetRunProgression();
            }

            GameSaveRepositoryResult<GameSaveData> writeResult =
                context.WriteActiveSave();

            if (!writeResult.Success)
            {
                Phase = StagePhase.CommitFailed;
                Debug.LogError(
                    $"[StageFlow] 關卡結果寫入失敗：{writeResult.Error}",
                    this);
                return;
            }

            if (!succeeded)
                progressionOwner.ResetPlayerExperiencesForFailedRun();

            if (debugFlow)
            {
                Debug.Log(
                    succeeded
                        ? $"[StageFlow] 撤離成功，存檔提升至 StageLevel " +
                          $"{context.ActiveSave.RunProgression.StageLevel}。"
                        : $"[StageFlow] 關卡失敗 ({failureReason})，循環進度已重設。",
                    this);
            }

            BeginSafeHouseLoad();
        }

        private void BeginSafeHouseLoad()
        {
            if (!Runner.IsSceneAuthority || Runner.IsSceneManagerBusy)
                return;

            if (string.IsNullOrWhiteSpace(safeHouseSceneName))
            {
                Phase = StagePhase.CommitFailed;
                Debug.LogError(
                    "[StageFlow] Safe House Scene Name 不可為空。",
                    this);
                return;
            }

            GameLogic gameLogic = FindObjectOfType<GameLogic>();
            if (gameLogic != null)
                gameLogic.PrepareForAuthoritativeSceneTransition();

            Phase = StagePhase.LoadingSafeHouse;
            Runner.LoadScene(
                safeHouseSceneName,
                LoadSceneMode.Single,
                LocalPhysicsMode.None,
                true);
        }

        private void CollectLivingPlayerState(
            StageExtractionPoint point,
            out int connectedCount,
            out int livingCount,
            out int livingInsideCount)
        {
            connectedCount = 0;
            livingCount = 0;
            livingInsideCount = 0;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                connectedCount++;

                if (!Runner.TryGetPlayerObject(
                        player,
                        out NetworkObject playerObject) ||
                    playerObject == null ||
                    !playerObject.IsValid)
                {
                    continue;
                }

                PlayerHealth health =
                    playerObject.GetComponent<PlayerHealth>();

                if (health == null || !health.IsAlive)
                    continue;

                livingCount++;
                if (point != null &&
                    point.Contains(playerObject.transform.position))
                    livingInsideCount++;
            }
        }

        private void BeginActiveStage(StageRuntimePlan runtimePlan)
        {
            float timeLimit = StageRules.ResolveTimeLimitSeconds(
                runtimePlan,
                defaultTimeLimitSeconds,
                IsUnlimitedStage(StageLevel),
                bossTimeLimitSeconds);

            HasTimeLimit = timeLimit > 0f;
            StageTimer = HasTimeLimit
                ? TickTimer.CreateFromSeconds(Runner, timeLimit)
                : TickTimer.None;
            ExtractionHoldTimer = TickTimer.None;
            Phase = StagePhase.Active;
            ApplyExtractionVisuals();
        }

        private void LogInitializationFailure(string message)
        {
            if (loggedInitializationFailure)
                return;

            loggedInitializationFailure = true;
            Debug.LogError(message, this);
        }

        private bool AreAllConnectedPlayersLoaded()
        {
            int connectedCount = 0;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                connectedCount++;
                if (!Runner.TryGetPlayerObject(
                        player,
                        out NetworkObject playerObject) ||
                    playerObject == null ||
                    !playerObject.IsValid || !HasCurrentLoadReport(player))
                {
                    return false;
                }
            }

            return connectedCount > 0;
        }

        private int ResolveHostStageLevel()
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();

            return context?.ActiveSave?.RunProgression != null
                ? Mathf.Max(1, context.ActiveSave.RunProgression.StageLevel)
                : 1;
        }

        private int ResolveCycleLength()
        {
            GameSaveRuntimeContext context =
                Runner.GetComponent<GameSaveRuntimeContext>();

            int savedCycleLength =
                context?.ActiveSave != null
                    ? context.ActiveSave.CycleLengthSnapshot
                    : fallbackCycleLength;

            return Mathf.Max(1, savedCycleLength);
        }

        private bool IsUnlimitedStage(int stageLevel)
        {
            if (unlimitedStageLevels == null)
                return false;

            for (int i = 0; i < unlimitedStageLevels.Length; i++)
            {
                if (unlimitedStageLevels[i] == stageLevel)
                    return true;
            }

            return false;
        }

        private void RebuildExtractionPointList()
        {
            extractionPoints.Clear();

            if (!TryGetComponent(out Purgers.Map.NetworkMapState map) ||
                !map.TryGetFinalChunk(out _))
            {
                return;
            }

            StageExtractionPoint[] found =
                FindObjectsOfType<StageExtractionPoint>(true);

            for (int i = 0; i < found.Length; i++)
            {
                StageExtractionPoint point = found[i];
                int candidateChunkIndex = IndexOfChunk(
                    map.Chunks,
                    point != null ? point.OwnerChunk : null);

                if (point != null &&
                    point.isActiveAndEnabled &&
                    point.gameObject.scene == gameObject.scene &&
                    StageRules.IsExtractionCandidateInFinalChunk(
                        candidateChunkIndex,
                        map.FinalChunkIndex))
                {
                    extractionPoints.Add(point);
                }
            }

            extractionPoints.Sort(CompareExtractionPoints);
        }

        private bool TryBindExtractionPointsToFinalChunk()
        {
            if (mapRunSelection == null ||
                !TryGetComponent(out Purgers.Map.NetworkMapState map) ||
                !map.TryGetFinalChunk(out MapChunk finalChunk))
            {
                return false;
            }

            if (boundExtractionLayoutRevision == map.LayoutRevision)
                return true;

            MapChunk startingChunk = mapRunSelection.StartingChunk;
            if (startingChunk == null)
                return false;

            StageExtractionPoint[] found =
                FindObjectsOfType<StageExtractionPoint>(true);
            bool finalChunkAlreadyOwnsCandidates = false;

            for (int i = 0; i < found.Length; i++)
            {
                StageExtractionPoint point = found[i];
                if (point != null &&
                    point.gameObject.scene == gameObject.scene &&
                    point.OwnerChunk == finalChunk)
                {
                    finalChunkAlreadyOwnsCandidates = true;
                    break;
                }
            }

            if (!finalChunkAlreadyOwnsCandidates && finalChunk != startingChunk)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    StageExtractionPoint point = found[i];
                    if (point == null ||
                        point.gameObject.scene != gameObject.scene ||
                        point.OwnerChunk != startingChunk)
                    {
                        continue;
                    }

                    Vector3 localPosition =
                        startingChunk.transform.InverseTransformPoint(
                            point.transform.position);
                    Quaternion localRotation =
                        Quaternion.Inverse(startingChunk.transform.rotation) *
                        point.transform.rotation;
                    Vector3 localScale = point.transform.localScale;

                    point.transform.SetParent(finalChunk.transform, false);
                    point.transform.localPosition = localPosition;
                    point.transform.localRotation = localRotation;
                    point.transform.localScale = localScale;
                }
            }

            boundExtractionLayoutRevision = map.LayoutRevision;
            RebuildExtractionPointList();
            return true;
        }

        private static int IndexOfChunk(
            IReadOnlyList<MapChunk> chunks,
            MapChunk candidate)
        {
            if (chunks == null || candidate == null)
                return -1;

            for (int index = 0; index < chunks.Count; index++)
            {
                if (chunks[index] == candidate)
                    return index;
            }

            return -1;
        }

        private void ApplyExtractionVisuals()
        {
            if (extractionPoints.Count == 0)
                RebuildExtractionPointList();

            int selectedIndex = SelectedExtractionIndex;
            for (int i = 0; i < extractionPoints.Count; i++)
            {
                if (extractionPoints[i] != null)
                    extractionPoints[i].SetSelected(i == selectedIndex);
            }

        }

        private static int CompareExtractionPoints(
            StageExtractionPoint first,
            StageExtractionPoint second)
        {
            int idComparison = string.CompareOrdinal(
                first != null ? first.PointId : string.Empty,
                second != null ? second.PointId : string.Empty);

            if (idComparison != 0)
                return idComparison;

            return string.CompareOrdinal(
                first != null ? first.transform.GetHierarchyPath() : string.Empty,
                second != null ? second.transform.GetHierarchyPath() : string.Empty);
        }

        private void OnValidate()
        {
            fallbackCycleLength = Mathf.Max(1, fallbackCycleLength);
            defaultTimeLimitSeconds = Mathf.Max(0f, defaultTimeLimitSeconds);
            bossTimeLimitSeconds = Mathf.Max(0f, bossTimeLimitSeconds);
            extractionHoldSeconds = Mathf.Max(0.1f, extractionHoldSeconds);
            extractionNavMeshSampleDistance =
                Mathf.Max(0.01f, extractionNavMeshSampleDistance);
        }
    }

    internal static class StageTransformPathExtensions
    {
        public static string GetHierarchyPath(this Transform transform)
        {
            if (transform == null)
                return string.Empty;

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
