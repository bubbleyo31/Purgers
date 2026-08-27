using Fusion;
using UnityEngine;

/// <summary>
/// Grapple Interaction Target 的目標種類。
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// 這不是 Physics Layer。
///
/// Layer 只負責：
/// 「勾索 Raycast 能不能碰到這個物件。」
///
/// GrappleInteractionTargetType 則負責：
/// 「這個物件在 Gameplay 上到底是什麼。」
///
/// ------------------------------------------------------------
///
/// 例如同樣都位於 Enemy Hitbox Layer：
///
/// 普通敵人
/// Boss
/// 重型敵人
///
/// 仍然全部都是 Enemy。
///
/// 但它們能不能：
///
/// 被標記
/// 被聚怪
/// 被 Support 拉動
///
/// 會由 GrappleInteractionTarget 的能力設定決定。
/// </summary>
public enum GrappleInteractionTargetType : byte
{
    /// <summary>
    /// 尚未指定 Gameplay Target。
    /// </summary>
    None = 0,

    /// <summary>
    /// 敵對戰鬥單位。
    /// </summary>
    Enemy = 1,

    /// <summary>
    /// 玩家角色。
    /// </summary>
    Player = 2,

    /// <summary>
    /// 其他特殊 Gameplay 物件。
    ///
    /// 目前先保留。
    /// </summary>
    Object = 3
}

/// <summary>
/// 勾索命中後經過職業判斷，
/// 最後被路由成哪一種職業 Grapple Interaction。
///
/// ------------------------------------------------------------
///
/// 這只是「辨識結果」，
/// 目前這一階段不會真的執行技能。
/// </summary>
public enum GrappleProfessionInteractionType : byte
{
    /// <summary>
    /// 沒有任何職業特殊效果。
    /// </summary>
    None = 0,

    /// <summary>
    /// Attack 勾中可標記敵人。
    ///
    /// 下一階段會變成五秒獵殺標記。
    /// </summary>
    AttackMarkCandidate = 1,

    /// <summary>
    /// Tank 勾中可作為聚怪中心的敵人。
    /// </summary>
    TankGatherAnchorCandidate = 2,

    /// <summary>
    /// Support 勾中可被拉動的敵人。
    /// </summary>
    SupportEnemyPullCandidate = 3,

    /// <summary>
    /// Support 勾中可被拉動的玩家。
    /// </summary>
    SupportPlayerPullCandidate = 4
}

/// <summary>
/// 一次職業勾索互動的完整本地 Gameplay Context。
///
/// ------------------------------------------------------------
///
/// 這份資料不會直接作為 RPC Payload。
///
/// 它是 State Authority 在完成 Grapple Hit 判斷後，
/// 提供給後續職業技能模組使用的資料。
///
/// ------------------------------------------------------------
///
/// 未來：
///
/// AttackGrappleMarkAbility
/// TankGrappleGatherAbility
/// SupportGrapplePullAbility
///
/// 都可以直接吃這份 Context。
/// </summary>
public struct GrappleInteractionContext
{
    /// <summary>
    /// 發動這次勾索的玩家。
    /// </summary>
    public PlayerRef SourcePlayer;

    /// <summary>
    /// 發動勾索時的職業。
    /// </summary>
    public PlayerProfessionType SourceProfession;

    /// <summary>
    /// 命中的 Grapple Interaction Target。
    /// </summary>
    public GrappleInteractionTarget Target;

    /// <summary>
    /// 目標 Gameplay 類型。
    /// </summary>
    public GrappleInteractionTargetType TargetType;

    /// <summary>
    /// 目標所屬 NetworkObject。
    ///
    /// Enemy 與 Player 正式版本通常都應該有。
    /// </summary>
    public NetworkObject TargetNetworkObject;

    /// <summary>
    /// Grapple Raycast 實際命中的 Collider。
    /// </summary>
    public Collider HitCollider;

    /// <summary>
    /// 勾索真正命中的世界座標。
    /// </summary>
    public Vector3 HitPoint;

    /// <summary>
    /// 命中表面的世界法線。
    /// </summary>
    public Vector3 HitNormal;

    /// <summary>
    /// 這次命中最後被路由成哪一種職業互動。
    /// </summary>
    public GrappleProfessionInteractionType InteractionType;
}