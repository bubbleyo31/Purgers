using Fusion;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家職業 Runtime 總管理器。
///
/// ------------------------------------------------------------
///
/// 負責：
///
/// PlayerProfession
/// ↓
/// 決定目前需要哪一個 Profession Runtime
///
/// Attack
/// → Attack Runtime Prefab
///
/// Tank
/// → Tank Runtime Prefab
///
/// Support
/// → Support Runtime Prefab
///
/// ------------------------------------------------------------
///
/// 同時目前提供測試快捷鍵：
///
/// F1 → Attack
/// F2 → Tank
/// F3 → Support
///
/// ------------------------------------------------------------
///
/// 注意：
///
/// F1 / F2 / F3 只是開發測試入口。
///
/// 未來飛船選職業時，
/// 不需要改 Runtime 系統。
///
/// 飛船只需要要求：
///
/// RequestProfessionChange(...)
///
/// 就能沿用同一套切換流程。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerActionGate))]
public class PlayerProfessionRuntimeManager :
    NetworkBehaviour
{
    // =====================================================================
    #region Player 引用

    [Header("Player 引用")]

    [SerializeField]
    [Tooltip("玩家職業資料。Runtime Manager 會根據 Current Profession 決定應該生成哪一個職業 Runtime。若留空會自動取得。")]
    private PlayerProfession profession;

    #endregion

    // =====================================================================
    #region Runtime Prefabs

    [Header("職業 Runtime Prefab")]

    [SerializeField]
    [Tooltip("Attack 職業 Runtime Network Prefab。Prefab Root 必須擁有 NetworkObject 與 PlayerProfessionRuntime，並將 Prefab Profession 設為 Attack。")]
    private NetworkObject attackRuntimePrefab;

    [SerializeField]
    [Tooltip("Tank 職業 Runtime Network Prefab。Prefab Root 必須擁有 NetworkObject 與 PlayerProfessionRuntime，並將 Prefab Profession 設為 Tank。")]
    private NetworkObject tankRuntimePrefab;

    [SerializeField]
    [Tooltip("Support 職業 Runtime Network Prefab。Prefab Root 必須擁有 NetworkObject 與 PlayerProfessionRuntime，並將 Prefab Profession 設為 Support。")]
    private NetworkObject supportRuntimePrefab;

    #endregion

    [SerializeField]
    [Tooltip("玩家統一操作封鎖管理器。職業 Runtime 被替換前，Runtime Manager 會清除舊武器可能留下的 WeaponAction Block。若留空會自動取得。")]
    private PlayerActionGate actionGate;

    // =====================================================================
    #region Movement Modifier Cache

    /// <summary>
    /// 上一次建立 Movement Modifier Cache 時
    /// 所使用的 Profession Runtime。
    ///
    /// Runtime 沒有改變時，
    /// 不會每 Tick 重掃 Component。
    /// </summary>
    private NetworkObject
        cachedMovementModifierRuntime;

    /// <summary>
    /// 目前 Runtime 裡所有
    /// IPlayerMovementInputModifier。
    ///
    /// 使用 MonoBehaviour 保存，
    /// 可以正確處理 Unity Destroy Null 判斷。
    /// </summary>
    private readonly List<MonoBehaviour>
        cachedMovementModifierBehaviours =
            new List<MonoBehaviour>(4);

    #endregion

    // =====================================================================
    #region 測試切換

    [Header("測試職業切換")]

    [SerializeField]
    [Tooltip("開啟後，本地 Input Authority 玩家可以使用 F1、F2、F3 即時切換職業。這只是目前開發測試功能，之後飛船選角完成後可以直接關閉。")]
    private bool enableTestHotkeys =
        true;

    [SerializeField]
    [Tooltip("開啟後，即使玩家目前已經是同一職業，再按一次該職業快捷鍵仍會重新建立 Runtime。這對測試冷卻與初始狀態非常方便。正式遊戲通常應關閉。")]
    private bool allowRefreshSameProfessionForTesting =
        true;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後會顯示職業切換請求、Runtime Spawn、Runtime Despawn 與目前職業資訊。這個階段建議保持開啟。")]
    private bool debugRuntimeManager =
        true;

    #endregion

    // =====================================================================
    #region Fusion State

    /// <summary>
    /// 玩家目前正式使用中的職業 Runtime NetworkObject。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這是一個 Networked Object Reference。
    ///
    /// 未來 PlayerCombatController、
    /// HUD 或其他 Core System
    /// 可以從這裡取得目前職業 Runtime。
    /// </summary>
    [Networked]
    public NetworkObject CurrentRuntimeObject
    {
        get;
        private set;
    }

    /// <summary>
    /// CurrentRuntimeObject
    /// 目前代表哪個職業。
    ///
    /// 額外保存一份是為了：
///
/// Debug
/// Runtime 驗證
/// Profession / Runtime 不一致時自動修正。
    /// </summary>
    [Networked]
    public PlayerProfessionType
        CurrentRuntimeProfession
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 公開資料

    /// <summary>
    /// 目前正式 Runtime。
    /// </summary>
    public PlayerProfessionRuntime CurrentRuntime
    {
        get
        {
            if (CurrentRuntimeObject == null)
            {
                return null;
            }

            return
                CurrentRuntimeObject
                    .GetComponent<PlayerProfessionRuntime>();
        }
    }

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (actionGate == null)
        {
            actionGate =
                GetComponent<PlayerActionGate>();
        }
    }

    /// <summary>
    /// 目前 F1 / F2 / F3 是開發測試快捷鍵，
    /// 所以直接由本地 Input Authority 在 Update 讀取。
    ///
    /// ------------------------------------------------------------
    ///
    /// 這不進 NetInput。
    ///
    /// 原因：
    ///
    /// 這不是持續型 Gameplay Input，
    /// 而是低頻的測試管理指令。
    ///
    /// 本地 Client 讀到後，
    /// 再透過 RPC 要求 State Authority 執行正式切換。
    /// </summary>
    private void Update()
    {
        if (enableTestHotkeys == false)
        {
            return;
        }

        if (Object == null ||
            Object.IsValid == false ||
            Object.HasInputAuthority == false)
        {
            return;
        }

        Keyboard keyboard =
            Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        // =============================================================
        // F1 → Attack
        // =============================================================

        if (keyboard.f1Key.wasPressedThisFrame)
        {
            RequestProfessionChange(
                PlayerProfessionType.Attack
            );

            return;
        }

        // =============================================================
        // F2 → Tank
        // =============================================================

        if (keyboard.f2Key.wasPressedThisFrame)
        {
            RequestProfessionChange(
                PlayerProfessionType.Tank
            );

            return;
        }

        // =============================================================
        // F3 → Support
        // =============================================================

        if (keyboard.f3Key.wasPressedThisFrame)
        {
            RequestProfessionChange(
                PlayerProfessionType.Support
            );
        }
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * 不在這裡直接 Spawn Runtime。
         *
         * 原因：
         *
         * PlayerProfession.Spawned()
         * 也會初始化 Default Profession。
         *
         * 同一 NetworkObject 上不同 NetworkBehaviour
         * 的初始化不應互相依賴執行先後。
         *
         * 下一個 FixedUpdateNetwork
         * 再統一確認 Runtime。
         */
    }

    public override void FixedUpdateNetwork()
    {
        /*
         * Profession Runtime 的正式 Spawn / Despawn
         * 只由 Player 的 State Authority 處理。
         */
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        EnsureRuntimeMatchesProfession();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        /*
         * Player 被 Despawn 時，
         * 它的獨立 Profession Runtime
         * 也不能留在世界裡。
         */
        NetworkObject runtime =
            CurrentRuntimeObject;

        if (runtime == null ||
            runtime.IsValid == false)
        {
            return;
        }

        /*
         * 只有 Runtime 的 State Authority
         * 可以正式 Despawn 它。
         */
        if (runtime.HasStateAuthority)
        {
            runner.Despawn(
                runtime
            );
        }
    }

    #endregion

    // =====================================================================
    #region 對外切換入口

    /// <summary>
    /// 要求玩家切換職業。
    ///
    /// ------------------------------------------------------------
    ///
    /// 本機 Input Authority：
    ///
    /// → RPC
    /// → State Authority
    /// → 正式切換。
    ///
    /// ------------------------------------------------------------
    ///
    /// 未來飛船選角 UI
    /// 也可以呼叫這個入口。
    /// </summary>
    public void RequestProfessionChange(
        PlayerProfessionType newProfession
    )
    {
        if (IsValidProfession(
                newProfession
            ) == false)
        {
            return;
        }

        if (Object == null ||
            Object.IsValid == false)
        {
            return;
        }

        /*
         * 只有操作這名 Player 的 Input Authority
         * 可以發出 Client 端切換要求。
         */
        if (Object.HasInputAuthority == false)
        {
            return;
        }

        /*
         * RPC 傳 byte，
         * 避免把測試系統額外依賴 enum serialization。
         */
        RPC_RequestProfessionChange(
            (byte)newProfession
        );
    }

    #endregion

    // =====================================================================
    #region RPC

    /// <summary>
    /// Input Authority
    /// 向 State Authority 要求切換職業。
    ///
    /// 真正：
///
/// SetProfession
/// Runner.Spawn
/// Runner.Despawn
///
/// 都只在 State Authority 發生。
    /// </summary>
    [Rpc(
        sources: RpcSources.InputAuthority,
        targets: RpcTargets.StateAuthority
    )]
    private void RPC_RequestProfessionChange(
        byte professionValue,
        RpcInfo info = default
    )
    {
        PlayerProfessionType requestedProfession =
            (PlayerProfessionType)professionValue;

        if (IsValidProfession(
                requestedProfession
            ) == false)
        {
            Debug.LogWarning(
                $"[{nameof(PlayerProfessionRuntimeManager)}] " +
                $"收到無效職業切換要求。" +
                $"\n來源：{info.Source}" +
                $"\n值：{professionValue}",
                this
            );

            return;
        }

        if (debugRuntimeManager)
        {
            Debug.Log(
                $"[職業切換要求]" +
                $"\n玩家：{Object.InputAuthority}" +
                $"\nRPC Source：{info.Source}" +
                $"\n目前職業：{profession.CurrentProfession}" +
                $"\n要求職業：{requestedProfession}",
                this
            );
        }

        ApplyProfessionChangeStateAuthority(
            requestedProfession,

            allowRefreshSameProfessionForTesting
        );
    }

    #endregion

    // =====================================================================
    #region Runtime 同步檢查

    /// <summary>
    /// 確保：
///
/// PlayerProfession.CurrentProfession
///
/// 與：
///
/// CurrentProfessionRuntime
///
/// 永遠一致。
///
/// ------------------------------------------------------------
///
/// 這很重要，因為未來職業不只可能從
/// F1 / F2 / F3 修改。
///
/// Lobby
/// 飛船
/// Server
///
/// 都可能直接修改 PlayerProfession。
///
/// Runtime Manager 不應依賴某一種入口。
    /// </summary>
    private void EnsureRuntimeMatchesProfession()
    {
        if (profession == null ||
            profession.HasProfession == false)
        {
            return;
        }

        bool runtimeMissing =
            CurrentRuntimeObject == null ||
            CurrentRuntimeObject.IsValid == false;

        bool professionMismatch =
            CurrentRuntimeProfession !=
            profession.CurrentProfession;

        if (runtimeMissing == false &&
            professionMismatch == false)
        {
            return;
        }

        /*
         * 這裡是「同步修正」，
         * 不是玩家重複按相同快捷鍵。
         *
         * 所以不需要刷新相同 Runtime，
         * 只有真的 Missing / Mismatch 才重建。
         */
        RebuildRuntime(
            profession.CurrentProfession
        );
    }

    #endregion

    /// <summary>
    /// 職業 Runtime 被替換前，
    /// 清除舊武器動作自己持有的 Action Block。
    ///
    /// 只清除 WeaponAction，
    /// 不會影響其他封鎖來源。
    /// </summary>
    private void ClearWeaponActionBlocks()
    {
        if (actionGate == null ||
            actionGate.Object == null ||
            actionGate.Object.IsValid == false)
        {
            return;
        }


        actionGate.ClearBlocks(
            PlayerActionBlockSource.WeaponAction
        );
    }

    // =====================================================================
    #region State Authority 切換

    /// <summary>
    /// State Authority 正式處理職業切換。
    /// </summary>
    private void ApplyProfessionChangeStateAuthority(
        PlayerProfessionType newProfession,
        bool allowSameProfessionRefresh
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return;
        }

        if (IsValidProfession(
                newProfession
            ) == false)
        {
            return;
        }

        bool sameProfession =
            profession.CurrentProfession ==
            newProfession;

        // =============================================================
        // 同職業，不允許刷新
        // =============================================================

        if (sameProfession &&
            allowSameProfessionRefresh == false)
        {
            return;
        }

        // =============================================================
        // 先檢查 Prefab
        // =============================================================

        /*
         * 在動任何現有狀態以前，
         * 先確認新職業有合法 Prefab。
         *
         * 避免：
         *
         * 舊 Runtime 已刪
         * ↓
         * 才發現 Tank Prefab 沒設定。
         */
        NetworkObject runtimePrefab =
            GetRuntimePrefab(
                newProfession
            );

        if (ValidateRuntimePrefab(
                runtimePrefab,
                newProfession
            ) == false)
        {
            return;
        }

        // =============================================================
        // 建立新的 Runtime
        // =============================================================

        NetworkObject newRuntime =
            SpawnRuntime(
                runtimePrefab,
                newProfession
            );

        if (newRuntime == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntimeManager)}] " +
                $"職業 Runtime Spawn 失敗。" +
                $"\n玩家：{Object.InputAuthority}" +
                $"\n職業：{newProfession}",
                this
            );

            return;
        }
        
        // =============================================================
        // Clear Old Weapon Action Blocks
        // =============================================================

        ClearWeaponActionBlocks();

        // =============================================================
        // 保存舊 Runtime
        // =============================================================

        NetworkObject oldRuntime =
            CurrentRuntimeObject;

        PlayerProfessionType oldRuntimeProfession =
            CurrentRuntimeProfession;

        // =============================================================
        // 正式修改職業
        // =============================================================

        /*
         * 如果只是 F1 → F1 測試刷新，
         * PlayerProfession 不需要重設，
         * 只需要重新建立 Runtime。
         */
        if (sameProfession == false)
        {
            profession.SetProfession(
                newProfession
            );
        }

        // =============================================================
        // 指向新 Runtime
        // =============================================================

        CurrentRuntimeObject =
            newRuntime;

        CurrentRuntimeProfession =
            newProfession;

        // =============================================================
        // 移除舊 Runtime
        // =============================================================

        if (oldRuntime != null &&
            oldRuntime.IsValid)
        {
            Runner.Despawn(
                oldRuntime
            );
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugRuntimeManager)
        {
            Debug.Log(
                $"[職業 Runtime 切換完成]" +
                $"\n玩家：{Object.InputAuthority}" +
                $"\n舊 Runtime 職業：{oldRuntimeProfession}" +
                $"\n新 Runtime 職業：{newProfession}" +
                $"\nCurrent Profession：{profession.CurrentProfession}" +
                $"\n新 Runtime：{newRuntime.name}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Runtime 建立

    /// <summary>
    /// 重新建立指定職業 Runtime。
    ///
    /// 主要提供：
    ///
    /// 初次 Spawn
    /// 外部 Profession Change
    /// Runtime Missing Recovery。
    /// </summary>
    private bool RebuildRuntime(
        PlayerProfessionType targetProfession
    )
    {
        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            return false;
        }

        NetworkObject runtimePrefab =
            GetRuntimePrefab(
                targetProfession
            );

        if (ValidateRuntimePrefab(
                runtimePrefab,
                targetProfession
            ) == false)
        {
            return false;
        }

        NetworkObject newRuntime =
            SpawnRuntime(
                runtimePrefab,
                targetProfession
            );

        if (newRuntime == null)
        {
            return false;
        }

        // =============================================================
        // Clear Old Weapon Action Blocks
        // =============================================================

        ClearWeaponActionBlocks();

        NetworkObject oldRuntime =
            CurrentRuntimeObject;

        CurrentRuntimeObject =
            newRuntime;

        CurrentRuntimeProfession =
            targetProfession;

        if (oldRuntime != null &&
            oldRuntime.IsValid)
        {
            Runner.Despawn(
                oldRuntime
            );
        }

        return true;
    }

    /// <summary>
    /// 使用 Fusion Runner.Spawn
    /// 建立 Profession Runtime。
    /// </summary>
    private NetworkObject SpawnRuntime(
        NetworkObject runtimePrefab,
        PlayerProfessionType runtimeProfession
    )
    {
        NetworkObject spawnedRuntime =
            Runner.Spawn(
                runtimePrefab,

                transform.position,

                Quaternion.identity,

                Object.InputAuthority,

                /*
                 * 在 Runtime 真正同步給其他 Peer 前，
                 * 先寫入 Owner 與 Profession。
                 */
                (runner, spawnedObject) =>
                {
                    PlayerProfessionRuntime runtime =
                        spawnedObject
                            .GetComponent<PlayerProfessionRuntime>();

                    if (runtime == null)
                    {
                        return;
                    }

                    runtime.InitializeBeforeSpawn(
                        Object,
                        runtimeProfession
                    );
                }
            );

        return spawnedRuntime;
    }

    #endregion

    // =====================================================================
    #region Prefab 查詢

    /// <summary>
    /// 根據職業取得 Runtime Prefab。
    /// </summary>
    private NetworkObject GetRuntimePrefab(
        PlayerProfessionType professionType
    )
    {
        switch (professionType)
        {
            case PlayerProfessionType.Attack:
            {
                return attackRuntimePrefab;
            }

            case PlayerProfessionType.Tank:
            {
                return tankRuntimePrefab;
            }

            case PlayerProfessionType.Support:
            {
                return supportRuntimePrefab;
            }

            case PlayerProfessionType.None:
            default:
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Spawn 前先檢查 Runtime Prefab。
    /// </summary>
    private bool ValidateRuntimePrefab(
        NetworkObject runtimePrefab,
        PlayerProfessionType professionType
    )
    {
        if (runtimePrefab == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntimeManager)}] " +
                $"{professionType} Runtime Prefab 尚未設定。",
                this
            );

            return false;
        }

        PlayerProfessionRuntime runtime =
            runtimePrefab
                .GetComponent<PlayerProfessionRuntime>();

        if (runtime == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerProfessionRuntimeManager)}] " +
                $"{professionType} Runtime Prefab 上找不到 " +
                $"{nameof(PlayerProfessionRuntime)}。" +
                $"\nPrefab：{runtimePrefab.name}",
                runtimePrefab
            );

            return false;
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region Validation

    /// <summary>
    /// 判斷是不是正式可使用職業。
    /// </summary>
    private bool IsValidProfession(
        PlayerProfessionType professionType
    )
    {
        return
            professionType ==
                PlayerProfessionType.Attack ||
            professionType ==
                PlayerProfessionType.Tank ||
            professionType ==
                PlayerProfessionType.Support;
    }

    #endregion
    
    // =====================================================================
    #region Profession Movement Modifier

    /// <summary>
    /// 取得目前 Profession Runtime
    /// 希望套用到 PlayerMovement 的最終移動輸入倍率。
    ///
    /// ------------------------------------------------------------
    ///
    /// Player Core 完全不知道：
    ///
    /// Tank Guard
    /// Support Slow
    /// 其他職業能力。
    ///
    /// ------------------------------------------------------------
    ///
    /// 它只知道：
    ///
    /// Current Runtime
    /// ↓
    /// IPlayerMovementInputModifier。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果同一 Runtime 有多個 Modifier，
    /// 所有倍率會相乘。
    /// </summary>
    public float GetCurrentMovementInputMultiplier(
        NetInput input
    )
    {
        RefreshMovementModifierCache();

        float multiplier =
            1f;

        for (int i = 0;
            i < cachedMovementModifierBehaviours.Count;
            i++)
        {
            MonoBehaviour behaviour =
                cachedMovementModifierBehaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IPlayerMovementInputModifier modifier)
            {
                multiplier *=
                    Mathf.Clamp01(
                        modifier
                            .GetMovementInputMultiplier(
                                input
                            )
                    );
            }
        }

        return
            Mathf.Clamp01(
                multiplier
            );
    }

    /// <summary>
    /// Current Runtime 改變時，
    /// 才重新搜尋 Movement Modifier。
    ///
    /// ------------------------------------------------------------
    ///
    /// 所以正常 Gameplay 每 Tick
    /// 不會一直 GetComponentsInChildren。
    /// </summary>
    private void RefreshMovementModifierCache()
    {
        NetworkObject currentRuntime =
            CurrentRuntimeObject;

        if (cachedMovementModifierRuntime ==
            currentRuntime)
        {
            return;
        }

        cachedMovementModifierRuntime =
            currentRuntime;

        cachedMovementModifierBehaviours
            .Clear();

        if (currentRuntime == null ||
            currentRuntime.IsValid == false)
        {
            return;
        }

        MonoBehaviour[] behaviours =
            currentRuntime
                .GetComponentsInChildren<
                    MonoBehaviour
                >(
                    true
                );

        for (int i = 0;
            i < behaviours.Length;
            i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IPlayerMovementInputModifier)
            {
                cachedMovementModifierBehaviours
                    .Add(
                        behaviour
                    );
            }
        }
    }

    #endregion

    /// <summary>
    /// 驅動目前正式 Profession Runtime。
    ///
    /// ------------------------------------------------------------
    ///
    /// Player.cs 完全不需要知道：
    ///
    /// Attack → Aim / Focus / Rifle
    /// Tank   → Guard / Melee / Dash
    /// Support→ 其他玩法
    ///
    /// Player 只把這個 Tick 的 Input
    /// 交給 Runtime Manager。
    ///
    /// ------------------------------------------------------------
    ///
    /// 如果職業切換的某個短暫 Tick
    /// Runtime 尚未同步完成，
    /// 這一 Tick 直接不執行職業 Gameplay，
    /// 不會退回執行上一個職業。
    /// </summary>
    public void SimulateCurrentRuntime(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        PlayerProfessionRuntime runtime =
            CurrentRuntime;

        if (runtime == null)
        {
            return;
        }

        /*
        * Runtime 必須與正式職業一致。
        *
        * 避免：
        *
        * F2 剛切 Tank
        * 但舊 Attack Runtime 還存在一個 Tick
        *
        * 就又開了一槍。
        */
        if (runtime.RuntimeProfession !=
            profession.CurrentProfession)
        {
            return;
        }

        runtime.Simulate(
            input,
            previousButtons
        );
    }
}