using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 本地呈現橋接器：讀取網路狀態，不反向控制 AI。
/// 以 Bool 呈現持續狀態，讓中途加入者取得正確姿勢；以 Sequence 播放一次呼喚音效事件。
/// 不需要 NetworkAnimator。所有客戶端各自播放場上的 3D 音效。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAwarenessAnimatorDriver : MonoBehaviour
{
    [Header("引用")]
    [SerializeField, Tooltip("Enemy Root 的 EnemyAwarenessBrain。留空往父階層尋找。")]
    private EnemyAwarenessBrain awareness;
    [SerializeField, Tooltip("VisualRoot 的 Animator，Apply Root Motion 必須關閉。留空搜尋本物件與子物件。")]
    private Animator animator;
    [SerializeField, Tooltip("同 Enemy Root 的 EnemyActor，提供生命與 Brain State；留空自動搜尋。")]
    private EnemyActor actor;
    [SerializeField, Tooltip("同 Enemy Root 的 EnemyIdlePatrolBrain，提供實際巡邏速度；留空自動搜尋。")]
    private EnemyIdlePatrolBrain patrol;

    [Header("Animator 參數名稱；留空可停用該參數")]
    [SerializeField, Tooltip("Bool：敵人是否存活。死亡應優先進入 Dead。")]
    private string aliveBoolName = "IsAlive";
    [SerializeField, Tooltip("Bool：目前是否處於 Announcing／Alerted／Investigating。")]
    private string alertedBoolName = "IsAlerted";
    [SerializeField, Tooltip("Bool：本敵人是否正在播放發現演出。只有取得群組演出權者為 true。")]
    private string announcingBoolName = "IsAnnouncing";
    [SerializeField, Tooltip("Float：實際巡邏速度，公尺／秒。可控制 Idle／Walk 動畫或 Blend Tree。")]
    private string moveSpeedName = "MoveSpeed";

    [Header("本機呼喚呈現事件")]
    [SerializeField, Tooltip("只在本機觀察到新 AnnouncementSequence 時觸發一次。可接敵人 AudioSource.Play。不要接傷害或 AI 邏輯；不要再用 Anim Event 播放同一段叫聲。中途加入不補播歷史叫聲。")]
    private UnityEvent onAnnouncement = new UnityEvent();

    private int aliveHash, alertedHash, announcingHash, speedHash;
    private bool initialized, parametersValid;
    private int observedSequence;

    private void Awake() { Resolve(); ValidateParameters(); }
    private void OnEnable() { initialized = false; Resolve(); ValidateParameters(); }
    private void OnValidate() { Resolve(); }

    private void LateUpdate()
    {
        if (awareness == null || !awareness.IsFusionSpawned || actor == null ||
            !actor.IsFusionSpawned || actor.Object == null || !actor.Object.IsValid)
        {
            initialized = false;
            return;
        }
        bool alive = actor.IsAlive;
        bool announcing = alive && awareness.CurrentAwarenessPhase ==
            EnemyAwarenessBrain.EnemyAwarenessPhase.Announcing;
        bool alerted = alive && awareness.CurrentAwarenessPhase !=
            EnemyAwarenessBrain.EnemyAwarenessPhase.Unaware;

        if (animator != null && parametersValid)
        {
            if (aliveHash != 0) animator.SetBool(aliveHash, alive);
            if (alertedHash != 0) animator.SetBool(alertedHash, alerted);
            if (announcingHash != 0) animator.SetBool(announcingHash, announcing);
            if (speedHash != 0) animator.SetFloat(speedHash,
                alive && patrol != null && patrol.IsFusionSpawned ? patrol.MoveSpeed : 0f);
        }

        int sequence = awareness.AnnouncementSequence;
        if (!initialized)
        {
            observedSequence = sequence;
            initialized = true;
            return;
        }
        if (observedSequence != sequence)
        {
            observedSequence = sequence;
            // 延遲快照已經跳過整段發現演出時，不補播過期叫聲。
            if (announcing) onAnnouncement?.Invoke();
        }
    }

    private void Resolve()
    {
        if (awareness == null) awareness = GetComponentInParent<EnemyAwarenessBrain>();
        if (actor == null) actor = GetComponentInParent<EnemyActor>();
        if (patrol == null) patrol = GetComponentInParent<EnemyIdlePatrolBrain>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
    }

    private void ValidateParameters()
    {
        parametersValid = animator != null && animator.runtimeAnimatorController != null;
        if (!parametersValid) return;
        aliveHash = FindParameter(aliveBoolName, AnimatorControllerParameterType.Bool);
        alertedHash = FindParameter(alertedBoolName, AnimatorControllerParameterType.Bool);
        announcingHash = FindParameter(announcingBoolName, AnimatorControllerParameterType.Bool);
        speedHash = FindParameter(moveSpeedName, AnimatorControllerParameterType.Float);
    }

    private int FindParameter(string parameterName, AnimatorControllerParameterType type)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return 0;
        int hash = Animator.StringToHash(parameterName.Trim());
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == hash && parameter.type == type) return hash;
        // 只在啟用時報告一次，不讓 Animator 每幀產生找不到參數的洗版錯誤。
        Debug.LogWarning($"[Enemy Animator] 缺少 {type} 參數 {parameterName}，已略過此欄。", this);
        return 0;
    }
}
