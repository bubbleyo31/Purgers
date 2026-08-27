using Fusion;
using UnityEngine;

/// <summary>
/// Rifle 正式命中時提供給特殊命中處理器的資料。
///
/// ====================================================================
///
/// 目的：
///
/// 不讓 AttackRifle 自己知道：
///
/// Support Healing
/// 特殊職業效果
/// 未來特殊彈藥
///
/// 等職業邏輯。
///
/// ====================================================================
///
/// AttackRifle 只會問：
///
/// 「這次命中有沒有其他系統要接手？」
///
/// 如果有系統回傳 true，
/// 代表這次命中已經被完整處理，
/// AttackRifle 不會再建立 DamageRequest。
///
/// 如果回傳 false，
/// AttackRifle 繼續正常傷害流程。
/// </summary>
public struct RifleHitContext
{
    // =====================================================================
    #region 攻擊者

    /// <summary>
    /// 這把 Rifle 真正所屬的 Player Core。
    /// </summary>
    public Player OwnerPlayer;

    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 不要使用 AttackRifle.Object 取代，
    /// 因為目前 Rifle 是存在 Profession Runtime NetworkObject 上。
    /// </summary>
    public NetworkObject OwnerPlayerNetworkObject;

    #endregion

    // =====================================================================
    #region 命中資料

    /// <summary>
    /// Raycast 真正命中的 GameObject。
    ///
    /// 可能是：
    ///
    /// Enemy Head
    /// Enemy Body
    /// Player Hitbox
    /// World Collider。
    /// </summary>
    public GameObject HitObject;

    /// <summary>
    /// 真正命中的世界座標。
    /// </summary>
    public Vector3 HitPoint;

    /// <summary>
    /// 命中表面的世界法線。
    /// </summary>
    public Vector3 HitNormal;

    /// <summary>
    /// 這發子彈真正飛行的世界方向。
    /// </summary>
    public Vector3 ShotDirection;

    /// <summary>
    /// 射擊起點到命中點的距離。
    /// </summary>
    public float Distance;

    #endregion

    // =====================================================================
    #region 射擊資料

    /// <summary>
    /// 這次射擊使用的 Damage Multiplier。
    ///
    /// Support 普通治療目前不會使用這個數值。
    ///
    /// 先保留下來是因為未來：
    ///
    /// 特殊子彈
    /// 卡牌
    /// 技能彈藥
    ///
    /// 可能需要知道這一發原本屬於什麼射擊狀態。
    /// </summary>
    public float ShotDamageMultiplier;

    /// <summary>
    /// AttackRifle 的 Shot Sequence。
    ///
    /// 未來可以用於：
    ///
    /// Healing Feedback
    /// Tracer 對應
    /// 防止重複 Presentation。
    /// </summary>
    public int ShotSequence;

    #endregion
}

/// <summary>
/// Rifle 特殊命中接管接口。
///
/// ====================================================================
///
/// 回傳 false：
///
/// 沒有處理這次命中。
///
/// AttackRifle 繼續：
///
/// DamageRequest
/// →
/// IDamageReceiver。
///
/// ====================================================================
///
/// 回傳 true：
///
/// 這次命中已經被完整接管。
///
/// AttackRifle 必須立即停止，
/// 不再對目標造成一般 Rifle Damage。
///
/// ====================================================================
///
/// Support Healing 就會使用這個接口：
///
/// Enemy
/// → false
/// → 正常傷害
///
/// Player
/// → true
/// → Healing
/// → 不造成傷害。
/// </summary>
public interface IRifleHitOverride
{
    /// <summary>
    /// 嘗試接管一次 Rifle 命中。
    /// </summary>
    /// <returns>
    /// true：
    /// 這次命中已經完整處理，
    /// AttackRifle 不可以再造成傷害。
    ///
    /// false：
    /// 沒有處理，
    /// AttackRifle 繼續原本傷害流程。
    /// </returns>
    bool TryConsumeHit(
        RifleHitContext context
    );
}