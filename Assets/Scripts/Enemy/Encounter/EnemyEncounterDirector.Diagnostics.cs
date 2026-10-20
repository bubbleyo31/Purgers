#if UNITY_EDITOR
using Purgers.GameFlow.Stage;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    public sealed partial class EnemyEncounterDirector
    {
        /// <summary>Inspector 唯讀快照。不擲骰、收集玩家、推進計畫或執行出生。</summary>
        public string DescribeInspectorState(EnemySpawnZone zone, out int severity)
        {
            severity = 1;
            if (!Application.isPlaying) return "待 Play：目前只檢查作者配置，尚未取得 Host 的實際生成條件。";
            if (!isActiveAndEnabled) { severity = 2; return "Director 已停用，無法生成。"; }
            if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning)
                return "等待 Fusion Spawned／Runner 就緒。";
            if (!Object.HasStateAuthority || !Runner.IsServer)
                return "此處是 Client；生成判定只在 Host。請選取 Host 的實際 Zone 查看結果。";
            if (stageFlow == null || mapState == null || mapSelection == null ||
                stageFlow.Runner != Runner || mapState.Runner != Runner ||
                mapSelection.gameObject.scene != gameObject.scene)
            { severity = 2; return "引用無效：StageFlow／NetworkMapState／MapSelection 必須對應同一 Runner 與場景。"; }
            if (stageFlow.Object == null || !stageFlow.Object.IsValid || mapState.Object == null || !mapState.Object.IsValid)
                return "等待 StageFlow／NetworkMapState 的網路生命週期就緒。";
            if (stageFlow.Phase != StagePhase.Active || stageFlow.IsBossStage)
                return "目前不是普通關卡的 Active 階段，Encounter 暫停生成。";
            if (!zone.isActiveAndEnabled) { severity = 2; return "此 Zone 已停用。"; }
            if (!mapState.IsReady || mapSelection.Alignment == null) return "等待地圖與 Alignment 就緒。";
            if (!zonesBuilt) return "等待區域登記／地面導航就緒；若持續等待，請檢查 Chunk 的 NavMesh 與巡邏資料。";
            if (worldOcclusionMask.value == 0 || spawnOccupancyMask.value == 0)
            { severity = 2; return "World Occlusion Mask／Spawn Occupancy Mask 為空，拒絕所有生成。"; }
            ZoneRuntime runtime = zones.Find(item => item.Zone == zone);
            if (runtime == null)
            { severity = 2; return "此 Zone 未登記於這個 Director：檢查它是否為本輪實際 Chunk、啟用狀態、Gate 與候選配置。"; }
            if (runtime.Candidates.Count == 0)
            { severity = 2; return "沒有可用敵人候選（可能全為 0% 或已被排除）；請看配置檢查與下方事件。"; }
            int alive = CountAllAliveEnemies(), local = CountAliveInZone(zone);
            if (alive >= maximumAliveEnemies || local >= maximumAlivePerZone || ownedEnemies.Count >= maximumOwnedNetworkObjects)
            { severity = 2; return $"數量上限阻擋：全場 {alive}/{maximumAliveEnemies}、此區 {local}/{maximumAlivePerZone}、持有物件 {ownedEnemies.Count}/{maximumOwnedNetworkObjects}。"; }
            if (players.Count == 0) return "目前沒有可追蹤的存活玩家，等待玩家就緒。";
            if (pendingZone != null && pendingZone.Zone == zone)
                return $"此區分批生成中；剩餘期限 {Mathf.Max(0, pendingExpiresAt - Time.time):0.0} 秒，下一隻等待 {Mathf.Max(0, nextSpawnTime - Time.time):0.0} 秒。";
            if (recentZones != null && recentZones.IsLocked(zone))
                return "此區已在最近成功區域 FIFO 鎖定中；需其他區域成功後輪替解鎖。";
            if (pendingZone != null) return "其他區域正在分批生成，本區等待下一次有效穿越。";
            if (Time.time < nextSpawnTime) return $"全場生成間隔尚餘 {nextSpawnTime - Time.time:0.0} 秒；目前不接受新啟動。";
            return $"已登記 {runtime.Candidates.Count} 個候選，等待有效 Gate 穿越。出生位置、玩家距離與遮擋仍須當次取樣通過。";
        }
    }
}
#endif
