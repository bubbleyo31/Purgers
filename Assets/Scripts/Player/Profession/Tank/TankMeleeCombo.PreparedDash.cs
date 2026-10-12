using System.Collections.Generic;
using Fusion;
using Fusion.Addons.KCC;
using UnityEngine;

public partial class TankMeleeCombo
{
    [Header("預備放開後接近敵人")]
    [SerializeField, Tooltip("僅幀模式按住到停格後放開適用；瞄準範圍內有存活敵人時，動畫恢復並於出傷幀抵達。點按與無目標維持原地攻擊。")]
    private bool enablePreparedDash = true;
    [SerializeField, Min(.1f), Tooltip("選敵最大距離，公尺；直線命中優先，再搜尋寬鬆半角內且未被遮擋的敵人。")]
    private float preparedDashRange = 12f;
    [SerializeField, HideInInspector] // 保留舊資產序列化；速度現由距離與動畫期限計算。
#pragma warning disable CS0414 // 僅保留舊資產資料，不參與新時序。
    private float preparedDashSpeed = 35f;
#pragma warning restore CS0414
    [SerializeField, Min(.01f), Tooltip("玩家瞄準高度位置與敵人命中表面的停靠距離，公尺。須在本段近戰範圍內，否則可能揮空。")]
    private float preparedDashStopDistance = 1f;
    [SerializeField, Min(.01f), Tooltip("瞄準射線相對 KCC 腳底高度，公尺；應與玩家準心視線高度一致。")]
    private float preparedDashAimHeight = 1.6f;
    [SerializeField, HideInInspector] // 保留舊資產序列化；期限現由出傷幀減停格幀計算。
#pragma warning disable CS0414 // 僅保留舊資產資料，不參與新時序。
    private float preparedDashTimeout = 1f;
#pragma warning restore CS0414
    [SerializeField, Tooltip("敵人真正戰鬥 Hitbox 的 Layer，預設 Hit Mask；不包含場景。")]
    private LayerMask preparedDashEnemyMask = 1 << 9;
    [SerializeField, Tooltip("瞄準遮擋 Layer；必須包含牆壁，預設排除 UI／Weapon_Visual／ViewModel。位移另由 KCC 膠囊及 Collision Layer Mask 阻擋。")]
    private LayerMask preparedDashObstructionMask = Physics.DefaultRaycastLayers & ~((1 << 5) | (1 << 7) | (1 << 8));

    [Networked] private NetworkObject PreparedDashTarget { get; set; }
    [Networked] private Vector3 PreparedDashLocalPoint { get; set; }
    [Networked] private Vector3 PreparedDashApproach { get; set; }
    [SerializeField, Range(0f, 90f), Tooltip("準心方向向兩側放寬的半角，度。0 只接受直線命中；越大越易選到側方敵人。直線優先，否則角度最小優先，近似同角度取較近者；仍必須沒有遮擋。")]
    private float preparedDashAimHalfAngle = 10f;
    [Networked] public float PreparedDashDurationSeconds { get; private set; }
    [Networked] public float PreparedDashElapsedSeconds { get; private set; }
    private readonly List<LagCompensatedHit> preparedDashCandidates = new List<LagCompensatedHit>(32);
    [Networked] private NetworkBool PreparedDashBlocked { get; set; }
    private readonly KCCShapeCastInfo preparedDashCast = new KCCShapeCastInfo();
    public bool IsPreparedDashing => Object != null && Object.IsValid && CurrentPhase == TankMeleeAttackPhase.PreparedDashing;

    private bool PreparedDashDisplaced() => ownerMovement == null || ownerMovement.HasActiveAbilityDash ||
        (ownerPlayer.SupportGrapplePlayerPullReceiver != null && ownerPlayer.SupportGrapplePlayerPullReceiver.IsPullActive) ||
        (ownerPlayer.Grapple != null && ownerPlayer.Grapple.IsNormalPlayerPullAttached);

    private bool TryStartPreparedDash()
    {
        if (ownerMovement == null || ownerMovement.KCC == null || PreparedDashDisplaced()) return false;
        var gate = ownerPlayer.GetComponent<PlayerActionGate>();
        if (gate != null && gate.IsBlocked(PlayerActionBlockMask.Movement)) return false;
        Vector3 offset = Vector3.up * preparedDashAimHeight;
        Vector3 position = ownerMovement.KCC.Data.TargetPosition;
        Vector3 direction = ownerMovement.GetAimDirection();
        if (!ActiveAbilityPhysics.TryGetShotOrigin(ownerPlayer, preparedDashAimHeight, 0f,
            preparedDashObstructionMask, out Vector3 origin)) return false;
        if (!PlayerEnemyAimQuery.TryGetAuthoritativeWithTolerance(ownerPlayer, origin, direction, preparedDashRange,
            preparedDashAimHalfAngle, preparedDashEnemyMask, preparedDashObstructionMask, preparedDashCandidates, out var enemy, out Vector3 point)) return false;
        direction = (point - origin).normalized;
        Vector3 destination = TankPreparedDashRules.Destination(point, direction, preparedDashStopDistance, offset);
        // 已在停靠距離內直接出手，不倒退衝刺。
        if (Vector3.Dot(destination - position, direction) <= .05f) return false;
        PreparedDashTarget = enemy.Object;
        PreparedDashLocalPoint = enemy.Object.transform.InverseTransformPoint(point);
        PreparedDashApproach = direction;
        PreparedDashDurationSeconds = Mathf.Max(Runner.DeltaTime, TankPreparedDashRules.Duration(GetDefinition(CurrentStep)));
        PreparedDashElapsedSeconds = 0f;
        PreparedDashBlocked = false;
        var progress = Preparation;
        progress.Released = true;
        progress.IsHolding = false;
        Preparation = progress; // 下一個模擬步起恢復動畫；兩種按住時間不再累加。
        CurrentPhase = TankMeleeAttackPhase.PreparedDashing;
        ownerPlayer.Grapple?.CancelFromSpecialAbility(false, true);
        gate?.SetBlocks(PlayerActionBlockSource.WeaponAction,
            PlayerActionBlockMask.Movement | PlayerActionBlockMask.Aim | PlayerActionBlockMask.QuickAction | PlayerActionBlockMask.Grapple);
        return true;
    }

    private Vector3 PreparedDashDestination() => TankPreparedDashRules.Destination(
        PreparedDashTarget.transform.TransformPoint(PreparedDashLocalPoint), PreparedDashApproach,
        preparedDashStopDistance, Vector3.up * preparedDashAimHeight);

    private void TickPreparedDash()
    {
        // Input Authority 可預測游標及移動；只有 Host 能完成本段並送出傷害。
        PreparedDashElapsedSeconds = Mathf.Min(PreparedDashDurationSeconds, PreparedDashElapsedSeconds + Runner.DeltaTime);
        var definition = GetDefinition(CurrentStep);
        var progress = Preparation;
        progress.CursorSeconds = Mathf.Min(definition.ResolvedStartup,
            definition.FrameTiming.FrameToSeconds(definition.PreparationFrame) + PreparedDashElapsedSeconds);
        Preparation = progress;
        if (Object.HasStateAuthority && PreparedDashElapsedSeconds + .00001f >= PreparedDashDurationSeconds)
        {
            ClearPreparedDash();
            EnterActivePhase();
        }
    }
    /// <summary>由既有 PlayerMovement／KCC 最後階段提交速度，保留 KCC 碰撞及子彈時間。</summary>
    public bool TryGetPreparedDashVelocity(KCC kcc, KCCData data, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        if (!IsPreparedDashing || kcc == null || data.DeltaTime <= 0f || PreparedDashDisplaced()) return false;
        if (PreparedDashTarget == null || !PreparedDashTarget.IsValid || PreparedDashBlocked) return true;
        var enemy = PreparedDashTarget.GetComponent<TestDamageReceiver>();
        if (enemy == null || !enemy.IsAlive) return true;
        Vector3 destination = PreparedDashDestination();
        Vector3 delta = destination - data.TargetPosition;

        const float skin = .02f;
        float multiplier = Mathf.Clamp01(PlayerBulletTimeSessionRegistry.GetPlayerMovementMultiplier(ownerPlayer));
        velocity = TankPreparedDashRules.VelocityForDeadline(data.TargetPosition, destination,
            PreparedDashDurationSeconds - PreparedDashElapsedSeconds, data.DeltaTime, multiplier);
        float travel = velocity.magnitude * data.DeltaTime * multiplier;
        float radius = Mathf.Max(.01f, kcc.Settings.Radius - skin);
        float height = Mathf.Max(radius * 2f, kcc.Settings.Height - skin * 2f);
        if (delta.sqrMagnitude > .000001f && kcc.CapsuleCast(preparedDashCast, data.TargetPosition + Vector3.up * skin,
            radius, height, delta.normalized, travel + skin, QueryTriggerInteraction.Ignore, true))
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < preparedDashCast.ColliderHitCount; i++)
            {
                var hit = preparedDashCast.ColliderHits[i].RaycastHit;
                if (Vector3.Dot(hit.normal, delta.normalized) >= -.001f) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            if (nearest <= travel + skin)
            {
                velocity = delta.normalized * Mathf.Max(0f, nearest - skin) / data.DeltaTime / Mathf.Max(.0001f, multiplier);
                if (Object.HasStateAuthority && kcc.IsInFixedUpdate) PreparedDashBlocked = true;
            }
        }
        return true;
    }

    private void ClearPreparedDash()
    {
        if (!IsPreparedDashing) return;
        ownerMovement?.KCC?.SetKinematicVelocity(Vector3.zero);
        var gate = ownerPlayer != null ? ownerPlayer.GetComponent<PlayerActionGate>() : null;
        if (gate != null && gate.Object != null && gate.Object.IsValid) gate.ClearBlocks(PlayerActionBlockSource.WeaponAction);
        PreparedDashTarget = null;
        PreparedDashLocalPoint = default;
        PreparedDashApproach = default;
        PreparedDashDurationSeconds = 0f;
        PreparedDashElapsedSeconds = 0f;
        PreparedDashBlocked = false;
    }
}
