using Fusion;
using UnityEngine;

/// <summary>
/// 警戒流程的唯一狀態寫入者。只有首名取得群組演出權的敵人停下呼喚；
/// 同伴收到一次位置情報直接進入 Chase。共享情報不再轉播，防止遞迴連鎖。
/// 受傷只排入待處理情報，不中斷 Attack／Defense，不在傷害回呼中遞迴執行 AI。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor), typeof(EnemyPerceptionController))]
public sealed class EnemyAwarenessBrain : NetworkBehaviour
{
    public enum EnemyAwarenessPhase : byte
    {
        Unaware = 0, Announcing = 1, Alerted = 2, Investigating = 3
    }

    [Header("核心引用")]
    [SerializeField, Tooltip("同 Root 的 EnemyActor。留空自動取得。")]
    private EnemyActor enemyActor;
    [SerializeField, Tooltip("同 Root 的 EnemyPerceptionController。留空自動取得。")]
    private EnemyPerceptionController perception;
    [SerializeField, Tooltip("同 Root 的待機巡邏決策器。留空自動取得。")]
    private EnemyIdlePatrolBrain idlePatrolBrain;
    [SerializeField, Tooltip("場景的 EnemyAlertDirector。Prefab 資產無法引用場景物件，請留空；Spawned 會尋找同 Unity Scene 的唯一 Director。場景實例可手動指定。")]
    private EnemyAlertDirector alertDirector;

    [Header("發現與同伴警戒")]
    [SerializeField, Min(0f), Tooltip("唯一呼喚者的發現演出秒數。期間封鎖移動、導航、攻擊、防禦；0 表示直接進入警戒。")]
    private float announcementDuration = 0.8f;
    [SerializeField, Tooltip("同一交戰區域使用相同 ID，例如 room_a。不同房間應使用不同 ID，避免跨牆呼喚；大小寫不同視為不同群組。")]
    private string alertGroupId = "default";
    [SerializeField, Min(0f), Tooltip("分享目標的三維直線半徑，公尺。只傳給同 Runner、同群組、存活且非 Dormant 的敵人；不檢查同伴之間的牆壁。")]
    private float alertRadius = 18f;
    [SerializeField, Min(0.01f), Tooltip("同群組兩次呼喚演出至少相隔幾秒。冷卻只限制演出，情報分享仍可發生。請不小於群組內最長發現動畫。")]
    private float groupAnnouncementCooldown = 4f;
    [SerializeField, Min(0.02f), Tooltip("同一敵人受傷後再次通知同伴的最短間隔，避免連發武器每顆子彈都廣播。共享接收者不會再次廣播。")]
    private float damageAlertInterval = 1f;
    [SerializeField, Min(0.02f), Tooltip("目標記憶消失後，在原地保持 Investigate 警戒多久，接著回到 Idle。本階段不包含前往最後位置的搜索導航。")]
    private float investigationSeconds = 4f;
    [SerializeField, Tooltip("輸出呼喚與共享警戒訊息。")]
    private bool debugAwareness = false;

    [Networked] public EnemyAwarenessPhase CurrentAwarenessPhase { get; private set; }
    [Networked] public TickTimer AnnouncementTimer { get; private set; }
    [Networked] public int AnnouncementSequence { get; private set; }
    [Networked] public int SharedAlertSequence { get; private set; }
    [Networked] private TickTimer InvestigationTimer { get; set; }
    [Networked] private TickTimer DamageAlertTimer { get; set; }

    // C# 傷害事件只在主機的前進模擬發生，本機暫存到下一次 AI Tick 再處理。
    private PlayerRef pendingAttacker;
    private bool damagePending;
    private bool spawned;
    private bool previouslyDead;
    private TestDamageReceiver subscribedHealth;

    public bool IsFusionSpawned => spawned && Object != null && Object.IsValid;
    public string AlertGroupId => string.IsNullOrWhiteSpace(alertGroupId) ? "default" : alertGroupId.Trim();
    public bool IsEligibleForSharedAlert => IsFusionSpawned && HasStateAuthority &&
        isActiveAndEnabled && enemyActor != null && enemyActor.IsAlive &&
        enemyActor.StateController.IsFusionSpawned &&
        enemyActor.StateController.CurrentBrainState != EnemyBrainState.Dormant;

    public override void Spawned()
    {
        Resolve();
        spawned = true;
        if (alertDirector == null)
        {
            foreach (EnemyAlertDirector candidate in FindObjectsOfType<EnemyAlertDirector>())
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                if (alertDirector != null)
                {
                    Debug.LogError("[Enemy Awareness] 同場景有多個 Director，請在場景實例明確指定。", this);
                    alertDirector = null;
                    break;
                }
                alertDirector = candidate;
            }
        }
        alertDirector?.Register(this);
        if (!HasStateAuthority) return;
        CurrentAwarenessPhase = EnemyAwarenessPhase.Unaware;
        AnnouncementTimer = TickTimer.None;
        InvestigationTimer = TickTimer.None;
        DamageAlertTimer = TickTimer.None;
        AnnouncementSequence = 0;
        SharedAlertSequence = 0;
        damagePending = false;
        previouslyDead = false;
        subscribedHealth = GetComponent<TestDamageReceiver>();
        if (subscribedHealth != null) subscribedHealth.HealthChanged += OnHealthChanged;
        if (alertDirector == null)
            Debug.LogError("[Enemy Awareness] 缺少 Director。仍可自己發現玩家，但暫停群組呼喚演出與分享。", this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (subscribedHealth != null) subscribedHealth.HealthChanged -= OnHealthChanged;
        subscribedHealth = null;
        alertDirector?.Unregister(this);
        spawned = false;
        damagePending = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsFusionSpawned || !HasStateAuthority || enemyActor == null ||
            perception == null || !perception.IsFusionSpawned) return;

        if (!enemyActor.IsAlive)
        {
            if (!previouslyDead)
            {
                ClearBrainLock();
                CurrentAwarenessPhase = EnemyAwarenessPhase.Unaware;
                AnnouncementTimer = TickTimer.None;
                InvestigationTimer = TickTimer.None;
                damagePending = false;
            }
            previouslyDead = true;
            return;
        }
        if (previouslyDead)
        {
            previouslyDead = false;
            idlePatrolBrain?.RestartIdleSchedule();
        }
        if (enemyActor.StateController.CurrentBrainState == EnemyBrainState.Dormant) return;

        if (damagePending)
        {
            damagePending = false;
            if (DamageAlertTimer.ExpiredOrNotRunning(Runner) &&
                Runner.TryGetPlayerObject(pendingAttacker, out NetworkObject attacker) &&
                perception.IsValidAlivePlayer(attacker))
            {
                Vector3 position = perception.GetPlayerObservationPosition(attacker);
                perception.TryAcceptSharedTarget(attacker, position);
                DamageAlertTimer = TickTimer.CreateFromSeconds(Runner, damageAlertInterval);
                // 已在作戰的受傷者仍通知附近同伴，但不因受傷重播發現、取消現有動作。
                bool canAnnounce = CurrentAwarenessPhase == EnemyAwarenessPhase.Unaware &&
                    enemyActor.StateController.CanRunBrain && !enemyActor.StateController.IsUsingAction;
                bool won = Broadcast(attacker, position);
                if (CurrentAwarenessPhase == EnemyAwarenessPhase.Unaware ||
                    CurrentAwarenessPhase == EnemyAwarenessPhase.Investigating)
                    BeginContact(canAnnounce && won);
            }
        }

        if (!enemyActor.StateController.CanRunBrain) return;
        if (perception.HasTarget &&
            (CurrentAwarenessPhase == EnemyAwarenessPhase.Unaware ||
             CurrentAwarenessPhase == EnemyAwarenessPhase.Investigating))
        {
            bool won = Broadcast(perception.CurrentTarget, perception.LastKnownTargetPosition);
            BeginContact(won && !enemyActor.StateController.IsUsingAction);
        }

        if ((CurrentAwarenessPhase == EnemyAwarenessPhase.Alerted ||
             CurrentAwarenessPhase == EnemyAwarenessPhase.Announcing) && !perception.HasTarget)
            BeginInvestigation();

        if (CurrentAwarenessPhase == EnemyAwarenessPhase.Announcing &&
            AnnouncementTimer.ExpiredOrNotRunning(Runner))
        {
            ClearBrainLock();
            CurrentAwarenessPhase = EnemyAwarenessPhase.Alerted;
            enemyActor.StateController.TrySetBrainState(EnemyBrainState.Chase);
        }

        if (CurrentAwarenessPhase == EnemyAwarenessPhase.Investigating &&
            InvestigationTimer.ExpiredOrNotRunning(Runner) &&
            !enemyActor.StateController.IsUsingAction)
        {
            CurrentAwarenessPhase = EnemyAwarenessPhase.Unaware;
            perception.ForgetTarget(true);
            enemyActor.StateController.TrySetBrainState(EnemyBrainState.Idle);
            idlePatrolBrain?.RestartIdleSchedule();
        }
    }

    private void OnHealthChanged(HealthChangeEventData data)
    {
        // 補血／復活不能引起仇恨。致死的一擊不讓屍體再呼喚；
        // 有效非致死傷害會於下一個 AI Tick 通知同伴。
        if (!IsFusionSpawned || !HasStateAuthority || data.Delta >= 0f ||
            !data.IsAlive || data.Instigator.IsNone) return;
        pendingAttacker = data.Instigator;
        damagePending = true;
    }

    public void ReceiveSharedAlert(NetworkObject target, Vector3 position, EnemyAwarenessBrain source)
    {
        if (!IsEligibleForSharedAlert || perception == null || !perception.IsFusionSpawned) return;
        if (!perception.TryAcceptSharedTarget(target, position)) return;
        SharedAlertSequence++;
        // 已有演出或能力時只更新情報，不改其控制狀態。
        if (CurrentAwarenessPhase == EnemyAwarenessPhase.Unaware ||
            CurrentAwarenessPhase == EnemyAwarenessPhase.Investigating)
            BeginContact(false);
        if (debugAwareness) Debug.Log($"[Enemy Awareness] 收到 {source.name} 的情報，不轉播。", this);
    }

    private bool Broadcast(NetworkObject target, Vector3 position) =>
        alertDirector != null && alertDirector.TryBroadcastAlert(this, target, position,
            AlertGroupId, alertRadius, Mathf.Max(groupAnnouncementCooldown, announcementDuration));

    private void BeginContact(bool announce)
    {
        idlePatrolBrain?.CancelPatrolForCombat();
        InvestigationTimer = TickTimer.None;
        if (announce && announcementDuration > 0f)
        {
            CurrentAwarenessPhase = EnemyAwarenessPhase.Announcing;
            AnnouncementSequence++;
            AnnouncementTimer = TickTimer.CreateFromSeconds(Runner, announcementDuration);
            enemyActor.StateController.TrySetBrainState(EnemyBrainState.Alert);
            enemyActor.ActionGate.SetLocks(EnemyActionLockSource.Brain,
                EnemyActionLockFlags.Movement | EnemyActionLockFlags.Navigation |
                EnemyActionLockFlags.Attack | EnemyActionLockFlags.Defense);
        }
        else
        {
            CurrentAwarenessPhase = EnemyAwarenessPhase.Alerted;
            AnnouncementTimer = TickTimer.None;
            if (!enemyActor.StateController.IsUsingAction)
                enemyActor.StateController.TrySetBrainState(EnemyBrainState.Chase);
        }
        if (debugAwareness) Debug.Log($"[Enemy Awareness] {CurrentAwarenessPhase}，群組 {AlertGroupId}", this);
    }

    private void BeginInvestigation()
    {
        ClearBrainLock();
        AnnouncementTimer = TickTimer.None;
        CurrentAwarenessPhase = EnemyAwarenessPhase.Investigating;
        InvestigationTimer = TickTimer.CreateFromSeconds(Runner, investigationSeconds);
        if (!enemyActor.StateController.IsUsingAction)
            enemyActor.StateController.TrySetBrainState(EnemyBrainState.Investigate);
    }

    private void ClearBrainLock()
    {
        // 本階段 Brain Slot 只由本元件管理。未來其他 Brain 功能需經本元件協調。
        if (enemyActor != null && enemyActor.ActionGate != null &&
            enemyActor.ActionGate.IsFusionSpawned)
            enemyActor.ActionGate.ClearLocks(EnemyActionLockSource.Brain);
    }

    private void Resolve()
    {
        if (enemyActor == null) enemyActor = GetComponent<EnemyActor>();
        if (perception == null) perception = GetComponent<EnemyPerceptionController>();
        if (idlePatrolBrain == null) idlePatrolBrain = GetComponent<EnemyIdlePatrolBrain>();
    }
    private void Awake() { Resolve(); }
    private void OnValidate()
    {
        Resolve();
        announcementDuration = Mathf.Max(0f, announcementDuration);
        alertRadius = Mathf.Max(0f, alertRadius);
        groupAnnouncementCooldown = Mathf.Max(0.01f, groupAnnouncementCooldown);
        damageAlertInterval = Mathf.Max(0.02f, damageAlertInterval);
        investigationSeconds = Mathf.Max(0.02f, investigationSeconds);
    }
}
