using Fusion;
using UnityEngine;


/// <summary>
/// 遠程攻擊 A：前搖後生成一發 Network Projectile。
/// 發射時刻完全由 TickTimer 決定，動畫只負責呈現。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyRangedProjectileAttack :
    EnemyCombatOption
{
    [Header("遠程 A 引用")]

    [SerializeField]
    [Tooltip("子彈生成位置與初始方向。必須放在穩定的武器槍口；若留空則使用 Enemy Root。")]
    private Transform muzzle;

    [SerializeField]
    [Tooltip("具有 NetworkObject、NetworkTransform、EnemyProjectile 的 Network Prefab。")]
    private NetworkPrefabRef projectilePrefab;

    [Header("遠程 A 時間")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("開始攻擊到真正生成子彈的秒數。用這個值對齊射擊動畫關鍵幀。")]
    private float activeDelaySeconds =
        0.35f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("子彈生成後的收招秒數。")]
    private float recoverySeconds =
        0.4f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("整個攻擊結束後才開始計算的冷卻秒數。")]
    private float cooldownSeconds =
        1.5f;

    [SerializeField]
    [Min(1f)]
    [Tooltip("從攻擊開始到 Recovery 收招完成前，持續朝玩家旋轉的最大角速度，度／秒。")]
    private float aimingTurnSpeed =
        240f;

    [Header("遠程 A 子彈")]

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Projectile 飛行速度，公尺／秒。")]
    private float projectileSpeed =
        18f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Projectile 命中玩家時造成的基礎傷害。")]
    private float projectileDamage =
        15f;

    [SerializeField]
    [Min(0.02f)]
    [Tooltip("Projectile 最長存在時間，秒。到期會進入 Impact 並 Despawn。")]
    private float projectileLifetimeSeconds =
        5f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("沿射擊方向把生成點往前推多少公尺，避免子彈出生在敵人自己的碰撞器內。")]
    private float muzzleForwardOffset =
        0.1f;

    [Header("除錯")]

    [SerializeField]
    [Tooltip("開啟後輸出 Projectile Spawn 成功或設定缺失。")]
    private bool debugProjectileAttack;

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
    public int ProjectileSequence
    {
        get;
        private set;
    }

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
            ProjectileSequence = 0;
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
        return projectilePrefab.IsValid;
    }

    public override void BeginOption(
        in EnemyCombatContext context
    )
    {
        FaceTarget(context);

        PhaseTimer =
            activeDelaySeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    activeDelaySeconds
                )
                : TickTimer.None;
    }

    public override EnemyCombatOptionTickResult TickOption(
        in EnemyCombatContext context
    )
    {
        // 整個攻擊生命週期都由本能力持有轉向。
        // 包含子彈生成後的 Recovery；Combat Controller 會在收招完成後才清除 Rotation Lock。
        FaceTarget(context);

        if (context.Controller.CurrentActionPhase ==
            EnemyCombatActionPhase.Startup)
        {
            if (context.Perception.HasDirectSight == false)
            {
                StartCooldown();
                return EnemyCombatOptionTickResult.Cancelled;
            }

            if (PhaseTimer.ExpiredOrNotRunning(Runner))
            {
                Fire(context);

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
        }

        if (context.Controller.CurrentActionPhase ==
                EnemyCombatActionPhase.Recovery &&
            PhaseTimer.ExpiredOrNotRunning(Runner))
        {
            StartCooldown();
            return EnemyCombatOptionTickResult.Completed;
        }

        return EnemyCombatOptionTickResult.Running;
    }

    public override void CancelOption(
        in EnemyCombatContext context
    )
    {
        PhaseTimer = TickTimer.None;
        StartCooldown();
    }

    private void Fire(
        in EnemyCombatContext context
    )
    {
        Transform sourceTransform =
            muzzle != null
                ? muzzle
                : transform;

        Vector3 direction =
            context.TargetPosition -
            sourceTransform.position;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = sourceTransform.forward;
        }

        direction.Normalize();

        Vector3 spawnPosition =
            sourceTransform.position +
            direction * muzzleForwardOffset;

        ProjectileSequence++;
        int sequence = ProjectileSequence;

        // context 是 in 參數，C# 不允許直接在 Spawn callback 內捕捉。
        // 先把真正需要的網路物件複製到區域變數，也讓 callback 不必依賴
        // 外層 context 的生命週期。
        NetworkObject ownerEnemy = context.Actor.Object;

        NetworkObject projectile = Runner.Spawn(
            projectilePrefab,
            spawnPosition,
            Quaternion.LookRotation(direction),
            PlayerRef.None,
            (runner, spawnedObject) =>
            {
                EnemyProjectile behaviour =
                    spawnedObject.GetComponent<EnemyProjectile>();

                if (behaviour != null)
                {
                    behaviour.Initialize(
                        runner,
                        ownerEnemy,
                        direction,
                        projectileSpeed,
                        projectileDamage,
                        projectileLifetimeSeconds,
                        sequence
                    );
                }
            }
        );

        if (debugProjectileAttack)
        {
            Debug.Log(
                $"[{nameof(EnemyRangedProjectileAttack)}] Projectile Spawn：{(projectile != null)}" +
                $"\nSequence：{sequence}",
                this
            );
        }
    }

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
                    aimingTurnSpeed * context.DeltaTime
                );
        }
    }

    private void StartCooldown()
    {
        CooldownTimer =
            cooldownSeconds > 0f
                ? TickTimer.CreateFromSeconds(
                    Runner,
                    cooldownSeconds
                )
                : TickTimer.None;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        activeDelaySeconds = Mathf.Max(0f, activeDelaySeconds);
        recoverySeconds = Mathf.Max(0f, recoverySeconds);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        aimingTurnSpeed = Mathf.Max(1f, aimingTurnSpeed);
        projectileSpeed = Mathf.Max(0.01f, projectileSpeed);
        projectileDamage = Mathf.Max(0f, projectileDamage);
        projectileLifetimeSeconds = Mathf.Max(0.02f, projectileLifetimeSeconds);
        muzzleForwardOffset = Mathf.Max(0f, muzzleForwardOffset);
    }
}
