using Fusion;
using UnityEngine;

/// <summary>
/// 本地第一人稱玩家視覺綁定器。
///
/// 只有具有 Input Authority 的玩家會啟動。
///
/// 負責：
/// 1. CamTarget LateUpdate。
/// 2. CameraFollow Target。
/// 3. 隱藏本地第三人稱模型。
/// 4. FirstPersonMovementEffects。
/// 5. GrappleFovEffect。
/// 6. ProfessionViewModelManager。
/// 7. PlayerGrappleVisual。
///
/// 不負責任何遊戲物理或網路玩法。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerGrappleVisual))]
public class PlayerLocalView : NetworkBehaviour
{
    // =====================================================================
    #region 引用

    [Header("玩家引用")]

    [SerializeField]
    [Tooltip("玩家根控制器。若留空會自動取得。")]
    private Player player;

    [SerializeField]
    [Tooltip("玩家移動模組。負責提供 CamTarget 並更新 Pitch。若留空會自動取得。")]
    private PlayerMovement movement;

    [SerializeField]
    [Tooltip("玩家職業資料。交給 ProfessionViewModelManager 使用。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("第一人稱勾索 LineRenderer 視覺。若留空會自動取得。")]
    private PlayerGrappleVisual grappleVisual;

    #endregion

    // =====================================================================
    #region 第三人稱本體

    [Header("本地第三人稱模型")]

    [SerializeField]
    [Tooltip("第一人稱本地玩家需要隱藏，但仍保留影子的第三人稱 MeshRenderer。遠端玩家不受影響。")]
    private MeshRenderer[] modelParts;

    #endregion

    // =====================================================================
    #region 執行狀態

    /// <summary>
    /// 是否已經完成本地玩家綁定。
    /// </summary>
    private bool localPlayerBound;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (player == null)
        {
            player =
                GetComponent<Player>();
        }

        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (grappleVisual == null)
        {
            grappleVisual =
                GetComponent<PlayerGrappleVisual>();
        }
    }

    private void LateUpdate()
    {
        if (localPlayerBound == false)
            return;

        /*
         * Camera Target 只在本地視覺 LateUpdate 更新一次。
         *
         * 不再於：
/// FixedUpdateNetwork
/// Fusion Render
/// 重複寫 Transform。
         */
        movement.UpdateCameraTargetVisual();
    }

    private void OnDestroy()
    {
        CleanupLocalView();
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (Object.HasInputAuthority == false)
            return;

        localPlayerBound =
            true;

        // -------------------------------------------------------------
        // 隱藏本地第三人稱模型
        // -------------------------------------------------------------

        if (modelParts != null)
        {
            foreach (
                MeshRenderer renderer
                in modelParts
            )
            {
                if (renderer == null)
                    continue;

                renderer.shadowCastingMode =
                    UnityEngine.Rendering
                        .ShadowCastingMode
                        .ShadowsOnly;
            }
        }

        // -------------------------------------------------------------
        // Camera
        // -------------------------------------------------------------

        if (CameraFollow.Singleton != null &&
            movement.CamTarget != null)
        {
            CameraFollow.Singleton.SetTarget(
                movement.CamTarget
            );
        }
        else if (CameraFollow.Singleton == null)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerLocalView)}] " +
                $"場景中找不到 CameraFollow。",
                this
            );
        }

        // -------------------------------------------------------------
        // 第一人稱移動特效
        // -------------------------------------------------------------

        if (FirstPersonMovementEffects.Singleton != null)
        {
            FirstPersonMovementEffects.Singleton.SetTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // 可滑鏟速度提示
        // -------------------------------------------------------------

        /*
        * 使用獨立的 ParticleSystem 與獨立水平速度門檻。
        * 不會影響原本的 FirstPersonMovementEffects。
        */
        if (FirstPersonSlideReadySpeedLines.Singleton != null)
        {
            FirstPersonSlideReadySpeedLines.Singleton.SetTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // 勾索 FOV
        // -------------------------------------------------------------

        if (GrappleFovEffect.Singleton != null)
        {
            GrappleFovEffect.Singleton.SetTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // 職業 ViewModel
        // -------------------------------------------------------------

        if (ProfessionViewModelManager.Singleton != null)
        {
            ProfessionViewModelManager.Singleton.SetTarget(
                profession
            );
        }

        // -------------------------------------------------------------
        // 勾索 LineRenderer
        // -------------------------------------------------------------

        if (grappleVisual != null)
        {
            grappleVisual.BindLocalVisual();
        }
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        CleanupLocalView();
    }

    #endregion

    // =====================================================================
    #region 清理

    private void CleanupLocalView()
    {
        if (localPlayerBound == false)
            return;

        localPlayerBound =
            false;

        // -------------------------------------------------------------
        // 勾索 Visual
        // -------------------------------------------------------------

        if (grappleVisual != null)
        {
            grappleVisual.UnbindLocalVisual();
        }

        // -------------------------------------------------------------
        // Camera
        // -------------------------------------------------------------

        if (CameraFollow.Singleton != null &&
            movement != null &&
            movement.CamTarget != null)
        {
            CameraFollow.Singleton.ClearTarget(
                movement.CamTarget
            );
        }

        // -------------------------------------------------------------
        // 移動 FX
        // -------------------------------------------------------------

        if (FirstPersonMovementEffects.Singleton != null &&
            player != null)
        {
            FirstPersonMovementEffects.Singleton.ClearTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // 可滑鏟速度提示
        // -------------------------------------------------------------

        if (FirstPersonSlideReadySpeedLines.Singleton != null &&
            player != null)
        {
            FirstPersonSlideReadySpeedLines.Singleton.ClearTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // FOV
        // -------------------------------------------------------------

        if (GrappleFovEffect.Singleton != null &&
            player != null)
        {
            GrappleFovEffect.Singleton.ClearTarget(
                player
            );
        }

        // -------------------------------------------------------------
        // ViewModel
        // -------------------------------------------------------------

        if (ProfessionViewModelManager.Singleton != null &&
            profession != null)
        {
            ProfessionViewModelManager.Singleton.ClearTarget(
                profession
            );
        }
    }

    #endregion
}