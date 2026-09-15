using Fusion;
using UnityEngine;


/// <summary>
/// 敵人死亡後的生命週期階段。
/// </summary>
public enum EnemyDeathPhase : byte
{
    Alive = 0,
    Dying = 1,
    Corpse = 2
}


/// <summary>
/// 未來特殊死亡 Gameplay 可以實作的擴充接口。
///
/// 例如：
///
/// - 死亡爆炸。
/// - 分裂小怪。
/// - 生成毒霧。
/// - 掉落獎勵。
/// - Boss 轉換第二階段。
///
/// 這些 Callback 只會在 Enemy 的 State Authority 執行。
/// 純動畫、聲音與 VFX 不應放在這個接口；它們應觀察 Networked Death Phase。
/// </summary>
public interface IEnemyDeathGameplayExtension
{
    void OnEnemyDeathStarted(
        EnemyActor enemy,
        CombatDeathEventData deathData
    );

    void OnEnemyCorpsePhaseStarted(
        EnemyActor enemy
    );

    void OnBeforeEnemyDespawn(
        EnemyActor enemy
    );
}


/// <summary>
/// 敵人的死亡動畫時間、屍體保留時間與最後 Despawn。
///
/// ====================================================================
///
/// Alive
/// ↓
/// Health 變成 Dead
/// ↓
/// Dying
/// ↓ Death Animation Duration
/// Corpse
/// ↓ Corpse Lifetime
/// Runner.Despawn
///
/// ====================================================================
///
/// CombatDeathHandler 繼續負責：
///
/// - 關閉 Collider。
/// - 關閉 HitboxRoot。
/// - 關閉 FocusTarget。
/// - 關閉其他指定 Behaviour。
///
/// 本腳本只負責死亡時間軸與 NetworkObject Despawn，兩者不可混為一談。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyActor))]
[RequireComponent(typeof(TestDamageReceiver))]
[RequireComponent(typeof(CombatDeathHandler))]
public sealed class EnemyDeathLifecycleController :
    NetworkBehaviour
{
    // =====================================================================
    #region References

    [Header("核心引用")]

    [SerializeField]
    [Tooltip(
        "這個 NetworkObject 的 EnemyActor。\n" +
        "若留空會自動取得同物件元件。")]
    private EnemyActor enemy;

    [SerializeField]
    [Tooltip(
        "這個敵人的既有生命系統。\n" +
        "若留空會自動取得同物件的 TestDamageReceiver。")]
    private TestDamageReceiver health;

    #endregion

    // =====================================================================
    #region Timing

    [Header("死亡時間軸")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "敵人從死亡開始到死亡動畫主要段落完成所需秒數。\n\n" +
        "這段時間 Current Death Phase = Dying。\n" +
        "之後切換成 Corpse。\n\n" +
        "此數值應配合死亡動畫長度，但真正時間軸由 Fusion TickTimer 控制，" +
        "不是由 Animation Event 決定。")]
    private float deathAnimationDuration =
        1.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "進入 Corpse 階段後，屍體在場景中保留幾秒才 Despawn。\n\n" +
        "設為 0 代表進入 Corpse 後至少等待下一個 Fusion Tick，再安全 Despawn。\n" +
        "不要在 Health.ReceiveDamage 的呼叫堆疊中立刻 Despawn 自己。")]
    private float corpseLifetime =
        3f;

    [SerializeField]
    [Tooltip(
        "開啟後，Corpse Lifetime 到期由 State Authority 執行 Runner.Despawn。\n\n" +
        "關閉後敵人會永久停留在 Corpse 階段，適合測試或未來由 Encounter Manager 統一清理。")]
    private bool despawnAfterCorpseLifetime =
        true;

    #endregion

    // =====================================================================
    #region Extensions

    [Header("特殊死亡 Gameplay 擴充")]

    [SerializeField]
    [Tooltip(
        "可選的死亡 Gameplay 擴充元件。\n\n" +
        "陣列中的 MonoBehaviour 必須實作 IEnemyDeathGameplayExtension。\n" +
        "Callback 只在 State Authority 執行。\n\n" +
        "純動畫、聲音或 VFX 不要放這裡，應另外觀察 Current Death Phase。")]
    private MonoBehaviour[] deathGameplayExtensions;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出 Dying、Corpse、Revive 與 Despawn 流程。\n" +
        "大量敵人測試時建議關閉。")]
    private bool debugDeathLifecycle;

    #endregion

    // =====================================================================
    #region Networked State

    [Networked]
    public EnemyDeathPhase CurrentDeathPhase
    {
        get;
        private set;
    }

    [Networked]
    public TickTimer DeathPhaseTimer
    {
        get;
        private set;
    }

    [Networked]
    public int DeathSequence
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region Runtime

    private bool fusionSpawned;
    private bool healthEventsSubscribed;

    #endregion

    // =====================================================================
    #region Public State

    public bool IsDying =>
        fusionSpawned &&
        CurrentDeathPhase ==
            EnemyDeathPhase.Dying;

    public bool IsCorpse =>
        fusionSpawned &&
        CurrentDeathPhase ==
            EnemyDeathPhase.Corpse;

    public float RemainingPhaseSeconds
    {
        get
        {
            if (fusionSpawned == false ||
                Runner == null)
            {
                return 0f;
            }

            float? remaining =
                DeathPhaseTimer.RemainingTime(
                    Runner
                );

            return remaining.HasValue
                ? Mathf.Max(0f, remaining.Value)
                : 0f;
        }
    }

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        ResolveReferences();
        ValidateExtensions();
    }

    private void OnValidate()
    {
        deathAnimationDuration =
            Mathf.Max(
                0f,
                deathAnimationDuration
            );

        corpseLifetime =
            Mathf.Max(
                0f,
                corpseLifetime
            );

        ResolveReferences();
        ValidateExtensions();
    }

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    public override void Spawned()
    {
        fusionSpawned =
            true;

        ResolveReferences();
        ValidateExtensions();

        if (Object.HasStateAuthority)
        {
            CurrentDeathPhase =
                EnemyDeathPhase.Alive;

            DeathPhaseTimer =
                TickTimer.None;

            DeathSequence =
                0;

            SubscribeHealthEvents();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object.HasStateAuthority == false ||
            health == null)
        {
            return;
        }

        /*
         * Died Event 是主要入口；這裡再用正式 Health State 做保險。
         * 即使 Authority 切換或某次 C# Event 沒有被訂閱，
         * 也不會讓死亡敵人永遠停留在 Alive Phase。
         */
        if (health.IsDead &&
            CurrentDeathPhase ==
                EnemyDeathPhase.Alive)
        {
            BeginDeath(
                default
            );
        }
        else if (health.IsAlive &&
                 CurrentDeathPhase !=
                    EnemyDeathPhase.Alive)
        {
            ResetToAlive();
            return;
        }

        switch (CurrentDeathPhase)
        {
            case EnemyDeathPhase.Dying:
                if (DeathPhaseTimer.Expired(
                        Runner
                    ))
                {
                    EnterCorpsePhase();
                }
                break;

            case EnemyDeathPhase.Corpse:
                if (despawnAfterCorpseLifetime &&
                    DeathPhaseTimer.Expired(
                        Runner
                    ))
                {
                    DespawnEnemy();
                }
                break;
        }
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        UnsubscribeHealthEvents();

        fusionSpawned =
            false;
    }

    #endregion

    // =====================================================================
    #region Health Events

    private void SubscribeHealthEvents()
    {
        if (healthEventsSubscribed ||
            health == null)
        {
            return;
        }

        health.Died +=
            HandleHealthDied;

        health.Revived +=
            HandleHealthRevived;

        healthEventsSubscribed =
            true;
    }

    private void UnsubscribeHealthEvents()
    {
        if (healthEventsSubscribed == false ||
            health == null)
        {
            return;
        }

        health.Died -=
            HandleHealthDied;

        health.Revived -=
            HandleHealthRevived;

        healthEventsSubscribed =
            false;
    }

    private void HandleHealthDied(
        CombatDeathEventData deathData
    )
    {
        BeginDeath(
            deathData
        );
    }

    private void HandleHealthRevived(
        CombatReviveEventData reviveData
    )
    {
        ResetToAlive();
    }

    #endregion

    // =====================================================================
    #region Death Timeline

    private void BeginDeath(
        CombatDeathEventData deathData
    )
    {
        if (CanWriteState() == false ||
            CurrentDeathPhase !=
                EnemyDeathPhase.Alive)
        {
            return;
        }

        DeathSequence++;

        CurrentDeathPhase =
            EnemyDeathPhase.Dying;

        if (deathAnimationDuration > 0f)
        {
            DeathPhaseTimer =
                TickTimer.CreateFromSeconds(
                    Runner,
                    deathAnimationDuration
                );
        }
        else
        {
            /*
             * 不在 Health Died Callback 內直接切到 Despawn。
             * 至少等待下一個 Fusion Tick，讓其他生命事件訂閱者完成。
             */
            DeathPhaseTimer =
                TickTimer.CreateFromTicks(
                    Runner,
                    1
                );
        }

        InvokeDeathStartedExtensions(
            deathData
        );

        if (debugDeathLifecycle)
        {
            Debug.Log(
                $"[{nameof(EnemyDeathLifecycleController)}] Dying。" +
                $"\nEnemy：{name}" +
                $"\nSequence：{DeathSequence}" +
                $"\nDuration：{deathAnimationDuration:F2}",
                this
            );
        }
    }

    private void EnterCorpsePhase()
    {
        if (CanWriteState() == false ||
            CurrentDeathPhase !=
                EnemyDeathPhase.Dying)
        {
            return;
        }

        CurrentDeathPhase =
            EnemyDeathPhase.Corpse;

        if (despawnAfterCorpseLifetime)
        {
            DeathPhaseTimer =
                corpseLifetime > 0f
                    ? TickTimer.CreateFromSeconds(
                        Runner,
                        corpseLifetime
                    )
                    : TickTimer.CreateFromTicks(
                        Runner,
                        1
                    );
        }
        else
        {
            DeathPhaseTimer =
                TickTimer.None;
        }

        InvokeCorpseExtensions();

        if (debugDeathLifecycle)
        {
            Debug.Log(
                $"[{nameof(EnemyDeathLifecycleController)}] Corpse。" +
                $"\nEnemy：{name}" +
                $"\nDespawn Enabled：{despawnAfterCorpseLifetime}" +
                $"\nCorpse Lifetime：{corpseLifetime:F2}",
                this
            );
        }
    }

    private void ResetToAlive()
    {
        if (CanWriteState() == false)
        {
            return;
        }

        CurrentDeathPhase =
            EnemyDeathPhase.Alive;

        DeathPhaseTimer =
            TickTimer.None;

        if (debugDeathLifecycle)
        {
            Debug.Log(
                $"[{nameof(EnemyDeathLifecycleController)}] " +
                $"敵人已恢復 Alive。\nEnemy：{name}",
                this
            );
        }
    }

    private void DespawnEnemy()
    {
        if (CanWriteState() == false)
        {
            return;
        }

        InvokeBeforeDespawnExtensions();

        NetworkObject enemyObject =
            Object;

        if (enemyObject == null ||
            enemyObject.IsValid == false)
        {
            return;
        }

        if (debugDeathLifecycle)
        {
            Debug.Log(
                $"[{nameof(EnemyDeathLifecycleController)}] " +
                $"準備 Despawn。\nEnemy：{name}",
                this
            );
        }

        Runner.Despawn(
            enemyObject
        );
    }

    #endregion

    // =====================================================================
    #region Extensions

    private void InvokeDeathStartedExtensions(
        CombatDeathEventData deathData
    )
    {
        ForEachExtension(
            extension =>
                extension.OnEnemyDeathStarted(
                    enemy,
                    deathData
                )
        );
    }

    private void InvokeCorpseExtensions()
    {
        ForEachExtension(
            extension =>
                extension.OnEnemyCorpsePhaseStarted(
                    enemy
                )
        );
    }

    private void InvokeBeforeDespawnExtensions()
    {
        ForEachExtension(
            extension =>
                extension.OnBeforeEnemyDespawn(
                    enemy
                )
        );
    }

    private void ForEachExtension(
        System.Action<IEnemyDeathGameplayExtension>
            callback
    )
    {
        if (deathGameplayExtensions == null ||
            callback == null)
        {
            return;
        }

        for (int index = 0;
             index < deathGameplayExtensions.Length;
             index++)
        {
            MonoBehaviour behaviour =
                deathGameplayExtensions[index];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IEnemyDeathGameplayExtension extension)
            {
                callback.Invoke(
                    extension
                );
            }
        }
    }

    #endregion

    // =====================================================================
    #region Setup And Validation

    private void ResolveReferences()
    {
        if (enemy == null)
        {
            enemy =
                GetComponent<EnemyActor>();
        }

        if (health == null)
        {
            health =
                GetComponent<TestDamageReceiver>();
        }
    }

    private void ValidateExtensions()
    {
        if (deathGameplayExtensions == null)
        {
            return;
        }

        for (int index = 0;
             index < deathGameplayExtensions.Length;
             index++)
        {
            MonoBehaviour behaviour =
                deathGameplayExtensions[index];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IEnemyDeathGameplayExtension)
            {
                continue;
            }

            Debug.LogError(
                $"[{nameof(EnemyDeathLifecycleController)}] " +
                "Death Gameplay Extensions 內含不合法元件。" +
                $"\nIndex：{index}" +
                $"\nComponent：{behaviour.GetType().Name}" +
                $"\n必須實作：{nameof(IEnemyDeathGameplayExtension)}",
                behaviour
            );
        }
    }

    private bool CanWriteState()
    {
        return
            fusionSpawned &&
            Object != null &&
            Object.HasStateAuthority;
    }

    #endregion
}
