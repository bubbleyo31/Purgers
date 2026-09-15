/// <summary>
/// 可以直接掛在任一 Profession Runtime Root 上的共用 Gameplay Module。
///
/// <para>
/// <see cref="PlayerProfessionRuntime"/> 會自動尋找此介面、綁定真正的
/// Owner Player，並在職業專屬 Driver 以前傳入當 Tick 的網路輸入。
/// 因此共用能力不需要再分別寫進 Attack、Tank、Support Driver。
/// </para>
///
/// <para>
/// 模組仍只會隨「目前正式職業 Runtime」執行；把元件掛到哪一個
/// Runtime Prefab，就代表哪一個職業擁有它。
/// </para>
/// </summary>
public interface IPlayerProfessionRuntimeModule
{
    /// <summary>
    /// Runtime 取得正式 Owner Player 後呼叫一次。
    /// 模組應從 Player Core 取得共用移動、狀態與 Action Gate 等依賴。
    /// </summary>
    void BindOwnerPlayer(
        Player ownerPlayer
    );


    /// <summary>
    /// 每個 Fusion Tick 在職業專屬 Driver 以前執行。
    /// </summary>
    void Simulate(
        NetInput input
    );
}
