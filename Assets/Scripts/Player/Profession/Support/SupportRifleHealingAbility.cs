using Fusion;
using UnityEngine;

/// <summary>
/// Support 職業 Rifle 治療能力。
///
/// ====================================================================
///
/// 核心規則：
///
/// Support Rifle 命中 Enemy
/// →
/// 不處理
/// →
/// AttackRifle 繼續正常 Damage。
///
/// ------------------------------------------------------------
///
/// Support Rifle 命中其他 Player
/// →
/// 攔截 Rifle Hit
/// →
/// 不建立 DamageRequest
/// →
/// 恢復 PlayerHealth。
///
/// ====================================================================
///
/// 非常重要：
///
/// 即使目標：
///
/// 已滿血
/// 已死亡
/// 本次實際治療量 = 0
///
/// 這次 Player Hit 仍然會被 Consume。
///
/// 原因是：
///
/// 「沒有成功回血」
///
/// 絕對不能變成：
///
/// 「那就改成對隊友造成傷害」。
///
/// ====================================================================
///
/// 目前 PVE 還沒有正式 Team System，
/// 因此暫時採用：
///
/// 自己以外的 PlayerHealth
/// =
/// 可治療隊友。
///
/// 未來如果加入敵對玩家、隊伍編號等系統，
/// 只需要在這支 Ability 增加 Team Validation，
/// 不需要修改 AttackRifle。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AttackRifle))]
public class SupportRifleHealingAbility :
    NetworkBehaviour,
    IRifleHitOverride
{
    // =====================================================================
    #region Healing 設定

    [Header("Support Rifle 治療設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 使用普通步槍射中其他玩家時，每一發希望恢復多少生命值。實際恢復量仍會受到目標 Maximum Health 限制。例如目標只缺少 4 HP，即使此數值為 10，最後也只會真正恢復 4 HP。")]
    private float normalHealAmount =
        10f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Support 未來啟動空中特殊技能後，步槍射中隊友時每一發使用的獨立治療量。這不是 Normal Heal Amount 的倍率，而是一個完全獨立的數值。目前特殊技能尚未實作，只先保留正式接口。")]
    private float specialHealAmount =
        20f;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，Support Rifle 命中玩家時會顯示治療模式、要求治療量、實際治療量、治療前後生命值，以及目標是否死亡或滿血。測試 Support 治療功能時建議保持開啟。")]
    private bool debugHealing =
        true;

    #endregion

    // =====================================================================
    #region Owner Binding

    /// <summary>
    /// 這個 Support Runtime 真正屬於哪一個 Player Core。
    /// </summary>
    private Player ownerPlayer;

    /// <summary>
    /// Owner Player 的 NetworkObject。
    ///
    /// 用來避免 Support 意外治療自己。
    /// </summary>
    private NetworkObject
        ownerPlayerNetworkObject;

    /// <summary>
    /// 將 Support Healing Ability
    /// 綁定到真正的 Player Core。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        ownerPlayerNetworkObject =
            newOwnerPlayer != null
                ? newOwnerPlayer.Object
                : null;

        if (debugHealing)
        {
            Debug.Log(
                $"[{nameof(SupportRifleHealingAbility)}] " +
                $"Owner Binding 完成。" +
                $"\nOwner Player：" +
                $"{(ownerPlayer != null ? ownerPlayer.name : "NULL")}" +
                $"\nOwner NetworkObject：" +
                $"{(ownerPlayerNetworkObject != null ? ownerPlayerNetworkObject.name : "NULL")}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Special Healing State

    /// <summary>
    /// Support 是否正在使用特殊技能的 Rifle Healing 模式。
    ///
    /// ------------------------------------------------------------
    ///
    /// 現在特殊技能還沒有開始實作。
    ///
    /// 但先把這個正式狀態留好，
    /// 之後 Support Air Special 只需要切換這個值，
    /// 不需要再回頭改普通治療架構。
    /// </summary>
    [Networked]
    public NetworkBool IsSpecialHealingActive
    {
        get;
        private set;
    }

    /// <summary>
    /// 目前這發 Support Rifle
    /// 應該使用多少治療量。
    /// </summary>
    public float CurrentHealAmount =>
        IsSpecialHealingActive
            ? specialHealAmount
            : normalHealAmount;

    /// <summary>
    /// 未來 Support 特殊技能使用的正式接口。
    ///
    /// ------------------------------------------------------------
    ///
    /// false：
    /// 使用 Normal Heal Amount。
    ///
    /// true：
    /// 使用 Special Heal Amount。
    ///
    /// ------------------------------------------------------------
    ///
    /// 只有 State Authority
    /// 可以正式修改這個 Gameplay 狀態。
    /// </summary>
    public void SetSpecialHealingActive(
        bool active
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        IsSpecialHealingActive =
            active;
    }

    #endregion

    // =====================================================================
    #region IRifleHitOverride

    /// <summary>
    /// AttackRifle 正式命中後，
    /// 在建立 DamageRequest 前進入這裡。
    ///
    /// ------------------------------------------------------------
    ///
    /// 回傳 false：
    ///
    /// 不是玩家。
    ///
    /// 例如 Enemy。
    ///
    /// AttackRifle 繼續正常傷害。
    ///
    /// ------------------------------------------------------------
    ///
    /// 回傳 true：
    ///
    /// 命中 Player。
    ///
    /// 這次 Rifle Hit 已經由 Support Healing 接管，
    /// 不得再對 Player 建立 DamageRequest。
    /// </summary>
    public bool TryConsumeHit(
        RifleHitContext context
    )
    {
        // =============================================================
        // 1. 沒有命中物件
        // =============================================================

        if (context.HitObject == null)
        {
            return false;
        }

        // =============================================================
        // 2. 判斷是不是 Player
        // =============================================================

        PlayerHealth targetHealth =
            context.HitObject
                .GetComponentInParent<PlayerHealth>();

        /*
         * 找不到 PlayerHealth：
         *
         * 代表：
         *
         * Enemy
         * World
         * Destructible
         * 其他 Damage Receiver。
         *
         * ------------------------------------------------------------
         *
         * Support 對這些目標仍然使用
         * AttackRifle 原本傷害流程。
         */
        if (targetHealth == null)
        {
            return false;
        }

        // =============================================================
        // 3. Player Hit 一律由 Healing Ability 接管
        // =============================================================

        /*
         * 從這一行開始，
         *
         * 無論最後有沒有成功回血，
         *
         * 都不能再 return false。
         *
         * ------------------------------------------------------------
         *
         * 因為：
         *
         * 滿血
         * 死亡
         * Authority 問題
         *
         * 都不能讓 Support 子彈
         * 突然掉回一般 Damage 流程。
         */

        // =============================================================
        // 4. 權限防呆
        // =============================================================

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            if (debugHealing)
            {
                Debug.LogWarning(
                    $"[{nameof(SupportRifleHealingAbility)}] " +
                    $"命中 Player，但目前不是 State Authority。" +
                    $"\n本次命中會被 Consume，不會造成隊友傷害。",
                    this
                );
            }

            return true;
        }

        // =============================================================
        // 5. Owner Binding 防呆
        // =============================================================

        if (ownerPlayer == null ||
            ownerPlayerNetworkObject == null)
        {
            if (debugHealing)
            {
                Debug.LogError(
                    $"[{nameof(SupportRifleHealingAbility)}] " +
                    $"命中 Player，但 Owner Player 尚未完成 Binding。" +
                    $"\n為避免錯誤造成 Friendly Fire，" +
                    $"本次命中只會被 Consume，不會造成傷害。",
                    this
                );
            }

            return true;
        }

        // =============================================================
        // 6. 不治療自己
        // =============================================================

        /*
         * 正常情況 AttackRifle 已經會排除
         * Owner Player Hitbox。
         *
         * 這裡仍然再做一次保險。
         *
         * 避免未來：
         *
         * Hit Mask
         * Collider
         * Lag Compensation
         *
         * 架構修改後造成 Self Healing。
         */
        if (targetHealth.Object ==
            ownerPlayerNetworkObject)
        {
            if (debugHealing)
            {
                Debug.LogWarning(
                    $"[{nameof(SupportRifleHealingAbility)}] " +
                    $"Support Rifle 意外命中自己。" +
                    $"\n本次命中已被忽略。",
                    targetHealth
                );
            }

            return true;
        }

        // =============================================================
        // 7. 正式治療
        // =============================================================

        float healthBefore =
            targetHealth.CurrentHealth;

        float requestedHeal =
            Mathf.Max(
                0f,
                CurrentHealAmount
            );

        bool healed =
            targetHealth.RestoreHealth(
                requestedHeal,
                out float appliedHeal
            );

        float healthAfter =
            targetHealth.CurrentHealth;

        // =============================================================
        // 8. Debug
        // =============================================================

        if (debugHealing)
        {
            Debug.Log(
                $"[Support Rifle Healing]" +
                $"\nTarget：{targetHealth.name}" +
                $"\nTarget Player：{targetHealth.Object.InputAuthority}" +
                $"\nMode：" +
                $"{(IsSpecialHealingActive ? "Special" : "Normal")}" +
                $"\nRequested Heal：{requestedHeal:F2}" +
                $"\nApplied Heal：{appliedHeal:F2}" +
                $"\nHealth Before：{healthBefore:F2}" +
                $"\nHealth After：{healthAfter:F2}" +
                $"\nMaximum Health：{targetHealth.MaximumHealth:F2}" +
                $"\nHealing Success：{healed}" +
                $"\nTarget Dead：{targetHealth.IsDead}" +
                $"\nShot Sequence：{context.ShotSequence}",
                targetHealth
            );
        }

        // =============================================================
        // 9. 非常重要：Player Hit 永遠 Consume
        // =============================================================

        /*
         * 就算：
         *
         * healed == false
         *
         * 也仍然 return true。
         *
         * ------------------------------------------------------------
         *
         * 例如：
         *
         * Target 已滿血
         * ↓
         * Applied Heal = 0
         * ↓
         * return true
         *
         * ------------------------------------------------------------
         *
         * 絕對不能：
         *
         * Applied Heal = 0
         * ↓
         * return false
         * ↓
         * AttackRifle 改成對隊友造成傷害。
         */

        return true;
    }

    #endregion
}