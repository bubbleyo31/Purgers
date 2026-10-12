using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Host 逐 Tick 平移固定弧形波牆，每名敵人每波最多命中一次；擊退交回 EnemyStateController，本機特效只讀快照。</summary>
public sealed class PlayerShockwaveAbility : PlayerActiveAbilityBase
{
    [Header("衝擊扇波")]
    [SerializeField, Min(0f), Tooltip("每名敵人的初次技能傷害；撞牆追加使用相同原始傷害再走正式傷害管線。")]
    private float damage = 40f;
    [SerializeField, Min(0.1f), Tooltip("波牆的固定局部弧半徑，公尺，至少 0.1；控制尺寸，不控制移動距離或高度。保留原欄位及人工值。")]
    private float radius = 5f;
    [SerializeField, Min(0.1f), Tooltip("整片波牆從起始位置向前平移的距離，公尺，至少 0.1，預設 20；不隨 Radius 改變。前緣最遠位置另包含波牆自身的弧形深度。")]
    private float travelDistance = 20f;
    [SerializeField, Min(0.1f), Tooltip("固定波牆高度，公尺，至少 0.1，預設 3；底部使用出手時發射點，整片隨瞄準角度傾斜。空中施放不吸附地板，之後不跟隨玩家。")]
    private float waveHeight = 3f;
    [SerializeField, Tooltip("發射點相對玩家 KCC 位置的公尺偏移，預設 (0,0,0)。以前搖結束時的瞄準座標計算：X 向右、Y 向上、Z 向前，可為負值；會隨抬頭低頭旋轉。若偏移穿過 Obstruction Mask 障礙，起點縮回障礙前。")]
    private Vector3 originOffset = Vector3.zero;
    [SerializeField, Range(0f, 180f), Tooltip("波牆局部扇形半角；60 表示總寬 120 度。")]
    private float halfAngleDegrees = 60f;
    [SerializeField, Min(0f), Tooltip("每次擊退最大距離，公尺；碰到阻擋物提前停止。")]
    private float knockbackDistance = 4f;
    [SerializeField, Min(0.1f), Tooltip("擊退速度，公尺／秒；不使用 Rigidbody 力量。")]
    private float knockbackSpeed = 12f;
    [SerializeField, Min(0f), Tooltip("擊退撞牆才套用的暈眩秒數；單純命中不暈眩。")]
    private float wallStunSeconds = 1f;
    [SerializeField, Tooltip("尋找敵人的 Collider Layer；仍會排除玩家並按 EnemyActor 去重。")]
    private LayerMask hitMask = ~0;
    [SerializeField, Tooltip("阻擋扇波的場景 Layer；建議包含地板與牆壁。自己及目標 Collider 不視為遮擋。")]
    private LayerMask obstructionMask = ~0;

    [Header("可替換本機特效")]
    [SerializeField, Tooltip("可選施放瞬間的附加特效；保留原插槽與人工引用。持續推進主波由 Wave Visual Prefab 呈現；本欄不決定傷害、波速或擊退。")]
    private PlayerAbilityLocalVfxSlot fireVfx = new PlayerAbilityLocalVfxSlot();
    [SerializeField, Min(0.05f), Tooltip("各端接收本次特效的有效秒數，預設 0.5 秒；逾期的晚加入不重播。只管理呈現事件，不延長傷害或擊退。")]
    private float visualEventSeconds = 0.5f;
    [Networked] private int WaveVisualSequence { get; set; }
    [Networked] private Vector3 WaveOrigin { get; set; }
    [Networked] private Vector3 WaveDirection { get; set; }
    [Networked] private Quaternion WaveRotation { get; set; }
    [Networked] private TickTimer WaveVisualTimer { get; set; }
    private int renderedVisualSequence = -1;

    [Header("向外推進的傷害波")]
    [SerializeField, Min(0.1f), Tooltip("波前推進速度，公尺／秒，至少 0.1，預設 20；與敵人擊退速度分開。每次施放快照，傷害與特效共用。")]
    private float waveSpeed = 20f;
    [SerializeField, Min(0.05f), Tooltip("弧形波牆的局部徑向厚度，公尺，至少 0.05，最多 Radius，預設 1；固定厚度不隨移動距離放大。")]
    private float waveThickness = 1f;
    [SerializeField, Tooltip("可選純視覺厚實扇波 Prefab；留空載入 Resources/Presentation/Shockwave/ThickWave。只調外觀；半徑、半角、高度、厚度與位移由權威快照自動給定。")]
    private PlayerShockwaveVisual waveVisualPrefab;
    [Networked] private NetworkBool WaveRunning { get; set; }
    [Networked] private float WaveFront { get; set; }
    [Networked] private float WaveRange { get; set; }
    [Networked] private float WaveRadius { get; set; }
    [Networked] private float WaveHeight { get; set; }
    [Networked] private float WaveHalfAngle { get; set; }
    [Networked] private float WaveSpeed { get; set; }
    [Networked] private float WaveThickness { get; set; }
    private PlayerShockwaveVisual waveVisual;
    private int visualInstanceSequence = -1;
    /// <summary>提供診斷與 Gizmo 安全讀取，不允許外部寫入波前。</summary>
    public bool IsWaveRunning => Ready && WaveRunning;
    public float CurrentWaveFront => Ready ? WaveFront : 0f;
    public Vector3 CurrentWaveOrigin => Ready ? WaveOrigin : transform.position;
    public Vector3 CurrentWaveDirection => Ready ? WaveDirection : transform.forward;
    public Quaternion CurrentWaveRotation => Ready ? WaveRotation : transform.rotation;
    /// <summary>最大平移距離，已與固定弧半徑分離；保留診斷介面名稱。</summary>
    public float CurrentWaveRange => Ready ? WaveRange : travelDistance;
    public float CurrentWaveRadius => Ready ? WaveRadius : radius;
    public float CurrentWaveHeight => Ready ? WaveHeight : waveHeight;
    public float CurrentWaveHalfAngle => Ready ? WaveHalfAngle : halfAngleDegrees;
    public float CurrentWaveThickness => Ready ? WaveThickness : waveThickness;

    private Collider[] overlaps = new Collider[256];
    private RaycastHit[] sightHits = new RaycastHit[64];
    private readonly HashSet<EnemyActor> visited = new HashSet<EnemyActor>();

    public override void Spawned()
    {
        renderedVisualSequence = -1;
        visualInstanceSequence = -1;
    }

    public override void FixedUpdateNetwork()
    {
        if (Authority && WaveRunning && !IsUsable) StopWave();
        base.FixedUpdateNetwork();
    }

    protected override bool CanBegin(NetInput input) => !WaveRunning;

    public override void Render()
    {
        if (!Ready || !WaveRunning) { ClearWaveVisual(); return; }
        if (renderedVisualSequence != WaveVisualSequence && !WaveVisualTimer.ExpiredOrNotRunning(Runner))
        {
            renderedVisualSequence = WaveVisualSequence;
            Quaternion rotation = WaveRotation;
            fireVfx?.TryPlay(transform, WaveOrigin, rotation, WaveOrigin + WaveDirection * WaveRange, WaveRadius, WaveHalfAngle);
        }
        if (visualInstanceSequence != WaveVisualSequence)
        {
            ClearWaveVisual();
            visualInstanceSequence = WaveVisualSequence;
            var template = waveVisualPrefab != null ? waveVisualPrefab :
                Resources.Load<GameObject>("Presentation/Shockwave/ThickWave")?.GetComponent<PlayerShockwaveVisual>();
            if (template != null && PlayerAbilityLocalVfxSlot.IsVisualOnly(template.gameObject))
            {
                waveVisual = Instantiate(template);
                waveVisual.name = "Shockwave live band (local)";
                waveVisual.hideFlags = HideFlags.DontSave;
                if (gameObject.scene.IsValid())
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(waveVisual.gameObject, gameObject.scene);
            }
        }
        if (waveVisual == null) return;
        float front = WaveFront;
        // 同一施放序號才插值；晚加入從快照進度開始，不從原點補播。
        if (TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
        {
            var sequence = GetPropertyReader<int>(nameof(WaveVisualSequence));
            if (sequence.Read(from) == WaveVisualSequence && sequence.Read(to) == WaveVisualSequence)
            {
                var distance = GetPropertyReader<float>(nameof(WaveFront));
                front = Mathf.Lerp(distance.Read(from), distance.Read(to), alpha);
            }
        }
        waveVisual.Present(WaveOrigin, WaveRotation, front, WaveRadius, WaveHalfAngle, WaveThickness, WaveRange, WaveHeight);
    }

    protected override bool Activate()
    {
        if (!Authority) return false;
        // Activate 在前搖結束才被呼叫；使用正式 KCC LookPitch/Yaw，不讀 LateUpdate 相機呈現。
        var kcc = Owner.Movement.KCC;
        Quaternion rotation = kcc.Data.TransformRotation * Quaternion.Euler(kcc.Data.LookPitch, 0f, 0f);
        Vector3 desired = ShockwavePropagationRules.EmissionOrigin(kcc.Data.TargetPosition, rotation, originOffset);
        Vector3 anchor = kcc.Data.TargetPosition + Vector3.up * (kcc.Settings.Height * 0.5f);
        if (!ActiveAbilityPhysics.TryResolveShotOrigin(Runner.GetPhysicsScene(), anchor, desired,
            0f, obstructionMask, Owner.Object, out Vector3 origin, true))
        {
            FinishAbility();
            return false;
        }
        WaveOrigin = origin;
        WaveRotation = rotation;
        WaveDirection = rotation * Vector3.forward;
        WaveRange = Mathf.Max(0.1f, travelDistance);
        WaveRadius = Mathf.Max(0.1f, radius);
        WaveHeight = Mathf.Max(0.1f, waveHeight);
        WaveHalfAngle = Mathf.Clamp(halfAngleDegrees, 0f, 180f);
        WaveSpeed = Mathf.Max(0.1f, waveSpeed);
        WaveThickness = ShockwavePropagationRules.GetThickness(WaveRadius, waveThickness);
        WaveFront = 0f;
        WaveVisualSequence = ActivationSequence;
        WaveVisualTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.05f, visualEventSeconds));
        visited.Clear();
        // 保留原有「施放後起算冷卻」。獨立 WaveRunning 防止零冷卻覆蓋還在前進的這一波。
        base.FinishAbility();
        WaveRunning = true;
        return true;
    }

    protected override void TickAbility()
    {
        if (!WaveRunning) return;
        float previous = WaveFront;
        WaveFront = Mathf.Min(WaveRange, previous + WaveSpeed * Runner.DeltaTime);
        ApplyWaveBand(previous, WaveFront);
        if (WaveFront >= WaveRange) StopWave();
    }

    protected override void FinishAbility()
    {
        StopWave();
        base.FinishAbility();
    }

    private void StopWave()
    {
        WaveRunning = false;
        visited.Clear();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => ClearWaveVisual();
    private void OnDisable() => ClearWaveVisual();
    private void OnDestroy() => ClearWaveVisual();
    private void ClearWaveVisual()
    {
        if (waveVisual != null) Destroy(waveVisual.gameObject);
        waveVisual = null;
        visualInstanceSequence = -1;
    }

    private void ApplyWaveBand(float previousFront, float currentFront)
    {
        Vector3 origin = WaveOrigin, forward = WaveDirection;
        PhysicsScene physics = Runner.GetPhysicsScene();
        // 保守包覆整段固定波牆路徑，再以受擊 Collider 中心做精確掃掠；候選範圍不能只取舊 Radius。
        // 沿用原本 Collider 候選流程，不以可見網格或本機 VFX 決定命中。
        float centerOffset = ShockwavePropagationRules.GetCenterOffset(WaveRadius, WaveHalfAngle, WaveThickness);
        Vector3 queryCenter = origin + forward * (WaveRange * 0.5f + centerOffset) + WaveRotation * Vector3.up * (WaveHeight * 0.5f);
        float horizontalExtent = WaveRadius + WaveRange * 0.5f;
        float queryRadius = Mathf.Sqrt(horizontalExtent * horizontalExtent + WaveHeight * WaveHeight * 0.25f);
        int count;
        do {
            count = physics.OverlapSphere(queryCenter, queryRadius, overlaps, hitMask, QueryTriggerInteraction.Collide);
            if (count < overlaps.Length) break;
            System.Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (true);
        for (int i = 0; i < count; ++i)
        {
            Collider collider = overlaps[i];
            EnemyActor enemy = collider != null ? collider.GetComponentInParent<EnemyActor>() : null;
            if (enemy == null || !enemy.IsFusionSpawned || !enemy.IsAlive || enemy.Runner != Runner || visited.Contains(enemy)) continue;
            Vector3 delta = enemy.transform.position - origin;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            // Root 常在腳底；傾斜或抬高的波牆可打到身體，即使 Root 在牆外。
            Vector3 targetPoint = collider.bounds.center;
            if (!ShockwavePropagationRules.ContainsSweptBand(targetPoint - origin, WaveRotation, previousFront, currentFront,
                WaveRadius, WaveHalfAngle, WaveThickness, WaveRange, WaveHeight)) continue;
            if (IsObstructed(physics, origin, targetPoint, enemy)) continue;
            visited.Add(enemy);
            Vector3 direction = horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : forward;
            DamageRequest request = new DamageRequest
            {
                RequestedDamage = Mathf.Max(0f, damage), BaseDamage = Mathf.Max(0f, damage),
                DamageType = DamageType.Ability, HitZone = DamageHitZoneType.Body,
                HeadshotDamageMultiplier = 1f, Attacker = Owner.Object.InputAuthority,
                SourceNetworkObject = Owner.Object, SourceObject = Owner.gameObject,
                HitObject = collider.gameObject, HitPoint = targetPoint, HitNormal = -direction,
                HitDirection = direction, Distance = delta.magnitude, Sequence = ActivationSequence
            };
            if (DamageReceiverUtility.TryApplyDamage(enemy.gameObject, request, out DamageResult result) && (result.Accepted || result.Deferred))
            {
                Owner.GetComponent<PlayerCombatFeedbackRelay>()?.ReportAbilityDamage(result);
                if (enemy.IsAlive && enemy.StateController != null)
                    enemy.StateController.TryBeginAbilityKnockback(direction, knockbackDistance,
                        knockbackSpeed, wallStunSeconds, request);
            }
        }
    }

    private bool IsObstructed(PhysicsScene physics, Vector3 start, Vector3 target, EnemyActor enemy)
    {
        Vector3 delta = target - start;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return false;
        int count;
        do {
            count = physics.Raycast(start, delta / distance, sightHits, distance, obstructionMask, QueryTriggerInteraction.Ignore);
            if (count < sightHits.Length) break;
            System.Array.Resize(ref sightHits, sightHits.Length * 2);
        } while (true);
        for (int i = 0; i < count; ++i)
        {
            Transform hit = sightHits[i].transform;
            if (hit == null || hit.IsChildOf(Owner.transform) || hit.IsChildOf(enemy.transform)) continue;
            return true;
        }
        return false;
    }
}
