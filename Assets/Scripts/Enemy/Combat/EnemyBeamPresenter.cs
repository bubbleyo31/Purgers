using UnityEngine;
using UnityEngine.Events;


/// <summary>
/// 遠程 B 的本地 LineRenderer 與射擊事件。
/// 只讀取 Networked 狀態，不做 Raycast 或傷害。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class EnemyBeamPresenter :
    MonoBehaviour
{
    [Header("引用")]

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyRangedBeamAttack。留空會往父階層尋找。")]
    private EnemyRangedBeamAttack beamAttack;

    [SerializeField]
    [Tooltip("Enemy Root 的 EnemyCombatDecisionController。留空會往父階層尋找。")]
    private EnemyCombatDecisionController combatDecision;

    [SerializeField]
    [Tooltip("顯示瞄準與發射 Beam 的 LineRenderer。Use World Space 必須開啟。")]
    private LineRenderer lineRenderer;

    [Header("本機發射呈現")]

    [SerializeField]
    [Tooltip("每次觀察到新的 BeamShotSequence 時觸發一次。可接世界槍聲、Muzzle Flash 或 Camera 可見的 VFX，不可接傷害。")]
    private UnityEvent onBeamFired =
        new UnityEvent();

    [Header("Tracking 線段平滑")]

    [SerializeField]
    [Tooltip(
        "Tracking 期間在兩次 Networked 目標取樣之間平滑 LineRenderer 終點。\n" +
        "只影響本機畫面，不會改變鎖定點、Hitscan 或傷害。")]
    private bool smoothTrackingVisualEnd = true;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip(
        "Tracking 視覺終點追向最新取樣點的平滑時間，單位為秒。\n" +
        "建議略小於 EnemyRangedBeamAttack 的 Tracking Target Refresh Interval，例如取樣 0.1、平滑 0.08。")]
    private float trackingVisualSmoothTime =
        0.08f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("視覺終點每秒最多移動多少公尺。避免極端網路校正讓 Line 瞬間高速掃過畫面。")]
    private float trackingVisualMaximumSpeed =
        80f;

    [SerializeField]
    [Min(0.1f)]
    [Tooltip(
        "新視覺終點與目前顯示位置相差超過此距離時直接對齊，避免重生、傳送或首次顯示時從遠處慢慢滑入。")]
    private float trackingVisualSnapDistance =
        8f;

    private int observedShotSequence;
    private bool initialized;
    private bool visualEndInitialized;
    private Vector3 displayedVisualEnd;
    private Vector3 displayedVisualEndVelocity;
    private EnemyCombatActionPhase previousActionPhase;

    private void Awake()
    {
        ResolveReferences();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        initialized = false;
        visualEndInitialized = false;
        displayedVisualEndVelocity = Vector3.zero;
        previousActionPhase = EnemyCombatActionPhase.None;
    }

    private void OnDisable()
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = false;
        }

        visualEndInitialized = false;
        displayedVisualEndVelocity = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (beamAttack == null ||
            combatDecision == null ||
            combatDecision.IsFusionSpawned == false ||
            lineRenderer == null)
        {
            return;
        }

        bool isTelegraph =
            combatDecision.ActiveOptionId ==
                EnemyCombatOptionId.RangedAttackB &&
            (combatDecision.CurrentActionPhase ==
                 EnemyCombatActionPhase.Tracking ||
             combatDecision.CurrentActionPhase ==
                EnemyCombatActionPhase.LockedDelay);

        EnemyCombatActionPhase currentPhase =
            combatDecision.CurrentActionPhase;

        bool showLine =
            isTelegraph ||
            beamAttack.IsShotVisualActive;

        lineRenderer.enabled = showLine;

        if (showLine)
        {
            Vector3 targetVisualEnd =
                beamAttack.BeamVisualEndPoint;

            bool shouldSmooth =
                smoothTrackingVisualEnd &&
                isTelegraph &&
                currentPhase == EnemyCombatActionPhase.Tracking;

            if (!visualEndInitialized ||
                !shouldSmooth ||
                previousActionPhase != EnemyCombatActionPhase.Tracking ||
                Vector3.Distance(
                    displayedVisualEnd,
                    targetVisualEnd
                ) > trackingVisualSnapDistance)
            {
                displayedVisualEnd = targetVisualEnd;
                displayedVisualEndVelocity = Vector3.zero;
                visualEndInitialized = true;
            }
            else
            {
                displayedVisualEnd = Vector3.SmoothDamp(
                    displayedVisualEnd,
                    targetVisualEnd,
                    ref displayedVisualEndVelocity,
                    trackingVisualSmoothTime,
                    trackingVisualMaximumSpeed,
                    Time.deltaTime
                );
            }

            lineRenderer.SetPosition(
                0,
                beamAttack.BeamStartPoint
            );
            lineRenderer.SetPosition(
                1,
                displayedVisualEnd
            );
        }
        else
        {
            visualEndInitialized = false;
            displayedVisualEndVelocity = Vector3.zero;
        }

        previousActionPhase = currentPhase;

        int sequence =
            beamAttack.BeamShotSequence;

        if (initialized == false)
        {
            observedShotSequence = sequence;
            initialized = true;
            return;
        }

        if (sequence != observedShotSequence)
        {
            observedShotSequence = sequence;
            onBeamFired?.Invoke();
        }
    }

    private void ResolveReferences()
    {
        if (beamAttack == null)
        {
            beamAttack =
                GetComponentInParent<EnemyRangedBeamAttack>();
        }

        if (combatDecision == null)
        {
            combatDecision =
                GetComponentInParent<EnemyCombatDecisionController>();
        }

        if (lineRenderer == null)
        {
            lineRenderer =
                GetComponent<LineRenderer>();
        }
    }

    private void OnValidate()
    {
        ResolveReferences();
        trackingVisualSmoothTime =
            Mathf.Max(0.001f, trackingVisualSmoothTime);
        trackingVisualMaximumSpeed =
            Mathf.Max(0.01f, trackingVisualMaximumSpeed);
        trackingVisualSnapDistance =
            Mathf.Max(0.1f, trackingVisualSnapDistance);
    }
}
