using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace Purgers.GameFlow.Stage
{
    /// <summary>
    /// Keeps exactly one stage-scoped HUD visible for each local Runner.
    /// Game and SafeHouse HUDs share this slot so a newly loaded level replaces
    /// presentation left behind by the previous scene immediately.
    /// </summary>
    public static class StageHudLifetimeRegistry
    {
        private sealed class Entry
        {
            public int StageLevel;
            public MonoBehaviour Hud;
        }

        private static readonly Dictionary<NetworkRunner, Entry> entries =
            new Dictionary<NetworkRunner, Entry>();

        public static void Claim(
            NetworkRunner runner,
            int stageLevel,
            MonoBehaviour hud)
        {
            if (!runner || !hud)
                return;

            RemoveDestroyedEntries();

            if (entries.TryGetValue(runner, out Entry current))
            {
                if (current.Hud == hud)
                {
                    current.StageLevel = stageLevel;
                    return;
                }

                HideAndDestroy(current.Hud);
            }

            entries[runner] = new Entry
            {
                StageLevel = stageLevel,
                Hud = hud
            };
        }

        public static bool IsOwner(
            NetworkRunner runner,
            int stageLevel,
            MonoBehaviour hud)
        {
            return runner &&
                   hud &&
                   entries.TryGetValue(runner, out Entry current) &&
                   current.StageLevel == stageLevel &&
                   current.Hud == hud;
        }

        public static void Release(NetworkRunner runner, MonoBehaviour hud)
        {
            if (!runner || !hud)
                return;

            if (entries.TryGetValue(runner, out Entry current) &&
                current.Hud == hud)
            {
                entries.Remove(runner);
            }
        }

        public static void ResetForTests()
        {
            entries.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnSubsystemRegistration()
        {
            entries.Clear();
        }

        private static void HideAndDestroy(MonoBehaviour hud)
        {
            if (!hud)
                return;

            GameObject root = hud.gameObject;
            root.SetActive(false);

            if (Application.isPlaying)
                Object.Destroy(root);
        }

        private static void RemoveDestroyedEntries()
        {
            List<NetworkRunner> staleRunners = null;
            foreach (KeyValuePair<NetworkRunner, Entry> pair in entries)
            {
                if (!pair.Key || pair.Value == null || !pair.Value.Hud)
                {
                    staleRunners ??= new List<NetworkRunner>();
                    staleRunners.Add(pair.Key);
                }
            }

            if (staleRunners == null)
                return;

            foreach (NetworkRunner staleRunner in staleRunners)
                entries.Remove(staleRunner);
        }
    }
}
