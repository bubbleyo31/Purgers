using Fusion;
using UnityEngine;

/// <summary>
/// 玩家所有 Gameplay 操作的統一封鎖管理器。
///
/// ------------------------------------------------------------
///
/// 其他模組不應該互相直接知道對方。
///
/// 例如：
///
/// AttackQuickMelee
///
/// 不應該直接：
///
/// attackRifle.enabled = false
/// aimController.enabled = false
///
/// 而是：
///
/// PlayerActionGate
/// ↓
/// Block Fire
/// Block Aim
/// Block Reload
///
/// 然後各模組自己詢問：
///
/// 「我現在能不能執行？」
///
/// ------------------------------------------------------------
///
/// 這可以避免未來：
///
/// Quick Action
/// Stun
/// Death
/// Special Ability
/// Weapon Switch
///
/// 全部互相引用。
/// </summary>
[DisallowMultipleComponent]
public class PlayerActionGate : NetworkBehaviour
{
    // =====================================================================
    #region Networked Block Sources

    /// <summary>
    /// Quick Action 目前禁止的操作。
    /// </summary>
    [Networked]
    private int QuickActionBlockMask
    {
        get;
        set;
    }

    /// <summary>
    /// Status Effect 目前禁止的操作。
    /// </summary>
    [Networked]
    private int StatusEffectBlockMask
    {
        get;
        set;
    }

    /// <summary>
    /// Death 目前禁止的操作。
    /// </summary>
    [Networked]
    private int DeathBlockMask
    {
        get;
        set;
    }

    /// <summary>
    /// Special Ability 目前禁止的操作。
    /// </summary>
    [Networked]
    private int SpecialAbilityBlockMask
    {
        get;
        set;
    }

    /// <summary>
    /// System 目前禁止的操作。
    /// </summary>
    [Networked]
    private int SystemBlockMask
    {
        get;
        set;
    }

    /// <summary>
    /// 目前武器動作禁止的操作。
    ///
    /// 例如 AttackRifle / SupportSMG 正在 Reload 時禁止 Aim。
    /// </summary>
    [Networked]
    private int WeaponActionBlockMask
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目前所有來源合併後，
    /// 玩家總共被禁止哪些操作。
    /// </summary>
    public PlayerActionBlockMask CurrentBlockedActions
    {
        get
        {
            int combined =
                QuickActionBlockMask |
                StatusEffectBlockMask |
                DeathBlockMask |
                SpecialAbilityBlockMask |
                SystemBlockMask |
                WeaponActionBlockMask;

            return
                (PlayerActionBlockMask)combined;
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * State Authority 建立正式初始狀態。
         *
         * Input Authority 在 Prediction 時
         * 也會依照相同輸入模擬之後的變化。
         */
        if (Object.HasStateAuthority)
        {
            QuickActionBlockMask =
                0;

            StatusEffectBlockMask =
                0;

            DeathBlockMask =
                0;

            SpecialAbilityBlockMask =
                0;

            SystemBlockMask =
                0;

            WeaponActionBlockMask =
                0;
        }
    }

    #endregion

    // =====================================================================
    #region 查詢

    /// <summary>
    /// 判斷某個操作目前是否被任何系統禁止。
    ///
    /// 例如：
    ///
    /// IsBlocked(PlayerActionBlockMask.Fire)
    /// </summary>
    public bool IsBlocked(
        PlayerActionBlockMask action
    )
    {
        return
            (CurrentBlockedActions &
             action) !=
            PlayerActionBlockMask.None;
    }

    #endregion

    // =====================================================================
    #region 設定來源

    /// <summary>
    /// 設定某一個 Gameplay Source
    /// 目前禁止哪些操作。
    ///
    /// 注意：
    ///
    /// 這不是在原本 Mask 上一直 OR。
    ///
    /// 而是這個 Source
    /// 完整擁有自己的 Block Mask。
    ///
    /// 因此 QuickAction 結束時，
    /// 只會清除 QuickAction 自己的 Block，
    /// 不會誤解鎖 Stun 或 Death。
    /// </summary>
    public void SetBlocks(
        PlayerActionBlockSource source,
        PlayerActionBlockMask blocks
    )
    {
        int value =
            (int)blocks;

        switch (source)
        {
            case PlayerActionBlockSource.QuickAction:
            {
                QuickActionBlockMask =
                    value;

                break;
            }

            case PlayerActionBlockSource.StatusEffect:
            {
                StatusEffectBlockMask =
                    value;

                break;
            }

            case PlayerActionBlockSource.Death:
            {
                DeathBlockMask =
                    value;

                break;
            }

            case PlayerActionBlockSource.SpecialAbility:
            {
                SpecialAbilityBlockMask =
                    value;

                break;
            }

            case PlayerActionBlockSource.System:
            {
                SystemBlockMask =
                    value;

                break;
            }
            
            case PlayerActionBlockSource.WeaponAction:
            {
                WeaponActionBlockMask =
                    value;

                break;
            }
        }
    }

    /// <summary>
    /// 只清除指定來源的操作封鎖。
    /// </summary>
    public void ClearBlocks(
        PlayerActionBlockSource source
    )
    {
        SetBlocks(
            source,
            PlayerActionBlockMask.None
        );
    }

    #endregion
}