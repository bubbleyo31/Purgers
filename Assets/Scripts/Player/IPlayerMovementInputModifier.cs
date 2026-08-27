/// <summary>
/// Profession Runtime 可以提供的
/// 共用玩家移動輸入倍率接口。
///
/// ====================================================================
///
/// Player Core 不應該知道：
///
/// TankGuardAbility
/// TankSlowAbility
/// SupportAbility
///
/// 等具體職業類別。
///
/// ====================================================================
///
/// Player Core 只會詢問目前 Profession Runtime：
///
/// 「你現在希望玩家保留多少移動輸入？」
///
/// ====================================================================
///
/// 1：
/// 完整移動。
///
/// 0.3：
/// 只保留 30% 移動。
///
/// 0：
/// 完全不允許普通 WASD 移動。
///
/// ====================================================================
///
/// 多個 Modifier 同時存在時，
/// PlayerProfessionRuntimeManager
/// 會把所有倍率相乘。
/// </summary>
public interface IPlayerMovementInputModifier
{
    /// <summary>
    /// 取得目前這個 Tick
    /// 希望套用到 PlayerMovement 的移動輸入倍率。
    ///
    /// NetInput 會一起傳入，
    /// 讓像 Tank Guard 這種 Hold Ability
    /// 可以在右鍵剛按下的同一個 Tick
    /// 就開始降低移動速度。
    /// </summary>
    float GetMovementInputMultiplier(
        NetInput input
    );
}