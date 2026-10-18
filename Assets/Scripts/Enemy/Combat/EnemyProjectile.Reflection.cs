using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed partial class EnemyProjectile
{
    [Header("近距追蹤與近戰反彈（新變體選用）")]
    [SerializeField, Tooltip("預設關閉；只在反彈敵人專用子彈啟用，原 Ranged A 不變。")]
    private bool reflectable;
    [SerializeField, Min(0f), Tooltip("近戰反彈額外容許半徑，公尺；加上實際碰撞半徑後擴張 F／電鋸搜尋邊界。只影響抓彈，不改傷害碰撞或反彈方向。0 表示只按球體大小。")]
    private float reflectionCapturePadding;
    [SerializeField, InspectorName("Reflectable By Player Shots"), Tooltip("允許 Attack／Support 槍擊沿該發實際射擊方向反彈本彈，包含俯仰與散布；命中原射手觸發背盾破綻、不扣血。已反彈彈可再次改向但不重置壽命。須同時啟用 Reflectable；預設關閉。")]
    // 保留既有序列化欄位名稱，避免覆寫 Prefab／Scene 人工設定。
    private bool destroyableByPlayerShots;
    [SerializeField, Tooltip("將 Projectile Renderer 的 Mesh 尺寸連動到 Collision Radius，維持世界空間直徑為半徑兩倍。僅用於本原型球體；預設關閉。")]
    private bool matchVisualToCollisionRadius;
    [SerializeField, Tooltip("選取時顯示黃色實際碰撞球、青色近戰抓取容許球、白色視覺外框。Runtime 黃線標示下一 Tick 掃掠距離。")]
    private bool drawCollisionGizmos = true;
    public float CollisionRadius => Mathf.Max(.001f,collisionRadius);
    public float ReflectionCaptureRadius => CollisionRadius+Mathf.Max(0,reflectionCapturePadding);
    public bool CanBeReflectedByPlayerShot => reflectionReady && reflectable && destroyableByPlayerShots &&
        Object != null && Object.IsValid && !HasImpacted;

    public bool TryReflectByPlayerShot(Player player, Vector3 shotDirection)
    {
        if (!CanBeReflectedByPlayerShot || !Object.HasStateAuthority || player == null || player.Object == null ||
            !player.Object.IsValid || !player.Object.HasStateAuthority || player.Runner != Runner ||
            shotDirection.sqrMagnitude < .000001f) return false;

        if (!IsReflected) return TryReflect(player, shotDirection);

        // 連射只改向，保留原射手、速度與剩餘壽命，避免無限延長單發留場。
        TravelDirection = shotDirection.normalized;
        return true;
    }
    public void SyncCollisionVisual()
    {
        if (!matchVisualToCollisionRadius || projectileRenderer==null) return;
        var filter=projectileRenderer.GetComponent<MeshFilter>();if(filter==null || filter.sharedMesh==null)return;
        var visual=projectileRenderer.transform;if(visual==transform)return;
        Vector3 size=filter.sharedMesh.bounds.size,parentScale=visual.parent!=null?visual.parent.lossyScale:Vector3.one;
        if(Mathf.Abs(size.x*parentScale.x)<.00001f || Mathf.Abs(size.y*parentScale.y)<.00001f || Mathf.Abs(size.z*parentScale.z)<.00001f)return;
        float diameter=CollisionRadius*2;
        visual.localScale=new Vector3(diameter/Mathf.Abs(size.x*parentScale.x),diameter/Mathf.Abs(size.y*parentScale.y),diameter/Mathf.Abs(size.z*parentScale.z));
    }
    private void OnDrawGizmosSelected()
    {
        if(!drawCollisionGizmos)return;var old=Gizmos.color;Gizmos.color=Color.yellow;
        Gizmos.DrawWireSphere(transform.position,CollisionRadius);
        if(reflectionReady && Object!=null && Object.IsValid && !HasImpacted)
        {Vector3 next=transform.position+TravelDirection*TravelSpeed*Runner.DeltaTime;Gizmos.DrawLine(transform.position,next);Gizmos.DrawWireSphere(next,CollisionRadius);}
        if(reflectable){Gizmos.color=Color.cyan;Gizmos.DrawWireSphere(transform.position,ReflectionCaptureRadius);}
        if(projectileRenderer!=null){Gizmos.color=Color.white;Gizmos.DrawWireCube(projectileRenderer.bounds.center,projectileRenderer.bounds.size);}
        Gizmos.color=old;
    }
    [SerializeField, Min(0f), Tooltip("與原目標距離小於此值後開始追蹤，公尺。預設 6；開始後持續追蹤至命中、反彈或失效。")]
    private float homingTriggerDistance = 6f;
    [SerializeField, Min(0f), Tooltip("追蹤最大角速度，度／秒。預設 90；0 不轉彎，不會瞬間折向。")]
    private float homingTurnSpeed = 90f;
    [SerializeField, Min(.02f), Tooltip("反彈後的最長直線飛行秒數，預設 5。反彈不再追蹤、不傷害玩家。")]
    private float reflectedLifetimeSeconds = 5f;
    [SerializeField, Tooltip("反彈後命中 Enemy 與實體環境的 Layer。忽略玩家與本子彈；牆壁會阻擋。")]
    private LayerMask reflectedHitMask = ~0;
    [SerializeField, Tooltip("飛行模型 Renderer；未反彈紫紅色、已反彈綠色，可留空。")]
    private Renderer projectileRenderer;
    [SerializeField, Tooltip("敵方子彈顏色。")]
    private Color hostileColor = new Color(1f, .15f, .65f);
    [SerializeField, Tooltip("反彈子彈顏色。")]
    private Color reflectedColor = new Color(.2f, 1f, .3f);
    [Networked] public NetworkObject HomingTarget { get; private set; }
    [Networked] public NetworkBool IsReflected { get; private set; }
    [Networked] public NetworkBool HomingStarted { get; private set; }
    private bool reflectionReady;
    private MaterialPropertyBlock reflectionProperties;
    private RaycastHit[] reflectedHits = new RaycastHit[16];
    private Collider[] reflectedOverlaps = new Collider[16];
    // 只列出選用本功能且已 Spawn 的彈；不同 Runner 的查詢必須分開。
    internal static readonly List<EnemyProjectile> ReflectableProjectiles = new List<EnemyProjectile>();
    public bool CanReflect => reflectionReady && reflectable && Object != null && Object.IsValid && !HasImpacted && !IsReflected;
    public override void Spawned()
    {
        reflectionReady = true;
        SyncCollisionVisual();
        if (reflectable && !ReflectableProjectiles.Contains(this)) ReflectableProjectiles.Add(this);
    }
    public override void Despawned(NetworkRunner runner, bool hasState)
    { reflectionReady = false; ReflectableProjectiles.Remove(this); }
    private void OnDestroy() { ReflectableProjectiles.Remove(this); }
    public void SetHomingTargetBeforeSpawn(NetworkObject target) { HomingTarget = reflectable ? target : null; }

    private void TickHoming()
    {
        if (!reflectable || IsReflected || HomingTarget == null || !HomingTarget.IsValid) return;
        PlayerHealth health = HomingTarget.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead) return;
        Vector3 targetPosition = HomingTarget.transform.position + Vector3.up;
        Vector3 toward = targetPosition - transform.position;
        if (EnemyReflectorRules.ShouldHome(true, false, toward.magnitude, homingTriggerDistance)) HomingStarted = true;
        if (HomingStarted) TravelDirection = EnemyReflectorRules.Turn(TravelDirection, toward, homingTurnSpeed, Runner.DeltaTime);
    }
    public bool TryReflect(Player player, Vector3 aimDirection)
    {
        if (!CanReflect || !Object.HasStateAuthority || player == null || player.Object == null ||
            !player.Object.IsValid || player.Runner != Runner || !player.Object.HasStateAuthority || aimDirection.sqrMagnitude < .000001f) return false;
        IsReflected = true;
        HomingTarget = null;
        HomingStarted = false;
        TravelDirection = aimDirection.normalized;
        LifetimeTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(.02f, reflectedLifetimeSeconds));
        return true;
    }
    private bool IsReflectedObstacle(Collider collider)
    { return collider != null && !collider.transform.IsChildOf(transform) && collider.GetComponentInParent<Player>() == null; }
    private void TickReflectedFlight(float distance)
    {
        var physics = Runner.GetPhysicsScene();
        int count;
        do {
            count = physics.OverlapSphere(transform.position, collisionRadius, reflectedOverlaps, reflectedHitMask, QueryTriggerInteraction.Ignore);
            if (count < reflectedOverlaps.Length) break;
            System.Array.Resize(ref reflectedOverlaps, reflectedOverlaps.Length * 2);
        } while (true);
        // 反彈當下已接觸牆／Enemy 也必須處理，不能从內部穿出。
        for (int i = 0; i < count; i++)
            if (IsReflectedObstacle(reflectedOverlaps[i])) { ImpactReflected(reflectedOverlaps[i], transform.position); return; }
        do {
            count = physics.SphereCast(transform.position, collisionRadius, TravelDirection, reflectedHits, distance, reflectedHitMask, QueryTriggerInteraction.Ignore);
            if (count < reflectedHits.Length) break;
            System.Array.Resize(ref reflectedHits, reflectedHits.Length * 2);
        } while (true);
        float nearest = float.PositiveInfinity;
        int best = -1;
        for (int i = 0; i < count; i++)
            if (IsReflectedObstacle(reflectedHits[i].collider) && reflectedHits[i].distance < nearest)
            { nearest = reflectedHits[i].distance; best = i; }
        if (best >= 0) ImpactReflected(reflectedHits[best].collider, reflectedHits[best].point);
        else transform.position += TravelDirection * distance;
    }
    private void ImpactReflected(Collider collider, Vector3 point)
    {
        var shield = collider.GetComponentInParent<EnemyReflectorShield>();
        if (shield != null) shield.TryReceiveReflectedProjectile(OwnerEnemy);
        // 反彈彈只觸發原射手破綻，不送入傷害／經驗管線。
        BeginImpact(point);
    }
    private void PresentReflection()
    {
        if (!reflectable || !reflectionReady || projectileRenderer == null) return;
        if (reflectionProperties == null) reflectionProperties = new MaterialPropertyBlock();
        projectileRenderer.GetPropertyBlock(reflectionProperties);
        Color color = IsReflected ? reflectedColor : hostileColor;
        reflectionProperties.SetColor("_BaseColor", color); reflectionProperties.SetColor("_Color", color);
        projectileRenderer.SetPropertyBlock(reflectionProperties);
    }
}
