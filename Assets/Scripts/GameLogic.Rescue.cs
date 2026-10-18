using Fusion;
using Purgers.GameFlow.SafeHouse;
using UnityEngine;

public partial class GameLogic
{
    [Header("倒地與復活模式")]
    [SerializeField, Tooltip("預設開啟：安全屋外使用隊友救援，正式死亡等回安全屋才復活。關閉可供測試沿用原自動復活。安全屋永遠沿用自動復活。")]
    private bool useTeammateRescueOutsideSafeHouse = true;

    public bool UsesTeammateRescue => useTeammateRescueOutsideSafeHouse && !SafeHouseFlowController.IsActiveFor(Runner);

    private void TickTeamRescueStateAuthority()
    {
        if (!IsPrimaryForRunner) return;
        TickMapFallDeaths();
        // 先撤銷失效救援，再收集本 Tick 輸入，最後計時，避免玩家迭代順序造成多人加速。
        foreach (var pair in Players)
            if (pair.Value != null) pair.Value.Health?.ValidateRescuerStateAuthority();
        foreach (var pair in Players)
            if (pair.Value != null) pair.Value.Health?.TickRescueIntentStateAuthority();
        bool hasHealthyMember = false;
        foreach (PlayerRef connected in Runner.ActivePlayers)
            if (Runner.TryGetPlayerObject(connected, out var obj) && obj != null && obj.IsValid &&
                obj.TryGetComponent<PlayerHealth>(out var health) && (health.IsAlive || health.CanSelfRescue))
                hasHealthyMember = true;
        foreach (var pair in Players)
            if (pair.Value != null) pair.Value.Health?.TickDownedStateAuthority(hasHealthyMember);
    }
}
