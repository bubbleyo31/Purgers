using System;
using Fusion;
using Purgers.Progression;

/// <summary>八槽只讀死亡顯示識別，64 bytes。不是裝備／存檔來源；避免長字串撐大 GameLogic。</summary>
public struct TeamAbilityDisplaySnapshot : INetworkStruct, IEquatable<TeamAbilityDisplaySnapshot>
{
    public const int Capacity=8;
    public ulong A, B, C, D, E, F, G, H;
    public ulong At(int slot)
    {
        switch(slot){case 0:return A;case 1:return B;case 2:return C;case 3:return D;case 4:return E;case 5:return F;case 6:return G;case 7:return H;default:return 0;}
    }
    public static ulong Token(string id)
    {
        if(string.IsNullOrEmpty(id))return 0;
        unchecked { ulong hash=14695981039346656037UL;foreach(char c in id){hash^=c;hash*=1099511628211UL;}return hash==0?1:hash; }
    }
    public static TeamAbilityDisplaySnapshot Capture(string[] ids)
    {
        var value=new TeamAbilityDisplaySnapshot();
        if(ids==null)return value;
        for(int i=0;i<Math.Min(Capacity,ids.Length);i++)
        {
            ulong id=Token(ids[i]);switch(i){case 0:value.A=id;break;case 1:value.B=id;break;case 2:value.C=id;break;case 3:value.D=id;break;case 4:value.E=id;break;case 5:value.F=id;break;case 6:value.G=id;break;case 7:value.H=id;break;}
        }
        return value;
    }
    public bool Contains(string id) {ulong token=Token(id);if(token==0)return false;for(int i=0;i<Capacity;i++)if(At(i)==token)return true;return false;}
    public PlayerAbilityDefinition Resolve(int slot,PlayerRewardCatalog catalog)
    {
        ulong token=At(slot);if(token==0||catalog==null)return null;PlayerAbilityDefinition match=null;
        foreach(var reward in catalog.Rewards)
        {
            var definition=reward!=null?reward.AbilityDefinition:null;
            if(definition==null||Token(definition.AbilityId)!=token)continue;
            // 碰撞不能顯示成另一技能；資產測試另檢查目前清單唯一性。
            if(match!=null&&match.AbilityId!=definition.AbilityId)return null;
            match=definition;
        }
        return match;
    }
    public bool Equals(TeamAbilityDisplaySnapshot value)=>A==value.A&&B==value.B&&C==value.C&&D==value.D&&E==value.E&&F==value.F&&G==value.G&&H==value.H;
}
