using Fusion;
using UnityEngine;

/// <summary>
/// 第一人稱職業 ViewModel 管理器。
///
/// ====================================================================
///
/// 主要負責：
///
/// 1. 綁定本地玩家。
/// 2. 讀取 PlayerProfession。
/// 3. 根據 Attack / Tank / Support
///    生成不同第一人稱 ViewModel。
/// 4. 將 ViewModel 放到 CameraRig 的 ViewModelRoot。
/// 5. 職業改變時自動切換 ViewModel。
/// 6. 從 ViewModel 取得 WeaponViewModelReferences。
/// 7. 將 MuzzlePoint 等視覺引用
///    綁定到目前 Profession Runtime 的 Gameplay Weapon。
///
/// ====================================================================
///
/// 正式目標架構：
///
/// Player Core
/// │
/// ├─ PlayerProfession
/// └─ PlayerProfessionRuntimeManager
///           │
///           ▼
///    Current Profession Runtime
///           │
///           └─ AttackRifle
///                  ▲
///                  │
///          SetTracerMuzzlePoint
///                  │
///     WeaponViewModelReferences
///                  ▲
///                  │
///          Attack ViewModel
///
/// ====================================================================
///
/// 非常重要：
///
/// ViewModel 是純本地 Presentation。
///
/// 不需要：
///
/// NetworkObject
/// NetworkBehaviour
/// Photon Fusion Transform Sync。
///
/// Gameplay Weapon 則存在 Profession Runtime。
///
/// ====================================================================
/// </summary>
[DisallowMultipleComponent]
public class ProfessionViewModelManager :
    MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中唯一的職業 ViewModel 管理器。
    /// </summary>
    public static ProfessionViewModelManager Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region ViewModel Root

    [Header("ViewModel Root")]

    [SerializeField]
    [Tooltip("第一人稱職業 ViewModel 的生成位置。請拖入 CameraRig 下的 ViewModelRoot。執行期間這個物件底下只會保留目前職業的 ViewModel。")]
    private Transform viewModelRoot;

    #endregion

    // =====================================================================
    #region 職業 ViewModel Prefab

    [Header("職業 ViewModel Prefab")]

    [SerializeField]
    [Tooltip("攻擊職業使用的第一人稱 ViewModel Prefab。應包含攻擊職業的手部模型、骨架、Animator、WeaponViewModelReferences，以及武器的 MuzzlePoint。")]
    private GameObject attackViewModelPrefab;

    [SerializeField]
    [Tooltip("坦克職業使用的第一人稱 ViewModel Prefab。未來會包含坦克的手部模型、電鋸、Animator，以及坦克武器所需要的 ViewModel References。")]
    private GameObject tankViewModelPrefab;

    [SerializeField]
    [Tooltip("輔助職業使用的第一人稱 ViewModel Prefab。未來會包含輔助職業的手部模型、Animator 與對應武器 ViewModel References。")]
    private GameObject supportViewModelPrefab;

    #endregion

    // =====================================================================
    #region Layer 設定

    [Header("ViewModel Layer")]

    [SerializeField]
    [Tooltip("開啟後，生成 ViewModel 時會自動將整個 ViewModel 階層設定為指定 Layer。建議開啟，避免子物件忘記設定 ViewModel Layer 而被 World Camera 渲染。")]
    private bool forceViewModelLayer =
        true;

    [SerializeField]
    [Tooltip("ViewModel 使用的 Layer 名稱。預設為 ViewModel。請確認 Unity Project Settings 的 Layer 中存在同名 Layer。")]
    private string viewModelLayerName =
        "ViewModel";

    #endregion

    // =====================================================================
    #region Runtime 遷移設定

    [Header("Attack Runtime 遷移階段")]

    [SerializeField]
    [Tooltip("開啟後，如果目前 Attack Profession Runtime 還找不到 AttackRifle，會暫時回到 Player Root 搜尋 AttackRifle。這只是目前重構期間的相容功能。等 AttackRifle 正式搬進 AttackProfessionRuntime 後會刪除。")]
    private bool allowLegacyAttackRiflePlayerRootFallback =
        true;

    #endregion

    // =====================================================================
    #region 除錯設定

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，職業 ViewModel 建立、切換、Runtime 武器綁定或移除時會在 Console 顯示詳細資訊。正式版本可以關閉。")]
    private bool debugViewModel =
        true;

    [SerializeField]
    [Tooltip("開啟後，如果 Attack ViewModel 目前仍然綁定 Player Root 上的舊版 AttackRifle，而不是 Attack Profession Runtime 裡的 AttackRifle，會顯示遷移提示。")]
    private bool debugLegacyAttackRifle =
        true;

    #endregion

    // =====================================================================
    #region 本地玩家資料

    /// <summary>
    /// 目前綁定的本地玩家職業元件。
    ///
    /// 只會是這台裝置真正控制的 Player。
    /// </summary>
    private PlayerProfession targetProfession;

    /// <summary>
    /// 本地玩家的 Profession Runtime Manager。
    ///
    /// ------------------------------------------------------------
    ///
    /// 之後取得 AttackRifle 時不再：
    ///
    /// Player.GetComponent&lt;AttackRifle&gt;()
    ///
    /// 而是：
    ///
    /// Runtime Manager
    /// ↓
    /// Current Runtime
    /// ↓
    /// AttackRifle。
    /// </summary>
    private PlayerProfessionRuntimeManager
        targetRuntimeManager;

    /// <summary>
    /// 目前 ViewModel 正在綁定的 AttackRifle。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式版本應該來自：
    ///
    /// AttackProfessionRuntime。
    ///
    /// ------------------------------------------------------------
    ///
    /// 遷移期間可能暫時來自：
///
/// Player Root。
    /// </summary>
    private AttackRifle targetAttackRifle;

    /// <summary>
    /// 目前 Support ViewModel
    /// 正在綁定的 SupportSMG。
    ///
    /// ------------------------------------------------------------
    ///
    /// 正式來源：
    ///
    /// SupportProfessionRuntime
    /// ↓
    /// SupportSMG。
    ///
    /// ------------------------------------------------------------
    ///
    /// 與 targetAttackRifle 分開保存，
    /// 避免 Support 繼續假裝自己使用 AttackRifle。
    /// </summary>
    private SupportSMG targetSupportSMG;

    /// <summary>
    /// 目前 Tank ViewModel 正在觀察的正式 Tank Runtime 近戰來源。
    /// Runtime 刷新後必須重新取得，不能永久保存舊 Runtime 引用。
    /// </summary>
    private TankMeleeCombo targetTankMeleeCombo;

    /// <summary>
    /// Tank Runtime 正式防禦來源。
    /// </summary>
    private TankGuardAbility targetTankGuardAbility;

    /// <summary>
    /// Tank Runtime 正式 GrappleAirborne Air Dash 來源。
    /// </summary>
    private TankAirDashAbility targetTankAirDashAbility;

    /// <summary>
    /// 目前 Attack / Support Profession Runtime
    /// 提供的正式 PlayerAimController。
    ///
    /// 只用來綁定本地 ViewModel ADS Animation。
    /// </summary>
    private PlayerAimController targetAimController;

    /// <summary>
    /// 本地 Player Root 上的正式 Quick Action Controller。
    ///
    /// ViewModel 不讀取 F 鍵，
    /// 而是用它的 ActivationSequence 與 CurrentPhase
    /// 判斷近戰是否真的成功開始及何時結束。
    /// </summary>
    private PlayerQuickActionController
        targetQuickActionController;

    /// <summary>
    /// 目前 targetAttackRifle
    /// 是否是從舊 Player Root Fallback 找到。
    ///
    /// 純粹用於 Debug。
    /// </summary>
    private bool targetAttackRifleIsLegacy;

    #endregion

    // =====================================================================
    #region Runtime 觀察資料

    /// <summary>
    /// 上一次檢查到的 Profession Runtime NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 用來解決：
///
/// Attack Runtime A
/// ↓
/// F1 Refresh
/// ↓
/// Attack Runtime B
///
/// 雖然：
///
/// CurrentProfession 仍然是 Attack，
/// ViewModel 也不需要重新 Instantiate。
///
/// 但是：
///
/// MuzzlePoint
///
/// 必須從 Runtime A 的 Rifle
/// 重新綁到 Runtime B 的 Rifle。
    /// </summary>
    private NetworkObject
        observedRuntimeObject;

    /// <summary>
    /// 上一次觀察到的 Runtime Profession。
    ///
    /// Runtime Object 與 Runtime Profession
    /// 任一變化都會重新綁定 Gameplay Weapon。
    /// </summary>
    private PlayerProfessionType
        observedRuntimeProfession =
            PlayerProfessionType.None;

    /// <summary>
    /// 是否已經完成過第一次 Runtime Observation。
    ///
    /// 避免初始：
///
/// null == null
///
/// 被誤判成「沒有改變」。
    /// </summary>
    private bool runtimeObservationInitialized;

    #endregion

    // =====================================================================
    #region ViewModel Runtime 資料

    /// <summary>
    /// 目前已經生成的第一人稱 ViewModel。
    /// </summary>
    private GameObject currentViewModel;

    /// <summary>
    /// 目前 ViewModel 對應的職業。
    /// </summary>
    private PlayerProfessionType
        currentLoadedProfession =
            PlayerProfessionType.None;

    /// <summary>
    /// 目前 ViewModel 提供的武器視覺引用。
    ///
    /// 例如：
    ///
    /// MuzzlePoint。
    /// </summary>
    private WeaponViewModelReferences
        currentWeaponReferences;

    /// <summary>
    /// 目前 Tank ViewModel 上的專用動畫控制器。
    /// 與 Attack / Support 的槍械 ActionAnimator 分開，避免兩套狀態互相污染。
    /// </summary>
    private FirstPersonTankViewModelAnimator
        currentTankAnimator;

    /// <summary>
    /// ViewModel Layer 的實際 Index。
    /// </summary>
    private int viewModelLayer =
        -1;

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目前已生成的職業 ViewModel。
    /// </summary>
    public GameObject CurrentViewModel =>
        currentViewModel;

    /// <summary>
    /// 目前 ViewModel 對應的職業。
    /// </summary>
    public PlayerProfessionType
        CurrentLoadedProfession =>
            currentLoadedProfession;

    /// <summary>
    /// 是否已經有 ViewModel。
    /// </summary>
    public bool HasViewModel =>
        currentViewModel != null;

    /// <summary>
    /// 目前 ViewModel 的武器引用。
    /// </summary>
    public WeaponViewModelReferences
        CurrentWeaponReferences =>
            currentWeaponReferences;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        RegisterSingleton();

        // =============================================================
        // ViewModel Layer
        // =============================================================

        if (forceViewModelLayer)
        {
            viewModelLayer =
                LayerMask.NameToLayer(
                    viewModelLayerName
                );

            if (viewModelLayer < 0)
            {
                Debug.LogError(
                    $"[{nameof(ProfessionViewModelManager)}] " +
                    $"找不到名稱為「{viewModelLayerName}」的 Layer。" +
                    $"\n請到 Project Settings → Tags and Layers 建立此 Layer。",
                    this
                );
            }
        }

        // =============================================================
        // ViewModel Root
        // =============================================================

        if (viewModelRoot == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"尚未指定 ViewModelRoot。",
                this
            );
        }
    }

    /// <summary>
    /// 本地 ViewModel 更新。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這裡現在監控兩件事情：
///
/// ① Profession 是否改變。
///
/// ② Profession Runtime 是否改變。
///
/// ------------------------------------------------------------
    ///
    /// ① 改變：
///
/// Attack → Tank
///
/// → 重建整個 ViewModel。
///
/// ------------------------------------------------------------
///
/// ② 改變：
///
/// Attack Runtime A
/// →
/// Attack Runtime B
///
/// → ViewModel 不重建。
/// → 只重新綁 Gameplay Weapon。
    /// </summary>
    private void Update()
    {
        // =============================================================
        // 尚未綁定 Player
        // =============================================================

        if (targetProfession == null)
        {
            return;
        }

        PlayerProfessionType profession =
            targetProfession.CurrentProfession;

        // =============================================================
        // Profession 尚未同步
        // =============================================================

        if (profession ==
            PlayerProfessionType.None)
        {
            return;
        }

        // =============================================================
        // Profession 改變 / ViewModel 遺失
        // =============================================================

        bool needsViewModelReload =
            profession !=
                currentLoadedProfession ||
            currentViewModel == null;

        if (needsViewModelReload)
        {
            LoadProfessionViewModel(
                profession
            );

            return;
        }

        // =============================================================
        // Profession 沒變
        // 但 Runtime 可能改變
        // =============================================================

        /*
         * 例如測試：
         *
         * Attack
         * ↓
         * 再按一次 F1
         *
         * Profession Enum 沒變，
         * 所以不用重新 Instantiate ViewModel。
         *
         * 但 Attack Runtime 已經刷新，
         * MuzzlePoint 必須綁到新的 AttackRifle。
         */
        RefreshRuntimeWeaponBinding(
            profession,
            force: false
        );
    }

    private void OnDestroy()
    {
        /*
         * Manager 消失前，
         * 先解除 Gameplay Weapon
         * 對 ViewModel Transform 的引用。
         */
        UnbindCurrentWeaponReferences();

        if (Singleton == this)
        {
            Singleton =
                null;
        }
    }

    #endregion

    // =====================================================================
    #region Singleton

    /// <summary>
    /// 註冊 Singleton。
    /// </summary>
    private void RegisterSingleton()
    {
        if (Singleton == null)
        {
            Singleton =
                this;

            return;
        }

        if (Singleton == this)
        {
            return;
        }

        Debug.LogError(
            $"場景中只能存在一個 " +
            $"{nameof(ProfessionViewModelManager)}。",
            this
        );

        Destroy(
            this
        );
    }

    #endregion

    // =====================================================================
    #region Player Binding

    /// <summary>
    /// 指定目前本地玩家。
    ///
    /// ------------------------------------------------------------
    ///
    /// 現在不再直接從 Player Root
    /// 永久取得 AttackRifle。
    ///
    /// 只保存：
///
/// PlayerProfession
/// PlayerProfessionRuntimeManager。
///
/// ------------------------------------------------------------
    ///
    /// 真正 AttackRifle
    /// 每次由 Current Runtime 動態解析。
    /// </summary>
    public void SetTarget(
        PlayerProfession profession
    )
    {
        // =============================================================
        // 同一 Player
        // =============================================================

        if (targetProfession ==
            profession)
        {
            return;
        }

        // =============================================================
        // 清除舊 Player
        // =============================================================

        ClearCurrentViewModel();

        targetProfession =
            profession;

        targetRuntimeManager =
            null;

        targetAttackRifle =
            null;

        targetSupportSMG =
            null;

        targetTankGuardAbility =
            null;

        targetTankAirDashAbility =
            null;

        targetTankMeleeCombo =
            null;
            
        targetAimController =
            null;

        targetQuickActionController =
            null;

        targetAttackRifleIsLegacy =
            false;

        ResetRuntimeObservation();

        // =============================================================
        // 沒有新 Player
        // =============================================================

        if (targetProfession == null)
        {
            return;
        }

        // =============================================================
        // 取得 Runtime Manager
        // =============================================================

        targetRuntimeManager =
            targetProfession
                .GetComponent<
                    PlayerProfessionRuntimeManager
                >();

        if (targetRuntimeManager == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"本地 Player 找不到 " +
                $"{nameof(PlayerProfessionRuntimeManager)}。" +
                $"\nPlayer：{targetProfession.name}",
                targetProfession
            );
        }

        // =============================================================
        // Player Quick Action
        // =============================================================

        targetQuickActionController =
            targetProfession
                .GetComponent<
                    PlayerQuickActionController
                >();


        if (targetQuickActionController == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"本地 Player 找不到 " +
                $"{nameof(PlayerQuickActionController)}，" +
                $"Attack / Support ViewModel 無法取得正式 Melee 狀態，Tank ViewModel 也無法取得 F Quick Action 動畫事件。" +
                $"\nPlayer：{targetProfession.name}",
                targetProfession
            );
        }

        // =============================================================
        // Profession 已經有效
        // =============================================================

        if (targetProfession.CurrentProfession !=
            PlayerProfessionType.None)
        {
            LoadProfessionViewModel(
                targetProfession.CurrentProfession
            );
        }
    }

    /// <summary>
    /// 清除指定本地玩家。
    /// </summary>
    public void ClearTarget(
        PlayerProfession profession
    )
    {
        if (targetProfession !=
            profession)
        {
            return;
        }

        ClearCurrentViewModel();

        targetProfession =
            null;

        targetRuntimeManager =
            null;

        targetAttackRifle =
            null;

        targetSupportSMG =
            null;

        targetTankGuardAbility =
            null;

        targetTankAirDashAbility =
            null;

        targetTankMeleeCombo =
            null;

        targetAimController =
            null;

        targetQuickActionController =
            null;

        targetAttackRifleIsLegacy =
            false;

        ResetRuntimeObservation();
    }

    #endregion

    // =====================================================================
    #region ViewModel Load

    /// <summary>
    /// 根據玩家職業生成對應 ViewModel。
    /// </summary>
    private void LoadProfessionViewModel(
        PlayerProfessionType profession
    )
    {
        if (viewModelRoot == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"沒有 ViewModelRoot，無法建立 ViewModel。",
                this
            );

            return;
        }

        GameObject prefab =
            GetViewModelPrefab(
                profession
            );

        if (prefab == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"職業「{profession}」沒有設定 ViewModel Prefab。",
                this
            );

            return;
        }

        // =============================================================
        // 清除舊 ViewModel
        // =============================================================

        ClearCurrentViewModel();

        // =============================================================
        // Instantiate
        // =============================================================

        currentViewModel =
            Instantiate(
                prefab,
                viewModelRoot
            );

        // =============================================================
        // Transform Reset
        // =============================================================

        Transform viewModelTransform =
            currentViewModel.transform;

        viewModelTransform.localPosition =
            Vector3.zero;

        viewModelTransform.localRotation =
            Quaternion.identity;

        viewModelTransform.localScale =
            Vector3.one;

        // =============================================================
        // Layer
        // =============================================================

        if (forceViewModelLayer &&
            viewModelLayer >= 0)
        {
            SetLayerRecursively(
                currentViewModel,
                viewModelLayer
            );
        }

        // =============================================================
        // Profession Cache
        // =============================================================

        currentLoadedProfession =
            profession;
        
        // =============================================================
        // Tank ViewModel Animation Reference
        // =============================================================

        currentTankAnimator =
            null;

        if (profession ==
            PlayerProfessionType.Tank)
        {
            currentTankAnimator =
                currentViewModel
                    .GetComponentInChildren<
                        FirstPersonTankViewModelAnimator
                    >(
                        true
                    );

            if (currentTankAnimator == null)
            {
                Debug.LogError(
                    $"[{nameof(ProfessionViewModelManager)}] " +
                    $"Tank ViewModel 找不到 " +
                    $"{nameof(FirstPersonTankViewModelAnimator)}。" +
                    $"\nViewModel：{currentViewModel.name}",
                    currentViewModel
                );
            }
        }

        // =============================================================
        // ViewModel References
        // =============================================================

        BindCurrentWeaponReferences(
            profession
        );

        // =============================================================
        // Appear Presentation
        // =============================================================

        /*
        * Appear 是新 ViewModel 建立完成後的 Presentation。
        *
        * 現在用於職業切換；
        * 未來重生、重新持槍等流程也可以直接呼叫
        * ActionAnimator.PlayAppear()，不必偽造職業切換。
        */
        if (currentWeaponReferences != null &&
            currentWeaponReferences.ActionAnimator != null)
        {
            currentWeaponReferences
                .ActionAnimator
                .PlayAppear();
        }

        if (profession ==
                PlayerProfessionType.Tank &&
            currentTankAnimator != null)
        {
            currentTankAnimator
                .PlayAppear();
        }
        // =============================================================
        // Debug
        // =============================================================

        if (debugViewModel)
        {
            Debug.Log(
                $"[第一人稱 ViewModel]" +
                $"\n已載入職業：{profession}" +
                $"\nPrefab：{prefab.name}" +
                $"\n生成物件：{currentViewModel.name}",
                currentViewModel
            );
        }
    }

    /// <summary>
    /// 取得對應職業 ViewModel Prefab。
    /// </summary>
    private GameObject GetViewModelPrefab(
        PlayerProfessionType profession
    )
    {
        switch (profession)
        {
            case PlayerProfessionType.Attack:
            {
                return
                    attackViewModelPrefab;
            }

            case PlayerProfessionType.Tank:
            {
                return
                    tankViewModelPrefab;
            }

            case PlayerProfessionType.Support:
            {
                return
                    supportViewModelPrefab;
            }

            case PlayerProfessionType.None:
            default:
            {
                return null;
            }
        }
    }

    #endregion

    #region Runtime Weapon Observation


    /// <summary>
    /// 重新檢查目前 Profession Runtime
    /// 所提供的 Gameplay Weapon。
    ///
    /// ====================================================================
    ///
    /// Attack：
    ///
    /// AttackProfessionRuntime
    /// ↓
    /// AttackRifle。
    ///
    /// --------------------------------------------------------------------
    ///
    /// Support：
    ///
    /// SupportProfessionRuntime
    /// ↓
    /// SupportSMG。
    ///
    /// --------------------------------------------------------------------
    ///
    /// Tank：
    ///
    /// 沒有第一人稱槍械 Muzzle Binding。
    ///
    /// ====================================================================
    ///
    /// Update 不會每 Frame 重複掃描 Component。
    ///
    /// 每 Frame 只比較：
    ///
    /// Runtime Object
    /// Runtime Profession。
    ///
    /// 只有 Runtime 真正改變時
    /// 才重新解析 Gameplay Weapon。
    /// </summary>
    private void RefreshRuntimeWeaponBinding(
        PlayerProfessionType profession,
        bool force
    )
    {
        // =============================================================
        // Current Runtime
        // =============================================================

        NetworkObject currentRuntimeObject =
            GetCurrentRuntimeObject();


        PlayerProfessionType
            currentRuntimeProfession =
                GetCurrentRuntimeProfession(
                    currentRuntimeObject
                );


        // =============================================================
        // Runtime 沒有改變
        // =============================================================

        if (force == false &&
            runtimeObservationInitialized &&
            observedRuntimeObject ==
                currentRuntimeObject &&
            observedRuntimeProfession ==
                currentRuntimeProfession)
        {
            return;
        }


        // =============================================================
        // 先解除舊 Gameplay Weapon Muzzle
        // =============================================================

        /*
        * 非常重要：
        *
        * Runtime A
        * ↓
        * Runtime B
        *
        * 必須先讓 Runtime A 清掉
        * 對目前 ViewModel MuzzlePoint 的引用。
        *
        * ------------------------------------------------------------
        *
        * 這同時適用：
        *
        * AttackRifle
        * SupportSMG。
        */

        /*
        * Runtime A → Runtime B 時，
        * ViewModel 不能繼續讀取舊 Runtime 的 Aim Controller。
        */
        UnbindCurrentAimAnimation();

        UnbindCurrentActionAnimation();

        UnbindCurrentTankAnimation();

        UnbindCurrentGameplayWeaponMuzzle();


        // =============================================================
        // 更新 Runtime Observation
        // =============================================================

        observedRuntimeObject =
            currentRuntimeObject;


        observedRuntimeProfession =
            currentRuntimeProfession;


        runtimeObservationInitialized =
            true;


        // =============================================================
        // 清除 Weapon Cache
        // =============================================================

        targetAttackRifle =
            null;

        targetSupportSMG =
            null;

        targetTankGuardAbility =
            null;

        targetTankAirDashAbility =
            null;

        targetTankMeleeCombo =
            null;

        targetAimController =
            null;

        targetAttackRifleIsLegacy =
            false;

        // =============================================================
        // Local ViewModel Aim Animation Binding
        // =============================================================

        targetAimController =
            ResolveRuntimeAimController(
                profession,
                currentRuntimeObject,
                currentRuntimeProfession
            );


        BindCurrentAimAnimation(
            profession
        );

        // =============================================================
        // Profession Routing
        // =============================================================

        switch (profession)
        {
            // =========================================================
            // Attack
            // =========================================================

            case PlayerProfessionType.Attack:
            {
                targetAttackRifle =
                    ResolveAttackRifle(
                        currentRuntimeObject,
                        currentRuntimeProfession,
                        out targetAttackRifleIsLegacy
                    );


                if (targetAttackRifle == null)
                {
                    LogWaitingForRuntimeWeapon(
                        profession,
                        currentRuntimeObject,
                        currentRuntimeProfession
                    );


                    return;
                }


                break;
            }


            // =========================================================
            // Support
            // =========================================================

            case PlayerProfessionType.Support:
            {
                targetSupportSMG =
                    ResolveSupportSMG(
                        currentRuntimeObject,
                        currentRuntimeProfession
                    );


                if (targetSupportSMG == null)
                {
                    LogWaitingForRuntimeWeapon(
                        profession,
                        currentRuntimeObject,
                        currentRuntimeProfession
                    );


                    return;
                }


                break;
            }


            // =========================================================
            // Tank
            // =========================================================

            case PlayerProfessionType.Tank:
            {
                if (currentRuntimeObject == null ||
                    currentRuntimeProfession !=
                        PlayerProfessionType.Tank)
                {
                    return;
                }

                targetTankMeleeCombo =
                    currentRuntimeObject
                        .GetComponentInChildren<
                            TankMeleeCombo
                        >(
                            true
                        );

                targetTankGuardAbility =
                    currentRuntimeObject
                        .GetComponentInChildren<
                            TankGuardAbility
                        >(
                            true
                        );

                targetTankAirDashAbility =
                    currentRuntimeObject
                        .GetComponentInChildren<
                            TankAirDashAbility
                        >(
                            true
                        );

                if (targetTankMeleeCombo == null ||
                    targetTankGuardAbility == null ||
                    targetTankAirDashAbility == null)
                {
                    Debug.LogError(
                        $"[{nameof(ProfessionViewModelManager)}] " +
                        $"Tank Runtime 缺少動畫需要的正式 Gameplay Source。" +
                        $"\nRuntime：{currentRuntimeObject.name}" +
                        $"\nMeleeCombo：" +
                        $"{(targetTankMeleeCombo != null ? "OK" : "Missing")}" +
                        $"\nGuardAbility：" +
                        $"{(targetTankGuardAbility != null ? "OK" : "Missing")}" +
                        $"\nAirDashAbility：" +
                        $"{(targetTankAirDashAbility != null ? "OK" : "Missing")}",
                        currentRuntimeObject
                    );
                }

                if (currentTankAnimator == null)
                {
                    return;
                }

                currentTankAnimator
                    .BindGameplaySources(
                        targetTankMeleeCombo,
                        targetQuickActionController,
                        targetTankGuardAbility,
                        targetTankAirDashAbility
                    );

                if (debugViewModel)
                {
                    Debug.Log(
                        $"[第一人稱 Tank ViewModel Animation 綁定]" +
                        $"\nViewModel：{currentViewModel.name}" +
                        $"\nTankMeleeCombo：" +
                        $"{(targetTankMeleeCombo != null ? targetTankMeleeCombo.name : "None")}" +
                        $"\nQuickAction：" +
                        $"{(targetQuickActionController != null ? targetQuickActionController.name : "None")}" +
                        $"\nTankGuardAbility：" +
                        $"{(targetTankGuardAbility != null ? targetTankGuardAbility.name : "None")}" +
                        $"\nTankAirDashAbility：" +
                        $"{(targetTankAirDashAbility != null ? targetTankAirDashAbility.name : "None")}",
                        currentTankAnimator
                    );
                }

                // Tank 沒有 Rifle / SMG Muzzle Binding。
                return;
            }

            // =========================================================
            // None
            // =========================================================

            case PlayerProfessionType.None:
            default:
            {
                return;
            }
        }


        // =============================================================
        // Local ViewModel Action Animation Binding
        // =============================================================

        BindCurrentActionAnimation(
            profession
        );

        // =============================================================
        // 正式 Muzzle Binding
        // =============================================================

        BindCurrentGameplayWeaponMuzzle(
            profession
        );
    }


    /// <summary>
    /// 取得目前正式 Profession Runtime Object。
    /// </summary>
    private NetworkObject GetCurrentRuntimeObject()
    {
        if (targetRuntimeManager == null)
        {
            return null;
        }


        NetworkObject runtimeObject =
            targetRuntimeManager
                .CurrentRuntimeObject;


        if (runtimeObject == null ||
            runtimeObject.IsValid == false)
        {
            return null;
        }


        return
            runtimeObject;
    }


    /// <summary>
    /// 取得 Runtime Manager
    /// 目前正式記錄的 Runtime Profession。
    ///
    /// Runtime Object 尚未 Resolve 時
    /// 回傳 None。
    /// </summary>
    private PlayerProfessionType
        GetCurrentRuntimeProfession(
            NetworkObject runtimeObject
        )
    {
        if (targetRuntimeManager == null ||
            runtimeObject == null)
        {
            return
                PlayerProfessionType.None;
        }


        return
            targetRuntimeManager
                .CurrentRuntimeProfession;
    }


    /// <summary>
    /// 解析 Attack 職業目前真正使用的 AttackRifle。
    ///
    /// ====================================================================
    ///
    /// 第一優先：
    ///
    /// AttackProfessionRuntime
    /// ↓
    /// AttackRifle。
    ///
    /// --------------------------------------------------------------------
    ///
    /// 遷移階段第二優先：
    ///
    /// Player Root
    /// ↓
    /// AttackRifle。
    ///
    /// --------------------------------------------------------------------
    ///
    /// Legacy Fallback
    /// 只保留給 Attack。
    /// Support 不會使用這條舊路徑。
    /// </summary>
    private AttackRifle ResolveAttackRifle(
        NetworkObject runtimeObject,
        PlayerProfessionType runtimeProfession,
        out bool usedLegacyFallback
    )
    {
        usedLegacyFallback =
            false;


        // =============================================================
        // 1. 正式 Attack Runtime
        // =============================================================

        if (runtimeObject != null &&
            runtimeObject.IsValid &&
            runtimeProfession ==
                PlayerProfessionType.Attack)
        {
            AttackRifle runtimeRifle =
                runtimeObject
                    .GetComponentInChildren<
                        AttackRifle
                    >(
                        true
                    );


            if (runtimeRifle != null)
            {
                if (debugViewModel)
                {
                    Debug.Log(
                        $"[第一人稱 ViewModel 武器解析]" +
                        $"\n職業：Attack" +
                        $"\nRuntime：{runtimeObject.name}" +
                        $"\nWeapon：{runtimeRifle.name}" +
                        $"\nType：{nameof(AttackRifle)}",
                        runtimeRifle
                    );
                }


                return
                    runtimeRifle;
            }
        }


        // =============================================================
        // 2. Legacy Attack Player Root
        // =============================================================

        if (allowLegacyAttackRiflePlayerRootFallback &&
            targetProfession != null)
        {
            AttackRifle legacyRifle =
                targetProfession
                    .GetComponent<AttackRifle>();


            if (legacyRifle != null)
            {
                usedLegacyFallback =
                    true;


                if (debugLegacyAttackRifle)
                {
                    Debug.LogWarning(
                        $"[{nameof(ProfessionViewModelManager)}] " +
                        $"Attack 目前仍在使用 Player Root 的舊版 AttackRifle。" +
                        $"\nRifle：{legacyRifle.name}" +
                        $"\n正式版本應從 AttackProfessionRuntime 取得。",
                        legacyRifle
                    );
                }


                return
                    legacyRifle;
            }
        }


        return null;
    }


    /// <summary>
    /// 解析 Support 職業目前真正使用的 SupportSMG。
    ///
    /// ====================================================================
    ///
    /// Support 沒有 Legacy Player Root Fallback。
    ///
    /// 正式來源只能是：
    ///
    /// SupportProfessionRuntime
    /// ↓
    /// SupportSMG。
    ///
    /// ====================================================================
    ///
    /// 這可以防止 Support 又意外拿到：
    ///
    /// AttackRifle
    /// Player Root Weapon
    /// 舊 Healing Override Weapon。
    /// </summary>
    private SupportSMG ResolveSupportSMG(
        NetworkObject runtimeObject,
        PlayerProfessionType runtimeProfession
    )
    {
        // =============================================================
        // Runtime 必須真的屬於 Support
        // =============================================================

        if (runtimeObject == null ||
            runtimeObject.IsValid == false ||
            runtimeProfession !=
                PlayerProfessionType.Support)
        {
            return null;
        }


        // =============================================================
        // Resolve SupportSMG
        // =============================================================

        SupportSMG runtimeSMG =
            runtimeObject
                .GetComponentInChildren<
                    SupportSMG
                >(
                    true
                );


        if (runtimeSMG == null)
        {
            return null;
        }


        // =============================================================
        // Debug
        // =============================================================

        if (debugViewModel)
        {
            Debug.Log(
                $"[第一人稱 ViewModel 武器解析]" +
                $"\n職業：Support" +
                $"\nRuntime：{runtimeObject.name}" +
                $"\nWeapon：{runtimeSMG.name}" +
                $"\nType：{nameof(SupportSMG)}",
                runtimeSMG
            );
        }


        return
            runtimeSMG;
    }


    /// <summary>
    /// Runtime Weapon 尚未同步完成時的共用 Debug。
    ///
    /// Client 剛切職業時可能會發生：
    ///
    /// Profession 已同步
    /// ↓
    /// ViewModel 已生成
    /// ↓
    /// Runtime NetworkObject 稍晚才 Resolve。
    ///
    /// 因此這裡只顯示等待資訊，
    /// 不視為致命錯誤。
    /// </summary>
    private void LogWaitingForRuntimeWeapon(
        PlayerProfessionType profession,
        NetworkObject runtimeObject,
        PlayerProfessionType runtimeProfession
    )
    {
        if (debugViewModel == false)
        {
            return;
        }


        Debug.Log(
            $"[{nameof(ProfessionViewModelManager)}] " +
            $"職業「{profession}」的 ViewModel 已建立，" +
            $"但目前尚未取得對應 Runtime Weapon。" +
            $"\nRuntime：" +
            $"{(runtimeObject != null ? runtimeObject.name : "尚未同步")}" +
            $"\nRuntime Profession：{runtimeProfession}" +
            $"\n等待 Profession Runtime Weapon。",
            this
        );
    }


    /// <summary>
    /// 重置 Runtime 觀察狀態。
    /// </summary>
    private void ResetRuntimeObservation()
    {
        observedRuntimeObject =
            null;


        observedRuntimeProfession =
            PlayerProfessionType.None;


        runtimeObservationInitialized =
            false;
    }


    #endregion

    #region ViewModel Weapon References

    /// <summary>
    /// 從目前正式 Profession Runtime
    /// 解析 Attack / Support 共用的 PlayerAimController。
    /// </summary>
    private PlayerAimController
        ResolveRuntimeAimController(
            PlayerProfessionType requestedProfession,
            NetworkObject runtimeObject,
            PlayerProfessionType runtimeProfession
        )
    {
        bool usesAim =
            requestedProfession ==
                PlayerProfessionType.Attack ||
            requestedProfession ==
                PlayerProfessionType.Support;


        if (usesAim == false ||
            runtimeObject == null ||
            runtimeObject.IsValid == false ||
            runtimeProfession !=
                requestedProfession)
        {
            return null;
        }


        return runtimeObject
            .GetComponentInChildren<
                PlayerAimController
            >(
                true
            );
    }


    /// <summary>
    /// 將目前 Runtime Aim Controller
    /// 綁定到目前 ViewModel 的 ADS 動畫控制器。
    /// </summary>
    private void BindCurrentAimAnimation(
        PlayerProfessionType profession
    )
    {
        bool requiresAimAnimation =
            profession ==
                PlayerProfessionType.Attack ||
            profession ==
                PlayerProfessionType.Support;


        if (requiresAimAnimation == false ||
            currentWeaponReferences == null)
        {
            return;
        }


        FirstPersonViewModelAimAnimator aimAnimator =
            currentWeaponReferences
                .AimAnimator;


        if (aimAnimator == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"職業「{profession}」的 ViewModel 找不到 " +
                $"{nameof(FirstPersonViewModelAimAnimator)}。" +
                $"\nViewModel：{currentViewModel.name}",
                currentViewModel
            );


            return;
        }


        aimAnimator.BindAimController(
            targetAimController
        );


        if (debugViewModel)
        {
            Debug.Log(
                $"[第一人稱 ViewModel Aim 綁定]" +
                $"\n職業：{profession}" +
                $"\nViewModel：{currentViewModel.name}" +
                $"\nAim Controller：" +
                $"{(targetAimController != null ? targetAimController.name : "等待 Runtime")}",
                aimAnimator
            );
        }
    }


    /// <summary>
    /// 解除目前 ViewModel 對舊 Runtime Aim Controller 的引用。
    /// </summary>
    private void UnbindCurrentAimAnimation()
    {
        if (currentWeaponReferences != null &&
            currentWeaponReferences.AimAnimator != null)
        {
            currentWeaponReferences
                .AimAnimator
                .BindAimController(
                    null
                );
        }


        targetAimController =
            null;
    }

    /// <summary>
    /// 將目前 Runtime Weapon 與本地 Quick Action Controller
    /// 綁定到目前 ViewModel 的基礎動作動畫控制器。
    ///
    /// Attack：AttackRifle。
    /// Support：SupportSMG。
    /// Melee：PlayerQuickActionController。
    /// </summary>
    private void BindCurrentActionAnimation(
        PlayerProfessionType profession
    )
    {
        bool requiresActionAnimation =
            profession ==
                PlayerProfessionType.Attack ||
            profession ==
                PlayerProfessionType.Support;


        if (requiresActionAnimation == false ||
            currentWeaponReferences == null)
        {
            return;
        }


        FirstPersonViewModelActionAnimator
            actionAnimator =
                currentWeaponReferences
                    .ActionAnimator;


        if (actionAnimator == null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"職業「{profession}」的 ViewModel 找不到 " +
                $"{nameof(FirstPersonViewModelActionAnimator)}。" +
                $"\nViewModel：{currentViewModel.name}",
                currentViewModel
            );


            return;
        }


        actionAnimator.BindGameplaySources(
            profession,
            targetAttackRifle,
            targetSupportSMG,
            targetQuickActionController
        );


        if (debugViewModel)
        {
            Debug.Log(
                $"[第一人稱 ViewModel Action Animation 綁定]" +
                $"\n職業：{profession}" +
                $"\nViewModel：{currentViewModel.name}" +
                $"\nAttackRifle：" +
                $"{(targetAttackRifle != null ? targetAttackRifle.name : "None")}" +
                $"\nSupportSMG：" +
                $"{(targetSupportSMG != null ? targetSupportSMG.name : "None")}" +
                $"\nQuickAction：" +
                $"{(targetQuickActionController != null ? targetQuickActionController.name : "None")}",
                actionAnimator
            );
        }
    }


    /// <summary>
    /// 解除目前 ViewModel 對舊 Runtime Weapon
    /// 與 Quick Action Controller 的動畫觀察。
    /// </summary>
    private void UnbindCurrentActionAnimation()
    {
        if (currentWeaponReferences == null ||
            currentWeaponReferences.ActionAnimator == null)
        {
            return;
        }


        currentWeaponReferences
            .ActionAnimator
            .UnbindGameplaySources();
}

    /// <summary>
    /// 取得目前職業 ViewModel 的
    /// WeaponViewModelReferences。
    ///
    /// ====================================================================
    ///
    /// Attack / Support：
    ///
    /// 必須提供：
    ///
    /// WeaponViewModelReferences
    /// ↓
    /// MuzzlePoint。
    ///
    /// --------------------------------------------------------------------
    ///
    /// Tank：
    ///
    /// 目前沒有槍械 MuzzlePoint 的硬性要求。
    ///
    /// ====================================================================
    ///
    /// ViewModel References
    /// 與
    /// Gameplay Weapon
    ///
    /// 是兩件不同的事情。
    ///
    /// ViewModel 不需要因為 Runtime Refresh
    /// 而重新 Instantiate。
    ///
    /// 同一個 MuzzlePoint
    /// 可以重新綁到新的 Runtime Weapon。
    /// </summary>
    private void BindCurrentWeaponReferences(
        PlayerProfessionType profession
    )
    {
        // =============================================================
        // Clear Local Reference Cache
        // =============================================================

        currentWeaponReferences =
            null;


        if (currentViewModel == null)
        {
            return;
        }


        // =============================================================
        // Find WeaponViewModelReferences
        // =============================================================

        currentWeaponReferences =
            currentViewModel
                .GetComponentInChildren<
                    WeaponViewModelReferences
                >(
                    true
                );


        // =============================================================
        // Rifle / SMG Profession
        // =============================================================

        bool requiresWeaponReferences =
            profession ==
                PlayerProfessionType.Attack ||
            profession ==
                PlayerProfessionType.Support;


        // =============================================================
        // Missing References
        // =============================================================

        if (currentWeaponReferences == null)
        {
            if (requiresWeaponReferences)
            {
                Debug.LogError(
                    $"[{nameof(ProfessionViewModelManager)}] " +
                    $"職業「{profession}」的 ViewModel 找不到 " +
                    $"{nameof(WeaponViewModelReferences)}。" +
                    $"\nViewModel：{currentViewModel.name}" +
                    $"\nAttack 與 Support 都需要提供動態 MuzzlePoint。",
                    currentViewModel
                );
            }


            /*
            * Runtime Observation 還是必須更新。
            *
            * 避免 Runtime Cache
            * 留著上一個職業資料。
            */
            RefreshRuntimeWeaponBinding(
                profession,
                force: true
            );


            return;
        }


        // =============================================================
        // Muzzle Validation
        // =============================================================

        if (requiresWeaponReferences &&
            currentWeaponReferences.MuzzlePoint ==
                null)
        {
            Debug.LogError(
                $"[{nameof(ProfessionViewModelManager)}] " +
                $"職業「{profession}」的 " +
                $"{nameof(WeaponViewModelReferences)} " +
                $"沒有指定 MuzzlePoint。" +
                $"\nViewModel：{currentViewModel.name}",
                currentWeaponReferences
            );
        }


        // =============================================================
        // Runtime Gameplay Weapon Binding
        // =============================================================

        RefreshRuntimeWeaponBinding(
            profession,
            force: true
        );
    }


    /// <summary>
    /// 將目前 ViewModel MuzzlePoint
    /// 綁定到目前職業真正的 Gameplay Weapon。
    ///
    /// ====================================================================
    ///
    /// Attack
    /// → AttackRifle。
    ///
    /// Support
    /// → SupportSMG。
    ///
    /// Tank
    /// → 不處理。
    /// </summary>
    private void BindCurrentGameplayWeaponMuzzle(
        PlayerProfessionType profession
    )
    {
        // =============================================================
        // ViewModel References
        // =============================================================

        if (currentWeaponReferences == null)
        {
            return;
        }


        Transform muzzlePoint =
            currentWeaponReferences
                .MuzzlePoint;


        if (muzzlePoint == null)
        {
            return;
        }


        // =============================================================
        // Profession Weapon
        // =============================================================

        switch (profession)
        {
            // =========================================================
            // Attack
            // =========================================================

            case PlayerProfessionType.Attack:
            {
                if (targetAttackRifle == null)
                {
                    return;
                }


                targetAttackRifle
                    .SetTracerMuzzlePoint(
                        muzzlePoint
                    );


                if (debugViewModel)
                {
                    string source =
                        targetAttackRifleIsLegacy
                            ? "Legacy Player Root"
                            : "Attack Profession Runtime";


                    Debug.Log(
                        $"[第一人稱 ViewModel 武器綁定]" +
                        $"\n職業：Attack" +
                        $"\n武器類型：{nameof(AttackRifle)}" +
                        $"\n武器來源：{source}" +
                        $"\nWeapon：{targetAttackRifle.name}" +
                        $"\nViewModel：{currentViewModel.name}" +
                        $"\nMuzzlePoint：{muzzlePoint.name}" +
                        $"\nMuzzle 世界位置：{muzzlePoint.position}",
                        muzzlePoint
                    );
                }


                break;
            }


            // =========================================================
            // Support
            // =========================================================

            case PlayerProfessionType.Support:
            {
                if (targetSupportSMG == null)
                {
                    return;
                }


                targetSupportSMG
                    .SetTracerMuzzlePoint(
                        muzzlePoint
                    );


                if (debugViewModel)
                {
                    Debug.Log(
                        $"[第一人稱 ViewModel 武器綁定]" +
                        $"\n職業：Support" +
                        $"\n武器類型：{nameof(SupportSMG)}" +
                        $"\n武器來源：Support Profession Runtime" +
                        $"\nWeapon：{targetSupportSMG.name}" +
                        $"\nViewModel：{currentViewModel.name}" +
                        $"\nMuzzlePoint：{muzzlePoint.name}" +
                        $"\nMuzzle 世界位置：{muzzlePoint.position}",
                        muzzlePoint
                    );
                }


                break;
            }


            // =========================================================
            // Tank / None
            // =========================================================

            case PlayerProfessionType.Tank:
            case PlayerProfessionType.None:
            default:
            {
                break;
            }
        }
    }


    /// <summary>
    /// 解除目前 Runtime Weapon
    /// 對 ViewModel MuzzlePoint 的引用。
    ///
    /// ====================================================================
    ///
    /// 一定要發生在：
    ///
    /// Destroy ViewModel
    ///
    /// 以前。
    ///
    /// --------------------------------------------------------------------
    ///
    /// 同時處理：
    ///
    /// AttackRifle
    /// SupportSMG。
    /// </summary>
    private void UnbindCurrentGameplayWeaponMuzzle()
    {
        // =============================================================
        // 沒有 ViewModel Muzzle
        // =============================================================

        if (currentWeaponReferences == null ||
            currentWeaponReferences.MuzzlePoint ==
                null)
        {
            targetAttackRifle =
                null;


            targetSupportSMG =
                null;


            targetAttackRifleIsLegacy =
                false;


            return;
        }


        Transform muzzlePoint =
            currentWeaponReferences
                .MuzzlePoint;


        // =============================================================
        // Attack
        // =============================================================

        if (targetAttackRifle != null)
        {
            targetAttackRifle
                .ClearTracerMuzzlePoint(
                    muzzlePoint
                );


            if (debugViewModel)
            {
                Debug.Log(
                    $"[第一人稱 ViewModel 武器解除]" +
                    $"\n職業：Attack" +
                    $"\nWeapon：{targetAttackRifle.name}" +
                    $"\nMuzzlePoint：{muzzlePoint.name}",
                    targetAttackRifle
                );
            }
        }


        // =============================================================
        // Support
        // =============================================================

        if (targetSupportSMG != null)
        {
            targetSupportSMG
                .ClearTracerMuzzlePoint(
                    muzzlePoint
                );


            if (debugViewModel)
            {
                Debug.Log(
                    $"[第一人稱 ViewModel 武器解除]" +
                    $"\n職業：Support" +
                    $"\nWeapon：{targetSupportSMG.name}" +
                    $"\nMuzzlePoint：{muzzlePoint.name}",
                    targetSupportSMG
                );
            }
        }


        // =============================================================
        // Clear Cache
        // =============================================================

        targetAttackRifle =
            null;


        targetSupportSMG =
            null;


        targetAttackRifleIsLegacy =
            false;
    }


    /// <summary>
    /// 解除目前 ViewModel
    /// 與 Gameplay Weapon 的所有引用。
    ///
    /// 必須在 Destroy ViewModel 前執行。
    /// </summary>
    private void UnbindCurrentWeaponReferences()
    {

        // =============================================================
        // ADS Aim Animation
        // =============================================================

        UnbindCurrentAimAnimation();

        // =============================================================
        // Gameplay Weapon
        // =============================================================

        UnbindCurrentGameplayWeaponMuzzle();

        // =============================================================
        // Base Action Animation
        // =============================================================

        UnbindCurrentActionAnimation();

        // =============================================================
        // Tank Animation
        // =============================================================

        UnbindCurrentTankAnimation();

        // =============================================================
        // ViewModel References
        // =============================================================

        currentWeaponReferences =
            null;

        currentTankAnimator =
            null;
    }

    /// <summary>
    /// 解除 Tank ViewModel 對舊 Profession Runtime 的動畫觀察。
    /// 必須在 Runtime Despawn 或 ViewModel Destroy 前完成。
    /// </summary>
    private void UnbindCurrentTankAnimation()
    {
        if (currentTankAnimator != null)
        {
            currentTankAnimator
                .UnbindGameplaySources();
        }

        targetTankMeleeCombo =
            null;

        targetTankGuardAbility =
            null;

        targetTankAirDashAbility =
            null;
    }

    #endregion

    // =====================================================================
    #region ViewModel Clear

    /// <summary>
    /// 移除目前職業 ViewModel。
    /// </summary>
    private void ClearCurrentViewModel()
    {
        // =============================================================
        // 先解除 Gameplay 引用
        // =============================================================

        /*
         * 一定要在 Destroy ViewModel 之前。
         *
         * 否則 Gameplay Weapon
         * 可能暫時保留一個
         * 已 Destroy 的 Transform Reference。
         */
        UnbindCurrentWeaponReferences();

        // =============================================================
        // Destroy
        // =============================================================

        if (currentViewModel != null)
        {
            if (debugViewModel)
            {
                Debug.Log(
                    $"[第一人稱 ViewModel]" +
                    $"\n移除職業：{currentLoadedProfession}" +
                    $"\n物件：{currentViewModel.name}",
                    currentViewModel
                );
            }

            Destroy(
                currentViewModel
            );
        }

        currentViewModel =
            null;

        currentLoadedProfession =
            PlayerProfessionType.None;
    }

    #endregion

    // =====================================================================
    #region Layer

    /// <summary>
    /// 將 GameObject 與所有子物件
    /// 設定成指定 Layer。
    /// </summary>
    private void SetLayerRecursively(
        GameObject target,
        int layer
    )
    {
        if (target == null)
        {
            return;
        }

        target.layer =
            layer;

        Transform targetTransform =
            target.transform;

        for (int i = 0;
             i < targetTransform.childCount;
             i++)
        {
            Transform child =
                targetTransform.GetChild(
                    i
                );

            SetLayerRecursively(
                child.gameObject,
                layer
            );
        }
    }

    #endregion
}