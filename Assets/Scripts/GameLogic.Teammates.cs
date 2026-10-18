using Fusion;
using UnityEngine;

/// <summary>由既有持續存在的 GameLogic 保存；角色 Despawn 不會丟失辨識與死亡位置。</summary>
public struct NetworkTeammateIdentity : INetworkStruct
{
    public NetworkString<_32> Name;
    public int ColorSlot;
    public NetworkBool IsDead;
    public Vector3 DeathPosition;
    public TeamAbilityDisplaySnapshot LastAbilityIds; // 死亡顯示快照，不參與裝備／重生決策。
}

public partial class GameLogic
{
    [Networked, Capacity(TeammateDisplayRules.Capacity)]
    private NetworkDictionary<PlayerRef,NetworkTeammateIdentity> TeammateIdentities=>default;
    private bool teammateRosterSpawned;
    public bool IsTeammateRosterReady=>teammateRosterSpawned && Object!=null && Object.IsValid && IsPrimaryForRunner;

    public bool TryGetTeammateIdentity(PlayerRef player,out NetworkTeammateIdentity identity)
    {
        identity=default;
        return IsTeammateRosterReady && TeammateIdentities.TryGet(player,out identity);
    }
    private void EnsureTeammateIdentity(PlayerRef player)
    {
        if(!IsTeammateRosterReady || !Object.HasStateAuthority || !player.IsRealPlayer || TeammateIdentities.ContainsKey(player))return;
        int mask=0;
        foreach(var item in TeammateIdentities)mask|=1<<item.Value.ColorSlot;
        int slot=TeammateDisplayRules.FirstFreeSlot(mask);
        if(slot<0)return;
        TeammateIdentities.Add(player,new NetworkTeammateIdentity{Name="Player "+(player.PlayerId+1),ColorSlot=slot});
    }
    /// <summary>僅接收現有聊天名稱註冊流程已清理的名稱；沒有新增名稱 RPC 或 Client 寫入路徑。</summary>
    public bool SetTeammateNameStateAuthority(PlayerRef player,NetworkString<_32> name)
    {
        if(!IsTeammateRosterReady || !Object.HasStateAuthority || !Runner.IsPlayerValid(player))return false;
        EnsureTeammateIdentity(player);
        if(!TeammateIdentities.TryGet(player,out var value))return false;
        value.Name=name;TeammateIdentities.Set(player,value);return true;
    }
    private void SetTeammateLife(PlayerRef player,bool dead,Vector3 position)
    {
        if(!IsTeammateRosterReady || !Object.HasStateAuthority)return;
        EnsureTeammateIdentity(player);
        if(!TeammateIdentities.TryGet(player,out var value))return;
        value.IsDead=dead;value.DeathPosition=position;
        if (!dead) value.LastAbilityIds = default;
        else if (Players.TryGet(player,out var current) && current != null && current.AbilityRuntimeManager != null)
            value.LastAbilityIds = TeamAbilityDisplaySnapshot.Capture(current.AbilityRuntimeManager.GetEquippedAbilityIds());
        TeammateIdentities.Set(player,value);
    }
    public PlayerProfessionType ReadTeamWeapon(PlayerRef player) => IsTeammateRosterReady &&
        SavedProfessions.TryGet(player,out var profession) ? profession : PlayerProfessionType.None;

    public bool TryGetTeammateTarget(PlayerRef player,out NetworkObject target,out Vector3 position,out bool dead)
    {
        target=null;position=default;dead=false;
        if(!TryGetTeammateIdentity(player,out var value))return false;
        dead=value.IsDead;
        if(dead){position=value.DeathPosition;return true;}
        if(!Runner.TryGetPlayerObject(player,out target)||target==null||!target.IsValid)return false;
        position=target.transform.position;return true;
    }
}
