using System.Collections.Generic;

namespace Purgers.Map
{
    /// <summary>
    /// Pure validation for the stage-scoped network map contract. Replica geometry is
    /// never instantiated until the complete authoritative topology passes these rules.
    /// </summary>
    public static class MapLayoutRules
    {
        public const int MaximumChunkCount = 16;
        private const int AllConnectorSidesMask = 0b1111;

        public static bool TryValidateLinearTopology(
            IReadOnlyList<MapChunkPlacement> placements,
            int chunkCount,
            int finalChunkIndex,
            out string failure)
        {
            if (placements == null ||
                chunkCount < 1 ||
                chunkCount > MaximumChunkCount ||
                placements.Count != chunkCount)
            {
                failure = "ChunkCount 或拓撲清單長度無效。";
                return false;
            }

            if (finalChunkIndex != chunkCount - 1)
            {
                failure = "FinalChunkIndex 必須指向唯一的線性拓撲末端。";
                return false;
            }

            for (int index = 0; index < chunkCount; index++)
            {
                MapChunkPlacement placement = placements[index];
                int expectedParent = index == 0 ? -1 : index - 1;
                if (placement.ConnectedFromIndex != expectedParent)
                {
                    failure = $"Chunk {index} 的 ConnectedFromIndex 不連續。";
                    return false;
                }

                if (!IsConnectorSide(placement.EntrySide))
                {
                    failure = $"Chunk {index} 的 EntrySide 無效。";
                    return false;
                }

                bool isFinal = index == finalChunkIndex;
                if ((!isFinal && !IsConnectorSide(placement.ExitSide)) ||
                    (isFinal && placement.ExitSide != -1))
                {
                    failure = $"Chunk {index} 的 ExitSide 與末端身分不一致。";
                    return false;
                }

                if (!isFinal && placement.EntrySide == placement.ExitSide)
                {
                    failure = $"Chunk {index} 不可從入口 Connector 原路退出。";
                    return false;
                }

                int expectedOpenSides = index == 0
                    ? 0
                    : 1 << placement.EntrySide;
                if (!isFinal)
                    expectedOpenSides |= 1 << placement.ExitSide;

                if ((placement.OpenSides & ~AllConnectorSidesMask) != 0 ||
                    placement.OpenSides != expectedOpenSides)
                {
                    failure = $"Chunk {index} 的 OpenSides 與拓撲入口／出口不一致。";
                    return false;
                }
            }

            failure = null;
            return true;
        }

        public static bool IsValidExtractionSelection(
            int selectedIndex,
            int extractionCandidateCount)
        {
            return extractionCandidateCount > 0 &&
                   selectedIndex >= 0 &&
                   selectedIndex < extractionCandidateCount;
        }

        private static bool IsConnectorSide(int side)
        {
            return side >= 0 && side < 4;
        }
    }
}
