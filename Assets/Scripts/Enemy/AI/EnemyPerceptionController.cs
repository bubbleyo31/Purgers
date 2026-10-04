using Fusion;
using UnityEngine;

/// <summary>
/// State Authority 的視野與最後已知位置記憶。只從 Runner 玩家綁定取得候選人；
/// 視線採用該 Runner 的 PhysicsScene，不混用另一個 Runner 的碰撞世界。
/// 外部可分享「當時的位置」，但不會把共享情報當成直接看見玩家。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyActor))]
public sealed class EnemyPerceptionController : NetworkBehaviour
{
    [Header("核心引用")]
    [SerializeField, Tooltip("同 Root 的 EnemyActor；留空自動取得。")]
    private EnemyActor enemyActor;
    [SerializeField, Tooltip("眼睛位置。使用穩定的 Root 子物件，避免動畫晃頭令偵測抖動。留空使用備援眼睛高度。")]
    private Transform eyePoint = null;
    [SerializeField, Min(0f), Tooltip("Eye Point 留空時，Root 往世界上方偏移多少公尺。")]
    private float fallbackEyeHeight = 1.5f;

    [Header("視野")]
    [SerializeField, Min(0.1f), Tooltip("從眼睛到玩家胸口的最大可見距離，單位為公尺。")]
    private float sightDistance = 20f;
    [SerializeField, Range(1f, 360f), Tooltip("水平總視角。120 表示左右各 60 度；360 不限制水平視角。")]
    private float horizontalFieldOfView = 120f;
    [SerializeField, Range(1f, 180f), Tooltip("垂直總視角。100 表示水平面上、下各 50 度。")]
    private float verticalFieldOfView = 100f;
    [SerializeField, Tooltip("只勾選牆壁、地板與障礙物 Layer。不得勾 Enemy；空 Mask 會造成穿牆偵測，因此會停止視覺掃描並警告。")]
    private LayerMask occlusionMask;
    [SerializeField, Min(0f), Tooltip("玩家 Root 往上偏移多少公尺作為胸口觀測點。本版使用單點可見判斷。")]
    private float playerTargetHeight = 1.1f;

    [Header("掃描與記憶")]
    [SerializeField, Min(0.02f), Tooltip("視野掃描間隔秒數。建議 0.1；初次掃描會分散，降低整群一起 Raycast 的尖峰。")]
    private float scanIntervalSeconds = 0.1f;
    [SerializeField, Min(0.02f), Tooltip("一般目標記憶秒數。未取得鎖定保留資格時，只保存最後看見／同伴分享的舊位置；已親眼鎖定者另受保留秒數與半徑限制。")]
    private float targetMemorySeconds = 3f;
    [SerializeField, Tooltip(
        "啟用後，親眼看見的目前玩家離開扇形或被遮擋時，會在保留秒數與半徑內繼續提供即時位置。\n" +
        "超時或超出半徑就解除鎖定；重新親眼看見才重設倒數。同伴情報不能延長此倒數。\n" +
        "扇形外但沒有牆壁遮擋時仍可轉向攻擊；遮擋期間只有位置情報，不授予攻擊視線。")]
    private bool enableLockedTargetRetention = true;
    [SerializeField, Min(0.02f), Tooltip(
        "最後一次在正常視野內親眼看見後，保留即時位置幾秒，預設 3，最小 0.02。\n" +
        "增加可容忍更久的高速繞背；減少則較容易甩開。仍受保留半徑限制，不會因持續追蹤自行續期。")]
    private float lockedTargetRetentionSeconds = 3f;
    [SerializeField, Min(0.1f), Tooltip(
        "離開正常視野後，玩家胸口觀測點與敵人 Root 的最大保留距離，公尺；最小 0.1。\n" +
        "超過此半徑立即停止保留即時位置。初次發現仍使用 Sight Distance、視角與 Occlusion Mask。")]
    private float lockedTargetRetentionRadius = 25f;
    [SerializeField, Range(0f, 10f), Tooltip("選擇玩家時，從目前目標的距離評分扣除此值，降低頻繁切換目標。")]
    private float currentTargetPreference = 2f;
    [SerializeField, Tooltip("輸出目標切換與失去視線訊息。")]
    private bool debugPerception = false;

    [Header("鎖定保留 視覺輔助線")]
    [SerializeField, Tooltip("選取敵人時顯示鎖定保留半徑。此球不是初次發現範圍。")]
    private bool drawLockedTargetRetentionGizmo = true;
    [SerializeField, Tooltip("鎖定保留半徑 Gizmo 顏色。")]
    private Color lockedTargetRetentionGizmoColor =
        new Color(0.15f, 0.85f, 1f, 0.65f);

    [Networked] public NetworkObject CurrentTarget { get; private set; }
    [Networked] public bool HasDirectSight { get; private set; }
    [Networked] public Vector3 LastKnownTargetPosition { get; private set; }
    [Networked] public bool HasLastKnownPosition { get; private set; }
    [Networked] public TickTimer TargetMemoryTimer { get; private set; }
    [Networked] public bool CurrentTargetWasDirectlySeen { get; private set; }
    [Networked] public bool IsInsideLockedTargetRetention { get; private set; }
    [Networked] public TickTimer LockedTargetRetentionTimer { get; private set; }
    [Networked] public bool HasRetainedLineOfSight { get; private set; }
    [Networked] public int TargetChangedSequence { get; private set; }
    [Networked] public int DirectSightAcquiredSequence { get; private set; }
    [Networked] private TickTimer ScanTimer { get; set; }

    private bool spawned;
    public bool IsFusionSpawned => spawned && Object != null && Object.IsValid;
    public bool HasTarget => IsFusionSpawned && CurrentTarget != null && CurrentTarget.IsValid;
    /// <summary>正常視線或有限保留期間的無遮擋視線；單純共享情報不能授予。</summary>
    public bool HasCombatSight => IsFusionSpawned && HasTarget &&
        (HasDirectSight || (IsInsideLockedTargetRetention && HasRetainedLineOfSight));
    public Vector3 EyePosition => eyePoint != null ? eyePoint.position :
        transform.position + Vector3.up * fallbackEyeHeight;
    public Vector3 GetPlayerObservationPosition(NetworkObject player) =>
        player.transform.position + Vector3.up * playerTargetHeight;

    private void Awake() { Resolve(); }

    public override void Spawned()
    {
        Resolve();
        spawned = true;
        if (!HasStateAuthority) return;
        CurrentTarget = null;
        HasDirectSight = false;
        HasLastKnownPosition = false;
        CurrentTargetWasDirectlySeen = false;
        IsInsideLockedTargetRetention = false;
        LastKnownTargetPosition = transform.position;
        TargetMemoryTimer = TickTimer.None;
        LockedTargetRetentionTimer = TickTimer.None;
        HasRetainedLineOfSight = false;
        TargetChangedSequence = 0;
        DirectSightAcquiredSequence = 0;
        ScanTimer = TickTimer.CreateFromSeconds(Runner,
            Random.Range(0.02f, Mathf.Max(0.021f, scanIntervalSeconds)));
        if (occlusionMask.value == 0)
            Debug.LogError("[Enemy Perception] 必須指定 Occlusion Mask，視覺掃描暫停。", this);
    }

    public override void Despawned(NetworkRunner runner, bool hasState) { spawned = false; }

    public override void FixedUpdateNetwork()
    {
        if (!IsFusionSpawned || !HasStateAuthority || enemyActor == null) return;
        // 同物件其他 NetworkBehaviour 的 Spawned 已完成，才讀生命與狀態。
        if (!enemyActor.IsAlive)
        {
            ForgetTarget(false);
            return;
        }
        if (enemyActor.StateController.CurrentBrainState == EnemyBrainState.Dormant) return;

        // 生命檢查每 Tick 執行，死亡／離線不用等下一輪視覺掃描。
        if (CurrentTarget != null && !IsValidAlivePlayer(CurrentTarget))
            ForgetTarget(false);
        if (enemyActor.StateController != null && enemyActor.StateController.IsTimeFrozen)
        {
            ScanTimer = ActiveControlRules.PauseTimerForOneTick(ScanTimer, Runner);
            TargetMemoryTimer = ActiveControlRules.PauseTimerForOneTick(TargetMemoryTimer, Runner);
            LockedTargetRetentionTimer = ActiveControlRules.PauseTimerForOneTick(LockedTargetRetentionTimer, Runner);
            return;
        }
        if (ScanTimer.ExpiredOrNotRunning(Runner))
        {
            ScanTimer = TickTimer.CreateFromSeconds(Runner, scanIntervalSeconds);
            ScanPlayers();
        }

        // 保留只提供有限時間的即時位置，不能自行延長親眼確認的期限。
        UpdateLockedTargetRetention();

        if (!HasDirectSight &&
            !IsInsideLockedTargetRetention &&
            TargetMemoryTimer.Expired(Runner))
            ForgetTarget(false);
    }

    /// <summary>
    /// 只接受同一 Runner 現有且存活的 Player Object。
    /// 以物件身分核對玩家綁定，舊屍體不會在同 PlayerRef 重生後變成新目標。
    /// </summary>
    public bool IsValidAlivePlayer(NetworkObject candidate)
    {
        if (candidate == null || !candidate.IsValid || candidate.Runner != Runner ||
            !Runner.TryGetPlayerObject(candidate.InputAuthority, out NetworkObject bound) ||
            bound != candidate) return false;
        PlayerHealth health = candidate.GetComponent<PlayerHealth>();
        if (health == null) health = candidate.GetComponentInChildren<PlayerHealth>(true);
        return health != null && health.Object != null && health.Object.IsValid && health.IsAlive;
    }

    public bool TryAcceptSharedTarget(NetworkObject target, Vector3 snapshotPosition)
    {
        if (!CanWrite || !IsValidAlivePlayer(target)) return false;
        // 正在親眼追蹤，或曾親眼確認且仍在保留半徑內的目標優先，
        // 不被同伴每次廣播改成另一名玩家。
        if (HasTarget &&
            (HasDirectSight ||
             IsCurrentTargetWithinLockedRetention(out _)))
        {
            return false;
        }

        SetTarget(target, false, snapshotPosition);
        return true;
    }

    /// <summary>清除身分時保留最後位置，供 Investigate 使用；回到 Idle 後才可清掉記憶。</summary>
    public void ForgetTarget(bool clearLastPosition)
    {
        if (!IsFusionSpawned || !HasStateAuthority) return;
        if (CurrentTarget != null) TargetChangedSequence++;
        CurrentTarget = null;
        HasDirectSight = false;
        CurrentTargetWasDirectlySeen = false;
        IsInsideLockedTargetRetention = false;
        TargetMemoryTimer = TickTimer.None;
        LockedTargetRetentionTimer = TickTimer.None;
        HasRetainedLineOfSight = false;
        if (clearLastPosition) HasLastKnownPosition = false;
    }

    private bool CanWrite => IsFusionSpawned && HasStateAuthority &&
        enemyActor != null && enemyActor.IsAlive;

    private void ScanPlayers()
    {
        HasRetainedLineOfSight = false;
        // 已親眼鎖定的玩家若仍在保留半徑內，這輪掃描只重新檢查該玩家的真正視線，
        // 不因另一名玩家剛好走入視野就切換目標。
        if (IsCurrentTargetWithinLockedRetention(
                out Vector3 retainedTargetPosition
            ))
        {
            if (occlusionMask.value != 0 &&
                CanSee(
                    CurrentTarget,
                    retainedTargetPosition,
                    out _
                ))
            {
                SetTarget(
                    CurrentTarget,
                    true,
                    retainedTargetPosition
                );
            }
            else
            {
                HasDirectSight = false;
                HasRetainedLineOfSight = HasClearLineOfSight(CurrentTarget, retainedTargetPosition);
            }

            return;
        }

        NetworkObject best = null;
        Vector3 bestPosition = default;
        float bestScore = float.PositiveInfinity;
        if (occlusionMask.value != 0)
        {
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetPlayerObject(player, out NetworkObject candidate) ||
                    !IsValidAlivePlayer(candidate)) continue;
                Vector3 position = GetPlayerObservationPosition(candidate);
                if (!CanSee(candidate, position, out float distance)) continue;
                float score = distance - (candidate == CurrentTarget ? currentTargetPreference : 0f);
                if (score >= bestScore) continue;
                best = candidate;
                bestPosition = position;
                bestScore = score;
            }
        }
        if (best != null) SetTarget(best, true, bestPosition);
        else HasDirectSight = false;
        // TargetMemoryTimer 在每次實際觀測／新情報時更新，此處不刷新，避免永不忘記。
    }

    private bool CanSee(NetworkObject player, Vector3 position, out float distance)
    {
        Vector3 offset = position - EyePosition;
        distance = offset.magnitude;
        if (distance > sightDistance) return false;
        if (distance < 0.001f) return true;
        Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flat.sqrMagnitude > 0.0001f &&
            Vector3.Angle(forward, flat) > horizontalFieldOfView * 0.5f) return false;
        float elevation = Mathf.Atan2(Mathf.Abs(offset.y), flat.magnitude) * Mathf.Rad2Deg;
        if (elevation > verticalFieldOfView * 0.5f) return false;
        return HasClearLineOfSight(player, position);
    }

    private bool HasClearLineOfSight(NetworkObject player, Vector3 position)
    {
        if (occlusionMask.value == 0) return false;
        Vector3 offset = position - EyePosition;
        float distance = offset.magnitude;
        if (distance < 0.001f) return true;
        if (Runner.GetPhysicsScene().Raycast(EyePosition, offset / distance, out RaycastHit hit,
                distance, occlusionMask, QueryTriggerInteraction.Ignore))
            return hit.transform != null && hit.transform.IsChildOf(player.transform);
        return true;
    }

    private void SetTarget(NetworkObject target, bool seen, Vector3 position)
    {
        bool changed = CurrentTarget != target;
        bool acquired = seen && (!HasDirectSight || changed);
        CurrentTarget = target;
        HasDirectSight = seen;
        HasRetainedLineOfSight = false;
        if (changed) LockedTargetRetentionTimer = TickTimer.None;
        if (seen)
            LockedTargetRetentionTimer = TickTimer.CreateFromSeconds(Runner, lockedTargetRetentionSeconds);

        // 換成共享情報目標時必須重新親眼確認，不能繼承上一名玩家的穿牆追蹤資格。
        if (changed)
            CurrentTargetWasDirectlySeen = seen;
        else if (seen)
            CurrentTargetWasDirectlySeen = true;

        IsInsideLockedTargetRetention = false;
        LastKnownTargetPosition = position;
        HasLastKnownPosition = true;
        TargetMemoryTimer = TickTimer.CreateFromSeconds(Runner, targetMemorySeconds);
        if (changed) TargetChangedSequence++;
        if (acquired) DirectSightAcquiredSequence++;
        if (debugPerception && (changed || acquired))
            Debug.Log($"[Enemy Perception] 目標：{target.name}，直接視線：{seen}", this);
    }

    /// <summary>倒數只由正常視野的親眼觀測重設。超時／超距後保留舊位置供 Investigate。</summary>
    private void UpdateLockedTargetRetention()
    {
        IsInsideLockedTargetRetention = false;
        if (!IsCurrentTargetWithinLockedRetention(out Vector3 targetPosition))
        {
            HasRetainedLineOfSight = false;
            if (enableLockedTargetRetention && HasTarget &&
                CurrentTargetWasDirectlySeen && !HasDirectSight)
                ForgetTarget(false);
            return;
        }

        IsInsideLockedTargetRetention = true;
        LastKnownTargetPosition = targetPosition;
        HasLastKnownPosition = true;
    }

    private bool IsCurrentTargetWithinLockedRetention(out Vector3 targetPosition)
    {
        targetPosition = default;
        if (!HasTarget) return false;
        targetPosition = GetPlayerObservationPosition(CurrentTarget);
        return CanRetainTarget(enableLockedTargetRetention, CurrentTargetWasDirectlySeen,
            LockedTargetRetentionTimer.RemainingTime(Runner) ?? 0f,
            (targetPosition - transform.position).sqrMagnitude, lockedTargetRetentionRadius);
    }

    internal static bool CanRetainTarget(bool enabled, bool directlySeen,
        float remainingSeconds, float distanceSquared, float radius)
    {
        return enabled && directlySeen && remainingSeconds > 0f &&
            distanceSquared <= radius * radius;
    }
    private void Resolve()
    {
        if (enemyActor == null) enemyActor = GetComponent<EnemyActor>();
    }
    private void OnValidate()
    {
        Resolve();
        sightDistance = Mathf.Max(0.1f, sightDistance);
        scanIntervalSeconds = Mathf.Max(0.02f, scanIntervalSeconds);
        targetMemorySeconds = Mathf.Max(0.02f, targetMemorySeconds);
        lockedTargetRetentionSeconds = Mathf.Max(0.02f, lockedTargetRetentionSeconds);
        lockedTargetRetentionRadius = Mathf.Max(0.1f, lockedTargetRetentionRadius);
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 origin = EyePosition;
        Gizmos.DrawWireSphere(origin, sightDistance);
        Gizmos.DrawRay(origin, Quaternion.AngleAxis(-horizontalFieldOfView / 2f, Vector3.up) *
            transform.forward * sightDistance);
        Gizmos.DrawRay(origin, Quaternion.AngleAxis(horizontalFieldOfView / 2f, Vector3.up) *
            transform.forward * sightDistance);

        if (drawLockedTargetRetentionGizmo)
        {
            Gizmos.color = lockedTargetRetentionGizmoColor;
            Gizmos.DrawWireSphere(
                transform.position,
                lockedTargetRetentionRadius
            );
        }
    }
}
