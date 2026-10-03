using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Purgers.GameFlow.Control
{
    /// <summary>One local input surface per application; never consulted by remote simulation.</summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class LocalPlayerControl : MonoBehaviour
    {
        public static PlayerControlLocks Locks { get; private set; } = new PlayerControlLocks();
        public static bool AllInputBlocked => (Locks.Mask & PlayerControlMask.AllInput) != 0;
        private static LocalPlayerControl instance;
        private readonly List<EventSystem> suspended = new List<EventSystem>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Locks = new PlayerControlLocks();
            instance = null;
        }

        public static IDisposable Acquire(PlayerControlMask mask, string reason)
        {
            if (Application.isPlaying && instance == null)
            {
                var root = new GameObject("LocalPlayerControl");
                instance = root.AddComponent<LocalPlayerControl>();
                DontDestroyOnLoad(root);
            }
            return Locks.Acquire(mask, reason);
        }

        private void Awake()
        {
            Locks.Changed += RefreshUI;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Update() => RefreshUI();
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshUI();

        private void RefreshUI()
        {
            if (!AllInputBlocked)
            {
                RestoreUI();
                return;
            }
            // A transparent raycast image alone cannot stop keyboard Submit/Navigation.
            foreach (EventSystem system in FindObjectsOfType<EventSystem>())
            {
                if (!system.enabled)
                    continue;
                system.SetSelectedGameObject(null);
                system.enabled = false;
                if (!suspended.Contains(system))
                    suspended.Add(system);
            }
        }

        private void RestoreUI()
        {
            foreach (EventSystem system in suspended)
                if (system != null)
                    system.enabled = true;
            suspended.Clear();
        }

        private void OnDestroy()
        {
            Locks.Changed -= RefreshUI;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RestoreUI();
            if (instance == this)
                instance = null;
        }
    }
}
