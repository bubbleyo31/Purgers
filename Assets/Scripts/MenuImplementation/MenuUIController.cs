using Fusion.Menu;
using System.Collections;
using Purgers.GameFlow.Transition;
using UnityEngine;

namespace MultiClimb.Menu
{
    /// <summary>
    /// Project-owned menu controller. It tracks the SDK loading screen's real
    /// hide completion so the local scene transition never reveals behind it.
    /// 連線載入視窗獨立排序，覆蓋已建立的小地圖與其他遊戲 HUD。
    /// </summary>
    public class MenuUIController : FusionMenuUIController<FusionMenuConnectArgs>
    {
        private Coroutine releaseConnectUiGateCoroutine;

        public override void Show<S>()
        {
            base.Show<S>();

            if (typeof(S) == typeof(FusionMenuUILoading))
            {
                // Canvas 啟用後才能套用獨立排序；仍在同一幀、繪製之前完成。
                ConfigureConnectionLoadingSorting();
                MenuConnectionUiTransitionGate.Begin(
                    this,
                    IsConnectionLoadingScreenVisible);
                StopReleaseConnectUiGate();
                return;
            }

            if (MenuConnectionUiTransitionGate.IsConnectUiVisible)
                StartReleaseConnectUiGateAfterLoadingHidden();
        }

        /// <summary>只提升既有 Loading Canvas；保留父 Canvas、動畫與連線生命週期。</summary>
        private void ConfigureConnectionLoadingSorting()
        {
            var loadingScreen = Get<FusionMenuUILoading>();
            if (loadingScreen == null || !loadingScreen.TryGetComponent(out Canvas loadingCanvas))
                return;

            loadingCanvas.overrideSorting = true;
            // 最高層保留給 ScreenFadeLayer；連線動畫使用其下一層。
            loadingCanvas.sortingOrder = short.MaxValue - 1;
            int highest = int.MinValue;
            foreach (SortingLayer layer in SortingLayer.layers)
            {
                if (layer.value < highest)
                    continue;
                highest = layer.value;
                loadingCanvas.sortingLayerID = layer.id;
            }
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
