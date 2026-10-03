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
        [SerializeField, Tooltip("出生盒中心相對此 Transform 的局部偏移；Transform 隨 MapChunk 一起旋轉。Stationary 定點怪只在此中心出生，不隨機散布或自動貼地，請自行對齊地板／平台。")]
        private Vector3 spawnBoxCenter;

        [SerializeField, Tooltip("出生盒的 X／Y／Z 完整尺寸，公尺。地面敵人的取樣點仍須投影到該 Chunk 的預烘焙 NavMesh。")]
        private Vector3 spawnBoxSize = new Vector3(12f, 4f, 12f);

        [Header("生成條件")]
        [SerializeField, InspectorName("生成需要視野遮擋"), Tooltip(
            "預設開啟：出生點必須對所有存活玩家都有實體遮擋才可生成。\n" +
            "測試時可關閉，允許玩家直接看見怪物生成；只略過出生視線遮擋。\n" +
            "最小玩家距離、占用、導航、前方、預算與門檻／輪替限制仍有效；落後清理仍要求遮擋。")]
        private bool requireSpawnOcclusion = true;

        [Header("此區域的敵人")]
        [SerializeField, Tooltip("此區域只放同一移動類型的 Network Prefab。Ground 使用該 Chunk 的 Runtime Ground Patrol Area；Free Flying 未指定人工 Patrol Area 時，自動規劃下方立體巡邏盒。Stationary 不需要任何巡邏區或導航，使用出生盒中心。")]
        private EnemyLocomotionKind locomotionKind = EnemyLocomotionKind.Ground;

        [SerializeField, Tooltip("可隨機選用的正式 Enemy Network Prefab；必須在 Fusion Prefab Table 登記，且 EnemyDefinition 的移動類型符合上方設定。")]
        private NetworkPrefabRef[] enemyPrefabs = Array.Empty<NetworkPrefabRef>();

        [SerializeField, Tooltip("只有 Free Flying 使用。留空由 Host 自動生成飛行巡邏區；有指定時沿用同一 MapChunk 內的人工 EnemyPatrolArea。Ground 與 Stationary 忽略此欄位。")]
        private EnemyPatrolArea flyingPatrolArea;

        [SerializeField, Min(1), Tooltip("一次成功啟動最多生成幾隻；Director 會逐隻間隔生成，玩家離開或條件失效時不補發剩餘數量。")]
        private int enemiesPerActivation = 2;

        [Header("自動飛行巡邏（Free Flying 且人工 Area 留空時使用）")]
        [SerializeField, Tooltip("巡邏 Root 可活動範圍的局部中心，隨 Zone／Chunk 旋轉。預設向上 6 公尺，搭配高度 12；需與出生盒重疊。不是敵人生成範圍。")]
        private Vector3 flyingPatrolBoxCenter = new Vector3(0f, 6f, 0f);
        [SerializeField, Tooltip("自動巡邏盒完整尺寸，局部公尺，各軸至少 0.1。預設 40×12×40；以敵人實際膠囊排除牆壁／屋頂，不保證盒內所有位置可飛。")]
        private Vector3 flyingPatrolBoxSize = new Vector3(40f, 12f, 40f);
        [SerializeField, Range(3, 6), Tooltip("每軸取樣格數，預設 5，範圍 3～6；另取出生盒 27 個候選。增加可找更窄通道，但提高每輪首次規劃成本。")]
        private int flyingPatrolSamplesPerAxis = 5;
        [SerializeField, Range(2, 64), Tooltip("最多保留的飛行巡邏點，預設 16、範圍 2～64；至少需找到 2 個相通節點，否則拒絕此區。")]
        private int flyingPatrolMaximumPoints = 16;
        [SerializeField, Min(0.1f), Tooltip("巡邏點最小世界距離，預設 4 公尺、至少 0.1；也會大於候選敵人的巡邏抵達距離，避免節點太近。")]
        private float flyingPatrolMinimumSpacing = 4f;

        [Header("預覽")]
        [SerializeField, Tooltip("只影響 Scene／Prefab 視窗的出生盒預覽。")]
        private Color gizmoColor = new Color(1f, 0.2f, 0.45f, 0.7f);

        public bool RequireSpawnOcclusion => requireSpawnOcclusion;
        public EnemyLocomotionKind LocomotionKind => locomotionKind;
        public EnemyPatrolArea FlyingPatrolArea => flyingPatrolArea;
        public int EnemiesPerActivation => enemiesPerActivation;
        public IReadOnlyList<NetworkPrefabRef> EnemyPrefabs => enemyPrefabs;
        public bool UsesAutomaticFlyingPatrol => locomotionKind == EnemyLocomotionKind.FreeFlying && flyingPatrolArea == null;
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

        public Vector3 RandomPoint()
        {
            // 保留既有 API；定點分支使用人工中心，不能抽到平台外或任意高度。
            if (locomotionKind == EnemyLocomotionKind.Stationary)
                return transform.TransformPoint(spawnBoxCenter);
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
            if (locomotionKind == EnemyLocomotionKind.Stationary)
            {
                Gizmos.DrawWireSphere(spawnBoxCenter, 0.35f);
                Gizmos.DrawRay(spawnBoxCenter, Vector3.forward * 2f);
            }
            else Gizmos.DrawWireCube(spawnBoxCenter, spawnBoxSize);
            if (UsesAutomaticFlyingPatrol)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(flyingPatrolBoxCenter, flyingPatrolBoxSize);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
