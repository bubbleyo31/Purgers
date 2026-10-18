using System.Collections.Generic;
using Fusion;
using UnityEngine;

public sealed partial class EnemyReflectorShield
{
    [Header("玩家子彈穿越警覺")]
    [SerializeField, Tooltip("玩家有效射線穿過感應盒即送到既有警戒佇列，不需要扣血。只限此敵人；牆壁與較近的命中會截斷射線。")]
    private bool alertOnPassingPlayerShot = true;
    [SerializeField, Tooltip("以 HitboxRoot.Offset 為中心的本地半尺寸，公尺。預設左右 1.75、上下 2.25、前後 1.15；可在 Scene 選取時看紅色感應盒。")]
    private Vector3 passingShotHalfExtents = new Vector3(1.75f,2.25f,1.15f);
    [SerializeField, Tooltip("感應盒到身體間的牆壁遮擋 Layer，只勾世界實體，不勾 Enemy／Player。預設 Default。")]
    private LayerMask passingShotOcclusionMask = 1;
    private static readonly List<EnemyReflectorShield> ShotThreatTargets = new List<EnemyReflectorShield>();
    private Vector3 ShotThreatCenter => GetComponent<HitboxRoot>() != null ? GetComponent<HitboxRoot>().Offset : Vector3.up;
    private Bounds ShotThreatBounds => new Bounds(ShotThreatCenter,new Vector3(Mathf.Max(.01f,passingShotHalfExtents.x),Mathf.Max(.01f,passingShotHalfExtents.y),Mathf.Max(.01f,passingShotHalfExtents.z))*2);
    private void OnDestroy() { ShotThreatTargets.Remove(this); }
    public static void NotifyPlayerShotSegment(Player player,Vector3 start,Vector3 end)
    {
        if(player==null || player.Object==null || !player.Object.IsValid || !player.Object.HasStateAuthority)return;
        foreach(var shield in ShotThreatTargets)
        {
            if(shield==null || !shield.ready || !shield.alertOnPassingPlayerShot || shield.Object==null || !shield.Object.IsValid ||
                !shield.Object.HasStateAuthority || shield.Runner!=player.Runner || !shield.actor.IsAlive)continue;
            Vector3 localStart=shield.transform.InverseTransformPoint(start),localEnd=shield.transform.InverseTransformPoint(end);
            Vector3 delta=localEnd-localStart;var bounds=shield.ShotThreatBounds;
            if(delta.sqrMagnitude<.000001f)continue;
            if(!bounds.Contains(localStart) && (!bounds.IntersectRay(new Ray(localStart,delta.normalized),out float hitDistance) || hitDistance>delta.magnitude))continue;
            Vector3 center=shield.transform.TransformPoint(shield.ShotThreatCenter);
            Vector3 segment=end-start;float t=Mathf.Clamp01(Vector3.Dot(center-start,segment)/segment.sqrMagnitude);
            Vector3 closest=start+segment*t,toward=center-closest;
            if(toward.magnitude>.001f && shield.Runner.GetPhysicsScene().Raycast(closest,toward.normalized,out _,toward.magnitude,shield.passingShotOcclusionMask,QueryTriggerInteraction.Ignore))continue;
            shield.GetComponent<EnemyAwarenessBrain>()?.QueuePlayerShotThreat(player.Object);
        }
    }
    private void OnDrawGizmosSelected()
    {
        if(!alertOnPassingPlayerShot)return;
        var matrix=Gizmos.matrix;var color=Gizmos.color;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.color=Color.red;
        var bounds=ShotThreatBounds;Gizmos.DrawWireCube(bounds.center,bounds.size);Gizmos.matrix=matrix;Gizmos.color=color;
    }
}
