using System;
using Fusion;
using Purgers.GameFlow.Control;
using UnityEngine;

namespace Purgers.GameFlow.Transition
{
    /// <summary>Owned by the Runner; scene callbacks do not own player spawning.</summary>
    [DisallowMultipleComponent]
    public sealed class LocalSceneTransition : MonoBehaviour
    {
        private NetworkRunner runner;
        private IDisposable loadingLock;
        private bool sceneLoadDone;
        private bool revealing;
        private bool preservingConnectUi;
        public ScreenFadeLayer Fade { get; private set; }
        public bool IsLocalLoadComplete { get; private set; }
        public bool IsTransitioning => loadingLock != null;

        public static LocalSceneTransition Ensure(NetworkRunner owner, float duration = 1.5f)
        {
            if (!owner.TryGetComponent(out LocalSceneTransition transition))
                transition = owner.gameObject.AddComponent<LocalSceneTransition>();
            transition.runner = owner;
            if (transition.Fade == null)
            {
                var root = new GameObject("SceneTransitionOverlay");
                owner.MakeDontDestroyOnLoad(root);
                transition.Fade = root.AddComponent<ScreenFadeLayer>();
                transition.Fade.FadeDuration = duration;
            }
            return transition;
        }

        public void BeginLoad()
        {
            loadingLock = loadingLock ?? LocalPlayerControl.Acquire(PlayerControlMask.AllInput, "Scene load");
            IsLocalLoadComplete = false;
            sceneLoadDone = revealing = false;
            preservingConnectUi =
                MenuConnectionUiTransitionGate.IsConnectUiVisible;

            // Keep Fusion Menu's connection animation visible during the
            // initial Menu -> gameplay load. Other scene changes cover the
            // old camera immediately as before.
            Fade.SetImmediate(preservingConnectUi ? 0f : 1f);
        }

        public void SceneLoadDone() => sceneLoadDone = true;

        public static bool TryGetReadyLocalPlayer(NetworkRunner owner, out NetworkObject player)
        {
            player = null;
            if (owner == null || !owner.IsRunning || owner.IsSceneManagerBusy ||
                !owner.TryGetPlayerObject(owner.LocalPlayer, out player) ||
                player == null || !player.IsValid || !player.HasInputAuthority)
                return false;
            PlayerLocalView view = player.GetComponent<PlayerLocalView>();
            return view != null && view.IsLocalViewReady;
        }

        private void Update()
        {
            if (!IsTransitioning || runner == null)
                return;
            if (!revealing)
            {
                if (!sceneLoadDone ||
                    MenuConnectionUiTransitionGate.IsConnectUiVisible ||
                    !TryGetReadyLocalPlayer(runner, out _))
                    return;
                IsLocalLoadComplete = true;
                revealing = true;

                // The connection UI is now gone. Cut to black once, then
                // reveal the ready local player through the configured fade.
                if (preservingConnectUi)
                    Fade.SetImmediate(1f);

                preservingConnectUi = false;
                Fade.FadeIn();
            }
            if (!Fade.IsFading)
            {
                loadingLock.Dispose();
                loadingLock = null;
            }
        }

        public void Cancel()
        {
            loadingLock?.Dispose();
            loadingLock = null;
            sceneLoadDone = revealing = preservingConnectUi =
                IsLocalLoadComplete = false;
            if (Fade != null)
                Fade.SetImmediate(0f);
        }

        private void OnDestroy()
        {
            Cancel();
            if (Fade != null)
                Destroy(Fade.gameObject);
        }
    }
}
