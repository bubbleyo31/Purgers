using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 專注技能可以自動鎖定的目標。
///
/// 建議掛在敵人的 Root 物件上。
///
/// 例如：
///
/// Enemy
/// ├─ FocusTarget
/// │
/// ├─ Body
/// │  ├─ Hitbox
/// │  └─ WeaponHitZone = Body
/// │
/// ├─ Head
/// │  ├─ Hitbox
/// │  └─ WeaponHitZone = Head
/// │
/// └─ FocusAimPoint
///    └─ 放在胸口附近
///
/// FocusTarget 本身不負責：
/// 1. 扣血。
/// 2. 暴頭。
/// 3. 射擊。
/// 4. 移動。
///
/// 它只負責告訴 AttackFocusAbility：
///
/// 「這個敵人可以被專注技能選為自動鎖定目標。」
/// </summary>
[DisallowMultipleComponent]
public class FocusTarget : MonoBehaviour
{
    // =====================================================================
    #region 全域目標登錄

    /// <summary>
    /// 目前場景中所有啟用中的 FocusTarget。
    ///
    /// AttackFocusAbility 只會在 Focus Shot 真正發生時搜尋，
    /// 不會每個 Update 都掃描。
    ///
    /// 因此目前不需要 Physics.OverlapSphere
    /// 或 FindObjectsByType 每次重新搜尋。
    /// </summary>
    private static readonly List<FocusTarget> activeTargets =
        new List<FocusTarget>();

    /// <summary>
    /// 所有目前可被搜尋的 FocusTarget。
    /// </summary>
    public static IReadOnlyList<FocusTarget> ActiveTargets =>
        activeTargets;

    #endregion

    // =====================================================================
    #region 目標設定

    [Header("專注鎖定位置")]

    [SerializeField]
    [Tooltip("專注子彈自動鎖定時瞄準的位置。建議放在敵人胸口或身體中心，不要放在頭部。這樣專注自動鎖定本身不會免費產生暴頭。如果留空，就使用 FocusTarget 物件自己的 Transform 位置。")]
    private Transform aimPoint;

    [SerializeField]
    [Tooltip("如果敵人使用 Photon Fusion Hitbox，建議指定一個身體 Hitbox 作為專注鎖定位置的 Lag Compensation 參考。系統會利用這個 Hitbox 取得射擊玩家當時看到的歷史位置。如果目前只是普通 Collider 測試，可以留空。")]
    private Hitbox lagCompensationReferenceHitbox;

    [Header("目標狀態")]

    [SerializeField]
    [Tooltip("是否允許這個敵人成為專注自動鎖定目標。未來敵人死亡、無敵、隱形或特殊狀態時，可以暫時關閉。")]
    private bool targetable = true;

    #endregion

    // =====================================================================
    #region 快取

    /// <summary>
    /// 這個敵人所屬的 NetworkObject。
    ///
    /// 主要用來防止玩家鎖定自己。
    /// </summary>
    private NetworkObject ownerNetworkObject;

    /// <summary>
    /// 這個 FocusTarget 所屬目標的生命狀態。
    ///
    /// FocusTarget 不直接依賴：
    ///
    /// EnemyHealth
    /// TestDamageReceiver
    /// BossHealth
    ///
    /// 而是只讀取共用接口：
    ///
    /// ICombatLifeState。
    ///
    /// 如果這個物件沒有任何 ICombatLifeState，
    /// 系統仍然允許它被鎖定，
    /// 方便目前使用普通測試 Cube。
    /// </summary>
    private ICombatLifeState combatLifeState;

    /// <summary>
    /// 實際提供 ICombatLifeState 的 MonoBehaviour。
    ///
    /// 額外保留 Component 引用是因為
    /// Unity 被 Destroy 的 Component
    /// 有自己的 Null 判定行為。
    /// </summary>
    private MonoBehaviour combatLifeStateBehaviour;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 這個物件目前是否允許成為 Focus Auto Lock 的候選目標。
    ///
    /// 必須同時符合：
    ///
    /// 1. FocusTarget 本身正在啟用。
    ///
    /// 2. Targetable 沒有被其他 Gameplay 系統手動關閉。
    ///
    /// 3. 如果目標具有 ICombatLifeState，
    ///    則必須 IsAlive = true。
    ///
    /// ------------------------------------------------------------
    ///
    /// 因此：
    ///
    /// 活著的敵人
    /// → 可以鎖。
    ///
    /// 死亡敵人
    /// → 自動不能鎖。
    ///
    /// 沒有生命系統的測試 Cube
    /// → 仍然可以鎖。
    /// </summary>
    public bool IsTargetable
    {
        get
        {
            // ---------------------------------------------------------
            // FocusTarget 本身沒有啟用
            // ---------------------------------------------------------

            if (isActiveAndEnabled ==
                false)
            {
                return false;
            }

            // ---------------------------------------------------------
            // Gameplay 手動禁止鎖定
            // ---------------------------------------------------------

            if (targetable ==
                false)
            {
                return false;
            }

            // ---------------------------------------------------------
            // 有生命系統
            // ---------------------------------------------------------

            /*
            * Component 還存在，
            * 代表我們可以讀取生命狀態。
            */
            if (combatLifeStateBehaviour !=
                null)
            {
                /*
                * 已死亡：
                *
                * 不再允許被 Focus 鎖定。
                */
                if (combatLifeState == null ||
                    combatLifeState.IsAlive ==
                    false)
                {
                    return false;
                }
            }

            // ---------------------------------------------------------
            // 所有條件通過
            // ---------------------------------------------------------

            return true;
        }
    }

    /// <summary>
    /// 這個目標所屬的 NetworkObject。
    /// </summary>
    public NetworkObject OwnerNetworkObject =>
        ownerNetworkObject;

    /// <summary>
    /// 實際設定的專注瞄準點。
    /// </summary>
    public Transform AimPoint =>
        aimPoint != null
            ? aimPoint
            : transform;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        ownerNetworkObject =
            GetComponentInParent<NetworkObject>();

        /*
        * 尋找這個 FocusTarget 所屬物件
        * 是否有提供標準生命狀態。
        */
        CacheCombatLifeState();
    }

    private void OnEnable()
    {
        if (activeTargets.Contains(this) == false)
        {
            activeTargets.Add(
                this
            );
        }
    }

    private void OnDisable()
    {
        activeTargets.Remove(
            this
        );
    }

    private void OnDestroy()
    {
        activeTargets.Remove(
            this
        );
    }

    #endregion

    // =====================================================================
    #region 目標位置

    /// <summary>
    /// 取得這個目標對指定射擊玩家來說，
    /// 應該使用的專注瞄準位置。
    ///
    /// 如果有指定 Fusion Hitbox：
    ///
    /// 使用 Lag Compensation PositionRotation
    /// 取得玩家當時看到的 Hitbox 歷史位置。
    ///
    /// 如果沒有 Hitbox：
    ///
    /// 直接使用目前 AimPoint 世界座標。
    /// </summary>
    public Vector3 GetAimPosition(
        NetworkRunner runner,
        PlayerRef viewingPlayer,
        bool useSubtickAccuracy
    )
    {
        Transform targetPoint =
            AimPoint;

        if (lagCompensationReferenceHitbox == null ||
            runner == null ||
            runner.LagCompensation == null)
        {
            return targetPoint.position;
        }

        /*
         * 先記錄 AimPoint 相對於參考 Hitbox 的局部位置。
         *
         * 例如：
         *
         * Body Hitbox 中心
         * ↓
         * AimPoint 稍微往胸口上方偏移
         *
         * 這個 Offset 會跟著歷史 Hitbox
         * Position / Rotation 一起回溯。
         */
        Vector3 localOffset =
            lagCompensationReferenceHitbox
                .transform
                .InverseTransformPoint(
                    targetPoint.position
                );

        runner.LagCompensation.PositionRotation(
            lagCompensationReferenceHitbox,
            viewingPlayer,
            out Vector3 historicalPosition,
            out Quaternion historicalRotation,
            useSubtickAccuracy
        );

        return historicalPosition +
               historicalRotation *
               localOffset;
    }

    #endregion

    // =====================================================================
    #region 命中歸屬

    /// <summary>
    /// 判斷某個被 Raycast 命中的 GameObject
    /// 是否屬於這個 FocusTarget。
    ///
    /// 例如射線命中：
    /// Enemy/Body
    ///
    /// FocusTarget 掛在：
    /// Enemy
    ///
    /// 仍然會正確回傳 true。
    /// </summary>
    public bool OwnsHit(
        GameObject hitObject
    )
    {
        if (hitObject == null)
            return false;

        FocusTarget hitTarget =
            hitObject.GetComponentInParent<FocusTarget>();

        return hitTarget ==
               this;
    }

    /// <summary>
    /// 搜尋這個 FocusTarget 所屬階層中
    /// 是否存在 ICombatLifeState。
    ///
    /// ------------------------------------------------------------
    ///
    /// 例如：
    ///
    /// TestEnemy
    /// ├─ TestDamageReceiver
    /// │    └─ ICombatLifeState
    /// │
    /// └─ Body
    ///      └─ FocusTarget
    ///
    /// 即使 FocusTarget 不在 Root，
    /// 也可以往父物件找到生命狀態。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前只在 Awake 時搜尋一次，
    /// 不會每次 Focus Shot 都重新掃描 Component。
    /// </summary>
    private void CacheCombatLifeState()
    {
        combatLifeState =
            null;

        combatLifeStateBehaviour =
            null;

        MonoBehaviour[] behaviours =
            GetComponentsInParent<MonoBehaviour>(
                true
            );

        for (int i = 0;
            i < behaviours.Length;
            i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
                continue;

            if (behaviour is
                ICombatLifeState lifeState)
            {
                combatLifeState =
                    lifeState;

                combatLifeStateBehaviour =
                    behaviour;

                return;
            }
        }
    }

    #endregion

    // =====================================================================
    #region 外部控制

    /// <summary>
    /// 修改這個敵人是否可以被專注自動鎖定。
    ///
    /// 未來 EnemyHealth 死亡時可以呼叫：
///
/// SetTargetable(false);
    /// </summary>
    public void SetTargetable(
        bool value
    )
    {
        targetable =
            value;
    }

    #endregion
}