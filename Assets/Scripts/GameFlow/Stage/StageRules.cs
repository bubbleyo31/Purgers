using System;
using System.Collections.Generic;

namespace Purgers.GameFlow.Stage
{
    public enum StageRuntimeKind : byte
    {
        Ordinary = 0,
        Boss = 1,
        UnsupportedOrdinary = 2
    }

    public enum StageMapRoute : byte
    {
        OrdinaryTopology = 0,
        DedicatedBossChunk = 1,
        Unsupported = 2
    }

    public enum StageObjectiveKind : byte
    {
        ReachExtractionAndHold = 0,
        DefeatBoss = 1
    }

    public readonly struct StageRuntimePlan
    {
        public StageRuntimePlan(
            int stageLevel,
            int cycleLength,
            int cycleStage,
            int additionalChunkCount,
            StageRuntimeKind kind,
            StageMapRoute mapRoute,
            StageObjectiveKind objectiveKind)
        {
            StageLevel = stageLevel;
            CycleLength = cycleLength;
            CycleStage = cycleStage;
            AdditionalChunkCount = additionalChunkCount;
            Kind = kind;
            MapRoute = mapRoute;
            ObjectiveKind = objectiveKind;
        }

        public int StageLevel { get; }
        public int CycleLength { get; }
        public int CycleStage { get; }
        public int AdditionalChunkCount { get; }
        public StageRuntimeKind Kind { get; }
        public StageMapRoute MapRoute { get; }
        public StageObjectiveKind ObjectiveKind { get; }
        public bool IsBossStage => Kind == StageRuntimeKind.Boss;
        public bool RequiresExtraction =>
            ObjectiveKind == StageObjectiveKind.ReachExtractionAndHold;
        public bool CanBuildOrdinaryTopology =>
            MapRoute == StageMapRoute.OrdinaryTopology;
    }

    public enum StagePhase : byte
    {
        Initializing = 0,
        Active = 1,
        Completed = 2,
        Failed = 3,
        CommitFailed = 4,
        LoadingSafeHouse = 5
    }

    public enum StageFailureReason : byte
    {
        None = 0,
        TimeExpired = 1,
        AllPlayersDead = 2
    }

    public static class StageRules
    {
        /// <summary>只允許 Editor Play Mode 的關卡權威切換仍未到期的有限倒數。</summary>
        public static bool CanToggleDevelopmentTimerPause(
            bool editorPlaying, bool stateAuthority, StagePhase phase,
            bool hasTimeLimit, int remainingTicks)
        {
            return editorPlaying && stateAuthority && phase == StagePhase.Active &&
                hasTimeLimit && remainingTicks > 0;
        }

        private const int ExtractionSeedSalt = 0x34A1C52D;
        public const int MaxOrdinaryAdditionalChunkCount = 2;

        public static StageRuntimePlan ResolveRuntimePlan(
            int stageLevel,
            int cycleLength)
        {
            int safeLevel = Math.Max(1, stageLevel);
            int safeCycleLength = Math.Max(1, cycleLength);
            int cycleStage = GetCycleStage(safeLevel, safeCycleLength);

            // Boss 必須先分流。即使 cycleStage - 1 等於 3，也絕不能把它
            // 當成普通多 Chunk 請求交給 MapTopologyPlanner。
            if (IsBossStage(safeLevel, safeCycleLength))
            {
                return new StageRuntimePlan(
                    safeLevel,
                    safeCycleLength,
                    cycleStage,
                    0,
                    StageRuntimeKind.Boss,
                    StageMapRoute.DedicatedBossChunk,
                    StageObjectiveKind.DefeatBoss);
            }

            int additionalChunkCount =
                GetAdditionalChunkCount(safeLevel, safeCycleLength);
            StageRuntimeKind kind =
                additionalChunkCount <= MaxOrdinaryAdditionalChunkCount
                    ? StageRuntimeKind.Ordinary
                    : StageRuntimeKind.UnsupportedOrdinary;

            return new StageRuntimePlan(
                safeLevel,
                safeCycleLength,
                cycleStage,
                additionalChunkCount,
                kind,
                kind == StageRuntimeKind.Ordinary
                    ? StageMapRoute.OrdinaryTopology
                    : StageMapRoute.Unsupported,
                StageObjectiveKind.ReachExtractionAndHold);
        }

        public static float ResolveTimeLimitSeconds(
            StageRuntimePlan plan,
            float ordinaryTimeLimitSeconds,
            bool ordinaryStageIsUnlimited,
            float bossTimeLimitSeconds)
        {
            if (plan.Kind == StageRuntimeKind.Boss)
                return Math.Max(0f, bossTimeLimitSeconds);

            if (plan.Kind != StageRuntimeKind.Ordinary ||
                ordinaryStageIsUnlimited)
            {
                return 0f;
            }

            return Math.Max(0f, ordinaryTimeLimitSeconds);
        }

        public static int GetCycleStage(int stageLevel, int cycleLength)
        {
            int safeLevel = Math.Max(1, stageLevel);
            int safeCycleLength = Math.Max(1, cycleLength);
            return ((safeLevel - 1) % safeCycleLength) + 1;
        }

        public static int GetAdditionalChunkCount(
            int stageLevel,
            int cycleLength)
        {
            return GetCycleStage(stageLevel, cycleLength) - 1;
        }

        public static bool IsBossStage(int stageLevel, int cycleLength)
        {
            int safeLevel = Math.Max(1, stageLevel);
            int safeCycleLength = Math.Max(1, cycleLength);
            return safeLevel % safeCycleLength == 0;
        }

        public static bool AreAllLivingPlayersInside(
            int livingPlayerCount,
            int livingPlayersInsideCount)
        {
            return livingPlayerCount > 0 &&
                   livingPlayersInsideCount == livingPlayerCount;
        }

        public static bool IsExtractionCandidateInFinalChunk(
            int candidateChunkIndex,
            int finalChunkIndex)
        {
            return finalChunkIndex >= 0 &&
                   candidateChunkIndex == finalChunkIndex;
        }

        public static bool IsExtractionPlacementAllowed(
            int candidateChunkIndex,
            int finalChunkIndex,
            int chunkCount,
            int candidateSide,
            int entrySide,
            int openSides)
        {
            // The validated linear topology starts at index 0 and ends at count - 1.
            // Its entry is the player spawn side for one chunk, and the incoming
            // connection for a later chunk. Never fall back to the starting chunk.
            return chunkCount > 0 &&
                   finalChunkIndex == chunkCount - 1 &&
                   IsExtractionCandidateInFinalChunk(candidateChunkIndex, finalChunkIndex) &&
                   candidateSide >= 0 && candidateSide < 4 &&
                   entrySide >= 0 && entrySide < 4 &&
                   (openSides & ~0b1111) == 0 &&
                   candidateSide != entrySide &&
                   (openSides & (1 << candidateSide)) == 0;
        }

        public static int SelectExtractionCandidateIndex(
            int runSeed,
            IReadOnlyList<int> eligibleCandidateIndices)
        {
            if (eligibleCandidateIndices == null ||
                eligibleCandidateIndices.Count == 0)
            {
                return -1;
            }

            var random = new Random(
                unchecked(runSeed ^ ExtractionSeedSalt));
            return eligibleCandidateIndices[
                random.Next(0, eligibleCandidateIndices.Count)];
        }
    }
}
