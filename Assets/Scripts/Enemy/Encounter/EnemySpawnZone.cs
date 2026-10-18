using System;
using System.Collections.Generic;
using Fusion;
using Purgers.Map;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    /// <summary>MapChunk Prefab 上由設計者配置的出生盒與其子物件啟動門檻。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnZone : MonoBehaviour
    {
        [Header("怪物出生範圍（此物件局部座標）")]
        [SerializeField, Tooltip("出生盒中心相對此 Transform 的局部偏移；Transform 隨 MapChunk 一起旋轉。所有種類都在盒內隨機散布；Stationary 出生後不自主移動，也不自動貼地，請調整盒體高度與範圍。")]
        private Vector3 spawnBoxCenter;

        [SerializeField, Tooltip("出生盒的 X／Y／Z 完整尺寸，公尺。所有種類每次嘗試重新取點，占用時換點重試直到 Director 的 Candidate Attempts 上限。Ground 仍須投影到該 Chunk 的預烘焙 NavMesh；Stationary 直接使用盒內高度。")]
        private Vector3 spawnBoxSize = new Vector3(12f, 4f, 12f);

        [Header("生成條件")]
        [SerializeField, InspectorName("生成需要視野遮擋"), Tooltip(
            "預設開啟：出生點必須對所有存活玩家都有實體遮擋才可生成。\n" +
            "測試時可關閉，允許玩家直接看見怪物生成；只略過出生視線遮擋。\n" +
            "最小玩家距離、占用、導航、前方、預算與門檻／輪替限制仍有效；落後清理仍要求遮擋。")]
        private bool requireSpawnOcclusion = true;

        [Header("混合敵人清單")]
        [SerializeField, Tooltip("每次穿越啟動時，各項目獨立判定一次機率；抽中就要求生成該項指定數量。可混放 Ground／Free Flying／Stationary，自動讀取 Prefab 的 EnemyDefinition。清單非空時取代下方舊版設定；清單全為 0% 時不生成。仍受 Director 預算、出生安全檢查與啟動期限限制。")]
        private EnemySpawnEntry[] enemyEntries = Array.Empty<EnemySpawnEntry>();

        [Header("舊版相容設定（混合清單為空時才使用）")]
        [SerializeField, Tooltip("此區域只放同一移動類型的 Network Prefab。Ground 使用該 Chunk 的 Runtime Ground Patrol Area；Free Flying 未指定人工 Patrol Area 時，自動規劃下方立體巡邏盒。Stationary 不需要任何巡邏區或導航，在出生盒內隨機取點。")]
        private EnemyLocomotionKind locomotionKind = EnemyLocomotionKind.Ground;

        [SerializeField, Tooltip("可隨機選用的正式 Enemy Network Prefab；必須在 Fusion Prefab Table 登記，且 EnemyDefinition 的移動類型符合上方設定。")]
        private NetworkPrefabRef[] enemyPrefabs = Array.Empty<NetworkPrefabRef>();

        [SerializeField, Min(1), Tooltip("只在混合清單為空時使用：一次成功啟動最多生成幾隻；Director 會逐隻間隔生成，玩家離開或條件失效時不補發剩餘數量。")]
        private int enemiesPerActivation = 2;

        [Header("飛行巡邏區（混合清單與舊版共用）")]
        [SerializeField, Tooltip("只有飛行敵人使用（包含混合清單）。留空由 Host 自動生成飛行巡邏區；有指定時沿用同一 MapChunk 內的人工 EnemyPatrolArea。Ground 與 Stationary 忽略此欄位。")]
        private EnemyPatrolArea flyingPatrolArea;

        [Header("自動飛行巡邏（有飛行敵人且人工 Area 留空時使用）")]
        [SerializeField, Tooltip("巡邏 Root 可活動範圍的局部中心，隨 Zone／Chunk 旋轉。預設向上 6 公尺，搭配高度 12；需與出生盒重疊。不是敵人生成範圍。")]
        private Vector3 flyingPatrolBoxCenter = new Vector3(0f, 6f, 0f);
        [SerializeField, Tooltip("自動巡邏盒完整尺寸，局部公尺，各軸至少 0.1。預設 40×12×40；以敵人實際膠囊排除牆壁／屋頂，不保證盒內所有位置可飛。")]
        private Vector3 flyingPatrolBoxSize = new Vector3(40f, 12f, 40f);
        [SerializeField, Range(3, 6), Tooltip("每軸取樣格數，預設 5，範圍 3～6；另取出生盒 27 個候選。增加可找更窄通道，但提高每輪首次規劃成本。")]
        private int flyingPatrolSamplesPerAxis = 5;
        [SerializeField, Range(2, 64), Tooltip("每種飛行 Prefab 最多保留的飛行巡邏點，預設 16、範圍 2～64；至少需找到 2 個相通節點，否則排除此飛行候選。")]
        private int flyingPatrolMaximumPoints = 16;
        [SerializeField, Min(0.1f), Tooltip("巡邏點最小世界距離，預設 4 公尺、至少 0.1；也會大於候選敵人的巡邏抵達距離，避免節點太近。")]
        private float flyingPatrolMinimumSpacing = 4f;

        [Header("預覽")]
        [SerializeField, Tooltip("只影響 Scene／Prefab 視窗的出生盒預覽。")]
        private Color gizmoColor = new Color(1f, 0.2f, 0.45f, 0.7f);

        public bool UsesEnemyEntries => enemyEntries != null && enemyEntries.Length > 0;
        public IReadOnlyList<EnemySpawnEntry> EnemyEntries => enemyEntries;

        public bool RequireSpawnOcclusion => requireSpawnOcclusion;
        public EnemyLocomotionKind LocomotionKind => locomotionKind;
        public EnemyPatrolArea FlyingPatrolArea => flyingPatrolArea;
        public int EnemiesPerActivation => enemiesPerActivation;
        public IReadOnlyList<NetworkPrefabRef> EnemyPrefabs => enemyPrefabs;
        public bool UsesAutomaticFlyingPatrol => (UsesEnemyEntries || locomotionKind == EnemyLocomotionKind.FreeFlying) && flyingPatrolArea == null;
        public Bounds SpawnLocalBounds => new Bounds(spawnBoxCenter, spawnBoxSize);
        public Bounds FlyingPatrolLocalBounds => new Bounds(flyingPatrolBoxCenter, flyingPatrolBoxSize);
        public int FlyingPatrolSamplesPerAxis => Mathf.Clamp(flyingPatrolSamplesPerAxis, 3, 6);
        public int FlyingPatrolMaximumPoints => Mathf.Clamp(flyingPatrolMaximumPoints, 2, 64);
        public float FlyingPatrolMinimumSpacing => Mathf.Max(0.1f, flyingPatrolMinimumSpacing);

        /// <summary>定點區免導航；地面／飛行區保留同 Chunk 的有效巡邏點要求。</summary>
        public bool IsPatrolConfigurationValid(EnemyPatrolArea area, MapChunk owner)
        {
            if (locomotionKind == EnemyLocomotionKind.Stationary) return true;
            if (locomotionKind != EnemyLocomotionKind.Ground &&
                locomotionKind != EnemyLocomotionKind.FreeFlying) return false;
            return owner != null && area != null && area.PointCount > 0 &&
                area.GetComponentInParent<MapChunk>() == owner;
        }

        public EnemySpawnApproachGate[] GetGates()
        {
            EnemySpawnApproachGate[] all = GetComponentsInChildren<EnemySpawnApproachGate>(true);
            return Array.FindAll(all, gate =>
                gate != null && gate.GetComponentInParent<EnemySpawnZone>() == this);
        }

        public bool TryChoosePrefab(out NetworkPrefabRef prefab)
        {
            prefab = default;
            if (enemyPrefabs == null || enemyPrefabs.Length == 0)
                return false;

            int start = UnityEngine.Random.Range(0, enemyPrefabs.Length);
            for (int offset = 0; offset < enemyPrefabs.Length; offset++)
            {
                NetworkPrefabRef candidate = enemyPrefabs[(start + offset) % enemyPrefabs.Length];
                if (!candidate.IsValid) continue;
                prefab = candidate;
                return true;
            }
            return false;
        }

        public Vector3 RandomPoint() => RandomPoint(locomotionKind);

        public Vector3 RandomPoint(EnemyLocomotionKind kind)
        {
            // 所有種類共用盒內散布；kind overload 保留既有呼叫端相容性。
            Vector3 half = spawnBoxSize * 0.5f;
            return transform.TransformPoint(spawnBoxCenter + new Vector3(
                UnityEngine.Random.Range(-half.x, half.x),
                UnityEngine.Random.Range(-half.y, half.y),
                UnityEngine.Random.Range(-half.z, half.z)));
        }

        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint) - spawnBoxCenter;
            Vector3 half = spawnBoxSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x &&
                   Mathf.Abs(local.y) <= half.y &&
                   Mathf.Abs(local.z) <= half.z;
        }

        private void OnValidate()
        {
            spawnBoxSize.x = Mathf.Max(0.1f, spawnBoxSize.x);
            spawnBoxSize.y = Mathf.Max(0.1f, spawnBoxSize.y);
            spawnBoxSize.z = Mathf.Max(0.1f, spawnBoxSize.z);
            enemiesPerActivation = Mathf.Max(1, enemiesPerActivation);
            flyingPatrolBoxSize = Vector3.Max(Vector3.one * 0.1f, flyingPatrolBoxSize);
            flyingPatrolSamplesPerAxis = Mathf.Clamp(flyingPatrolSamplesPerAxis, 3, 6);
            flyingPatrolMaximumPoints = Mathf.Clamp(flyingPatrolMaximumPoints, 2, 64);
            flyingPatrolMinimumSpacing = Mathf.Max(0.1f, flyingPatrolMinimumSpacing);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(spawnBoxCenter, spawnBoxSize);
            if (UsesAutomaticFlyingPatrol)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(flyingPatrolBoxCenter, flyingPatrolBoxSize);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }

    /// <summary>一次啟動各自擲骰；數量不參與機率正規化。</summary>
    [Serializable]
    public sealed class EnemySpawnEntry
    {
        [SerializeField, InspectorName("敵人 Prefab"), Tooltip("已登記於 Fusion 的 Enemy Network Prefab；移動類型由 EnemyDefinition 自動辨識。")]
        private NetworkPrefabRef prefab;
        [SerializeField, Range(0f, 100f), InspectorName("出現機率 (%)"), Tooltip("每次區域啟動獨立判定一次。0 永不出現，100 必定抽中；不需總和等於 100。抽中不代表能略過預算或出生安全檢查。")]
        private float chancePercent = 100f;
        [SerializeField, Min(1), InspectorName("抽中生成數量"), Tooltip("抽中後要求生成的隻數，至少 1。沿 Director 間隔逐隻生成；未通過安全檢查、達到預算或啟動期限時，剩餘數量取消、不補發。")]
        private int count = 1;

        public NetworkPrefabRef Prefab => prefab;
        public float ChancePercent => float.IsNaN(chancePercent) ? 0f : Mathf.Clamp(chancePercent, 0f, 100f);
        public int Count => Mathf.Max(1, count);
        public bool IsEnabled => prefab.IsValid && ChancePercent > 0f;

        public EnemySpawnEntry() { }
        public EnemySpawnEntry(NetworkPrefabRef prefab, float chancePercent, int count)
        {
            this.prefab = prefab;
            this.chancePercent = chancePercent;
            this.count = count;
        }

        public int RollCount(float sample01)
        {
            if (!IsEnabled || float.IsNaN(sample01) || sample01 < 0f || sample01 > 1f) return 0;
            return ChancePercent >= 100f || sample01 < ChancePercent / 100f ? Count : 0;
        }
    }
}
