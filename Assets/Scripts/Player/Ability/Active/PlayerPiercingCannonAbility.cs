using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>延遲後自動發射的直線貫穿砲；瞄準、移動與取消權仍由玩家既有流程管理。</summary>
public sealed class PlayerPiercingCannonAbility : PlayerActiveAbilityBase
{
    [Header("貫穿砲")]
    [SerializeField, Min(0f), Tooltip("第一名敵人的基礎傷害；後續每名乘以 0.75，同一敵人的多個命中部位不重複扣血或衰減。")]
    private float baseDamage = 100f;
    [SerializeField, Min(0.1f), Tooltip("最大直線距離，公尺；第一個不可傷害的世界實體會擋住攻擊。")]
    private float maximumDistance = 60f;
    [SerializeField, Min(0f), Tooltip("起點相對玩家 KCC 的向上高度，公尺；方向取實際發射瞬間的準星。")]
    private float originHeight = 1.5f;
    [SerializeField, Tooltip("必須包含敵人與場景牆壁。Player 碰撞會擋住砲擊，但不造成未指定的友傷。")]
    private LayerMask hitMask = ~0;
    [SerializeField, Tooltip("開啟 Fusion 子 Tick 精度，與現有槍械命中一致。")]
    private bool useSubtickAccuracy = true;
    [SerializeField, Min(0.01f), Tooltip("各端顯示砲擊直線的秒數，只控制本機視覺，不影響傷害或冷卻。")]
    private float beamVisibleSeconds = 0.15f;
    [SerializeField, HideInInspector]
    private float beamWidth = 0.08f; // 保留舊序列化值；四階段啟用時由 presentation 控制。
    [SerializeField, HideInInspector]
    private Color beamColor = new Color(0.3f, 1f, 0.95f, 1f);
    [Header("四階段雷射")]
    [SerializeField, Range(0f, 1f), Tooltip("細線占共用 Cast Delay Seconds 的比例，其餘為中線蓄力。0 全中線、1 全細線；不改出手時間或鎖定準星。")]
    private float aimStageRatio = .55f;
    [SerializeField] private BeamPresentationSettings presentation = new BeamPresentationSettings();
    [Header("可替換本機特效")]
    [SerializeField, Tooltip("確認射出時在各端播放一次。留空維持示意線；Anchor 是相對發射位置的外觀偏移，不改命中射線。特效可實作 IPlayerAbilityLocalVfxReceiver 取得被牆截斷的終點。")]
    private PlayerAbilityLocalVfxSlot fireVfx = new PlayerAbilityLocalVfxSlot();

    [Networked] private Vector3 BeamStart { get; set; }
    [Networked] private Vector3 BeamEnd { get; set; }
    [Networked] private TickTimer BeamTimer { get; set; }
    [Networked] private int BeamVisualSequence { get; set; }
    private readonly List<LagCompensatedHit> hits = new List<LagCompensatedHit>();
    private readonly HashSet<MonoBehaviour> receivers = new HashSet<MonoBehaviour>();
    private RaycastHit[] physicsHits = new RaycastHit[16];
    private BeamPresentationRuntime visual;
    [Networked] private float BeamDuration { get; set; }

    public override void Spawned()
    {
        visual?.Hide();
    }

    protected override bool Activate()
    {
        if (!Authority) return false;
        if (!ActiveAbilityPhysics.TryGetShotOrigin(Owner, originHeight, 0f, hitMask, out Vector3 origin))
        {
            FinishAbility();
            return false;
        }
        Vector3 direction = Owner.Movement != null ? Owner.Movement.GetAimDirection() : Owner.transform.forward;
        direction.Normalize();
        hits.Clear();
        if (Runner.LagCompensation != null)
        {
            HitOptions options = HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority;
            if (useSubtickAccuracy) options |= HitOptions.SubtickAccuracy;
            Runner.LagCompensation.RaycastAll(origin, direction, maximumDistance, Owner.Object.InputAuthority,
                hits, hitMask, true, options, QueryTriggerInteraction.Ignore);
            hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }
        else
        {
            int count = ActiveAbilityPhysics.SphereCastSorted(Runner.GetPhysicsScene(), origin, direction,
                maximumDistance, 0f, hitMask, Owner.Object, ref physicsHits);
            for (int i = 0; i < count; i++) hits.Add((LagCompensatedHit)physicsHits[i]);
        }
        BeamStart = origin;
        BeamEnd = origin + direction * maximumDistance;
        receivers.Clear();
        int penetrations = 0;
        foreach (var hit in hits)
        {
            if (hit.GameObject == null || hit.GameObject.transform.IsChildOf(Owner.transform)) continue;
            var receiver = ActiveAbilityPhysics.ResolveReceiver(hit.GameObject);
            if (receiver == null || receiver is PlayerHealth)
            {
                BeamEnd = hit.Point;
                break;
            }
            if (!receivers.Add(receiver)) continue;
            ActiveAbilityPhysics.ApplyDamage(Owner, hit.GameObject, hit.Point, hit.Normal, direction,
                hit.Distance, ActiveAbilityRules.PiercingDamage(baseDamage, penetrations), DamageType.Ability, ActivationSequence);
            penetrations++;
        }
        BeamDuration = Mathf.Max(Runner.DeltaTime, beamVisibleSeconds);
        BeamTimer = TickTimer.CreateFromSeconds(Runner, BeamDuration);
        BeamVisualSequence = ActivationSequence;
        FinishAbility();
        return true;
    }

    public override void Render()
    {
        if (!Ready) { visual?.Hide(); return; }
        if (presentation == null) presentation = new BeamPresentationSettings { shotWidth = beamWidth, shotColor = beamColor };
        if (visual == null) visual = new BeamPresentationRuntime(transform);
        float remaining = BeamPresentationRules.Remaining(BeamTimer, Object, Runner);
        bool casting = Phase == PlayerActiveAbilityPhase.Casting && IsUsable;
        bool fired = !casting && remaining > 0f;
        var stage = casting
            ? (BeamPresentationRules.IsCharging(ActiveRemainingSeconds, PhaseDurationSeconds, aimStageRatio)
                ? BeamPresentationStage.Charge : BeamPresentationStage.Aim)
            : fired ? BeamPresentationStage.Fired : BeamPresentationStage.Hidden;
        Vector3 start = BeamStart, end = BeamEnd;
        if (casting && !TryPreviewRay(out start, out end)) stage = BeamPresentationStage.Hidden;
        bool custom = fireVfx != null && fireVfx.HasValidPrefab;
        bool newShot = visual.Present(presentation, stage, fired ? BeamVisualSequence : ActivationSequence, start, end,
            BeamPresentationRules.ShotScale(remaining, BeamDuration, presentation.collapse), !custom);
        if (newShot && custom)
        {
            Vector3 direction = end - start;
            Quaternion rotation = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
            fireVfx.TryPlay(transform, start, rotation, end);
        }
    }
    // 各端只查視覺遮擋，預告不寫入 BeamStart/End，也不送傷害。
    private bool TryPreviewRay(out Vector3 start, out Vector3 end)
    {
        end = default;
        if (!ActiveAbilityPhysics.TryGetShotOrigin(Owner, originHeight, 0f, hitMask, out start)) return false;
        Vector3 direction = Owner.Movement != null ? Owner.Movement.GetAimDirection() : Owner.transform.forward;
        end = start + direction.normalized * maximumDistance;
        int count = ActiveAbilityPhysics.SphereCastSorted(Runner.GetPhysicsScene(), start, direction,
            maximumDistance, 0f, hitMask, Owner.Object, ref physicsHits);
        for (int i = 0; i < count; i++)
        {
            var receiver = ActiveAbilityPhysics.ResolveReceiver(physicsHits[i].collider.gameObject);
            if (receiver != null && !(receiver is PlayerHealth)) continue;
            end = physicsHits[i].point;
            break;
        }
        return true;
    }
    public override void Despawned(NetworkRunner runner, bool hasState) => DisposePresentation();
    private void OnDisable() => DisposePresentation();
    private void OnDestroy() => DisposePresentation();
    private void DisposePresentation() { visual?.Dispose(); visual = null; }
}