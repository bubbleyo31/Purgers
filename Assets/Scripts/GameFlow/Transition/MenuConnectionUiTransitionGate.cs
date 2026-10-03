using System;
using UnityEngine;

namespace Purgers.GameFlow.Transition
{
    /// <summary>
    /// Keeps a scene transition covered until Fusion Menu has finished hiding
    /// its connection loading screen.
    /// </summary>
    public static class MenuConnectionUiTransitionGate
    {
        private static UnityEngine.Object loadingScreenOwner;
        private static Func<bool> isLoadingScreenVisible;

        public static bool IsConnectUiVisible =>
            loadingScreenOwner != null &&
            isLoadingScreenVisible != null &&
            isLoadingScreenVisible();

        public static void Begin(
            UnityEngine.Object owner,
            Func<bool> visibilityCheck)
        {
            loadingScreenOwner = owner;
            isLoadingScreenVisible = visibilityCheck;
        }

        public static void End(UnityEngine.Object owner)
        {
            if (ReferenceEquals(loadingScreenOwner, owner))
            {
                loadingScreenOwner = null;
                isLoadingScreenVisible = null;
            }
        }
    }
}
