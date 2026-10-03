using Fusion.Menu;
using System.Collections;
using Purgers.GameFlow.Transition;
using UnityEngine;

namespace MultiClimb.Menu
{
    /// <summary>
    /// Project-owned menu controller. It tracks the SDK loading screen's real
    /// hide completion so the local scene transition never reveals behind it.
    /// </summary>
    public class MenuUIController : FusionMenuUIController<FusionMenuConnectArgs>
    {
        private Coroutine releaseConnectUiGateCoroutine;

        public override void Show<S>()
        {
            base.Show<S>();

            if (typeof(S) == typeof(FusionMenuUILoading))
            {
                MenuConnectionUiTransitionGate.Begin(
                    this,
                    IsConnectionLoadingScreenVisible);
                StopReleaseConnectUiGate();
                return;
            }

            if (MenuConnectionUiTransitionGate.IsConnectUiVisible)
                StartReleaseConnectUiGateAfterLoadingHidden();
        }

        private void StartReleaseConnectUiGateAfterLoadingHidden()
        {
            StopReleaseConnectUiGate();
            releaseConnectUiGateCoroutine =
                StartCoroutine(ReleaseConnectUiGateAfterLoadingHidden());
        }

        private IEnumerator ReleaseConnectUiGateAfterLoadingHidden()
        {
            while (IsConnectionLoadingScreenVisible())
                yield return null;

            MenuConnectionUiTransitionGate.End(this);
            releaseConnectUiGateCoroutine = null;
        }

        private bool IsConnectionLoadingScreenVisible()
        {
            FusionMenuUILoading loadingScreen =
                Get<FusionMenuUILoading>();

            return loadingScreen != null &&
                   loadingScreen.IsShowing &&
                   loadingScreen.gameObject.activeInHierarchy;
        }

        private void StopReleaseConnectUiGate()
        {
            if (releaseConnectUiGateCoroutine == null)
                return;

            StopCoroutine(releaseConnectUiGateCoroutine);
            releaseConnectUiGateCoroutine = null;
        }

        protected virtual void OnDestroy()
        {
            StopReleaseConnectUiGate();
            MenuConnectionUiTransitionGate.End(this);
        }
    }
}
