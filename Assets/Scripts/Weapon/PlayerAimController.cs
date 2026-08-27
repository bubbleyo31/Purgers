using Fusion;
using UnityEngine;

/// <summary>
/// 玩家目前的瞄準階段。
/// </summary>
public enum PlayerAimPhase : byte
{
    /// <summary>
    /// 腰射狀態。
    ///
    /// 沒有進行 ADS。
    /// </summary>
    Hip = 0,

    /// <summary>
    /// 玩家已經要求瞄準，
/// FOV 正在由普通視野拉近到 ADS 視野。
///
/// 這個階段尚未視為真正完成瞄準。
    /// </summary>
    Entering = 1,

    /// <summary>
    /// ADS 已完成。
    ///
    /// 只有進入這個階段，
/// 其他 Gameplay 系統才應該把玩家視為「正在瞄準」。
    /// </summary>
    Aiming = 2
}

/// <summary>
/// 玩家統一瞄準控制器。
///
/// 這支腳本是整個玩家系統中：
///
/// 「玩家現在到底有沒有瞄準」
///
/// 的唯一權威來源。
///
/// ------------------------------------------------------------
///
/// 輸入：
///
/// InputButton.Aim
///
/// ↓
///
/// PlayerAimController
///
/// ↓
///
/// 一方面驅動 FirstPersonFovManager
///
/// 另一方面產生 Gameplay Aim State。
///
/// ------------------------------------------------------------
///
/// 因此其他系統不應再自己判斷：
///
/// input.Buttons.IsSet(InputButton.Aim)
///
/// 而應該讀取：
///
/// PlayerAimController.IsAiming
///
/// ------------------------------------------------------------
///
/// 例如：
///
/// AttackFocusAbility
/// Weapon Accuracy
/// ADS Animation
/// ADS Sensitivity
/// Crosshair
/// Weapon Sway
///
/// 未來全部可以使用同一份狀態。
/// </summary>
[DisallowMultipleComponent]
public class PlayerAimController : NetworkBehaviour
{
    // =====================================================================
    #region ADS 時間

    [Header("ADS 時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("從玩家開始要求瞄準，到正式被判定為 Aiming 所需的時間。這個時間同時會拿來當 FOV 的 Blend In Duration，因此 Gameplay 的瞄準完成時間會與視覺 FOV 拉近同步。目前步槍建議先設為 0.1 秒。")]
    private float aimEnterDuration =
        0.1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家放開瞄準後，FOV 從 ADS 視野恢復到一般視野所需的時間。放開右鍵後 Gameplay 會立即不再視為 Aiming，但 FOV 可以用這個時間平滑恢復。")]
    private float aimExitDuration =
        0.12f;

    #endregion

    // =====================================================================
    #region ADS FOV

    [Header("ADS FOV")]

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("完成瞄準時 World Camera 的目標 FOV。這是 Override FOV，不是減少多少角度。例如一般 FOV 為 90、這裡設 65，就會從 90 平滑拉近到 65。")]
    private float aimedWorldFov =
        65f;

    [SerializeField]
    [Range(1f, 179f)]
    [Tooltip("完成瞄準時 Weapon Camera 的目標 FOV。可以和 World Camera 不同，用來控制第一人稱槍械在 ADS 時的縮放程度。")]
    private float aimedWeaponFov =
        50f;

    [SerializeField]
    [Tooltip("ADS FOV 要影響哪些攝影機。一般第一人稱武器建議使用 Both，讓 World Camera 與 Weapon Camera 一起進入瞄準。")]
    private FovCameraChannel aimFovChannels =
        FovCameraChannel.Both;

    [SerializeField]
    [Tooltip("ADS FOV Request 的優先權。因為 ADS 應該能覆蓋勾索、跑步等 Additive FOV，建議設為很高，例如 1000。")]
    private int aimFovPriority =
        1000;

    #endregion

    [SerializeField]
    [Tooltip("玩家統一操作封鎖管理器。當 Quick Action 等系統禁止 Aim 時，PlayerAimController 會立即退出 ADS，並讓 FOV Manager 正常執行 Aim Blend Out。若留空會自動取得。")]
    private PlayerActionGate actionGate;

    // =====================================================================
    #region Owner Player Binding

    /// <summary>
    /// 這個 Attack Aim Controller
    /// 實際屬於哪一個 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前遷移階段：
    ///
    /// PlayerAimController
    /// 還在 Player Root。
    ///
    /// 未來：
    ///
    /// PlayerAimController
    /// 會搬到 AttackProfessionRuntime。
    ///
    /// 因此不能再假設：
    ///
    /// PlayerActionGate
    ///
    /// 一定和自己位於同一 GameObject。
    /// </summary>
    private Player ownerPlayer;

    /// <summary>
    /// 目前綁定的 Player Core。
    /// </summary>
    public Player OwnerPlayer =>
        ownerPlayer;

    /// <summary>
    /// 將 Aim Controller 綁定到真正的 Player Core。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這個方法之後會由：
    ///
    /// AttackProfessionRuntimeDriver
    ///
    /// 在 Runtime Spawn 完成後呼叫。
    /// </summary>
    public void BindOwnerPlayer(
        Player newOwnerPlayer
    )
    {
        ownerPlayer =
            newOwnerPlayer;

        if (ownerPlayer == null)
        {
            actionGate =
                null;

            return;
        }

        /*
        * PlayerActionGate 屬於 Player Core。
        *
        * 所以未來 Aim 搬到 Runtime 後，
        * 必須從 Owner Player 取得。
        */
        actionGate =
            ownerPlayer
                .GetComponent<PlayerActionGate>();

        if (actionGate == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerAimController)}] " +
                $"Owner Player 找不到 PlayerActionGate。" +
                $"\nOwner：{ownerPlayer.name}",
                ownerPlayer
            );
        }
    }

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，玩家進入 Entering、Aiming、Hip 時會在 Console 顯示資訊。")]
    private bool debugAim =
        true;

    #endregion

    // =====================================================================
    #region Fusion 狀態

    /// <summary>
    /// 玩家目前 ADS 階段。
    /// </summary>
    [Networked]
    public PlayerAimPhase CurrentAimPhase
    {
        get;
        private set;
    }

    /// <summary>
    /// ADS 進入計時器。
    ///
    /// 只有 Entering 時使用。
    /// </summary>
    [Networked]
    private TickTimer AimEnterTimer
    {
        get;
        set;
    }

    #endregion

    // =====================================================================
    #region 本地 FOV 資料

    /// <summary>
    /// FirstPersonFovManager 建立的 ADS Request Handle。
    /// </summary>
    private int fovRequestHandle =
        FirstPersonFovManager.InvalidHandle;

    /// <summary>
    /// 建立 Request 時所使用的 FOV Manager。
    ///
    /// 保留實際引用，
    /// 避免場景切換或 Singleton 改變後
    /// 無法正確清除舊 Request。
    /// </summary>
    private FirstPersonFovManager boundFovManager;

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// 玩家是否已經「真正完成瞄準」。
    ///
    /// 注意：
    ///
    /// 玩家只是剛按下右鍵時，
    /// 這裡仍然是 false。
    ///
    /// 必須等待 Aim Enter Duration 完成。
    ///
    /// AttackFocusAbility 就應該讀這個值。
    /// </summary>
    public bool IsAiming =>
        CurrentAimPhase ==
        PlayerAimPhase.Aiming;

    /// <summary>
    /// ADS 是否正在進入過程。
    /// </summary>
    public bool IsEnteringAim =>
        CurrentAimPhase ==
        PlayerAimPhase.Entering;

    /// <summary>
    /// 玩家目前是否仍然要求保持 ADS。
    ///
    /// Entering 與 Aiming 都會是 true。
    ///
    /// 其他 Gameplay 系統通常應優先使用 IsAiming。
    /// </summary>
    public bool IsAimRequested =>
        CurrentAimPhase !=
        PlayerAimPhase.Hip;

    #endregion

    // =====================================================================
    #region Fusion 生命週期

    public override void Spawned()
    {
        /*
         * 正式初始狀態由 State Authority 設定。
         */
        if (Object.HasStateAuthority)
        {
            CurrentAimPhase =
                PlayerAimPhase.Hip;

            AimEnterTimer =
                TickTimer.None;
        }

        /*
         * FOV 是純本地視覺。
         *
         * 只有 Input Authority
         * 需要建立 ADS FOV Request。
         */
        if (Object.HasInputAuthority)
        {
            EnsureFovRequest();
        }
    }

    #endregion

    // =====================================================================
    #region Gameplay 模擬

    /// <summary>
    /// 每個 Fusion Tick 由 Player 呼叫。
    ///
    /// 整個玩家系統只有這裡負責讀取
    /// InputButton.Aim。
    ///
    /// 其他系統只能讀：
    ///
    /// IsAiming
    /// IsEnteringAim
    /// IsAimRequested
    /// </summary>
    public void Simulate(
        NetInput input
    )
    {
        bool aimBlocked =
            actionGate != null &&
            actionGate.IsBlocked(
                PlayerActionBlockMask.Aim
            );

        /*
        * Aim 的真正 Gameplay 要求：
        *
        * 右鍵 Hold
        * +
        * 沒有被其他 Gameplay 系統禁止
        */
        
        bool aimInputHeld =
            aimBlocked == false &&
            input.Buttons.IsSet(
                InputButton.Aim
            );

        // =============================================================
        // 玩家沒有要求瞄準
        // =============================================================

        if (aimInputHeld == false)
        {
            if (CurrentAimPhase !=
                PlayerAimPhase.Hip)
            {
                CurrentAimPhase =
                    PlayerAimPhase.Hip;

                AimEnterTimer =
                    TickTimer.None;

                if (debugAim &&
                    Object.HasInputAuthority)
                {
                    Debug.Log(
                        "[ADS] 退出瞄準。",
                        this
                    );
                }
            }

            return;
        }

        // =============================================================
        // Hip → Entering
        // =============================================================

        if (CurrentAimPhase ==
            PlayerAimPhase.Hip)
        {
            BeginAim();

            return;
        }

        // =============================================================
        // Entering → Aiming
        // =============================================================

        if (CurrentAimPhase ==
            PlayerAimPhase.Entering)
        {
            if (AimEnterTimer
                .Expired(Runner))
            {
                CompleteAim();
            }

            return;
        }

        // =============================================================
        // Aiming
        // =============================================================

        /*
         * 已經完成 ADS。
         *
         * 只要右鍵繼續保持，
         * 就維持此狀態。
         */
    }

    #endregion

    // =====================================================================
    #region ADS 狀態切換

    /// <summary>
    /// 玩家開始進入 ADS。
    ///
    /// FOV 視覺也會在 Render 階段開始拉近。
    /// </summary>
    private void BeginAim()
    {
        if (aimEnterDuration <= 0f)
        {
            CurrentAimPhase =
                PlayerAimPhase.Aiming;

            AimEnterTimer =
                TickTimer.None;

            return;
        }

        CurrentAimPhase =
            PlayerAimPhase.Entering;

        AimEnterTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                aimEnterDuration
            );

        if (debugAim &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                $"[ADS] 開始瞄準。" +
                $"\n進入時間：{aimEnterDuration:F2} 秒" +
                $"\nWorld Aim FOV：{aimedWorldFov:F1}" +
                $"\nWeapon Aim FOV：{aimedWeaponFov:F1}",
                this
            );
        }
    }

    /// <summary>
    /// ADS 進入時間完成。
    ///
    /// 從現在開始，
    /// Gameplay 才真正視為玩家正在瞄準。
    /// </summary>
    private void CompleteAim()
    {
        CurrentAimPhase =
            PlayerAimPhase.Aiming;

        AimEnterTimer =
            TickTimer.None;

        if (debugAim &&
            Object.HasInputAuthority)
        {
            Debug.Log(
                "[ADS] 瞄準完成。IsAiming = true。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region FOV 視覺

    /// <summary>
    /// Render 階段更新本地 ADS FOV Request。
    ///
    /// 不在 FixedUpdateNetwork 裡直接改 FOV。
    ///
    /// 原因：
    /// FOV 是本地視覺效果，
    /// 不應該成為 Fusion Rollback / Re-simulation
    /// 期間反覆執行的 Gameplay Side Effect。
    /// </summary>
    public override void Render()
    {
        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        EnsureFovRequest();

        if (boundFovManager == null ||
            fovRequestHandle ==
            FirstPersonFovManager.InvalidHandle)
        {
            return;
        }

        /*
         * Entering 時就開始 FOV 拉近。
         *
         * Aiming 時維持完整 ADS FOV。
         *
         * Hip 時讓 Request Weight 回到零，
         * FOV Manager 會按照 Aim Exit Duration
         * 平滑恢復。
         */
        float targetWeight =
            CurrentAimPhase ==
            PlayerAimPhase.Hip
                ? 0f
                : 1f;

        boundFovManager.SetRequestWeight(
            fovRequestHandle,
            targetWeight
        );
    }

    /// <summary>
    /// 確保本地玩家已經向 FOV Manager
    /// 建立 ADS Request。
    /// </summary>
    private void EnsureFovRequest()
    {
        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        FirstPersonFovManager manager =
            FirstPersonFovManager.Singleton;

        if (manager == null)
            return;

        /*
         * 已經綁定同一個 Manager，
         * Request 也有效。
         */
        if (boundFovManager ==
                manager &&
            fovRequestHandle !=
                FirstPersonFovManager.InvalidHandle)
        {
            return;
        }

        /*
         * 如果舊 Manager 還存在，
         * 先移除舊 Request。
         */
        RemoveFovRequest();

        boundFovManager =
            manager;

        /*
         * ADS 使用 Override。
         *
         * 因此：
         *
         * Grapple FOV
         * Sprint FOV
         * 其他低 Priority Additive
         *
         * 都先計算，
         * 最後 ADS 再把結果混合到精確的目標 FOV。
         */
        fovRequestHandle =
            boundFovManager.CreateRequest(
                "Weapon ADS",
                FovModifierMode.Override,
                aimFovChannels,
                aimFovPriority,
                aimedWorldFov,
                aimedWeaponFov,
                aimEnterDuration,
                aimExitDuration
            );
    }

    /// <summary>
    /// 移除 ADS FOV Request。
    /// </summary>
    private void RemoveFovRequest()
    {
        if (boundFovManager != null &&
            fovRequestHandle !=
            FirstPersonFovManager.InvalidHandle)
        {
            boundFovManager
                .RemoveRequestImmediately(
                    fovRequestHandle
                );
        }

        fovRequestHandle =
            FirstPersonFovManager.InvalidHandle;

        boundFovManager =
            null;
    }

    #endregion

    // =====================================================================
    #region 清理

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        RemoveFovRequest();
    }

    private void OnDestroy()
    {
        RemoveFovRequest();
    }

    #endregion

    private void Awake()
    {
        /*
        * 舊架構相容：
        *
        * PlayerAimController 目前還掛在 Player Root，
        * 所以先允許從同物件取得。
        *
        * 等真正搬進 Attack Runtime 後，
        * 這裡找不到也完全正常，
        * Runtime Driver 之後會呼叫 BindOwnerPlayer()。
        */
        if (actionGate == null)
        {
            actionGate =
                GetComponent<PlayerActionGate>();
        }

        /*
        * 如果目前本身就是掛在 Player Root，
        * 也先保存 Owner。
        *
        * 這讓目前版本在還沒搬家前
        * 行為完全維持不變。
        */
        if (ownerPlayer == null)
        {
            ownerPlayer =
                GetComponent<Player>();
        }
    }
}