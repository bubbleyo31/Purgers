#if UNITY_EDITOR
using UnityEngine;

// 僅供 EditMode 測試建立暫時物件，不可掛入正式 Prefab。
public sealed class CombatRegressionProbe : MonoBehaviour,
    IDamageReceiver, IDamageRequestModifier,
    IOutgoingDamageModifier, IOutgoingDamageResultListener
{
    public bool BlockFully;
    public bool RejectDamage;
    public bool KillTarget;
    public float OutgoingMultiplier = 1f;
    public int ReceiveCount;
    public int NotificationCount;
    public DamageResult LastResult;

    public DamageResult ReceiveDamage(DamageRequest request)
    {
        ReceiveCount++;
        if (RejectDamage)
            return DamageResult.CreateRejected(request, gameObject, DamageRejectReason.Invulnerable);
        // 刻意省略 Request 和 Target，驗證共用入口會補齊結果。
        return new DamageResult
        {
            Accepted = true,
            AppliedDamage = request.RequestedDamage,
            KilledTarget = KillTarget
        };
    }

    public void ModifyDamageRequest(ref DamageRequest request)
    {
        if (!BlockFully) return;
        request.BlockedDamage += request.RequestedDamage;
        request.RequestedDamage = 0f;
    }

    public void ModifyOutgoingDamage(ref DamageRequest request)
    {
        request.RequestedDamage *= OutgoingMultiplier;
    }

    public void OnOutgoingDamageResolved(in DamageResult result)
    {
        NotificationCount++;
        LastResult = result;
    }
}
#endif
