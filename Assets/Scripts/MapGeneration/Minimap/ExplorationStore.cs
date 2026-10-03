using System.Collections.Generic;

namespace Purgers.Map
{
    /// <summary>Monotonic per-player history. Sharing is a read policy, never a destructive merge.</summary>
    public sealed class ExplorationStore
    {
        /// <summary>Presentation notification. Does not consume the authoritative network delta queue.</summary>
        public event System.Action<Word> Changed;
        public readonly struct Word
        {
            public readonly int Player, Chunk, Index;
            public readonly uint Bits;
            public Word(int player, int chunk, int index, uint bits)
            { Player = player; Chunk = chunk; Index = index; Bits = bits; }
        }

        private readonly Dictionary<(int player, int chunk, int word), uint> words = new();
        private readonly Dictionary<(int chunk, int word), uint> shared = new();
        private readonly HashSet<(int player, int chunk, int word)> dirty = new();

        public bool Reveal(int player, int chunk, int cell)
        {
            if (cell < 0) return false;
            return Merge(player, chunk, cell >> 5, 1u << (cell & 31), true);
        }

        public bool Merge(int player, int chunk, int word, uint bits, bool trackChanges = false)
        {
            var key = (player, chunk, word);
            words.TryGetValue(key, out uint before);
            uint after = before | bits;
            if (after == before) return false;
            words[key] = after;
            var sharedKey = (chunk, word);
            shared.TryGetValue(sharedKey, out uint union);
            shared[sharedKey] = union | bits;
            if (trackChanges) dirty.Add(key);
            Changed?.Invoke(new Word(player, chunk, word, after));
            return true;
        }

        public bool IsExplored(int player, int chunk, int cell, bool share)
        {
            if (cell < 0) return false;
            uint value;
            if (share) shared.TryGetValue((chunk, cell >> 5), out value);
            else words.TryGetValue((player, chunk, cell >> 5), out value);
            return (value & (1u << (cell & 31))) != 0;
        }

        public List<Word> Snapshot()
        {
            var result = new List<Word>(words.Count);
            foreach (var pair in words)
                result.Add(new Word(pair.Key.player, pair.Key.chunk, pair.Key.word, pair.Value));
            return result;
        }

        public List<Word> DrainChanges()
        {
            var result = new List<Word>(dirty.Count);
            foreach (var key in dirty)
                result.Add(new Word(key.player, key.chunk, key.word, words[key]));
            dirty.Clear();
            return result;
        }
    }
}
