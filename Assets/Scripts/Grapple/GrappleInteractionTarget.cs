using Fusion;
using UnityEngine;

/// <summary>
/// 告訴玩家勾索系統：
///
/// 「這個物件是一個可以產生職業 Grapple Interaction 的目標。」
///
/// ------------------------------------------------------------
///
/// 這支腳本不執行任何職業技能。
///
/// 它只描述目標的 Gameplay 身分與能力。
///
/// ------------------------------------------------------------
///
/// 例如普通敵人：
///
/// Target Type
/// Enemy
///
/// Can Receive Attack Mark
/// true
///
/// Can Act As Tank Gather Anchor
/// true
///
/// Can Be Tank Gathered
/// true
///
/// Can Be Support Pulled
/// true
///
/// ------------------------------------------------------------
///
/// Boss 則可能是：
///
/// Enemy
///
/// Attack Mark
/// true
///
/// Tank Gather Anchor
/// true
///
/// Tank Gathered
/// false
///
/// Support Pulled
/// false
///
/// ------------------------------------------------------------
///
/// 所以 Layer 不再負責判斷敵人能力。
/// </summary>
[DisallowMultipleComponent]
public class GrappleInteractionTarget :
    MonoBehaviour
{
    // =====================================================================
    #region 目標類型

    [Header("目標類型")]

    [SerializeField]
    [Tooltip("這個 Grapple Interaction Target 在 Gameplay 上的種類。Enemy 代表敵人，Player 代表玩家，Object 預留給未來特殊互動物件。")]
    private GrappleInteractionTargetType targetType =
        GrappleInteractionTargetType.Enemy;

    #endregion

    // =====================================================================
    #region Attack

    [Header("Attack 勾索能力")]

    [SerializeField]
    [Tooltip("這個目標是否允許受到 Attack 職業的勾索標記。下一階段 Attack 勾中這種敵人後，會由施加標記的 Attack 玩家在五秒內把自己對此目標造成的傷害視為暴頭效果。")]
    private bool canReceiveAttackMark =
        true;

    #endregion

    // =====================================================================
    #region Tank

    [Header("Tank 勾索能力")]

    [SerializeField]
    [Tooltip("這個敵人是否可以成為 Tank 聚怪技能的中心目標。Tank 直接勾中的敵人 A 就是 Gather Anchor，玩家之後會往這個敵人移動。")]
    private bool canActAsTankGatherAnchor =
        true;

    [SerializeField]
    [Tooltip("這個敵人是否允許被其他 Tank 聚怪中心吸引。例如普通敵人可以開啟，Boss 或重型不可位移敵人可以關閉。")]
    private bool canBeTankGathered =
        true;

    #endregion

    // =====================================================================
    #region Support

    [Header("Support 勾索能力")]

    [SerializeField]
    [Tooltip("這個目標是否允許被 Support 勾索拉向 Support 玩家面前。普通敵人與玩家可以開啟，Boss 或特殊不可位移敵人可以關閉。")]
    private bool canBeSupportPulled =
        true;

    #endregion

    // =====================================================================
    #region 互動位置

    [Header("互動位置")]

    [SerializeField]
    [Tooltip("職業 Grapple 技能需要一個代表目標中心的位置時優先使用這個 Transform。例如敵人胸口、角色中心或大型怪物的拉動中心。若留空會退回 NetworkObject Root，再沒有才使用此物件 Transform。")]
    private Transform interactionPoint;

    #endregion

    // =====================================================================
    #region Runtime Cache

    /// <summary>
    /// 這個 Target 所屬的 NetworkObject。
    /// </summary>
    private NetworkObject ownerNetworkObject;

    /// <summary>
    /// 如果這個 Gameplay Target
    /// 有生命狀態，
    /// 在這裡快取。
    ///
    /// FocusTarget 目前也是使用相同標準。
    /// </summary>
    private ICombatLifeState combatLifeState;

    /// <summary>
    /// Unity Component 形式的生命狀態引用。
    ///
    /// 用來處理 Unity Destroy Null 判斷。
    /// </summary>
    private MonoBehaviour combatLifeStateBehaviour;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目標種類。
    /// </summary>
    public GrappleInteractionTargetType TargetType =>
        targetType;

    /// <summary>
    /// 目標所屬 NetworkObject。
    /// </summary>
    public NetworkObject OwnerNetworkObject =>
        ownerNetworkObject;

    /// <summary>
    /// 是否允許 Attack 標記。
    /// </summary>
    public bool CanReceiveAttackMark =>
        canReceiveAttackMark;

    /// <summary>
    /// 是否可以成為 Tank 聚怪中心。
    /// </summary>
    public bool CanActAsTankGatherAnchor =>
        canActAsTankGatherAnchor;

    /// <summary>
    /// 是否允許被 Tank 聚怪拉動。
    ///
    /// 這個值主要會在搜尋 B、C、D 時使用。
    /// </summary>
    public bool CanBeTankGathered =>
        canBeTankGathered;

    /// <summary>
    /// 是否允許被 Support 拉動。
    /// </summary>
    public bool CanBeSupportPulled =>
        canBeSupportPulled;

    /// <summary>
    /// 如果這個目標具有生命系統，
    /// 回傳目前是否仍然存活。
    ///
    /// 沒有 ICombatLifeState 的特殊 Gameplay Object
    /// 預設仍然視為有效。
    /// </summary>
    public bool IsInteractionAvailable
    {
        get
        {
            if (isActiveAndEnabled == false)
            {
                return false;
            }

            if (combatLifeStateBehaviour != null)
            {
                if (combatLifeState == null)
                {
                    return false;
                }

                if (combatLifeState.IsAlive ==
                    false)
                {
                    return false;
                }
            }

            return true;
        }
    }

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        CacheNetworkObject();
        CacheCombatLifeState();
    }

    #endregion

    // =====================================================================
    #region Network Object

    /// <summary>
    /// 搜尋這個 Target 所屬 NetworkObject。
    /// </summary>
    private void CacheNetworkObject()
    {
        ownerNetworkObject =
            GetComponentInParent<NetworkObject>();
    }

    #endregion

    // =====================================================================
    #region Combat Life State

    /// <summary>
    /// 搜尋這個 Target 所屬階層中的
    /// ICombatLifeState。
    ///
    /// ------------------------------------------------------------
///
/// 例如：
///
/// TestEnemy
/// ├─ TestDamageReceiver
/// │    └─ ICombatLifeState
/// │
/// └─ GrappleInteractionTarget
///
/// 就會找到 TestDamageReceiver。
///
/// ------------------------------------------------------------
///
/// 如果目前 Player 還沒有 PlayerHealth，
/// 找不到 ICombatLifeState 也沒關係。
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
                ICombatLifeState foundLifeState)
            {
                combatLifeState =
                    foundLifeState;

                combatLifeStateBehaviour =
                    behaviour;

                return;
            }
        }
    }

    #endregion

    // =====================================================================
    #region Interaction Point

    /// <summary>
    /// 取得後續職業能力應該使用的代表位置。
    ///
    /// 優先順序：
///
/// Interaction Point
/// ↓
/// NetworkObject Root
/// ↓
/// GrappleInteractionTarget Transform。
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// 目前一般 Grapple 繩索仍然使用真正 Raycast Hit Point。
///
/// 這個位置是給未來：
///
/// Tank 聚怪
/// Support Pull
///
/// 等 Gameplay Skill 使用。
/// </summary>
    public Vector3 GetInteractionPosition()
    {
        if (interactionPoint != null)
        {
            return interactionPoint.position;
        }

        if (ownerNetworkObject != null)
        {
            return
                ownerNetworkObject.transform.position;
        }

        return transform.position;
    }

    #endregion
}