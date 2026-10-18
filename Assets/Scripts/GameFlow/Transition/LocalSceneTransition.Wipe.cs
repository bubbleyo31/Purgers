using UnityEngine;

namespace Purgers.GameFlow.Transition
{
    /// <summary>全黑及靜音均完成後才起算停留；完成當幀的 delta 不算進停留。</summary>
    public sealed class WipeTransitionClock
    {
        public const float FadeSeconds = 1.5f;
        public const float HoldSeconds = 2f;
        private bool holding;
        private float elapsed;
        public bool Ready => holding && elapsed >= HoldSeconds;
        public void Advance(float delta, bool black, bool silent)
        {
            if (!black || !silent) { holding = false; elapsed = 0f; return; }
            if (!holding) { holding = true; return; }
            elapsed += Mathf.Max(0f, delta);
        }
    }

    public sealed partial class LocalSceneTransition
    {
        private WipeTransitionClock wipeClock;
        private bool wipePreparing, wipeAudio;
        private float wipeElapsed;
        public bool IsWipePreparing => wipePreparing;
        public bool IsWipeReadyToLoad => wipePreparing && wipeClock != null && wipeClock.Ready;
        public void BeginWipe()
        {
            if (wipePreparing || wipeAudio) return;
            loadingLock = loadingLock ?? Purgers.GameFlow.Control.LocalPlayerControl.Acquire(
                Purgers.GameFlow.Control.PlayerControlMask.AllInput, "Team wipe");
            wipeClock = new WipeTransitionClock(); wipeElapsed = 0f;
            wipePreparing = wipeAudio = true;
            IsLocalLoadComplete = false; sceneLoadDone = revealing = preservingConnectUi = false;
            Fade.FadeDuration = WipeTransitionClock.FadeSeconds;
            Fade.FadeOut();
            GameplayAudioTransitionGain.Set(this, 1f);
        }
        private void TickWipe()
        {
            wipeElapsed += Time.unscaledDeltaTime;
            float gain = Mathf.Clamp01(1f - wipeElapsed / WipeTransitionClock.FadeSeconds);
            GameplayAudioTransitionGain.Set(this, gain);
            wipeClock.Advance(Time.unscaledDeltaTime, !Fade.IsFading && Fade.Alpha >= 1f, gain <= 0f);
        }
        private void ResetWipe()
        {
            wipePreparing = wipeAudio = false; wipeClock = null; wipeElapsed = 0f;
            GameplayAudioTransitionGain.Release(this);
        }
    }
}
