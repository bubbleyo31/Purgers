using Fusion;


/// <summary>
/// 可由獨立 Player Ability Runtime 載入的共用能力模組。
/// </summary>
public interface IPlayerAbilityRuntimeModule :
    IPlayerAbilityCategorized
{
    /// <summary>
    /// 將能力綁定到真正 Player Core。
    /// </summary>
    void BindOwnerPlayer(
        Player ownerPlayer
    );


    /// <summary>
    /// 每個 Fusion Tick 接收玩家輸入。
    /// 不需要逐 Tick 執行的命中能力可以保持空實作。
    /// </summary>
    void SimulateAbility(
        NetInput input,
        NetworkButtons previousButtons
    );


    /// <summary>
    /// 目前職業因 Ability Definition 限制而不可使用時呼叫。
    /// Active 能力必須在這裡安全結束；不得清除裝備或重置冷卻。
    /// </summary>
    void SetProfessionAvailable(
        bool isAvailable
    );
}
