#if UNITY_EDITOR
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 僅供扇波受控雙 Runner 驗證：使用 Fusion 建立的各自 Physics Scene，
/// 只容納動態生成測試物件，不載入正式關卡，因此不驗證場景切換。
/// </summary>
public sealed class ShockwaveVerificationSceneManager : NetworkSceneManagerDefault, INetworkSceneManager
{
    public override bool IsBusy => false;

    bool INetworkSceneManager.MoveGameObjectToScene(GameObject target, SceneRef sceneRef)
    {
        if (!MultiPeerScene.IsValid() || !MultiPeerScene.isLoaded) return false;
        SceneManager.MoveGameObjectToScene(target, MultiPeerScene);
        return true;
    }
}
#endif
