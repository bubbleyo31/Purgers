using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Purgers.MapGeneration.Topology
{
    public enum MapTopologyDirection : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public enum MapTopologyFailureReason : byte
    {
        None = 0,
        RetryLimitReached = 1
    }

    public readonly struct MapTopologyPoint : IEquatable<MapTopologyPoint>
    {
        public MapTopologyPoint(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public static MapTopologyPoint operator +(
            MapTopologyPoint left,
            MapTopologyPoint right)
        {
            return new MapTopologyPoint(
                checked(left.X + right.X),
                checked(left.Z + right.Z));
        }

        public static MapTopologyPoint operator -(
            MapTopologyPoint left,
            MapTopologyPoint right)
        {
            return new MapTopologyPoint(
                checked(left.X - right.X),
                checked(left.Z - right.Z));
        }

        public bool Equals(MapTopologyPoint other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is MapTopologyPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0},{1})",
                X,
                Z);
        }
    }

    public readonly struct MapTopologyRect : IEquatable<MapTopologyRect>
    {
        public MapTopologyRect(int minX, int minZ, int maxX, int maxZ)
        {
            if (maxX <= minX)
                throw new ArgumentOutOfRangeException(nameof(maxX));
            if (maxZ <= minZ)
                throw new ArgumentOutOfRangeException(nameof(maxZ));

            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        public int MinX { get; }
        public int MinZ { get; }
        public int MaxX { get; }
        public int MaxZ { get; }

        public bool Equals(MapTopologyRect other)
        {
            return MinX == other.MinX &&
                   MinZ == other.MinZ &&
                   MaxX == other.MaxX &&
                   MaxZ == other.MaxZ;
        }

        public override bool Equals(object obj)
        {
            return obj is MapTopologyRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = MinX;
                hashCode = (hashCode * 397) ^ MinZ;
                hashCode = (hashCode * 397) ^ MaxX;
                hashCode = (hashCode * 397) ^ MaxZ;
                return hashCode;
            }
        }
    }

    public readonly struct MapTopologyConnector
    {
        public MapTopologyConnector(
            MapTopologyDirection direction,
            MapTopologyPoint localPosition)
        {
            Direction = direction;
            LocalPosition = localPosition;
        }

        public MapTopologyDirection Direction { get; }
        public MapTopologyPoint LocalPosition { get; }
    }

    public sealed class MapChunkTopologyDefinition
    {
        private readonly ReadOnlyCollection<MapTopologyConnector> connectors;

        public MapChunkTopologyDefinition(
            string stableId,
            MapTopologyRect localBounds,
            IReadOnlyList<MapTopologyConnector> connectors)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException(
                    "Chunk topology requires a stable ID.",
                    nameof(stableId));
            if (connectors == null)
                throw new ArgumentNullException(nameof(connectors));
            if (connectors.Count < 2)
                throw new ArgumentException(
                    "Chunk topology requires at least two connectors.",
                    nameof(connectors));

            var copy = new List<MapTopologyConnector>(connectors.Count);
            var directions = new HashSet<MapTopologyDirection>();

            for (int index = 0; index < connectors.Count; index++)
            {
                MapTopologyConnector connector = connectors[index];

                if (!directions.Add(connector.Direction))
                {
                    throw new ArgumentException(
                        $"Chunk {stableId} contains duplicate {connector.Direction} connectors.",
                        nameof(connectors));
                }

                copy.Add(connector);
            }

            StableId = stableId;
            LocalBounds = localBounds;
            this.connectors = copy.AsReadOnly();
        }

        public string StableId { get; }
        public MapTopologyRect LocalBounds { get; }
        public IReadOnlyList<MapTopologyConnector> Connectors => connectors;

        public MapTopologyConnector GetConnector(
            MapTopologyDirection direction)
        {
            for (int index = 0; index < connectors.Count; index++)
            {
                if (connectors[index].Direction == direction)
                    return connectors[index];
            }

            throw new InvalidOperationException(
                $"Chunk {StableId} does not define a {direction} connector.");
        }
    }

    public sealed class MapTopologyBuildRequest
    {
        private readonly ReadOnlyCollection<MapChunkTopologyDefinition> candidateChunks;

        public MapTopologyBuildRequest(
            int seed,
            int additionalChunkCount,
            int maxAttemptsPerChunk,
            MapChunkTopologyDefinition startingChunk,
            IReadOnlyList<MapChunkTopologyDefinition> candidateChunks,
            MapTopologyDirection? startingEntryDirection = null,
            MapTopologyDirection? startingExitDirection = null)
        {
            if (additionalChunkCount < 0)
                throw new ArgumentOutOfRangeException(nameof(additionalChunkCount));
            if (maxAttemptsPerChunk <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAttemptsPerChunk));
            if (startingChunk == null)
                throw new ArgumentNullException(nameof(startingChunk));
            if (candidateChunks == null)
                throw new ArgumentNullException(nameof(candidateChunks));
            if (additionalChunkCount > 0 && candidateChunks.Count == 0)
                throw new ArgumentException(
                    "At least one candidate Chunk is required.",
                    nameof(candidateChunks));
            if (startingExitDirection.HasValue && additionalChunkCount == 0)
                throw new ArgumentException(
                    "A fixed starting exit requires at least one additional Chunk.",
                    nameof(startingExitDirection));
            if (startingEntryDirection.HasValue &&
                startingExitDirection.HasValue &&
                startingEntryDirection.Value == startingExitDirection.Value)
            {
                throw new ArgumentException(
                    "Starting entry and exit connectors must be different.",
                    nameof(startingExitDirection));
            }

            if (startingEntryDirection.HasValue)
                startingChunk.GetConnector(startingEntryDirection.Value);
            if (startingExitDirection.HasValue)
                startingChunk.GetConnector(startingExitDirection.Value);

            var copy = new List<MapChunkTopologyDefinition>(candidateChunks.Count);

            for (int index = 0; index < candidateChunks.Count; index++)
            {
                if (candidateChunks[index] == null)
                {
                    throw new ArgumentException(
                        "Candidate Chunk list cannot contain null.",
                        nameof(candidateChunks));
                }

                copy.Add(candidateChunks[index]);
            }

            Seed = seed;
            AdditionalChunkCount = additionalChunkCount;
            MaxAttemptsPerChunk = maxAttemptsPerChunk;
            StartingChunk = startingChunk;
            StartingEntryDirection = startingEntryDirection;
            StartingExitDirection = startingExitDirection;
            this.candidateChunks = copy.AsReadOnly();
        }

        public int Seed { get; }
        public int AdditionalChunkCount { get; }
        public int MaxAttemptsPerChunk { get; }
        public MapChunkTopologyDefinition StartingChunk { get; }
        public MapTopologyDirection? StartingEntryDirection { get; }
        public MapTopologyDirection? StartingExitDirection { get; }
        public IReadOnlyList<MapChunkTopologyDefinition> CandidateChunks => candidateChunks;
    }

    public sealed class MapTopologyPlacement
    {
        internal MapTopologyPlacement(
            int instanceIndex,
            MapChunkTopologyDefinition definition,
            MapTopologyPoint origin,
            int quarterTurnsClockwise,
            int connectedFromInstanceIndex,
            MapTopologyDirection localEntryDirection,
            MapTopologyDirection? localExitDirection,
            MapTopologyRect worldBounds,
            bool isFinalChunk)
        {
            InstanceIndex = instanceIndex;
            Definition = definition;
            Origin = origin;
            QuarterTurnsClockwise = quarterTurnsClockwise;
            ConnectedFromInstanceIndex = connectedFromInstanceIndex;
            LocalEntryDirection = localEntryDirection;
            LocalExitDirection = localExitDirection;
            WorldBounds = worldBounds;
            IsFinalChunk = isFinalChunk;
        }

        public int InstanceIndex { get; }
        public MapChunkTopologyDefinition Definition { get; }
        public string DefinitionId => Definition.StableId;
        public MapTopologyPoint Origin { get; }
        public int QuarterTurnsClockwise { get; }
        public int ConnectedFromInstanceIndex { get; }
        public MapTopologyDirection LocalEntryDirection { get; }
        public MapTopologyDirection? LocalExitDirection { get; }
        public MapTopologyRect WorldBounds { get; }
        public bool IsFinalChunk { get; }

        public MapTopologyDirection WorldEntryDirection =>
            MapTopologyRules.Rotate(LocalEntryDirection, QuarterTurnsClockwise);

        public MapTopologyDirection? WorldExitDirection =>
            LocalExitDirection.HasValue
                ? MapTopologyRules.Rotate(
                    LocalExitDirection.Value,
                    QuarterTurnsClockwise)
                : (MapTopologyDirection?)null;

        public MapTopologyPoint GetWorldConnectorPosition(
            MapTopologyDirection localDirection)
        {
            MapTopologyConnector connector =
                Definition.GetConnector(localDirection);

            return Origin + MapTopologyRules.Rotate(
                connector.LocalPosition,
                QuarterTurnsClockwise);
        }

        internal MapTopologyPlacement WithExit(
            MapTopologyDirection localExitDirection)
        {
            return new MapTopologyPlacement(
                InstanceIndex,
                Definition,
                Origin,
                QuarterTurnsClockwise,
                ConnectedFromInstanceIndex,
                LocalEntryDirection,
                localExitDirection,
                WorldBounds,
                false);
        }

        internal MapTopologyPlacement AsFinal()
        {
            return new MapTopologyPlacement(
                InstanceIndex,
                Definition,
                Origin,
                QuarterTurnsClockwise,
                ConnectedFromInstanceIndex,
                LocalEntryDirection,
                LocalExitDirection,
                WorldBounds,
                true);
        }
    }

    public sealed class MapTopologyPlan
    {
        private readonly ReadOnlyCollection<MapTopologyPlacement> placements;

        internal MapTopologyPlan(
            int seed,
            bool isComplete,
            MapTopologyFailureReason failureReason,
            List<MapTopologyPlacement> placements,
            int attemptsUsed,
            int rejectedOverlapCount)
        {
            Seed = seed;
            IsComplete = isComplete;
            FailureReason = failureReason;
            this.placements = placements.AsReadOnly();
            AttemptsUsed = attemptsUsed;
            RejectedOverlapCount = rejectedOverlapCount;
            FinalChunk = isComplete && placements.Count > 0
                ? placements[placements.Count - 1]
                : null;
            Signature = BuildSignature(seed, this.placements);
        }

        public int Seed { get; }
        public bool IsComplete { get; }
        public MapTopologyFailureReason FailureReason { get; }
        public IReadOnlyList<MapTopologyPlacement> Placements => placements;
        public int AttemptsUsed { get; }
        public int RejectedOverlapCount { get; }
        public MapTopologyPlacement FinalChunk { get; }
        public int FinalChunkInstanceIndex =>
            FinalChunk != null ? FinalChunk.InstanceIndex : -1;
        public string Signature { get; }

        private static string BuildSignature(
            int seed,
            IReadOnlyList<MapTopologyPlacement> source)
        {
            var builder = new StringBuilder();
            builder.Append(seed.ToString(CultureInfo.InvariantCulture));

            for (int index = 0; index < source.Count; index++)
            {
                MapTopologyPlacement placement = source[index];
                builder.Append('|');
                builder.Append(placement.InstanceIndex);
                builder.Append(':');
                builder.Append(placement.DefinitionId);
                builder.Append('@');
                builder.Append(placement.Origin.X);
                builder.Append(',');
                builder.Append(placement.Origin.Z);
                builder.Append(',');
                builder.Append(placement.QuarterTurnsClockwise);
                builder.Append(',');
                builder.Append(placement.ConnectedFromInstanceIndex);
                builder.Append(',');
                builder.Append((int)placement.LocalEntryDirection);
                builder.Append(',');
                builder.Append(
                    placement.LocalExitDirection.HasValue
                        ? ((int)placement.LocalExitDirection.Value)
                            .ToString(CultureInfo.InvariantCulture)
                        : "-");
                builder.Append(placement.IsFinalChunk ? 'F' : '-');
            }

            return builder.ToString();
        }
    }

    public static class MapTopologyRules
    {
        public static bool Overlaps(
            MapTopologyRect left,
            MapTopologyRect right)
        {
            return left.MinX < right.MaxX &&
                   left.MaxX > right.MinX &&
                   left.MinZ < right.MaxZ &&
                   left.MaxZ > right.MinZ;
        }

        public static MapTopologyDirection Opposite(
            MapTopologyDirection direction)
        {
            return (MapTopologyDirection)(((int)direction + 2) & 3);
        }

        public static MapTopologyDirection Rotate(
            MapTopologyDirection direction,
            int quarterTurnsClockwise)
        {
            return (MapTopologyDirection)(
                ((int)direction + NormalizeTurns(quarterTurnsClockwise)) & 3);
        }

        public static MapTopologyPoint Rotate(
            MapTopologyPoint point,
            int quarterTurnsClockwise)
        {
            switch (NormalizeTurns(quarterTurnsClockwise))
            {
                case 0:
                    return point;
                case 1:
                    return new MapTopologyPoint(point.Z, checked(-point.X));
                case 2:
                    return new MapTopologyPoint(checked(-point.X), checked(-point.Z));
                default:
                    return new MapTopologyPoint(checked(-point.Z), point.X);
            }
        }

        public static MapTopologyRect TransformBounds(
            MapTopologyRect localBounds,
            MapTopologyPoint origin,
            int quarterTurnsClockwise)
        {
            MapTopologyPoint first = Rotate(
                new MapTopologyPoint(localBounds.MinX, localBounds.MinZ),
                quarterTurnsClockwise) + origin;
            MapTopologyPoint second = Rotate(
                new MapTopologyPoint(localBounds.MinX, localBounds.MaxZ),
                quarterTurnsClockwise) + origin;
            MapTopologyPoint third = Rotate(
                new MapTopologyPoint(localBounds.MaxX, localBounds.MinZ),
                quarterTurnsClockwise) + origin;
            MapTopologyPoint fourth = Rotate(
                new MapTopologyPoint(localBounds.MaxX, localBounds.MaxZ),
                quarterTurnsClockwise) + origin;

            int minX = Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X));
            int minZ = Math.Min(Math.Min(first.Z, second.Z), Math.Min(third.Z, fourth.Z));
            int maxX = Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X));
            int maxZ = Math.Max(Math.Max(first.Z, second.Z), Math.Max(third.Z, fourth.Z));

            return new MapTopologyRect(minX, minZ, maxX, maxZ);
        }

        internal static int RequiredRotation(
            MapTopologyDirection localEntry,
            MapTopologyDirection sourceWorldExit)
        {
            int desired = (int)Opposite(sourceWorldExit);
            return NormalizeTurns(desired - (int)localEntry);
        }

        private static int NormalizeTurns(int quarterTurnsClockwise)
        {
            int normalized = quarterTurnsClockwise % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }
    }

    public static class MapTopologyPlanner
    {
        public static MapTopologyPlan Build(MapTopologyBuildRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var random = new StableRandom(request.Seed);
            var placements = new List<MapTopologyPlacement>(
                request.AdditionalChunkCount + 1);

            MapChunkTopologyDefinition start = request.StartingChunk;
            MapTopologyConnector startEntry = request.StartingEntryDirection.HasValue
                ? start.GetConnector(request.StartingEntryDirection.Value)
                : start.Connectors[random.Next(start.Connectors.Count)];
            placements.Add(
                new MapTopologyPlacement(
                    0,
                    start,
                    new MapTopologyPoint(0, 0),
                    0,
                    -1,
                    startEntry.Direction,
                    null,
                    start.LocalBounds,
                    false));

            int attemptsUsed = 0;
            int rejectedOverlapCount = 0;

            for (int chunkIndex = 1;
                 chunkIndex <= request.AdditionalChunkCount;
                 chunkIndex++)
            {
                bool placed = false;

                for (int attempt = 0;
                     attempt < request.MaxAttemptsPerChunk;
                     attempt++)
                {
                    attemptsUsed++;
                    int previousIndex = placements.Count - 1;
                    MapTopologyPlacement previous = placements[previousIndex];
                    MapTopologyConnector sourceExit =
                        previousIndex == 0 && request.StartingExitDirection.HasValue
                            ? previous.Definition.GetConnector(
                                request.StartingExitDirection.Value)
                            : SelectDifferentConnector(
                                previous.Definition,
                                previous.LocalEntryDirection,
                                ref random);
                    MapChunkTopologyDefinition definition =
                        request.CandidateChunks[random.Next(request.CandidateChunks.Count)];
                    MapTopologyConnector entry =
                        definition.Connectors[random.Next(definition.Connectors.Count)];

                    MapTopologyDirection sourceWorldExit =
                        MapTopologyRules.Rotate(
                            sourceExit.Direction,
                            previous.QuarterTurnsClockwise);
                    int rotation = MapTopologyRules.RequiredRotation(
                        entry.Direction,
                        sourceWorldExit);
                    MapTopologyPoint sourceWorldPosition =
                        previous.Origin +
                        MapTopologyRules.Rotate(
                            sourceExit.LocalPosition,
                            previous.QuarterTurnsClockwise);
                    MapTopologyPoint origin =
                        sourceWorldPosition -
                        MapTopologyRules.Rotate(entry.LocalPosition, rotation);
                    MapTopologyRect worldBounds =
                        MapTopologyRules.TransformBounds(
                            definition.LocalBounds,
                            origin,
                            rotation);

                    if (OverlapsAny(worldBounds, placements))
                    {
                        rejectedOverlapCount++;
                        continue;
                    }

                    placements[previousIndex] =
                        previous.WithExit(sourceExit.Direction);
                    placements.Add(
                        new MapTopologyPlacement(
                            chunkIndex,
                            definition,
                            origin,
                            rotation,
                            previous.InstanceIndex,
                            entry.Direction,
                            null,
                            worldBounds,
                            false));
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    return new MapTopologyPlan(
                        request.Seed,
                        false,
                        MapTopologyFailureReason.RetryLimitReached,
                        placements,
                        attemptsUsed,
                        rejectedOverlapCount);
                }
            }

            int finalIndex = placements.Count - 1;
            placements[finalIndex] = placements[finalIndex].AsFinal();

            return new MapTopologyPlan(
                request.Seed,
                true,
                MapTopologyFailureReason.None,
                placements,
                attemptsUsed,
                rejectedOverlapCount);
        }

        private static MapTopologyConnector SelectDifferentConnector(
            MapChunkTopologyDefinition definition,
            MapTopologyDirection excludedDirection,
            ref StableRandom random)
        {
            int selectedIndex = random.Next(definition.Connectors.Count - 1);

            for (int index = 0; index < definition.Connectors.Count; index++)
            {
                MapTopologyConnector connector = definition.Connectors[index];

                if (connector.Direction == excludedDirection)
                    continue;

                if (selectedIndex == 0)
                    return connector;

                selectedIndex--;
            }

            throw new InvalidOperationException(
                "Topology definition did not provide a non-entry connector.");
        }

        private static bool OverlapsAny(
            MapTopologyRect candidate,
            IReadOnlyList<MapTopologyPlacement> placements)
        {
            for (int index = 0; index < placements.Count; index++)
            {
                if (MapTopologyRules.Overlaps(
                    candidate,
                    placements[index].WorldBounds))
                {
                    return true;
                }
            }

            return false;
        }

        private struct StableRandom
        {
            private uint state;

            public StableRandom(int seed)
            {
                state = unchecked((uint)seed) ^ 0xA511E9B3u;

                if (state == 0u)
                    state = 0x6D2B79F5u;
            }

            public int Next(int maxExclusive)
            {
                if (maxExclusive <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maxExclusive));

                uint value = NextUInt();
                return (int)(value % (uint)maxExclusive);
            }

            private uint NextUInt()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }
    }
}
