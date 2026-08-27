using UnityEngine;

/// <summary>
/// 所有職業 Runtime Gameplay Driver 的共同基底。
///
/// ------------------------------------------------------------
///
/// Player.cs 不應該知道：
///
/// Attack
/// → Aim
/// → Focus
/// → Rifle
///
/// Tank
/// → Guard
/// → Melee Combo
/// → Dash
///
/// Support
/// → Support Weapon
/// → Pull
/// → Ability
///
/// ------------------------------------------------------------
///
/// Player 只會告訴：
///
/// PlayerProfessionRuntimeManager
///
/// 「這個 Tick 的 NetInput 是什麼。」
///
/// 然後由目前職業 Runtime
/// 自己執行真正的職業玩法。
///
/// ------------------------------------------------------------
///
/// 這也是之後把 Attack 元件真正搬離
/// Player Prefab 的第一個必要步驟。
/// </summary>
public abstract class PlayerProfessionRuntimeDriver :
    MonoBehaviour
{
    // =====================================================================
    #region Runtime 引用

    /// <summary>
    /// 這個 Driver 所屬的 Profession Runtime。
    /// </summary>
    protected PlayerProfessionRuntime Runtime
    {
        get;
        private set;
    }

    /// <summary>
    /// 這個 Runtime 所屬的 Player Core。
    /// </summary>
    protected Player OwnerPlayer
    {
        get;
        private set;
    }

    /// <summary>
    /// Owner Player 的 PlayerProfession。
    /// </summary>
    protected PlayerProfession OwnerProfession
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 這個 Driver 代表哪個職業。
    /// </summary>
    public abstract PlayerProfessionType Profession
    {
        get;
    }

    /// <summary>
    /// 是否已經成功綁定 Owner Player。
    /// </summary>
    public bool IsBound =>
        Runtime != null &&
        OwnerPlayer != null &&
        OwnerProfession != null;

    #endregion

    // =====================================================================
    #region Binding

    /// <summary>
    /// 將 Runtime Driver 綁定到真正的 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前：
    ///
    /// Attack Runtime
    ///
    /// 會藉由 Owner Player 找到暫時仍掛在
    /// Player Root 的：
    ///
    /// PlayerAimController
    /// AttackFocusAbility
    /// PlayerWeaponController。
    ///
    /// ------------------------------------------------------------
    ///
    /// 下一階段這些元件真的搬進
    /// AttackProfessionRuntime 後，
    /// Attack Driver 的 BindOwner()
    /// 再改成從 Runtime 自己取得即可。
    ///
    /// Player.cs 不需要再次修改。
    /// </summary>
    public bool BindRuntime(
        PlayerProfessionRuntime runtime
    )
    {
        if (runtime == null)
        {
            ClearBinding();

            return false;
        }

        Player ownerPlayer =
            runtime.OwnerPlayer;

        if (ownerPlayer == null)
        {
            ClearBinding();

            return false;
        }

        Runtime =
            runtime;

        OwnerPlayer =
            ownerPlayer;

        OwnerProfession =
            ownerPlayer.Profession;

        if (OwnerProfession == null)
        {
            ClearBinding();

            return false;
        }

        /*
         * Runtime Profession 與 Driver Profession
         * 必須一致。
         *
         * 避免：
         *
         * TankProfessionRuntime
         * 不小心掛 Attack Driver。
         */
        if (runtime.RuntimeProfession !=
            Profession)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntimeDriver)}] " +
                $"Runtime Profession 與 Driver 不一致。" +
                $"\nRuntime：{runtime.RuntimeProfession}" +
                $"\nDriver：{Profession}" +
                $"\n物件：{name}",
                this
            );

            ClearBinding();

            return false;
        }

        OnOwnerBound();

        return true;
    }

    /// <summary>
    /// Runtime 被正式綁定後，
    /// 讓個別職業取得自己需要的 Component。
    /// </summary>
    protected abstract void OnOwnerBound();

    /// <summary>
    /// 清除本地 Binding。
    /// </summary>
    protected virtual void ClearBinding()
    {
        Runtime =
            null;

        OwnerPlayer =
            null;

        OwnerProfession =
            null;
    }

    #endregion

    // =====================================================================
    #region Simulation

    /// <summary>
    /// Player 每個 Fusion Tick
    /// 只會把 Input 丟進這個統一入口。
    ///
    /// 真正怎麼使用 Input
    /// 完全由目前職業決定。
    /// </summary>
    public abstract void Simulate(
        NetInput input,
        Fusion.NetworkButtons previousButtons
    );

    #endregion
}