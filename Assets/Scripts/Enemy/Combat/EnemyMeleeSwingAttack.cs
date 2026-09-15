using Fusion;
using UnityEngine;


/// <summary>
/// 近戰 A：靠近玩家後，使用動畫中的實際武器位置進行揮擊。
///
/// Active Delay／Active Window 只定義「哪段動畫時間允許傷害」。
/// 真正命中必須由 Weapon Base／Weapon Tip 所形成的劍刃膠囊碰到玩家。
///
/// State Authority 每 Tick 比較上一幀與目前劍刃位置，沿劍刃取樣做
/// SphereCast，再補一次目前劍刃的 OverlapCapsule，降低快速揮劍穿透玩家的機率。
/// 傷害不由 Animation Event 呼叫；Animation Event 只能播放音效或 VFX。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyMeleeSwingAttack :
    EnemyCombatOption
{
    private const int MaximumOverlapResults = 16;

    [Header("近戰 A 武器判定引用")]

    [SerializeField]
    [Tooltip(
        "劍刃靠近手柄的起點 Transform。必須放在會跟著揮砍動畫移動的武器骨架下。\n" +
        "不可使用 Enemy Root 或固定胸口點代替，否則傷害不會真正跟著劍移動。")]
    private Transform weaponBase;

    [SerializeField]
    [Tooltip(
        "劍刃尖端 Transform。必須和 Weapon Base 位於同一把武器上，並跟著動畫移動。\n" +
        "兩點之間會形成實際劍刃膠囊。")]
    private Transform weaponTip;

    [SerializeField]
    [Tooltip(
        "用來檢查牆壁是否擋住劍與玩家的穩定起點。建議放在敵人胸口。\n" +
        "若留空，使用 Enemy Root 加上 Damage Origin Height。")]
    private Transform damageOrigin;

    [Header("近戰 A 動畫時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "從攻擊 State 開始，到劍刃進入有效傷害區段的秒數。\n" +
        "請依動畫接觸起始幀 ÷ FPS ÷ Animator State Speed 計算。")]
    private float activeDelaySeconds = 0.3f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip(
        "劍刃可以造成傷害的動畫窗口秒數。窗口內每 Tick 掃掠真實劍刃位置，" +
        "但同一次揮擊最多只成功傷害一名玩家一次。")]
    private float activeWindowSeconds = 0.18f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Active Window 結束後的收招秒數。期間不可移動、再次攻擊或防禦。")]
    private float recoverySeconds = 0.45f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("完整揮擊結束或被取消後才開始計算的冷卻秒數。")]
    private float cooldownSeconds = 1.2f;

    [SerializeField]
    [Min(1f)]
    [Tooltip("Startup 期間面向玩家的最大水平轉向速度，度／秒。進入 Active 後鎖定 Root 朝向。")]
    private float startupTurnSpeed = 420f;

    [SerializeField]
    [Range(1f, 180f)]
    [Tooltip("允許開始揮擊時，玩家與敵人正前方的最大水平夾角。80 代表正面 160 度。")]
    private float startFacingHalfAngle = 80f;

    [Header("近戰 A 劍刃判定")]

    [SerializeField]
    [Tooltip("只勾 Player Layer。劍刃掃到這個 Layer 的 Collider 後，才會尋找 PlayerHealth。")]
    private LayerMask playerHitMask;

    [SerializeField]
    [Tooltip("只勾 World／Environment 等實體場景 Layer，不可勾 Player 或 Enemy。牆位於敵人與接觸點之間時會阻擋傷害。")]
    private LayerMask obstructionMask;

    [SerializeField]
    [Min(0.005f)]
    [Tooltip("劍刃膠囊與每個掃掠取樣球的半徑，公尺。應貼近實際刀刃厚度，不要拿來補攻擊距離。")]
    private float bladeRadius = 0.08f;

    [SerializeField]
    [Range(2, 8)]
    [Tooltip(
        "沿 Weapon Base 到 Weapon Tip 之間取幾個點進行逐 Tick 掃掠。\n" +
        "一般長劍建議 4；武器越長或揮動越快可提高，但會增加物理查詢次數。")]
    private int bladeSweepSampleCount = 4;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Damage Origin 留空時，從 Enemy Root 往上偏移的世界高度，公尺。")]
    private float damageOriginHeight = 0.9f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("命中玩家時造成的基礎近戰傷害。同一次揮擊最多成功套用一次。")]
    private float damage = 18f;

    [Header("近戰 A Gizmos")]

    [SerializeField]
    [Tooltip("選取敵人時顯示 Weapon Base、Weapon Tip、劍刃膠囊半徑與 Damage Origin。")]
    private bool drawWeaponGizmos = true;

    [SerializeField]
    [Tooltip("Scene 視窗中劍刃判定 Gizmos 的顏色。")]
    private Color weaponGizmoColor =
        new Color(1f, 0.25f, 0.1f, 0.9f);

    [Header("除錯")]

    [SerializeField]
    [Tooltip("開啟後輸出每次揮擊是命中玩家、被場景遮擋或揮空。大量敵人時建議關閉。")]
    private bool debugMeleeSwing;

    [Networked] public TickTimer PhaseTimer { get; private set; }
    [Networked] public TickTimer CooldownTimer { get; private set; }
    [Networked] public Vector3 LockedSwingDirection { get; private set; }
    [Networked] public int DamageSequence { get; private set; }
    [Networked] public bool LastSwingHitPlayer { get; private set; }

    private readonly Collider[] overlapResults =
        new Collider[MaximumOverlapResults];

    private Vector3 previousWeaponBasePosition;
    private Vector3 previousWeaponTipPosition;
    private bool hasPreviousWeaponPose;
    private bool damageCommittedThisSwing;

    public override EnemyActionState ActionState => EnemyActionState.Attack;

    public override EnemyActionLockFlags LocksWhileActive =>
        EnemyActionLockFlags.Movement |
        EnemyActionLockFlags.Rotation |
        EnemyActionLockFlags.Navigation |
        EnemyActionLockFlags.Attack |
        EnemyActionLockFlags.Defense;

    public override void Spawned()
    {
        if (Object.HasStateAuthority == false) return;

        PhaseTimer = TickTimer.None;
        CooldownTimer = TickTimer.None;
        LockedSwingDirection = transform.forward;
        DamageSequence = 0;
        LastSwingHitPlayer = false;
        damageCommittedThisSwing = false;
        CaptureCurrentWeaponPose();
    }

    public override bool IsCooldownReady(NetworkRunner runner) =>
        CooldownTimer.ExpiredOrNotRunning(runner);

    public override bool CanStartOption(in EnemyCombatContext context)
    {
        if (weaponBase == null || weaponTip == null || weaponBase == weaponTip ||
            playerHitMask.value == 0 || obstructionMask.value == 0)
            return false;

        Vector3 toTarget = Vector3.ProjectOnPlane(
            context.TargetPosition - transform.position, Vector3.up);

        return toTarget.sqrMagnitude > 0.0001f &&
               Vector3.Angle(transform.forward, toTarget) <= startFacingHalfAngle;
    }

    public override void BeginOption(in EnemyCombatContext context)
    {
        LastSwingHitPlayer = false;
        damageCommittedThisSwing = false;
        FaceTarget(context);
        CaptureCurrentWeaponPose();
        PhaseTimer = activeDelaySeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, activeDelaySeconds)
            : TickTimer.None;
    }

    public override EnemyCombatOptionTickResult TickOption(in EnemyCombatContext context)
    {
        switch (context.Controller.CurrentActionPhase)
        {
            case EnemyCombatActionPhase.Startup:
                FaceTarget(context);
                // Startup 不允許傷害，持續刷新姿勢，避免把整段前搖誤算成有效揮擊。
                CaptureCurrentWeaponPose();
                if (PhaseTimer.ExpiredOrNotRunning(Runner)) BeginActive(context);
                return EnemyCombatOptionTickResult.Running;

            case EnemyCombatActionPhase.Active:
                TickWeaponDamageWindow(context);
                if (PhaseTimer.ExpiredOrNotRunning(Runner)) BeginRecovery(context);
                return EnemyCombatOptionTickResult.Running;

            case EnemyCombatActionPhase.Recovery:
                if (PhaseTimer.ExpiredOrNotRunning(Runner))
                {
                    StartCooldown();
                    return EnemyCombatOptionTickResult.Completed;
                }
                return EnemyCombatOptionTickResult.Running;

            default:
                StartCooldown();
                return EnemyCombatOptionTickResult.Cancelled;
        }
    }

    public override void CancelOption(in EnemyCombatContext context)
    {
        PhaseTimer = TickTimer.None;
        damageCommittedThisSwing = false;
        CaptureCurrentWeaponPose();
        StartCooldown();
    }

    private void BeginActive(in EnemyCombatContext context)
    {
        Vector3 direction = Vector3.ProjectOnPlane(
            context.TargetPosition - transform.position, Vector3.up);
        LockedSwingDirection = direction.sqrMagnitude > 0.0001f
            ? direction.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(LockedSwingDirection);

        // Root 最後一次對準後重取位置，不把瞬間旋轉算成劍刃揮動。
        CaptureCurrentWeaponPose();
        CheckCurrentBladeOverlap(context);
        PhaseTimer = TickTimer.CreateFromSeconds(Runner, activeWindowSeconds);
        context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Active);
    }

    private void TickWeaponDamageWindow(in EnemyCombatContext context)
    {
        if (weaponBase == null || weaponTip == null) return;

        Vector3 currentBase = weaponBase.position;
        Vector3 currentTip = weaponTip.position;
        if (hasPreviousWeaponPose == false)
        {
            previousWeaponBasePosition = currentBase;
            previousWeaponTipPosition = currentTip;
            hasPreviousWeaponPose = true;
        }

        if (damageCommittedThisSwing == false)
        {
            PhysicsScene physics = Runner.GetPhysicsScene();
            int samples = Mathf.Clamp(bladeSweepSampleCount, 2, 8);

            for (int index = 0; index < samples && !damageCommittedThisSwing; index++)
            {
                float ratio = index / (float)(samples - 1);
                Vector3 from = Vector3.Lerp(previousWeaponBasePosition,
                    previousWeaponTipPosition, ratio);
                Vector3 to = Vector3.Lerp(currentBase, currentTip, ratio);
                Vector3 movement = to - from;
                float distance = movement.magnitude;
                if (distance <= 0.00001f) continue;

                if (physics.SphereCast(from, bladeRadius, movement / distance,
                        out RaycastHit hit, distance, playerHitMask,
                        QueryTriggerInteraction.Ignore))
                    TryCommitDamage(hit.collider, hit.point, context);
            }

            if (!damageCommittedThisSwing) CheckCurrentBladeOverlap(context);
        }

        previousWeaponBasePosition = currentBase;
        previousWeaponTipPosition = currentTip;
    }

    private void CheckCurrentBladeOverlap(in EnemyCombatContext context)
    {
        if (damageCommittedThisSwing || weaponBase == null || weaponTip == null) return;

        int count = Runner.GetPhysicsScene().OverlapCapsule(
            weaponBase.position, weaponTip.position, bladeRadius, overlapResults,
            playerHitMask, QueryTriggerInteraction.Ignore);
        Vector3 bladeCenter = (weaponBase.position + weaponTip.position) * 0.5f;

        for (int index = 0; index < count && !damageCommittedThisSwing; index++)
        {
            Collider candidate = overlapResults[index];
            if (candidate == null) continue;
            TryCommitDamage(candidate, candidate.ClosestPoint(bladeCenter), context);
        }
    }

    private void TryCommitDamage(Collider hitCollider, Vector3 hitPoint,
        in EnemyCombatContext context)
    {
        if (damageCommittedThisSwing || hitCollider == null) return;

        PlayerHealth health = hitCollider.GetComponentInParent<PlayerHealth>();
        if (health == null || health.Object == null || !health.Object.IsValid || !health.IsAlive)
            return;

        if (IsObstructed(hitPoint))
        {
            if (debugMeleeSwing)
                Debug.Log("[Enemy Melee A] 劍碰到玩家，但中間有場景遮擋，不出傷。", this);
            return;
        }

        // 一旦本次有效劍刃接觸已交給 Damage Pipeline，就封存本次揮擊。
        // 即使玩家正處於無敵、格擋或其他零有效傷害狀態，也不可在後續 Tick
        // 對同一揮擊重複送出 DamageRequest。
        damageCommittedThisSwing = true;
        DamageSequence++;
        LastSwingHitPlayer = EnemyDamageUtility.TryDamagePlayer(
            context.Actor, health.Object, hitCollider.gameObject, hitPoint,
            -LockedSwingDirection, LockedSwingDirection, damage, DamageType.Melee,
            DamageSequence, out DamageResult result) && result.HasEffectiveDamage;

        if (debugMeleeSwing)
            Debug.Log(LastSwingHitPlayer
                ? "[Enemy Melee A] 動畫劍刃實際掃到玩家，成功出傷。"
                : "[Enemy Melee A] 有接觸，但 Damage Pipeline 沒有有效傷害。", this);
    }

    private bool IsObstructed(Vector3 hitPoint)
    {
        Vector3 origin = damageOrigin != null ? damageOrigin.position :
            transform.position + Vector3.up * damageOriginHeight;
        Vector3 offset = hitPoint - origin;
        float distance = offset.magnitude;
        return distance > 0.001f && Runner.GetPhysicsScene().Raycast(
            origin, offset / distance, out _, distance, obstructionMask,
            QueryTriggerInteraction.Ignore);
    }

    private void BeginRecovery(in EnemyCombatContext context)
    {
        PhaseTimer = recoverySeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, recoverySeconds)
            : TickTimer.None;
        context.Controller.TrySetActionPhase(this, EnemyCombatActionPhase.Recovery);
    }

    private void FaceTarget(in EnemyCombatContext context)
    {
        Vector3 direction = Vector3.ProjectOnPlane(
            context.TargetPosition - transform.position, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation,
            Quaternion.LookRotation(direction), startupTurnSpeed * context.DeltaTime);
    }

    private void CaptureCurrentWeaponPose()
    {
        if (weaponBase == null || weaponTip == null)
        {
            hasPreviousWeaponPose = false;
            return;
        }
        previousWeaponBasePosition = weaponBase.position;
        previousWeaponTipPosition = weaponTip.position;
        hasPreviousWeaponPose = true;
    }

    private void StartCooldown()
    {
        CooldownTimer = cooldownSeconds > 0f
            ? TickTimer.CreateFromSeconds(Runner, cooldownSeconds)
            : TickTimer.None;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        activeDelaySeconds = Mathf.Max(0f, activeDelaySeconds);
        activeWindowSeconds = Mathf.Max(0.02f, activeWindowSeconds);
        recoverySeconds = Mathf.Max(0f, recoverySeconds);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        startupTurnSpeed = Mathf.Max(1f, startupTurnSpeed);
        bladeRadius = Mathf.Max(0.005f, bladeRadius);
        bladeSweepSampleCount = Mathf.Clamp(bladeSweepSampleCount, 2, 8);
        damageOriginHeight = Mathf.Max(0f, damageOriginHeight);
        damage = Mathf.Max(0f, damage);
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        if (!drawWeaponGizmos || weaponBase == null || weaponTip == null) return;

        Gizmos.color = weaponGizmoColor;
        Gizmos.DrawLine(weaponBase.position, weaponTip.position);
        Gizmos.DrawWireSphere(weaponBase.position, bladeRadius);
        Gizmos.DrawWireSphere(weaponTip.position, bladeRadius);
        Vector3 origin = damageOrigin != null ? damageOrigin.position :
            transform.position + Vector3.up * damageOriginHeight;
        Gizmos.DrawWireSphere(origin, Mathf.Max(0.03f, bladeRadius * 0.5f));
    }
}
