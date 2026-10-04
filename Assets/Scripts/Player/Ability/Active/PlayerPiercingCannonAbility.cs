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
    [SerializeField, Min(0.001f), Tooltip("砲擊示意線寬，公尺。正式 VFX 可在之後替換本機呈現。")]
    private float beamWidth = 0.08f;
    [SerializeField, Tooltip("砲擊示意線顏色。")]
    private Color beamColor = new Color(0.3f, 1f, 0.95f, 1f);
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
    private LineRenderer beam;
    private Material beamMaterial;
    private int renderedVisualSequence = -1;
    private bool customBeamPresented;

    public override void Spawned()
    {
        renderedVisualSequence = -1;
        customBeamPresented = false;
        if (beam != null) beam.enabled = false;
    }

    protected override void Activate()
    {
        if (!Authority) return;
        Vector3 origin = ActiveAbilityPhysics.ShotOrigin(Owner, originHeight);
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
        BeamTimer = TickTimer.CreateFromSeconds(Runner, beamVisibleSeconds);
        BeamVisualSequence = ActivationSequence;
        FinishAbility();
    }

    public override void Render()
    {
        if (!Ready) return;
        bool visible = !BeamTimer.ExpiredOrNotRunning(Runner);
        if (visible && renderedVisualSequence != BeamVisualSequence)
        {
            renderedVisualSequence = BeamVisualSequence;
            Vector3 direction = BeamEnd - BeamStart;
            Quaternion rotation = direction.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
            customBeamPresented = fireVfx != null && fireVfx.TryPlay(transform, BeamStart, rotation, BeamEnd);
        }
        visible &= !customBeamPresented;
        if (visible && beam == null)
        {
            var visual = new GameObject("Piercing Cannon Beam (Local)");
            visual.transform.SetParent(transform, false);
            beam = visual.AddComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) { beamMaterial = new Material(shader); beam.sharedMaterial = beamMaterial; }
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = beam.endWidth = beamWidth;
            beam.startColor = beam.endColor = beamColor;
        }
        if (beam == null) return;
        beam.enabled = visible;
        if (visible) { beam.SetPosition(0, BeamStart); beam.SetPosition(1, BeamEnd); }
    }
    private void OnDestroy()
    {
        if (beamMaterial != null) Destroy(beamMaterial);
    }
}
