using System.Collections.Generic;
using Fusion.Menu;

namespace MultiClimb.Menu
{
    public static class MenuSceneLaunchPolicy
    {
        public static bool TryResolve(
            PhotonMenuSceneInfo selectedScene,
            IReadOnlyList<PhotonMenuSceneInfo> availableScenes,
            out PhotonMenuSceneInfo resolvedScene)
        {
            resolvedScene = default;

            if (availableScenes == null || availableScenes.Count == 0)
                return false;

            for (int i = 0; i < availableScenes.Count; i++)
            {
                PhotonMenuSceneInfo candidate = availableScenes[i];
                if (!string.IsNullOrEmpty(selectedScene.ScenePath) &&
                    candidate.ScenePath == selectedScene.ScenePath)
                {
                    resolvedScene = candidate;
                    return true;
                }
            }

            for (int i = 0; i < availableScenes.Count; i++)
            {
                PhotonMenuSceneInfo candidate = availableScenes[i];
                if (!string.IsNullOrEmpty(selectedScene.Name) &&
                    candidate.Name == selectedScene.Name)
                {
                    resolvedScene = candidate;
                    return true;
                }
            }

            resolvedScene = availableScenes[0];
            return true;
        }
    }
}
