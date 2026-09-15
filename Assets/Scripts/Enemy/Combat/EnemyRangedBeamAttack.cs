using Fusion;
using UnityEngine;


/// <summary>
/// 遠程攻擊 B：追蹤瞄準 → 鎖定世界點 → 延遲 → 瞬間 Beam／Hitscan。
///
/// Tracking 期間失去直接視線就取消並進入短冷卻；
/// LockedDelay 後不再跟隨玩家，玩家可以離開固定射線位置來閃避。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyRangedBeamAttack :
    EnemyCombatOption
{
    [Header("遠程 B 引用")]

    [SerializeField]
    [Tooltip("Beam 起點。通常是穩定的槍口 Transform；若留空使用 Enemy Root。")]
    private Transform muzzle;

    [SerializeField]
    [Tooltip("Hitscan 會命中的 Layer。必須包含 Player 與場景實體，不可包含 Enemy Layer。")]
    private LayerMask beamHitMask;

    [Header("遠程 B 時間")]

    [SerializeField]
    [Min(0.02f)]
    [Tooltip("Line 持續跟隨玩家胸口的瞄準秒數。設計確認值為 1 秒。")]
    private float trackingSeconds =
        1f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip(
        "Tracking 尚未鎖定時，每隔多久重新取樣一次玩家胸口位置，單位為秒。\n" +
        "建議 0.08～0.15；0.1 代表每秒最多更新 10 次。\n" +
        "降低此頻率可避免高速移動玩家讓 Networked 線段終點每 Tick 抖動；視覺端會在取樣點之間平滑。")]
    private float trackingTargetRefreshIntervalSeconds =
        0.1f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("停止跟隨並鎖定世界點後，到正式發射 Hitscan 的等待秒數。設計確認值為 0.5 秒。")]
    private float lockedDelaySeconds =
        0.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Hitscan 發射後的收招秒數。")]
    private float recoverySeconds =
        0.5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("完整攻擊結束後的普通冷卻秒數。")]
    private float cooldownSeconds =
        4f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tracking 期間因視線被牆擋住而取消時使用的短冷卻秒數。")]
    private float cancelledCooldownSeconds =
        0.8f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("發射瞬間的 Beam 線保留顯示幾秒。只影響呈現，不影響傷害。")]
    private float shotBeamVisibleSeconds =
        0.08f;

    [SerializeField]
    [Min(1f)]
    [Tooltip(
        "從 Tracking 開始到 Recovery 收招完成前，持續朝玩家旋轉的最大角速度，度／秒。\n" +
        "LockedDelay 的角色朝向仍會追蹤玩家，但已鎖定的 Beam 世界射線不會重新跟隨。")]
    private float trackingTurnSpeed =
        180f;

    [Header("遠程 B 傷害")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("瞬間 Beam 命中玩家時造成的基礎 Bullet 傷害。")]
    private float damage =
        35f;

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "鎖定點後方額外延伸多少公尺做 Hitscan。\n" +
        "0 代表只射到原鎖定距離；小幅延伸可避免玩家當時位於胸口點邊緣而漏判。")]
    private float rayExtensionDistance =
        1f;

    [Header("遠程 B 線段視覺終點")]

    [SerializeField]
    [Tooltip(
        "只勾選會阻擋 Beam 視覺線的場景實體 Layer，例如 World／Environment。\n" +
        "不得包含 Player 或 Enemy；LineRenderer 才會穿過玩家，繼續延伸到玩家後方第一個場景表面。\n" +
        "此 Mask 只決定視覺終點，不參與傷害。")]
    private LayerMask beamVisualObstacleMask;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip(
        "LineRenderer 穿過鎖定玩家位置後，最多再往後搜尋多少公尺。\n" +
        "若這段內碰到 Visual Obstacle Mask，就以該牆面作終點；否則畫到完整延伸距離。")]
    private float visualDistanceBeyondAimPoint =
        12f;

    [Header("除錯")]

    [SerializeField]
    [Tooltip("開啟後輸出視線取消與 Hitscan 命中結果。")]
    private bool debugBeamAttack;

    [Networked]
    public TickTimer PhaseTimer
    {
        get;
        private set;
    }

    [Networked]
    public TickTimer CooldownTimer
    {
        get;
        private set;
    }

    [Networked]
    public TickTimer ShotVisualTimer
    {
        get;
        private set;
    }

    [Networked]
    private TickTimer TrackingTargetRefreshTimer
    {
        get;
        set;
    }

    [Networked]
    public Vector3 BeamStartPoint
    {
        get;
        private set;
    }

    [Networked]
    public Vector3 BeamEndPoint
    {
        get;
        private set;
    }

    /// <summary>
    /// 純 LineRenderer 使用的終點。與 BeamEndPoint 傷害鎖定點分離，
    /// 因此視覺線可以穿過玩家，實際 Hitscan 仍由原本射線決定第一個命中物。
    /// </summary>
    [Networked]
    public Vector3 BeamVisualEndPoint
    {
        get;
        private set;
    }

    [Networked]
    public int BeamShotSequence
    {
        get;
        private set;
    }

    [Networked]
    public bool LastShotHitPlayer
    {
        get;
        private set;
    }

    public bool IsShotVisualActive =>
        Object != null &&
        Object.IsValid &&
        ShotVisualTimer.ExpiredOrNotRunning(Runner) == false;

    public override EnemyActionState ActionState =>
        EnemyActionState.Attack;

    public override EnemyActionLockFlags LocksWhileActive =>
        EnemyActionLockFlags.Movement |
        EnemyActionLockFlags.Rotation |
        EnemyActionLockFlags.Navigation |
        EnemyActionLockFlags.Attack |
        EnemyActionLockFlags.Defense;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            PhaseTimer = TickTimer.None;
            CooldownTimer = TickTimer.None;
            ShotVisualTimer = TickTimer.None;
            TrackingTargetRefreshTimer = TickTimer.None;
            BeamStartPoint = GetMuzzlePosition();
            BeamEndPoint = BeamStartPoint;
            BeamVisualEndPoint = BeamStartPoint;
            BeamShotSequence = 0;
            LastShotHitPlayer = false;
        }
    }

    public override bool IsCooldownReady(
        NetworkRunner runner
    )
    {
        return CooldownTimer.ExpiredOrNotRunning(runner);
    }

    public override bool CanStartOption(
        in EnemyCombatContext context
    )
    {
        return beamHitMask.value != 0;
    }

    public override void BeginOption(
        in EnemyCombatContext context
    )
    {
        UpdateBeamStartPoint();
        FaceTarget(context);
        RefreshTrackingTarget(context);
        ScheduleNextTrackingTargetRefresh();
        PhaseTimer = TickTimer.CreateFromSeconds(
            Runner,
            trackingSeconds
        );

        context.Controller.TrySetActionPhase(
            this,
            EnemyCombatActionPhase.Tracking
        );
    }

    public override EnemyCombatOptionTickResult TickOption(
        in EnemyCombatContext context
    )
    {
        // Rotation Lock 由攻擊開始維持到 Recovery 完成。
        // 角色可持續看著玩家，但 BeamEndPoint 只有 Tracking 低頻取樣才會改變；
        // LockedDelay 之後仍是固定世界射線，不會因角色轉身偷追玩家。
        UpdateBeamStartPoint();
        FaceTarget(context);

        switch (context.Controller.CurrentActionPhase)
        {
            case EnemyCombatActionPhase.Tracking:
                if (context.Perception.HasDirectSight == false)
                {
                    StartCooldown(cancelledCooldownSeconds);

                    if (debugBeamAttack)
                    {
                        Debug.Log(
                            "[Enemy Beam] Tracking 期間失去視線，取消並進短冷卻。",
                            this
                        );
                    }

                    return EnemyCombatOptionTickResult.Cancelled;
                }

                if (TrackingTargetRefreshTimer.ExpiredOrNotRunning(Runner))
                {
                    RefreshTrackingTarget(context);
                    ScheduleNextTrackingTargetRefresh();
                }

                if (PhaseTimer.ExpiredOrNotRunning(Runner))
                {
                    // 鎖定當下沿用玩家已看見的最後一個低頻取樣點，
                    // 不在切換瞬間偷抓一次新位置造成線段突然跳動。
                    TrackingTargetRefreshTimer = TickTimer.None;
                    PhaseTimer =
                        lockedDelaySeconds > 0f
                            ? TickTimer.CreateFromSeconds(
                                Runner,
                                lockedDelaySeconds
                            )
                            : TickTimer.None;

                    context.Controller.TrySetActionPhase(
                        this,
                        EnemyCombatActionPhase.LockedDelay
                    );
                }

                return EnemyCombatOptionTickResult.Running;

            case EnemyCombatActionPhase.LockedDelay:
                // 槍口由 Tick 開頭更新；終點保持上一階段鎖定的世界座標。

                if (PhaseTimer.ExpiredOrNotRunning(Runner))
                {
                    FireHitscan(context);
                    PhaseTimer =
                        recoverySeconds > 0f
                            ? TickTimer.CreateFromSeconds(
                                Runner,
                                recoverySeconds
                            )
                            : TickTimer.None;

                    context.Controller.TrySetActionPhase(
                        this,
                        EnemyCombatActionPhase.Recovery
                    );
                }

                return EnemyCombatOptionTickResult.Running;

            case EnemyCombatActionPhase.Recovery:
                if (PhaseTimer.ExpiredOrNotRunning(Runner))
                {
                    StartCooldown(cooldownSeconds);
                    return EnemyCombatOptionTickResult.Completed;
                }

                return EnemyCombatOptionTickResult.Running;

            default:
                StartCooldown(cancelledCooldownSeconds);
                return EnemyCombatOptionTickResult.Cancelled;
        }
    }

    public override void CancelOption(
        in EnemyCombatContext context
    )
    {
        PhaseTimer = TickTimer.None;
        TrackingTargetRefreshTimer = TickTimer.None;
        StartCooldown(cancelledCooldownSeconds);
    }

    /// <summary>每 Tick 維持 Line 起點貼住目前槍口，但不在這裡刷新玩家鎖定點。</summary>
    private void UpdateBeamStartPoint()
    {
        BeamStartPoint = GetMuzzlePosition();
    }

    /// <summary>
    /// 攻擊開始到 Recovery 完成前持續水平面向目前玩家。
    /// 此函式只改 Enemy Root 朝向，不會改寫已鎖定的 BeamEndPoint。
    /// </summary>
    private void FaceTarget(
        in EnemyCombatContext context
    )
    {
        Vector3 horizontal =
            Vector3.ProjectOnPlane(
                context.TargetPosition - transform.position,
                Vector3.up
            );

        if (horizontal.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(horizontal),
                    trackingTurnSpeed * context.DeltaTime
                );
        }
    }

    /// <summary>
    /// 依可調頻率取得玩家目前位置，並同時求出穿過玩家後的純視覺終點。
    /// </summary>
    private void RefreshTrackingTarget(
        in EnemyCombatContext context
    )
    {
        BeamEndPoint = context.TargetPosition;

        Vector3 direction =
            BeamEndPoint - BeamStartPoint;

        float aimDistance = direction.magnitude;

        if (aimDistance <= 0.001f)
        {
            direction = transform.forward;
            aimDistance = 0.001f;
        }
        else
        {
            direction /= aimDistance;
        }

        BeamVisualEndPoint = FindVisualEndPoint(
            BeamStartPoint,
            direction,
            aimDistance
        );
    }

    private void ScheduleNextTrackingTargetRefresh()
    {
        TrackingTargetRefreshTimer = TickTimer.CreateFromSeconds(
            Runner,
            trackingTargetRefreshIntervalSeconds
        );
    }

    private void FireHitscan(
        in EnemyCombatContext context
    )
    {
        Vector3 direction =
            BeamEndPoint - BeamStartPoint;

        float lockedDistance =
            direction.magnitude;

        if (lockedDistance <= 0.001f)
        {
            direction = transform.forward;
            lockedDistance = 0.001f;
        }
        else
        {
            direction /= lockedDistance;
        }

        float castDistance =
            lockedDistance + rayExtensionDistance;

        // 傷害射線與 LineRenderer 視覺終點分開。
        // 視覺射線只查場景阻擋 Layer，所以可以穿過玩家後再停在牆面。
        BeamVisualEndPoint = FindVisualEndPoint(
            BeamStartPoint,
            direction,
            lockedDistance
        );

        BeamShotSequence++;
        LastShotHitPlayer = false;

        if (Runner.GetPhysicsScene().Raycast(
                BeamStartPoint,
                direction,
                out RaycastHit hit,
                castDistance,
                beamHitMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            BeamEndPoint = hit.point;

            PlayerHealth health =
                hit.collider != null
                    ? hit.collider.GetComponentInParent<PlayerHealth>()
                    : null;

            if (health != null &&
                health.Object != null &&
                health.IsAlive)
            {
                LastShotHitPlayer =
                    EnemyDamageUtility.TryDamagePlayer(
                        context.Actor,
                        health.Object,
                        hit.collider.gameObject,
                        hit.point,
                        hit.normal,
                        direction,
                        damage,
                        DamageType.Bullet,
                        BeamShotSequence,
                        out DamageResult result
                    ) && result.HasEffectiveDamage;
            }
        }
        else
        {
            BeamEndPoint =
                BeamStartPoint +
                direction * castDistance;
        }

        ShotVisualTimer =
            TickTimer.CreateFromSeconds(
                Runner,
                Mathf.Max(
                    Runner.DeltaTime,
                    shotBeamVisibleSeconds
                )
            );

        if (debugBeamAttack)
        {
            Debug.Log(
                $"[Enemy Beam] Hitscan 發射。" +
                $"\nHit Player：{LastShotHitPlayer}" +
                $"\nSequence：{BeamShotSequence}",
                this
            );
        }
    }

    private Vector3 GetMuzzlePosition()
    {
        return muzzle != null
            ? muzzle.position
            : transform.position;
    }

    /// <summary>
    /// 從槍口沿瞄準方向穿過 Aim Point，再尋找玩家後方第一個場景阻擋面。
    /// Visual Obstacle Mask 為空時仍會延伸固定距離，但不執行場景 Raycast。
    /// </summary>
    private Vector3 FindVisualEndPoint(
        Vector3 start,
        Vector3 direction,
        float aimDistance
    )
    {
        float visualDistance =
            Mathf.Max(0.001f, aimDistance) +
            visualDistanceBeyondAimPoint;

        if (beamVisualObstacleMask.value != 0 &&
            Runner.GetPhysicsScene().Raycast(
                start,
                direction,
                out RaycastHit visualHit,
                visualDistance,
                beamVisualObstacleMask,
                QueryTriggerInteraction.Ignore
            ))
        {
            return visualHit.point;
        }

        return start + direction * visualDistance;
    }

    private void StartCooldown(
        float duration
    )
    {
        CooldownTimer =
            duration > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    duration
                )
                : TickTimer.None;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        trackingSeconds = Mathf.Max(0.02f, trackingSeconds);
        trackingTargetRefreshIntervalSeconds =
            Mathf.Max(0.02f, trackingTargetRefreshIntervalSeconds);
        lockedDelaySeconds = Mathf.Max(0f, lockedDelaySeconds);
        recoverySeconds = Mathf.Max(0f, recoverySeconds);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        cancelledCooldownSeconds = Mathf.Max(0f, cancelledCooldownSeconds);
        shotBeamVisibleSeconds = Mathf.Max(0f, shotBeamVisibleSeconds);
        trackingTurnSpeed = Mathf.Max(1f, trackingTurnSpeed);
        damage = Mathf.Max(0f, damage);
        rayExtensionDistance = Mathf.Max(0f, rayExtensionDistance);
        visualDistanceBeyondAimPoint =
            Mathf.Max(0.1f, visualDistanceBeyondAimPoint);
    }
}
