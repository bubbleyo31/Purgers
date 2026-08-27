using Fusion;
using UnityEngine;

/// <summary>
/// 玩家勾索充能與逐格恢復系統。
///
/// 負責：
/// 1. 讀取 PlayerProfession。
/// 2. 取得 ProfessionDefinition。
/// 3. 最大勾索充能。
/// 4. 目前勾索充能。
/// 5. 每格恢復計時。
/// 6. 擊殺回充。
/// 7. 提供未來 UI 所需資料。
///
/// 不負責：
/// 1. 勾索射線。
/// 2. 勾索拉動。
/// 3. LineRenderer。
/// 4. Momentum。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
public class PlayerGrappleCharges : NetworkBehaviour
{
    // =====================================================================
    #region 引用

    [Header("職業引用")]

    [SerializeField]
    [Tooltip("玩家職業資料。最大充能與冷卻時間會從目前職業的 ProfessionDefinition 取得。若留空會自動取得。")]
    private PlayerProfession playerProfession;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示勾索充能消耗、自然恢復、初始化與擊殺回充資訊。")]
    private bool debugCharges = true;

    #endregion

    // =====================================================================
    #region Fusion 狀態

    /// <summary>
    /// 玩家目前剩餘勾索充能。
    /// </summary>
    [Networked]
    public int CurrentCharges { get; private set; }

    /// <summary>
    /// 是否已經依照職業完成初始化。
    /// </summary>
    [Networked]
    private NetworkBool IsInitialized { get; set; }

    /// <summary>
    /// 上一次初始化時使用的職業。
    ///
    /// 若未來戰鬥途中允許切換職業，
    /// 可以偵測職業變化。
    /// </summary>
    [Networked]
    private PlayerProfessionType InitializedProfession { get; set; }

    /// <summary>
    /// 正在恢復的下一格充能計時器。
    /// </summary>
    [Networked]
    private TickTimer RechargeTimer { get; set; }

    /// <summary>
    /// 是否正在恢復下一格。
    /// </summary>
    [Networked]
    private NetworkBool RechargeActive { get; set; }

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目前職業的勾索最大充能。
    /// </summary>
    public int MaxCharges
    {
        get
        {
            if (playerProfession == null)
                return 0;

            return Mathf.Max(
                0,
                playerProfession.GrappleMaxCharges
            );
        }
    }

    /// <summary>
    /// 目前職業每恢復一格所需時間。
    /// </summary>
    public float RechargeDuration
    {
        get
        {
            if (playerProfession == null)
                return 0f;

            return Mathf.Max(
                0f,
                playerProfession.GrappleRechargeDuration
            );
        }
    }

    /// <summary>
    /// 是否至少還有 1 格可以使用。
    /// </summary>
    public bool HasCharge =>
        IsInitialized &&
        CurrentCharges > 0;

    /// <summary>
    /// 是否正在自然恢復下一格。
    /// </summary>
    public bool IsRecharging =>
        RechargeActive;

    /// <summary>
    /// 下一格剩餘恢復秒數。
    /// </summary>
    public float RechargeRemainingSeconds
    {
        get
        {
            if (Runner == null ||
                RechargeActive == false)
            {
                return 0f;
            }

            return RechargeTimer
                .RemainingTime(Runner) ?? 0f;
        }
    }

    /// <summary>
    /// 下一格充能目前恢復進度。
    ///
    /// 0 = 剛開始。
/// 1 = 即將完成。
    /// </summary>
    public float RechargeProgress
    {
        get
        {
            if (RechargeActive == false ||
                Runner == null)
            {
                return 0f;
            }

            float duration =
                RechargeDuration;

            if (duration <= 0f)
                return 1f;

            float remaining =
                RechargeTimer
                    .RemainingTime(Runner) ?? 0f;

            return 1f -
                   Mathf.Clamp01(
                       remaining /
                       duration
                   );
        }
    }

    #endregion

    // =====================================================================
    #region Unity 生命週期

    private void Awake()
    {
        if (playerProfession == null)
        {
            playerProfession =
                GetComponent<PlayerProfession>();
        }
    }

    #endregion

    // =====================================================================
    #region 每 Tick 更新

    /// <summary>
    /// 每個 Fusion Tick 由 Player 根控制器呼叫。
    /// </summary>
    public void TickRecharge()
    {
        /*
         * PlayerProfession 可能在 NetworkObject Spawn 後
         * 才正式同步職業。
         *
         * 所以不要依賴 NetworkBehaviour.Spawned() 的腳本順序，
         * 而是在 Tick 裡確認職業已準備好。
         */
        if (EnsureInitialized() == false)
        {
            return;
        }

        int maxCharges =
            MaxCharges;

        if (maxCharges <= 0)
        {
            CurrentCharges =
                0;

            StopRechargeTimer();

            return;
        }

        /*
         * 未來如果戰鬥途中更換職業，
         * 目前先採用「重新滿充能」規則。
         *
         * 如果之後真的允許戰鬥換職，
         * 我們再另外決定要保留比例還是保留格數。
         */
        if (InitializedProfession !=
            playerProfession.CurrentProfession)
        {
            InitializeFromCurrentProfession();
            return;
        }

        CurrentCharges =
            Mathf.Clamp(
                CurrentCharges,
                0,
                maxCharges
            );

        // -------------------------------------------------------------
        // 已滿格
        // -------------------------------------------------------------

        if (CurrentCharges >= maxCharges)
        {
            StopRechargeTimer();
            return;
        }

        // -------------------------------------------------------------
        // 尚未開始恢復
        // -------------------------------------------------------------

        if (RechargeActive == false)
        {
            StartRechargeIfNeeded();
            return;
        }

        // -------------------------------------------------------------
        // 還沒恢復完成
        // -------------------------------------------------------------

        if (RechargeTimer.Expired(Runner) == false)
        {
            return;
        }

        // -------------------------------------------------------------
        // 恢復一格
        // -------------------------------------------------------------

        CurrentCharges =
            Mathf.Min(
                maxCharges,
                CurrentCharges + 1
            );

        if (debugCharges)
        {
            Debug.Log(
                $"[勾索充能] 自然恢復 1 格。" +
                $"\n職業：{playerProfession.CurrentProfession}" +
                $"\n目前：{CurrentCharges}/{maxCharges}",
                this
            );
        }

        /*
         * 還沒滿就接著恢復下一格。
         */
        if (CurrentCharges < maxCharges)
        {
            StartRechargeTimer();
        }
        else
        {
            StopRechargeTimer();
        }
    }

    #endregion

    // =====================================================================
    #region 初始化

    /// <summary>
    /// 確認目前職業資料是否已準備完成。
    /// </summary>
    private bool EnsureInitialized()
    {
        if (playerProfession == null)
            return false;

        if (playerProfession.CurrentProfession ==
            PlayerProfessionType.None)
        {
            return false;
        }

        if (playerProfession.CurrentDefinition == null)
        {
            return false;
        }

        if (IsInitialized == false)
        {
            InitializeFromCurrentProfession();
        }

        return true;
    }

    /// <summary>
    /// 根據目前職業重新初始化。
    ///
    /// 玩家第一次生成時直接滿格。
    /// </summary>
    private void InitializeFromCurrentProfession()
    {
        int maxCharges =
            MaxCharges;

        CurrentCharges =
            Mathf.Max(
                0,
                maxCharges
            );

        InitializedProfession =
            playerProfession.CurrentProfession;

        IsInitialized =
            true;

        StopRechargeTimer();

        if (debugCharges)
        {
            Debug.Log(
                $"[勾索充能] 初始化完成。" +
                $"\n職業：{InitializedProfession}" +
                $"\n充能：{CurrentCharges}/{maxCharges}" +
                $"\n每格恢復：{RechargeDuration:F2} 秒",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region 消耗充能

    /// <summary>
    /// 嘗試消耗 1 格勾索充能。
    ///
    /// 勾索只有真正命中有效目標後才應呼叫。
    /// </summary>
    public bool ConsumeCharge()
    {
        if (EnsureInitialized() == false)
            return false;

        if (CurrentCharges <= 0)
        {
            if (debugCharges)
            {
                Debug.Log(
                    $"[勾索充能] 沒有剩餘充能。" +
                    $"\n目前：{CurrentCharges}/{MaxCharges}",
                    this
                );
            }

            return false;
        }

        CurrentCharges--;

        CurrentCharges =
            Mathf.Max(
                0,
                CurrentCharges
            );

        /*
         * 如果原本滿格，
         * 第一次消耗後會開始第一格冷卻。
         *
         * 如果本來就在冷卻，
         * 不重置目前進度。
         */
        StartRechargeIfNeeded();

        if (debugCharges)
        {
            Debug.Log(
                $"[勾索充能] 消耗 1 格。" +
                $"\n職業：{playerProfession.CurrentProfession}" +
                $"\n目前：{CurrentCharges}/{MaxCharges}",
                this
            );
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region 恢復充能

    /// <summary>
    /// 立即恢復指定數量充能。
    ///
    /// 不會重置原本正在跑的自然冷卻進度。
    ///
    /// 例如：
/// 0 / 3
/// 自然冷卻已跑 7 / 10 秒
/// 擊殺 +1
/// → 1 / 3
/// 原本冷卻仍只剩 3 秒。
    /// </summary>
    public void RestoreCharge(
        int amount
    )
    {
        if (amount <= 0)
            return;

        if (EnsureInitialized() == false)
            return;

        int maxCharges =
            MaxCharges;

        int previous =
            CurrentCharges;

        CurrentCharges =
            Mathf.Clamp(
                CurrentCharges + amount,
                0,
                maxCharges
            );

        if (debugCharges)
        {
            Debug.Log(
                $"[勾索充能] 立即回復。" +
                $"\n回復數量：{amount}" +
                $"\n之前：{previous}/{maxCharges}" +
                $"\n現在：{CurrentCharges}/{maxCharges}",
                this
            );
        }

        if (CurrentCharges >= maxCharges)
        {
            StopRechargeTimer();
        }
        else
        {
            /*
             * 若原本有冷卻：
             * 不重置。
             *
             * 若原本沒有：
             * 補啟動。
             */
            StartRechargeIfNeeded();
        }
    }

    /// <summary>
    /// 依目前職業設定執行一次「擊殺回充」。
    /// </summary>
    public void RestoreChargeFromKill()
    {
        if (playerProfession == null)
            return;

        int amount =
            playerProfession.GrappleRestoreOnKill;

        if (amount <= 0)
            return;

        RestoreCharge(
            amount
        );
    }

    #endregion

    // =====================================================================
    #region 冷卻計時

    /// <summary>
    /// 若尚未滿格且目前沒有計時，
    /// 啟動下一格恢復。
    /// </summary>
    private void StartRechargeIfNeeded()
    {
        if (CurrentCharges >= MaxCharges)
        {
            StopRechargeTimer();
            return;
        }

        if (RechargeActive)
        {
            /*
             * 已在恢復中，不重置目前進度。
             */
            return;
        }

        StartRechargeTimer();
    }

    /// <summary>
    /// 正式開始下一格恢復計時。
    /// </summary>
    private void StartRechargeTimer()
    {
        float duration =
            RechargeDuration;

        if (duration <= 0f)
        {
            CurrentCharges =
                MaxCharges;

            StopRechargeTimer();
            return;
        }

        RechargeTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                duration
            );

        RechargeActive =
            true;

        if (debugCharges)
        {
            Debug.Log(
                $"[勾索充能] 開始恢復下一格。" +
                $"\n目前：{CurrentCharges}/{MaxCharges}" +
                $"\n需要：{duration:F2} 秒",
                this
            );
        }
    }

    /// <summary>
    /// 停止目前恢復計時。
    /// </summary>
    private void StopRechargeTimer()
    {
        RechargeTimer =
            TickTimer.None;

        RechargeActive =
            false;
    }

    #endregion
}