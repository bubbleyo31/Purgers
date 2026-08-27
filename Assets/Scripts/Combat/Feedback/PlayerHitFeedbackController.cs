using Fusion;
using UnityEngine;

/// <summary>
/// 玩家本地命中回饋總控制器。
///
/// 這是：
///
/// PlayerCombatFeedbackRelay
///
/// 與：
///
/// Camera Shake
/// Hit Marker
/// Hit Sound
/// Damage Number
///
/// 之間的 Presentation 中介層。
///
/// ------------------------------------------------------------
///
/// 目前第一階段只實作：
///
/// Camera Hit Shake。
///
/// ------------------------------------------------------------
///
/// 下一階段 Hit Marker 也會直接接在這裡，
/// 不需要再去修改 AttackRifle 或 DamageSystem。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCombatFeedbackRelay))]
public class PlayerHitFeedbackController :
    NetworkBehaviour
{
    // =====================================================================
    #region 玩家引用

    [Header("玩家引用")]

    [SerializeField]
    [Tooltip("玩家身上的 Combat Feedback Relay。負責接收 State Authority 回傳給這名玩家本人的正式命中結果。若留空會自動取得。")]
    private PlayerCombatFeedbackRelay feedbackRelay;

    #endregion

    // =====================================================================
    #region Runtime

    /// <summary>
    /// 是否已經訂閱 Relay。
    /// </summary>
    private bool isSubscribed;

    #endregion

    // =====================================================================
    #region Unity / Fusion

    private void Awake()
    {
        if (feedbackRelay == null)
        {
            feedbackRelay =
                GetComponent<PlayerCombatFeedbackRelay>();
        }
    }

    public override void Spawned()
    {
        /*
         * 只有這台裝置真正控制的玩家
         * 才需要建立 Local Presentation。
         *
         * 遠端玩家完全不訂閱。
         */
        if (Object.HasInputAuthority == false)
        {
            return;
        }

        Subscribe();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    #endregion

    // =====================================================================
    #region Event

    private void Subscribe()
    {
        if (isSubscribed)
            return;

        if (feedbackRelay == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerHitFeedbackController)}] " +
                $"找不到 PlayerCombatFeedbackRelay。",
                this
            );

            return;
        }

        // =============================================================
        // 攻擊動作
        // =============================================================

        feedbackRelay.LocalAttackPerformed +=
            OnLocalAttackPerformed;

        // =============================================================
        // 正式命中
        // =============================================================

        feedbackRelay.LocalHitConfirmed +=
            OnLocalHitConfirmed;

        isSubscribed =
            true;

        Debug.Log(
            $"[{nameof(PlayerHitFeedbackController)}] " +
            $"已訂閱本地戰鬥回饋。" +
            $"\nLocalAttackPerformed：✓" +
            $"\nLocalHitConfirmed：✓",
            this
        );
    }

    private void Unsubscribe()
    {
        if (isSubscribed == false)
            return;

        if (feedbackRelay != null)
        {
            feedbackRelay.LocalAttackPerformed -=
                OnLocalAttackPerformed;

            feedbackRelay.LocalHitConfirmed -=
                OnLocalHitConfirmed;
        }

        isSubscribed =
            false;
    }

    #endregion

    // =====================================================================
    #region 命中回饋

    /// <summary>
    /// 本地玩家自己的攻擊
    /// 經過 State Authority 正式確認後呼叫。
    ///
    /// 這裡是所有「命中表現」的統一入口。
    ///
    /// 目前包含：
    ///
    /// 1. Camera Shake
    /// 2. Hit Marker
    ///
    /// 這一版額外加入詳細 Debug，
    /// 用來確認 Camera Shake 為什麼沒有被播放。
    /// </summary>
    private void OnLocalHitConfirmed(
        CombatHitFeedbackData feedback
    )
    {
        // =============================================================
        // Debug：確認事件真的有進來
        // =============================================================

        Debug.Log(
            $"[Player Hit Feedback] 收到 LocalHitConfirmed。" +
            $"\nDamage Type：{feedback.DamageType}" +
            $"\nApplied Damage：{feedback.AppliedDamage:F2}" +
            $"\nEffective Damage：{feedback.HasEffectiveDamage}" +
            $"\nHeadshot：{feedback.IsHeadshot}" +
            $"\nKill：{feedback.KilledTarget}" +
            $"\nSequence：{feedback.Sequence}",
            this
        );

        // =============================================================
        // Camera Shake
        // =============================================================

        FirstPersonHitCameraShake cameraShake =
            FirstPersonHitCameraShake.Singleton;

        if (cameraShake == null)
        {
            /*
            * 如果看到這則錯誤，
            * 問題就確定是 Singleton 沒有正確註冊。
            *
            * Camera Shake 本身不用改，
            * 下一步只處理 Singleton。
            */
            Debug.LogError(
                "[Player Hit Feedback] " +
                "LocalHitConfirmed 已收到，" +
                "但是 FirstPersonHitCameraShake.Singleton = NULL。" +
                "\n所以目前無法播放命中 Camera Shake。",
                this
            );
        }
        else
        {
            Debug.Log(
                $"[Player Hit Feedback] 找到 Camera Shake。" +
                $"\n物件：{cameraShake.name}" +
                $"\n準備呼叫 PlayHitShake()。",
                cameraShake
            );

            cameraShake.PlayHitShake(
                feedback
            );
        }

        // =============================================================
        // Hit Marker
        // =============================================================

        FirstPersonHitMarker hitMarker =
            FirstPersonHitMarker.Singleton;

        if (hitMarker == null)
        {
            Debug.LogWarning(
                "[Player Hit Feedback] " +
                "FirstPersonHitMarker.Singleton = NULL。",
                this
            );
        }
        else
        {
            hitMarker.PlayHitMarker(
                feedback
            );
        }
    }

    #endregion

    private void OnLocalAttackPerformed(
        CombatFeedbackId feedbackId
    )
    {
        Debug.Log(
            $"[Player Hit Feedback] 收到 LocalAttackPerformed。" +
            $"\nFeedback ID：{feedbackId}",
            this
        );

        FirstPersonHitCameraShake cameraShake =
            FirstPersonHitCameraShake.Singleton;

        if (cameraShake == null)
        {
            Debug.LogError(
                "[Player Hit Feedback] " +
                "FirstPersonHitCameraShake.Singleton = NULL。",
                this
            );

            return;
        }

        cameraShake.PlayAttackShake(
            feedbackId
        );
    }
}