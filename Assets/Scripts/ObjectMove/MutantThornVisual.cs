using UnityEngine;

/// <summary>
/// 荊棘的本機視覺呼吸與短促受擊抽動。不處理生命、傷害、生成或網路同步。
/// 必須指定不含碰撞體的視覺子階層；碰撞與權威根物件維持不動。
/// </summary>
[DisallowMultipleComponent]
public sealed class MutantThornVisual : MonoBehaviour
{
    [Header("視覺階層（不能包含碰撞體）")]
    [SerializeField, Tooltip("指定此物件的子物件 Visual；不能指定自己、外部物件或含 Collider 的階層。停用時還原原始姿勢。")]
    private Transform visualRoot;

    [Header("Idle 呼吸")]
    [SerializeField, Range(0f, .08f), Tooltip("縮放振幅，0～0.08；0 停止呼吸。預設 0.025 代表約 2.5%，只影響視覺。")]
    private float breathAmplitude = .025f;
    [SerializeField, Range(.1f, 2f), Tooltip("每秒呼吸週期，0.1～2 Hz；預設 0.32，約每 3.125 秒一週期。")]
    private float breathFrequency = .32f;

    [Header("受傷微抽動（由已確認的受擊事件呼叫）")]
    [SerializeField, Range(.05f, 1f), Tooltip("抽動衰減時間，0.05～1 秒；預設 0.28 秒，結束後只保留呼吸。")]
    private float hitDuration = .28f;
    [SerializeField, Range(0f, 10f), Tooltip("最大局部旋轉角度，0～10 度；預設 3 度。0 停止旋轉抽動。")]
    private float hitAngle = 3f;
    [SerializeField, Range(0f, .08f), Tooltip("受擊時額外縮放振幅，0～0.08；預設 0.035。不會移動碰撞根。")]
    private float hitCompression = .035f;

    private Vector3 restPosition;
    private Vector3 restScale;
    private Quaternion restRotation;
    private Transform cachedVisual;
    private float phase;
    private float hitStarted = float.NegativeInfinity;
    private float hitStrength;

    private void OnEnable()
    {
        if (visualRoot == null || visualRoot == transform || !visualRoot.IsChildOf(transform) ||
            visualRoot.GetComponentInChildren<Collider>(true) != null)
            return;
        cachedVisual = visualRoot;
        restPosition = cachedVisual.localPosition;
        restScale = cachedVisual.localScale;
        restRotation = cachedVisual.localRotation;
        // 不消耗遊戲的 Random 狀態；每個 instance 錯開相位，避免整張地圖同步鼓動。
        phase = (GetInstanceID() & 1023) * (Mathf.PI * 2f / 1024f);
        hitStarted = float.NegativeInfinity;
        hitStrength = 0f;
    }

    private void LateUpdate()
    {
        if (cachedVisual == null) return;
        float breath = Mathf.Sin(Time.time * breathFrequency * Mathf.PI * 2f + phase) * breathAmplitude;
        float elapsed = Time.time - hitStarted;
        float envelope = EvaluateHitEnvelope(elapsed, hitDuration) * hitStrength;
        float twitch = envelope > 0f ? Mathf.Cos(elapsed * 65f) * envelope : 0f;
        cachedVisual.localScale = Vector3.Scale(restScale,
            new Vector3(1f + breath - twitch * hitCompression,
                1f + breath * .7f + twitch * hitCompression,
                1f + breath - twitch * hitCompression));
        cachedVisual.localRotation = restRotation * Quaternion.Euler(twitch * hitAngle, 0f, twitch * hitAngle * .6f);
    }

    /// <summary>由確認成功的受擊呈現事件呼叫；strength 為 0～1，重複命中重新起算，不能在此決定傷害。</summary>
    public void PlayHitReaction(float strength = 1f)
    {
        if (!isActiveAndEnabled || cachedVisual == null || float.IsNaN(strength) || float.IsInfinity(strength)) return;
        hitStrength = Mathf.Clamp01(strength);
        hitStarted = Time.time;
    }

    /// <summary>只供美術預覽；不送出任何傷害或網路事件。</summary>
    [ContextMenu("預覽受傷微抽動（Play Mode）")]
    private void PreviewHitReaction() { if (Application.isPlaying) PlayHitReaction(); }

    /// <summary>有限時間平方衰減；處理無效／尚未開始的時間，避免殘餘姿勢或 NaN。</summary>
    public static float EvaluateHitEnvelope(float elapsed, float duration)
    {
        if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < 0f || duration <= 0f) return 0f;
        float value = 1f - Mathf.Clamp01(elapsed / duration);
        return value * value;
    }

    private void OnDisable()
    {
        if (cachedVisual != null)
        {
            cachedVisual.localPosition = restPosition;
            cachedVisual.localScale = restScale;
            cachedVisual.localRotation = restRotation;
        }
        cachedVisual = null;
        hitStarted = float.NegativeInfinity;
    }

    private void OnValidate()
    {
        breathAmplitude = Mathf.Clamp(breathAmplitude, 0f, .08f);
        breathFrequency = Mathf.Clamp(breathFrequency, .1f, 2f);
        hitDuration = Mathf.Clamp(hitDuration, .05f, 1f);
        hitAngle = Mathf.Clamp(hitAngle, 0f, 10f);
        hitCompression = Mathf.Clamp(hitCompression, 0f, .08f);
    }
}
