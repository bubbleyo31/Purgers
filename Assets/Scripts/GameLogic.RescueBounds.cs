using UnityEngine;
using Purgers.Map;

public partial class GameLogic
{
    [Header("地圖外死亡")]
    [SerializeField, Min(1f), Tooltip("低於地圖底部多少公尺立即死亡且不可救援，預設 20。底部優先使用人工參考點，再使用已就緒 MapChunk 靜態碰撞底部；無地圖資料時使用出生高度。")]
    private float fallDeathDistanceBelowMap = 20f;
    [SerializeField, Tooltip("可選的地圖底部參考點，取世界 Y。留空由本關 MapChunk 的非 Trigger 碰撞範圍計算。安全屋若無地圖資料，建議人工指定。")]
    private Transform mapBottomReference;
    private NetworkMapState rescueMap;
    private int rescueMapRevision = -1;
    private float rescueMapBottom;
    private float nextRescueMapSearch;

    public float FallDeathWorldY => ResolveMapBottom() - Mathf.Max(1f, fallDeathDistanceBelowMap);

    private float ResolveMapBottom()
    {
        if (mapBottomReference != null) return mapBottomReference.position.y;
        if (rescueMap == null && Time.unscaledTime >= nextRescueMapSearch)
        {
            nextRescueMapSearch = Time.unscaledTime + 1f;
            rescueMapRevision = -1;
            foreach (var candidate in FindObjectsOfType<NetworkMapState>())
                if (candidate.Object != null && candidate.Object.IsValid && candidate.Runner == Runner)
                { rescueMap = candidate; break; }
        }
        if (rescueMap != null && rescueMap.Object != null && rescueMap.Object.IsValid && rescueMap.IsReady)
        {
            if (rescueMapRevision != rescueMap.LayoutRevision)
            {
                rescueMapBottom = float.PositiveInfinity;
                foreach (var chunk in rescueMap.Chunks)
                {
                    if (chunk == null) continue;
                    foreach (var shape in chunk.GetComponentsInChildren<Collider>())
                        if (shape.enabled && !shape.isTrigger && shape.attachedRigidbody == null &&
                            shape.GetComponentInParent<Player>() == null && shape.GetComponentInParent<EnemyActor>() == null)
                            rescueMapBottom = Mathf.Min(rescueMapBottom, shape.bounds.min.y);
                }
                rescueMapRevision = rescueMap.LayoutRevision;
            }
            if (!float.IsPositiveInfinity(rescueMapBottom)) return rescueMapBottom;
        }
        float bottom = float.PositiveInfinity;
        if (playerSpawnPoints != null)
            foreach (var point in playerSpawnPoints) if (point != null) bottom = Mathf.Min(bottom, point.position.y);
        return float.IsPositiveInfinity(bottom) ? fallbackSpawnPosition.y : bottom;
    }

    private void TickMapFallDeaths()
    {
        float killY = FallDeathWorldY;
        foreach (var pair in Players)
        {
            var player = pair.Value;
            if (player != null && player.Health != null && !player.Health.IsDead &&
                player.transform.position.y < killY) player.Health.KillUnrescuable();
        }
    }
}
