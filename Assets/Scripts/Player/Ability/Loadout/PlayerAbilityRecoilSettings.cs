using System;
using UnityEngine;

/// <summary>每個後座力通道可獨立選擇；0 保留既有資產的無效果行為。</summary>
[Flags]
public enum PlayerAbilityRecoilTiming
{
    [InspectorName("未設定（不觸發）")] Unconfigured = 0,
    [InspectorName("開始準備／前搖開始前")] BeforeCast = 1,
    [InspectorName("實際施放／前搖結束後")] AfterCast = 2
}

/// <summary>模組發出的施放事件；無前搖技能只送一次 InstantActivation。</summary>
public enum PlayerAbilityRecoilEvent { BeforeCast = 1, AfterCast = 2, InstantActivation = 3 }

/// <summary>僅解析各技能的後座力設定；正式視角與速度仍由 PlayerMovement／KCC 寫入。</summary>
[Serializable]
public sealed class PlayerAbilityRecoilSettings
{
    [Header("鏡頭後座力")]
    [SerializeField, Tooltip("是否啟用技能視角後座力。預設關閉；也必須選擇觸發時機。沿用 KCC 視角，會影響後續瞄準；沿用槍械的分段施加、延遲回正及手動壓槍抵銷；僅向上的 Pitch 自動回正。")]
    private bool cameraEnabled = false;
    [SerializeField, Tooltip("本技能鏡頭後座力的複選時機。可同時勾選開始準備與實際施放，各觸發一次。閃現第一次 E 為準備、第二次 E 成功衝刺為實際施放；失敗或逾時不發施放事件。舊空中技能仍合併為成功啟動一次。")]
    private PlayerAbilityRecoilTiming cameraTiming = PlayerAbilityRecoilTiming.Unconfigured;
    [SerializeField, Tooltip("每個勾選時機的總角度，單位度。X = Pitch，負值抬頭、正值低頭；Y 的絕對值為左右交替幅度，沿用槍械確定性序號。預設 (0,0)；僅向上 Pitch 回正，Yaw 不回正，最終 Pitch 受 KCC 限制。")]
    private Vector2 cameraAngles = Vector2.zero;

    [Header("鏡頭後座力施加與回正")]
    [SerializeField, Min(0f), Tooltip("總角度分段施加的秒數，預設 0.06，範圍 >= 0；0 立即施加。重複觸發合併未施加角度並重設剩餘時間，不改總角度。")]
    private float cameraApplicationDuration = 0.06f;
    [SerializeField, Min(0f), Tooltip("從最後一次鏡頭後座力觸發起算的回正延遲，秒，預設 0.1、範圍 >= 0；也必須等待所有待施加角度結清，才開始回正。")]
    private float cameraRecoveryDelay = 0.1f;
    [SerializeField, Min(0.1f), Tooltip("向上 Pitch 的自動回正速度，度／秒，預設 20、最小 0.1。只回正實際施加且未被手動壓槍抵銷的部分；不回正 Yaw 或向下 Pitch。")]
    private float cameraRecoverySpeed = 20f;

    public float CameraApplicationDuration => Mathf.Max(0f, cameraApplicationDuration);
    public float CameraRecoveryDelay => Mathf.Max(0f, cameraRecoveryDelay);
    public float CameraRecoverySpeed => Mathf.Max(0.1f, cameraRecoverySpeed);

    [Header("玩家移動後座力")]
    [SerializeField, Tooltip("是否啟用技能移動後座力。預設關閉；也必須選擇觸發時機。由 State Authority 在既有 KCC 疊加短暫位移，不受地面／空中摩擦，不移動 Transform、不清除原有速度。")]
    private bool movementEnabled = false;
    [SerializeField, Tooltip("本技能移動後座力的複選時機，與鏡頭獨立。兩項全勾時各追加一次後座力；不勾選不觸發。舊空中技能合併為成功啟動一次；閃現分第一次 E 準備、第二次 E 成功衝刺，衝刺期間會消耗並丟棄當 Tick 的後座力位移，建議關閉移動後座力。")]
    private PlayerAbilityRecoilTiming movementTiming = PlayerAbilityRecoilTiming.Unconfigured;
    [SerializeField, Min(0f), Tooltip("後座力初速，公尺／秒，範圍 >= 0；預設 0。於 Movement Decay Duration 內線性降至零，無阻擋距離 = 初速 × 衰減時間 ÷ 2。一般摩擦不影響此增量；碰撞、子彈時間及強制移動仲裁仍有效。")]
    private float movementSpeed = 0f;
    [SerializeField, Min(0.01f), Tooltip("移動後座力線性衰減秒數，預設 0.2、最小 0.01。地面與空中相同；初速 × 秒數 ÷ 2 為無阻擋額外距離。重觸發合併剩餘距離並重設時間；被碰撞阻擋或高優先移動覆蓋的當 Tick 位移丟棄，不補發。")]
    private float movementDecayDuration = 0.2f;
    public float MovementDecayDuration => Mathf.Max(0.01f, movementDecayDuration);
    [SerializeField, Tooltip("觸發當下瞄準座標的方向：X 右、Y 上、Z 前，會隨鏡頭俯仰轉動。預設 (0,0,-1) 為視線反向。向量會正規化，不以長度放大速度；零向量回退為視線反向。")]
    private Vector3 movementLocalDirection = Vector3.back;

    public Vector2 ResolveCamera(PlayerAbilityRecoilEvent trigger) =>
        cameraEnabled && Matches(cameraTiming, trigger) ? cameraAngles : Vector2.zero;

    /// <summary>先依未受本次鏡頭後座力影響的視角換算方向，再交由既有移動入口疊加。</summary>
    public Vector3 ResolveMovement(PlayerAbilityRecoilEvent trigger, float lookPitch, float lookYaw)
    {
        if (!movementEnabled || !Matches(movementTiming, trigger) || movementSpeed <= 0f) return Vector3.zero;
        Vector3 direction = movementLocalDirection.sqrMagnitude > .000001f
            ? movementLocalDirection.normalized : Vector3.back;
        return Quaternion.Euler(lookPitch, lookYaw, 0f) * direction * movementSpeed;
    }

    private static bool Matches(PlayerAbilityRecoilTiming timing, PlayerAbilityRecoilEvent trigger)
    {
        var selected = timing & (PlayerAbilityRecoilTiming.BeforeCast | PlayerAbilityRecoilTiming.AfterCast);
        if (trigger == PlayerAbilityRecoilEvent.InstantActivation) return selected != 0;
        if (trigger != PlayerAbilityRecoilEvent.BeforeCast && trigger != PlayerAbilityRecoilEvent.AfterCast) return false;
        return (selected & (PlayerAbilityRecoilTiming)(int)trigger) != 0;
    }
}
