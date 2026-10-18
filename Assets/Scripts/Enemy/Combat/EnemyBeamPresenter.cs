using UnityEngine;
using UnityEngine.Events;

/// <summary>遠程 B／C 光束的純本機呈現；讀取權威階段與發射快照，不決定傷害。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class EnemyBeamPresenter : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private EnemyRangedBeamAttack beamAttack;
    [SerializeField] private EnemyCombatDecisionController combatDecision;
    [SerializeField] private LineRenderer lineRenderer;
    [Header("四階段視聽設定")]
    [SerializeField] private BeamPresentationSettings presentation = new BeamPresentationSettings();
    [Header("本機發射呈現")]
    [SerializeField, Tooltip("每次新的、仍有效的正式發射觸發一次；不可接傷害。晚加入不補播。")]
    private UnityEvent onBeamFired = new UnityEvent();
    [Header("追蹤 線段平滑")]
    [SerializeField, Tooltip("只平滑追蹤視覺，不改鎖定點或命中。")] private bool smoothTrackingVisualEnd = true;
    [SerializeField, Min(.001f)] private float trackingVisualSmoothTime = .08f;
    [SerializeField, Min(.01f)] private float trackingVisualMaximumSpeed = 80f;
    [SerializeField, Min(.1f)] private float trackingVisualSnapDistance = 8f;

    private BeamPresentationRuntime visual;
    private bool visualEndInitialized;
    private Vector3 displayedVisualEnd, displayedVisualEndVelocity;
    private EnemyCombatActionPhase previousActionPhase;

    private void Awake()
    {
        ResolveReferences();
        if (lineRenderer != null) lineRenderer.enabled = false;
    }
    private void OnEnable()
    {
        visualEndInitialized = false; displayedVisualEndVelocity = Vector3.zero;
        previousActionPhase = EnemyCombatActionPhase.None;
    }
    private void OnDisable()
    {
        visual?.Dispose(); visual = null;
        if (lineRenderer != null) lineRenderer.enabled = false;
        visualEndInitialized = false;
    }
    private void LateUpdate()
    {
        if (beamAttack == null || combatDecision == null || !combatDecision.IsFusionSpawned || lineRenderer == null)
        {
            visual?.Hide(); return;
        }
        if (visual == null) visual = new BeamPresentationRuntime(transform, lineRenderer);
        var phase = combatDecision.CurrentActionPhase;
        bool telegraph = combatDecision.ActiveOptionId == EnemyCombatOptionId.RangedAttackB &&
            (phase == EnemyCombatActionPhase.Tracking || phase == EnemyCombatActionPhase.LockedDelay);
        float remaining = beamAttack.ShotVisualRemaining;
        bool fired = !telegraph && remaining > 0f;
        var stage = telegraph ? (phase == EnemyCombatActionPhase.Tracking ? BeamPresentationStage.Aim : BeamPresentationStage.Charge)
            : fired ? BeamPresentationStage.Fired : BeamPresentationStage.Hidden;
        Vector3 start = fired ? beamAttack.ShotStartPoint : beamAttack.BeamStartPoint;
        Vector3 end = fired ? beamAttack.ShotEndPoint : beamAttack.BeamVisualEndPoint;
        bool smooth = smoothTrackingVisualEnd && stage == BeamPresentationStage.Aim;
        if (!visualEndInitialized || !smooth || previousActionPhase != EnemyCombatActionPhase.Tracking ||
            Vector3.Distance(displayedVisualEnd, end) > trackingVisualSnapDistance)
        {
            displayedVisualEnd = end; displayedVisualEndVelocity = Vector3.zero; visualEndInitialized = true;
        }
        else displayedVisualEnd = Vector3.SmoothDamp(displayedVisualEnd, end, ref displayedVisualEndVelocity,
            trackingVisualSmoothTime, trackingVisualMaximumSpeed, Time.deltaTime);
        if (stage == BeamPresentationStage.Hidden) visualEndInitialized = false;
        float scale = BeamPresentationRules.ShotScale(remaining, beamAttack.ShotVisualDuration, presentation.collapse);
        if (visual.Present(presentation, stage, combatDecision.ActionSequence, start, displayedVisualEnd, scale))
            onBeamFired?.Invoke();
        previousActionPhase = phase;
    }
    private void ResolveReferences()
    {
        if (beamAttack == null) beamAttack = GetComponentInParent<EnemyRangedBeamAttack>();
        if (combatDecision == null) combatDecision = GetComponentInParent<EnemyCombatDecisionController>();
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
    }
    private void OnValidate()
    {
        ResolveReferences();
        trackingVisualSmoothTime = Mathf.Max(.001f, trackingVisualSmoothTime);
        trackingVisualMaximumSpeed = Mathf.Max(.01f, trackingVisualMaximumSpeed);
        trackingVisualSnapDistance = Mathf.Max(.1f, trackingVisualSnapDistance);
    }
}
