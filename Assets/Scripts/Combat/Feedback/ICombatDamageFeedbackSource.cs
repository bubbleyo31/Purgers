using System;

/// <summary>
/// 所有「玩家造成傷害後，可以提供正式命中回饋」的傷害來源
/// 都實作這個介面。
///
/// ------------------------------------------------------------
///
/// PlayerCombatFeedbackRelay 不應該知道：
///
/// AttackRifle
/// AttackQuickMelee
/// TankLightAttack
/// TankHeavyAttack
/// TankQuickDash
/// TankAirStrike。
///
/// ------------------------------------------------------------
///
/// Relay 只知道：
///
/// ICombatDamageFeedbackSource
///
/// ↓
/// DamageConfirmed
///
/// ------------------------------------------------------------
///
/// 實作者只需要在：
///
/// DamageResult.Accepted == true
///
/// 的正式傷害成功後觸發 DamageConfirmed。
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// 這個 Event 是 Gameplay Authority → Feedback Relay 的入口。
///
/// 這裡不能直接播放：
///
/// UI
/// Camera Shake
/// Hit Sound。
///
/// Presentation 仍然由：
///
/// PlayerCombatFeedbackRelay
/// ↓
/// PlayerHitFeedbackController
///
/// 處理。
/// </summary>
public interface ICombatDamageFeedbackSource
{
    /// <summary>
    /// State Authority 已經正式確認
    /// 這次 DamageResult 是有效命中時觸發。
    /// </summary>
    event Action<DamageResult>
        DamageConfirmed;
}