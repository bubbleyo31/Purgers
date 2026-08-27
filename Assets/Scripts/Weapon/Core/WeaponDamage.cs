/// <summary>
/// WeaponHitZone 使用的武器命中區域類型。
///
/// 目前仍保留這個 enum，
/// 因為 WeaponHitZone 已經在使用它。
///
/// 真正送進共用 DamageSystem 時，
/// AttackRifle 會將它轉換成 DamageHitZoneType。
/// </summary>
public enum WeaponHitZoneType : byte
{
    /// <summary>
    /// 一般身體。
    /// </summary>
    Body = 0,

    /// <summary>
    /// 頭部。
    /// </summary>
    Head = 1
}