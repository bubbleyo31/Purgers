using Fusion;
using UnityEngine;

/// <summary>
/// 已取得接收器且只套用一次來源倍率的命中快照。目標防禦、生命與結果通知留待結算。
/// 這不是第二套生命系統；每份快照只能由 DamageReceiverUtility 結算一次。
/// </summary>
public sealed class PreparedDamageSnapshot
{
    public DamageRequest Request { get; internal set; }
    public GameObject HitObject { get; }
    public GameObject TargetObject => TargetBehaviour != null ? TargetBehaviour.gameObject : null;
    public NetworkObject TargetNetworkObject { get; }
    public bool IsResolved { get; private set; }
    internal readonly IDamageReceiver Receiver;
    internal readonly MonoBehaviour TargetBehaviour;
    internal readonly MonoBehaviour[] SourceBehaviours;
    private readonly NetworkId targetId;
    private readonly bool hadNetworkObject;

    internal PreparedDamageSnapshot(GameObject hitObject, DamageRequest request,
        IDamageReceiver receiver, MonoBehaviour target, MonoBehaviour[] sources)
    {
        HitObject = hitObject;
        Request = request;
        Receiver = receiver;
        TargetBehaviour = target;
        SourceBehaviours = sources;
        TargetNetworkObject = target != null ? target.GetComponentInParent<NetworkObject>() : null;
        hadNetworkObject = TargetNetworkObject != null;
        targetId = TargetNetworkObject != null && TargetNetworkObject.IsValid
            ? TargetNetworkObject.Id : default;
    }

    internal bool HasValidTarget => HitObject != null && TargetBehaviour != null &&
        (!hadNetworkObject || (TargetNetworkObject != null && TargetNetworkObject.IsValid && TargetNetworkObject.Id == targetId));

    internal bool TryBeginResolution()
    {
        if (IsResolved) return false;
        IsResolved = true;
        return true;
    }
}
