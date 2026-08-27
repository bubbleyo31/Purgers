using Fusion;
using UnityEngine;
using UnityEngine.Serialization;


/// <summary>
/// 本地玩家準心瞄準合法勾索點時，
/// 依照真正命中的 Collider Layer 顯示對應提示。
///
/// ====================================================================
///
/// 提示狀態：
///
/// Hidden
/// → 沒有合法勾索點，兩種提示都隱藏。
///
/// Grapple
/// → 命中一般合法勾索點，顯示普通 Grapple 提示。
///
/// Enemy
/// → 命中 Enemy HitMask，顯示 Enemy 提示。
///
/// ====================================================================
///
/// 真正的勾索合法性仍然全部由：
///
/// PlayerGrapple.TryGetValidGrappleAimHit()
///
/// 負責檢查：
///
/// 1. Gameplay Aim Direction。
/// 2. Runner PhysicsScene。
/// 3. Grapple Distance。
/// 4. 目前職業 Grapple Mask。
/// 5. 不可命中玩家自己。
///
/// 本 UI 新增的 Enemy Hit Mask 只負責決定「顯示哪張圖」，
/// 不會把原本不能鈎的 Layer 變成合法勾索目標。
///
/// ====================================================================
///
/// 玩家死亡後舊 Player NetworkObject 會被 Despawn，
/// 重生時會建立全新的 PlayerGrapple。
///
/// 因此本腳本仍會從 Runner PlayerObject：
///
/// 自動解除舊 PlayerGrapple
/// 自動綁定新 PlayerGrapple。
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerGrappleAimIndicator :
    MonoBehaviour
{
    // =====================================================================
    #region Indicator State

    /// <summary>
    /// 本地可鈎索提示目前應呈現的狀態。
    ///
    /// 使用單一狀態而不是兩個獨立 bool，
    /// 從資料結構上保證普通提示與 Enemy 提示互斥。
    /// </summary>
    private enum GrappleAimIndicatorState : byte
    {
        Hidden = 0,
        Grapple = 1,
        Enemy = 2
    }

    #endregion

    // =====================================================================
    #region Runner

    [Header("Fusion Runner")]

    [SerializeField]
    [Tooltip(
        "目前 Gameplay Session 使用的 NetworkRunner。\n\n" +
        "如果 Runner 是執行期間建立，可以留空；" +
        "HUD 會自動尋找目前正在執行的 NetworkRunner。")]
    private NetworkRunner runner;

    #endregion

    // =====================================================================
    #region Indicator Roots

    [Header("可鈎索提示物件")]

    [FormerlySerializedAs("validIndicatorRoot")]
    [SerializeField]
    [Tooltip(
        "準心命中一般合法勾索點時顯示的 UI Root。\n\n" +
        "請指定 PlayerHUDController 底下的 validIndicatorforGappleRoot。\n\n" +
        "FormerlySerializedAs 會嘗試保留上一版 Valid Indicator Root 的 Inspector 引用，" +
        "但替換腳本後仍請親自確認一次。\n\n" +
        "不可指定掛有 LocalPlayerGrappleAimIndicator 的同一個 GameObject。")]
    private GameObject validIndicatorForGrappleRoot;

    [SerializeField]
    [Tooltip(
        "準心命中 Enemy Hit Mask 時顯示的 UI Root。\n\n" +
        "請指定 PlayerHUDController 底下的 validIndicatorforEnemyRoot。\n\n" +
        "Enemy 提示優先於一般 Grapple 提示，兩者不會同時顯示。\n\n" +
        "不可指定掛有 LocalPlayerGrappleAimIndicator 的同一個 GameObject。")]
    private GameObject validIndicatorForEnemyRoot;

    #endregion

    // =====================================================================
    #region Enemy Classification

    [Header("Enemy HitMask 分類")]

    [SerializeField]
    [Tooltip(
        "哪些 Layer 代表 Enemy 的戰鬥 HitMask Collider。\n\n" +
        "請勾選目前 Enemy Fusion Hitbox 真正使用的 HitMask Layer；" +
        "不要勾 Ground、Wall、Environment 或 Player。\n\n" +
        "這個 Mask 只決定合法命中後要顯示 Enemy 提示還是普通 Grapple 提示，" +
        "不會改變 PlayerGrapple 的合法命中 Layer。")]
    private LayerMask enemyHitMask;

    [SerializeField]
    [Tooltip(
        "開啟後，除了目標與距離合法，還要求勾索目前真的可以施放：\n" +
        "Current Phase = Idle，而且至少有一格 Grapple Charge。\n\n" +
        "建議保持開啟，避免沒有充能或正在收繩時仍顯示提示。")]
    private bool requireGrappleReady =
        true;

    #endregion

    // =====================================================================
    #region Runtime State

    /// <summary>
    /// 目前正在查詢的本地 PlayerGrapple。
    /// 死亡 Despawn 後會清除，重生後重新綁定。
    /// </summary>
    private PlayerGrapple boundGrapple;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        ValidateIndicatorRoots();

        if (enemyHitMask.value == 0)
        {
            Debug.LogWarning(
                "[Grapple Aim UI] Enemy Hit Mask 目前沒有選擇任何 Layer。" +
                "在指定之前，所有合法命中都只會顯示普通 Grapple 提示。",
                this
            );
        }

        TryResolveRunner();

        ApplyIndicatorState(
            GrappleAimIndicatorState.Hidden
        );
    }

    private void Update()
    {
        if (validIndicatorForGrappleRoot == null &&
            validIndicatorForEnemyRoot == null)
        {
            return;
        }

        if (TryResolveRunner() == false ||
            TryResolveLocalPlayerGrapple(
                out PlayerGrapple currentGrapple
            ) == false)
        {
            boundGrapple =
                null;

            ApplyIndicatorState(
                GrappleAimIndicatorState.Hidden
            );

            return;
        }

        if (boundGrapple !=
            currentGrapple)
        {
            boundGrapple =
                currentGrapple;

            /*
             * 新玩家剛綁定時先清除舊提示，
             * 本 Frame 後面的合法命中判斷通過後才顯示。
             */
            ApplyIndicatorState(
                GrappleAimIndicatorState.Hidden
            );
        }

        bool grappleReady =
            requireGrappleReady == false ||
            boundGrapple.CanUseGrapple;

        if (grappleReady == false ||
            boundGrapple.TryGetValidGrappleAimHit(
                out RaycastHit hit
            ) == false)
        {
            ApplyIndicatorState(
                GrappleAimIndicatorState.Hidden
            );

            return;
        }

        /*
         * PlayerGrapple 已確認：
         *
         * 1. 命中 Layer 合法。
         * 2. 距離在 Grapple Distance 內。
         * 3. 沒有命中自己。
         *
         * UI 現在只分類真正被命中的 Collider Layer。
         */
        bool hitEnemyMask =
            IsLayerInMask(
                hit.collider.gameObject.layer,
                enemyHitMask
            );

        ApplyIndicatorState(
            hitEnemyMask
                ? GrappleAimIndicatorState.Enemy
                : GrappleAimIndicatorState.Grapple
        );
    }

    private void OnDisable()
    {
        boundGrapple =
            null;

        ApplyIndicatorState(
            GrappleAimIndicatorState.Hidden
        );
    }

    #endregion

    // =====================================================================
    #region Validation

    /// <summary>
    /// 驗證兩個提示 Root 不會關閉控制腳本本身，
    /// 也不能誤填成同一個 UI 物件。
    /// </summary>
    private void ValidateIndicatorRoots()
    {
        if (validIndicatorForGrappleRoot == null)
        {
            Debug.LogError(
                "[Grapple Aim UI] 尚未指定 Valid Indicator For Grapple Root。",
                this
            );
        }
        else if (validIndicatorForGrappleRoot ==
                 gameObject)
        {
            Debug.LogError(
                "[Grapple Aim UI] Valid Indicator For Grapple Root 不可是掛有控制腳本的同一個 GameObject。",
                this
            );

            validIndicatorForGrappleRoot =
                null;
        }

        if (validIndicatorForEnemyRoot == null)
        {
            Debug.LogError(
                "[Grapple Aim UI] 尚未指定 Valid Indicator For Enemy Root。",
                this
            );
        }
        else if (validIndicatorForEnemyRoot ==
                 gameObject)
        {
            Debug.LogError(
                "[Grapple Aim UI] Valid Indicator For Enemy Root 不可是掛有控制腳本的同一個 GameObject。",
                this
            );

            validIndicatorForEnemyRoot =
                null;
        }

        if (validIndicatorForGrappleRoot != null &&
            validIndicatorForGrappleRoot ==
                validIndicatorForEnemyRoot)
        {
            Debug.LogError(
                "[Grapple Aim UI] 普通 Grapple 與 Enemy 提示不可指定成同一個 GameObject。" +
                "請分別指定兩個獨立 UI Root。",
                this
            );

            /*
             * 保留普通提示，停用錯誤的 Enemy 引用。
             * 避免同一物件在一個 Frame 內被要求同時開啟與關閉。
             */
            validIndicatorForEnemyRoot =
                null;
        }
    }

    #endregion

    // =====================================================================
    #region Runner / Player Resolve

    private bool TryResolveRunner()
    {
        if (runner != null &&
            runner.IsRunning)
        {
            return true;
        }

        runner =
            FindFirstObjectByType<NetworkRunner>();

        return
            runner != null &&
            runner.IsRunning;
    }

    private bool TryResolveLocalPlayerGrapple(
        out PlayerGrapple playerGrapple
    )
    {
        playerGrapple =
            null;

        if (runner == null ||
            runner.IsRunning == false ||
            runner.LocalPlayer.IsRealPlayer == false)
        {
            return false;
        }

        if (runner.TryGetPlayerObject(
                runner.LocalPlayer,
                out NetworkObject playerObject
            ) == false ||
            playerObject == null ||
            playerObject.IsValid == false)
        {
            return false;
        }

        playerGrapple =
            playerObject.GetComponent<PlayerGrapple>();

        return
            playerGrapple != null;
    }

    #endregion

    // =====================================================================
    #region Layer Classification

    /// <summary>
    /// 判斷指定 Layer 是否包含在 LayerMask 中。
    ///
    /// 必須使用真正被 Raycast 命中的 Collider GameObject Layer，
    /// 不可改用 Enemy Root Layer；Fusion Hitbox 常位於子物件 Layer。
    /// </summary>
    private static bool IsLayerInMask(
        int layer,
        LayerMask layerMask
    )
    {
        int layerBit =
            1 << layer;

        return
            (layerMask.value & layerBit) !=
            0;
    }

    #endregion

    // =====================================================================
    #region Visual

    /// <summary>
    /// 套用唯一的提示狀態。
    ///
    /// Enemy 與 Grapple Root 由同一個狀態決定，
    /// 因此不會發生兩張圖同時顯示。
    /// </summary>
    private void ApplyIndicatorState(
        GrappleAimIndicatorState state
    )
    {
        bool showGrapple =
            state ==
            GrappleAimIndicatorState.Grapple;

        bool showEnemy =
            state ==
            GrappleAimIndicatorState.Enemy;

        SetRootActive(
            validIndicatorForGrappleRoot,
            showGrapple
        );

        SetRootActive(
            validIndicatorForEnemyRoot,
            showEnemy
        );
    }

    /// <summary>
    /// 只有 GameObject 真正需要改變時才呼叫 SetActive。
    /// </summary>
    private static void SetRootActive(
        GameObject root,
        bool active
    )
    {
        if (root == null ||
            root.activeSelf == active)
        {
            return;
        }

        root.SetActive(
            active
        );
    }

    #endregion
}