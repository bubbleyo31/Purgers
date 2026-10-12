using System;
using Fusion;

/// <summary>離手武器的 F 回充快照；使用同一 Runner 的模擬時間，不受 Runtime 銷毀影響。</summary>
public struct WeaponQuickActionReserve : INetworkStruct
{
    public int Charges;
    public int Capacity;
    public double NextChargeAt;
    public double RecoveryEndsAt;
    public float RechargeSeconds;

    public WeaponQuickActionReserve Advance(double now)
    {
        var result = this;
        result.Charges = Math.Max(0, Math.Min(Capacity, Charges));
        if (result.Charges < Capacity && now >= NextChargeAt)
        {
            int restored = RechargeSeconds > 0
                ? (int)Math.Min(Capacity, 1 + Math.Floor((now - NextChargeAt) / RechargeSeconds))
                : Capacity;
            result.Charges = Math.Min(Capacity, result.Charges + restored);
            result.NextChargeAt += restored * RechargeSeconds;
        }
        return result;
    }
}
