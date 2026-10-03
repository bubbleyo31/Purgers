using Fusion;
using UnityEngine;

namespace Purgers.GameFlow.Stage
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossStageObjectiveController : NetworkBehaviour
    {
        [Header("Boss 權威生成")]
        [SerializeField, Tooltip(
            "Boss 的 Fusion Network Prefab。必須包含 TestDamageReceiver；" +
            "State Authority 只會在規格目標為 DefeatBoss 時生成一次。")]
        private NetworkPrefabRef bossPrefab;

        [Networked] public NetworkId BossObjectId { get; private set; }
        [Networked] public NetworkBool BossDefeated { get; private set; }
        [Networked] public NetworkBool ObjectiveReady { get; private set; }

        private TestDamageReceiver bossHealth;
        private bool preparationAttempted;

        public bool TryPrepare(
            StageRuntimePlan plan,
            MapChunk bossChunk)
        {
            if (!Object.HasStateAuthority ||
                !Runner.IsServer ||
                plan.ObjectiveKind != StageObjectiveKind.DefeatBoss)
            {
                return false;
            }

            if (preparationAttempted)
                return ObjectiveReady;

            preparationAttempted = true;

            if (!bossPrefab.IsValid || bossChunk == null)
            {
                Debug.LogError(
                    "[BossObjective] 缺少有效 Boss Network Prefab 或 Boss Chunk。",
                    this);
                return false;
            }

            BossSpawnPoint[] markers =
                bossChunk.GetComponentsInChildren<BossSpawnPoint>(true);
            BossSpawnPoint selectedMarker = null;

            for (int index = 0; index < markers.Length; index++)
            {
                BossSpawnPoint marker = markers[index];
                if (marker == null || !marker.isActiveAndEnabled ||
                    !marker.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (selectedMarker != null)
                {
                    Debug.LogError(
                        "[BossObjective] Boss Chunk 必須剛好有一個啟用的 BossSpawnPoint。",
                        bossChunk);
                    return false;
                }

                selectedMarker = marker;
            }

            if (selectedMarker == null)
            {
                Debug.LogError(
                    "[BossObjective] Boss Chunk 沒有啟用的 BossSpawnPoint。",
                    bossChunk);
                return false;
            }

            NetworkObject spawned = Runner.Spawn(
                bossPrefab,
                selectedMarker.transform.position,
                selectedMarker.transform.rotation,
                PlayerRef.None);

            if (spawned == null)
            {
                Debug.LogError(
                    "[BossObjective] Runner.Spawn 沒有回傳 Boss NetworkObject。",
                    this);
                return false;
            }

            bossHealth = spawned.GetComponent<TestDamageReceiver>();
            if (bossHealth == null)
            {
                Runner.Despawn(spawned);
                Debug.LogError(
                    "[BossObjective] Boss Prefab 缺少正式生命元件 TestDamageReceiver。",
                    this);
                return false;
            }

            bossHealth.Died += HandleBossDied;
            BossObjectId = spawned.Id;
            BossDefeated = bossHealth.IsDead;
            ObjectiveReady = true;
            return true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !ObjectiveReady || BossDefeated)
                return;

            if (bossHealth != null && bossHealth.IsDead)
                BossDefeated = true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            UnsubscribeBossHealth();
            preparationAttempted = false;
        }

        private void HandleBossDied(CombatDeathEventData deathData)
        {
            if (Object != null && Object.HasStateAuthority)
                BossDefeated = true;
        }

        private void UnsubscribeBossHealth()
        {
            if (bossHealth != null)
                bossHealth.Died -= HandleBossDied;

            bossHealth = null;
        }
    }
}
