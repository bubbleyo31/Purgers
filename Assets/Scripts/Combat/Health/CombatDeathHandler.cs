using Fusion;
using UnityEngine;

/// <summary>
/// 戰鬥單位死亡狀態處理器。
///
/// ------------------------------------------------------------
///
/// 這支腳本不負責：
///
/// HP
/// Damage
/// Heal
/// Revive
///
/// 那些仍然屬於：
///
/// TestDamageReceiver
/// 未來 EnemyHealth
///
/// ------------------------------------------------------------
///
/// CombatDeathHandler 只負責：
///
/// 「當一個戰鬥單位死亡後，
/// 哪些 Gameplay / Collision / Target 元件
/// 應該被關閉？」
///
/// ------------------------------------------------------------
///
/// 目前可以統一控制：
///
/// 1. Unity Collider。
/// 2. Photon Fusion HitboxRoot。
/// 3. FocusTarget。
/// 4. 未來 EnemyAI。
/// 5. 其他 Behaviour。
/// 6. 死亡時要隱藏的 GameObject。
/// 7. 死亡時才要顯示的 GameObject。
///
/// ------------------------------------------------------------
///
/// 復活時：
///
/// 不會粗暴地把所有東西設成 Enabled。
///
/// 而是恢復「死亡前記錄的原始狀態」。
///
/// 例如：
///
/// 某個 Collider 原本就是 Disabled
/// ↓
/// 死亡
/// ↓
/// 復活
///
/// 它仍然保持 Disabled。
///
/// 這可以避免 Revive
/// 意外把其他 Gameplay 系統關掉的東西重新打開。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class CombatDeathHandler :
    NetworkBehaviour
{
    // =====================================================================
    #region 生命狀態來源

    [Header("生命狀態來源")]

    [SerializeField]
    [Tooltip("提供存活與死亡狀態的元件。這個元件必須實作 ICombatLifeState。目前可以直接拖入 TestDamageReceiver。未來換成 EnemyHealth 後，也只需要讓 EnemyHealth 實作 ICombatLifeState 即可。若留空，系統會在同物件與父物件中自動尋找。")]
    private MonoBehaviour lifeStateSource;

    #endregion

    // =====================================================================
    #region Unity Collider

    [Header("死亡後停用的 Unity Collider")]

    [SerializeField]
    [Tooltip("死亡後需要停止碰撞與普通 Physics Raycast 的 Collider。請只放真正需要死亡後失效的碰撞體。例如敵人的 Body、Head 命中 Collider。若你未來希望屍體仍然具有物理碰撞，就不要把屍體碰撞 Collider 放進這個陣列。")]
    private Collider[] collidersToDisableOnDeath;

    #endregion

    // =====================================================================
    #region Photon Fusion Hitbox

    [Header("死亡後停用的 Fusion Hitbox")]

    [SerializeField]
    [Tooltip("死亡後要停止 Photon Fusion Lag Compensation 命中的 HitboxRoot。系統會透過 HitboxRootActive 控制，而不是 Destroy Hitbox。正式傷害判定使用 Fusion Hitbox 時，建議將敵人的 HitboxRoot 放進這裡。")]
    private HitboxRoot[] hitboxRootsToDisableOnDeath;

    #endregion

    // =====================================================================
    #region Behaviour

    [Header("死亡後停用的 Behaviour")]

    [SerializeField]
    [Tooltip("死亡後需要停用的 MonoBehaviour 或其他 Behaviour。建議目前放入 FocusTarget；未來可以加入 EnemyAI、攻擊控制器、移動控制器等。不要放 TestDamageReceiver、CombatDeathHandler、NetworkObject、NetworkTransform 或其他維持網路狀態所需的核心元件。")]
    // ★ 修復：明確指定為 UnityEngine.Behaviour，避免與 Fusion.Behaviour 衝突
    private UnityEngine.Behaviour[] behavioursToDisableOnDeath;

    #endregion

    // =====================================================================
    #region GameObject

    [Header("死亡後關閉的物件")]

    [SerializeField]
    [Tooltip("死亡後要 SetActive(false) 的物件。例如存活狀態才顯示的特效、武器、警示 UI 等。復活時會恢復死亡前原本的 Active 狀態。不要把整個 NetworkObject Root 放進來，否則 CombatDeathHandler 自己也會被關閉。")]
    private GameObject[] objectsToDisableOnDeath;

    [Header("死亡後開啟的物件")]

    [SerializeField]
    [Tooltip("只有死亡後才要 SetActive(true) 的物件。例如死亡特效 Root、屍體特殊模型、死亡標記等。復活時會恢復死亡前原本的 Active 狀態。")]
    private GameObject[] objectsToEnableOnDeath;

    #endregion

    // =====================================================================
    #region 復活設定

    [Header("復活設定")]

    [SerializeField]
    [Tooltip("開啟後，當 ICombatLifeState 再次變成 Alive 時，會自動將 Collider、HitboxRoot、Behaviour 與 GameObject 恢復到死亡前記錄的狀態。建議保持開啟，讓 ReceiveRevive 可以完整恢復戰鬥單位。")]
    private bool restoreStateOnRevive =
        true;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，CombatDeathHandler 套用死亡或復活狀態時會輸出詳細資訊。")]
    private bool debugDeathHandler =
        true;

    #endregion

    // =====================================================================
    #region Runtime - Life State

    /// <summary>
    /// 真正提供生命狀態的接口。
    ///
    /// CombatDeathHandler 不知道：
    ///
    /// TestDamageReceiver
    /// EnemyHealth
    /// BossHealth
    ///
    /// 的具體類型。
    ///
    /// 它只認：
    ///
    /// ICombatLifeState。
    /// </summary>
    private ICombatLifeState lifeState;

    /// <summary>
    /// 是否已經成功套用過至少一次生命狀態。
    ///
    /// 第一次 Tick 一定會強制更新。
    /// </summary>
    private bool hasAppliedLifeState;

    /// <summary>
    /// 上一次已經套用的死亡狀態。
    ///
    /// false = Alive
    /// true  = Dead
    /// </summary>
    private bool lastAppliedDeadState;

    #endregion

    // =====================================================================
    #region Runtime - 原始狀態

    /// <summary>
    /// Collider 死亡前的 Enabled 狀態。
    /// </summary>
    private bool[] originalColliderStates;

    /// <summary>
    /// Fusion HitboxRoot 死亡前的 Active 狀態。
    /// </summary>
    private bool[] originalHitboxRootStates;

    /// <summary>
    /// Behaviour 死亡前的 Enabled 狀態。
    /// </summary>
    private bool[] originalBehaviourStates;

    /// <summary>
    /// Objects To Disable On Death
    /// 死亡前的 ActiveSelf 狀態。
    /// </summary>
    private bool[] originalDisableObjectStates;

    /// <summary>
    /// Objects To Enable On Death
    /// 死亡前的 ActiveSelf 狀態。
    /// </summary>
    private bool[] originalEnableObjectStates;

    /// <summary>
    /// 是否已經記錄原始狀態。
    ///
    /// 一個 NetworkObject Spawn 生命週期
    /// 只需要記錄一次。
    /// </summary>
    private bool hasCapturedOriginalStates;

    #endregion

    // =====================================================================
    #region 公開狀態

    /// <summary>
    /// CombatDeathHandler 目前認為
    /// 這個單位是否處於死亡狀態。
    /// </summary>
    public bool IsDeathStateApplied =>
        hasAppliedLifeState &&
        lastAppliedDeadState;

    /// <summary>
    /// 是否已成功找到 ICombatLifeState。
    /// </summary>
    public bool HasLifeState =>
        lifeState != null;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        ResolveLifeState();
    }

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        /*
         * NetworkObject 已經正式 Attach 後，
         * 再記錄 Component 原始狀態。
         *
         * 特別是 Fusion HitboxRoot，
         * 在 NetworkObject Spawn 後才進入正式 Fusion 生命週期。
         */
        ResolveLifeState();

        CaptureOriginalStates();

        /*
         * 不在 Spawned() 立刻判斷 Alive / Dead。
         *
         * 原因：
         *
         * 同一個 NetworkObject 上可能有多個
         * NetworkBehaviour.Spawned()。
         *
         * TestDamageReceiver 也會在 Spawned()
         * 初始化 CurrentHealth。
         *
         * 讓第一次 FixedUpdateNetwork / Render
         * 再正式套用狀態會比較安全。
         */
        hasAppliedLifeState =
            false;
    }

    /// <summary>
    /// Fusion Simulation 階段。
    ///
    /// State Authority 的 CurrentHealth
    /// 一旦變化，就會從這裡觀察死亡狀態。
    ///
    /// Networked State 一般應在 Fusion Simulation
    /// 中進行 Gameplay 邏輯處理。
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        RefreshLifeState();
    }

    /// <summary>
    /// Render 階段再補一次狀態更新。
    ///
    /// 主要用途：
    ///
    /// Client Proxy 收到新的 Networked CurrentHealth 後，
    /// 也能更新本地 Collider、FocusTarget 等表示狀態。
    ///
    /// RefreshLifeState 本身有狀態比較，
    /// 沒有變化時不會重複執行 Apply。
    /// </summary>
    public override void Render()
    {
        RefreshLifeState();
    }

    #endregion

    // =====================================================================
    #region Life State 尋找

    /// <summary>
    /// 將 Inspector 指定的 MonoBehaviour
    /// 轉換成 ICombatLifeState。
    ///
    /// 如果沒有手動指定，
    /// 就自動往目前物件與父階層搜尋。
    /// </summary>
    private void ResolveLifeState()
    {
        lifeState =
            null;

        // =============================================================
        // Inspector 有指定
        // =============================================================

        if (lifeStateSource != null)
        {
            if (lifeStateSource is
                ICombatLifeState source)
            {
                lifeState =
                    source;

                return;
            }

            Debug.LogError(
                $"[{nameof(CombatDeathHandler)}] " +
                $"指定的 Life State Source " +
                $"「{lifeStateSource.GetType().Name}」" +
                $"沒有實作 ICombatLifeState。",
                lifeStateSource
            );

            return;
        }

        // =============================================================
        // 自動搜尋
        // =============================================================

        MonoBehaviour[] behaviours =
            GetComponentsInParent<MonoBehaviour>(
                true
            );

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
                continue;

            if (behaviour is
                ICombatLifeState foundLifeState)
            {
                lifeStateSource =
                    behaviour;

                lifeState =
                    foundLifeState;

                return;
            }
        }

        Debug.LogError(
            $"[{nameof(CombatDeathHandler)}] " +
            $"找不到任何實作 ICombatLifeState 的元件。" +
            $"\n目前 TestEnemy 請指定 TestDamageReceiver。",
            this
        );
    }

    #endregion

    // =====================================================================
    #region 原始狀態快照

    /// <summary>
    /// 記錄死亡前所有受控元件的原始狀態。
    ///
    /// Revive 時不是一律 Enabled = true，
    /// 而是恢復這份 Snapshot。
    /// </summary>
    private void CaptureOriginalStates()
    {
        if (hasCapturedOriginalStates)
            return;

        // =============================================================
        // Collider
        // =============================================================

        int colliderCount =
            collidersToDisableOnDeath != null
                ? collidersToDisableOnDeath.Length
                : 0;

        originalColliderStates =
            new bool[colliderCount];

        for (int i = 0;
             i < colliderCount;
             i++)
        {
            Collider targetCollider =
                collidersToDisableOnDeath[i];

            originalColliderStates[i] =
                targetCollider != null &&
                targetCollider.enabled;
        }

        // =============================================================
        // Hitbox Root
        // =============================================================

        int hitboxRootCount =
            hitboxRootsToDisableOnDeath != null
                ? hitboxRootsToDisableOnDeath.Length
                : 0;

        originalHitboxRootStates =
            new bool[hitboxRootCount];

        for (int i = 0;
             i < hitboxRootCount;
             i++)
        {
            HitboxRoot hitboxRoot =
                hitboxRootsToDisableOnDeath[i];

            originalHitboxRootStates[i] =
                hitboxRoot != null &&
                hitboxRoot.HitboxRootActive;
        }

        // =============================================================
        // Behaviour
        // =============================================================

        int behaviourCount =
            behavioursToDisableOnDeath != null
                ? behavioursToDisableOnDeath.Length
                : 0;

        originalBehaviourStates =
            new bool[behaviourCount];

        for (int i = 0;
             i < behaviourCount;
             i++)
        {
            // ★ 修復：明確指定為 UnityEngine.Behaviour
            UnityEngine.Behaviour behaviour =
                behavioursToDisableOnDeath[i];

            originalBehaviourStates[i] =
                behaviour != null &&
                behaviour.enabled;
        }

        // =============================================================
        // Disable Objects
        // =============================================================

        int disableObjectCount =
            objectsToDisableOnDeath != null
                ? objectsToDisableOnDeath.Length
                : 0;

        originalDisableObjectStates =
            new bool[disableObjectCount];

        for (int i = 0;
             i < disableObjectCount;
             i++)
        {
            GameObject targetObject =
                objectsToDisableOnDeath[i];

            originalDisableObjectStates[i] =
                targetObject != null &&
                targetObject.activeSelf;
        }

        // =============================================================
        // Enable Objects
        // =============================================================

        int enableObjectCount =
            objectsToEnableOnDeath != null
                ? objectsToEnableOnDeath.Length
                : 0;

        originalEnableObjectStates =
            new bool[enableObjectCount];

        for (int i = 0;
             i < enableObjectCount;
             i++)
        {
            GameObject targetObject =
                objectsToEnableOnDeath[i];

            originalEnableObjectStates[i] =
                targetObject != null &&
                targetObject.activeSelf;
        }

        hasCapturedOriginalStates =
            true;
    }

    #endregion

    // =====================================================================
    #region 狀態偵測

    /// <summary>
    /// 讀取 ICombatLifeState，
    /// 並只在 Alive / Dead 真正改變時套用。
    /// </summary>
    private void RefreshLifeState()
    {
        if (lifeState == null)
        {
            ResolveLifeState();

            if (lifeState == null)
                return;
        }

        if (hasCapturedOriginalStates ==
            false)
        {
            CaptureOriginalStates();
        }

        bool isDead =
            lifeState.IsDead;

        /*
         * 已經套用相同狀態，
         * 不重複執行。
         */
        if (hasAppliedLifeState &&
            lastAppliedDeadState ==
            isDead)
        {
            return;
        }

        // =============================================================
        // Dead
        // =============================================================

        if (isDead)
        {
            ApplyDeathState();
        }

        // =============================================================
        // Alive
        // =============================================================

        else
        {
            /*
             * 第一次 Spawn 時也需要套用 Alive State，
             * 確保 Prefab 初始狀態與 Snapshot 一致。
             *
             * 之後 Revive 則按照設定恢復。
             */
            if (hasAppliedLifeState ==
                    false ||
                restoreStateOnRevive)
            {
                ApplyAliveState();
            }
        }

        lastAppliedDeadState =
            isDead;

        hasAppliedLifeState =
            true;
    }

    #endregion

    // =====================================================================
    #region Death

    /// <summary>
    /// 套用死亡狀態。
    ///
    /// 注意：
    ///
    /// 不 Destroy 任何 Component。
    ///
    /// 這樣未來 Revive
    /// 才能完整恢復。
    /// </summary>
    private void ApplyDeathState()
    {
        // =============================================================
        // Unity Collider
        // =============================================================

        if (collidersToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 collidersToDisableOnDeath.Length;
                 i++)
            {
                Collider targetCollider =
                    collidersToDisableOnDeath[i];

                if (targetCollider == null)
                    continue;

                targetCollider.enabled =
                    false;
            }
        }

        // =============================================================
        // Fusion Hitbox
        // =============================================================

        /*
         * HitboxRootActive 屬於 Fusion
         * Lag Compensation 命中狀態。
         *
         * 正式 Gameplay Hitbox
         * 只由 State Authority 修改。
         */
        if (Object != null &&
            Object.HasStateAuthority &&
            hitboxRootsToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 hitboxRootsToDisableOnDeath.Length;
                 i++)
            {
                HitboxRoot hitboxRoot =
                    hitboxRootsToDisableOnDeath[i];

                if (hitboxRoot == null)
                    continue;

                hitboxRoot.HitboxRootActive =
                    false;
            }
        }

        // =============================================================
        // Behaviour
        // =============================================================

        if (behavioursToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 behavioursToDisableOnDeath.Length;
                 i++)
            {
                // ★ 修復：明確指定為 UnityEngine.Behaviour
                UnityEngine.Behaviour behaviour =
                    behavioursToDisableOnDeath[i];

                if (behaviour == null)
                    continue;

                /*
                 * 絕對不能把自己關掉。
                 *
                 * 否則 Revive 時沒有人
                 * 可以重新開啟其他元件。
                 */
                if (behaviour ==
                    this)
                {
                    continue;
                }

                /*
                 * 生命狀態來源也不能關。
                 *
                 * 否則 CurrentHealth
                 * 可能無法繼續正常同步。
                 */
                if (behaviour ==
                    lifeStateSource)
                {
                    continue;
                }

                behaviour.enabled =
                    false;
            }
        }

        // =============================================================
        // Objects To Disable
        // =============================================================

        if (objectsToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 objectsToDisableOnDeath.Length;
                 i++)
            {
                GameObject targetObject =
                    objectsToDisableOnDeath[i];

                if (targetObject == null)
                    continue;

                /*
                 * 防止把 NetworkObject Root
                 * 或 DeathHandler 本身關掉。
                 */
                if (targetObject ==
                    gameObject)
                {
                    Debug.LogError(
                        $"[{nameof(CombatDeathHandler)}] " +
                        $"Objects To Disable On Death " +
                        $"不可包含 CombatDeathHandler 所在的 " +
                        $"NetworkObject Root。",
                        this
                    );

                    continue;
                }

                targetObject.SetActive(
                    false
                );
            }
        }

        // =============================================================
        // Objects To Enable
        // =============================================================

        if (objectsToEnableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 objectsToEnableOnDeath.Length;
                 i++)
            {
                GameObject targetObject =
                    objectsToEnableOnDeath[i];

                if (targetObject == null)
                    continue;

                targetObject.SetActive(
                    true
                );
            }
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugDeathHandler)
        {
            Debug.Log(
                $"[{nameof(CombatDeathHandler)}] " +
                $"死亡狀態已套用。" +
                $"\n物件：{name}" +
                $"\nCollider 已處理：" +
                $"{(collidersToDisableOnDeath != null ? collidersToDisableOnDeath.Length : 0)}" +
                $"\nHitboxRoot 已處理：" +
                $"{(hitboxRootsToDisableOnDeath != null ? hitboxRootsToDisableOnDeath.Length : 0)}" +
                $"\nBehaviour 已處理：" +
                $"{(behavioursToDisableOnDeath != null ? behavioursToDisableOnDeath.Length : 0)}",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Alive / Revive

    /// <summary>
    /// 將 Death Handler 控制的內容
    /// 恢復到死亡前記錄的狀態。
    ///
    /// 不是簡單全部 Enabled = true。
    /// </summary>
    private void ApplyAliveState()
    {
        if (hasCapturedOriginalStates ==
            false)
        {
            return;
        }

        // =============================================================
        // Collider
        // =============================================================

        if (collidersToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 collidersToDisableOnDeath.Length;
                 i++)
            {
                Collider targetCollider =
                    collidersToDisableOnDeath[i];

                if (targetCollider == null)
                    continue;

                if (i >=
                    originalColliderStates.Length)
                {
                    continue;
                }

                targetCollider.enabled =
                    originalColliderStates[i];
            }
        }

        // =============================================================
        // Fusion Hitbox
        // =============================================================

        if (Object != null &&
            Object.HasStateAuthority &&
            hitboxRootsToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 hitboxRootsToDisableOnDeath.Length;
                 i++)
            {
                HitboxRoot hitboxRoot =
                    hitboxRootsToDisableOnDeath[i];

                if (hitboxRoot == null)
                    continue;

                if (i >=
                    originalHitboxRootStates.Length)
                {
                    continue;
                }

                hitboxRoot.HitboxRootActive =
                    originalHitboxRootStates[i];
            }
        }

        // =============================================================
        // Behaviour
        // =============================================================

        if (behavioursToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 behavioursToDisableOnDeath.Length;
                 i++)
            {
                // ★ 修復：明確指定為 UnityEngine.Behaviour
                UnityEngine.Behaviour behaviour =
                    behavioursToDisableOnDeath[i];

                if (behaviour == null)
                    continue;

                if (behaviour ==
                        this ||
                    behaviour ==
                        lifeStateSource)
                {
                    continue;
                }

                if (i >=
                    originalBehaviourStates.Length)
                {
                    continue;
                }

                behaviour.enabled =
                    originalBehaviourStates[i];
            }
        }

        // =============================================================
        // Disable Objects Restore
        // =============================================================

        if (objectsToDisableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 objectsToDisableOnDeath.Length;
                 i++)
            {
                GameObject targetObject =
                    objectsToDisableOnDeath[i];

                if (targetObject == null)
                    continue;

                if (i >=
                    originalDisableObjectStates.Length)
                {
                    continue;
                }

                targetObject.SetActive(
                    originalDisableObjectStates[i]
                );
            }
        }

        // =============================================================
        // Enable Objects Restore
        // =============================================================

        if (objectsToEnableOnDeath !=
            null)
        {
            for (int i = 0;
                 i <
                 objectsToEnableOnDeath.Length;
                 i++)
            {
                GameObject targetObject =
                    objectsToEnableOnDeath[i];

                if (targetObject == null)
                    continue;

                if (i >=
                    originalEnableObjectStates.Length)
                {
                    continue;
                }

                targetObject.SetActive(
                    originalEnableObjectStates[i]
                );
            }
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugDeathHandler &&
            hasAppliedLifeState)
        {
            Debug.Log(
                $"[{nameof(CombatDeathHandler)}] " +
                $"存活狀態已恢復。" +
                $"\n物件：{name}",
                this
            );
        }
    }

    #endregion
}