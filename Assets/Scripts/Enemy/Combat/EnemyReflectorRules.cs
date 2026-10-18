using UnityEngine;

/// <summary>反彈護盾敵人的純規則；不寫入生命、移動或網路狀態。</summary>
public static class EnemyReflectorRules
{
    public static bool ShouldHome(bool targetValid, bool reflected, float distance, float triggerDistance)
        => targetValid && !reflected && distance <= Mathf.Max(0f, triggerDistance);

    public static Vector3 Turn(Vector3 direction, Vector3 toward, float degreesPerSecond, float deltaTime)
    {
        if (toward.sqrMagnitude < .000001f) return direction;
        return Vector3.RotateTowards(direction.normalized, toward.normalized,
            Mathf.Max(0f, degreesPerSecond) * Mathf.Deg2Rad * Mathf.Max(0f, deltaTime), 0f).normalized;
    }

    // HitDirection 是傷害向目標飛行的方向，從背後射入時與敵人 forward 同向。
    public static bool AllowsDamage(bool opening, Vector3 forward, Vector3 incoming, float rearFullAngle)
    {
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        incoming = Vector3.ProjectOnPlane(incoming, Vector3.up);
        return opening && forward.sqrMagnitude > .000001f && incoming.sqrMagnitude > .000001f &&
            Vector3.Angle(forward, incoming) < Mathf.Clamp(rearFullAngle, 1f, 180f) * .5f;
    }
    public static bool WithinMeleeReflection(Vector3 delta,Vector3 forward,float range,float searchRadius,float angle,float padding)
    {
        padding=Mathf.Max(0,padding);float distance=delta.magnitude;
        float angularPadding=distance>padding?Mathf.Asin(Mathf.Clamp01(padding/distance))*Mathf.Rad2Deg:180f;
        return distance<=range+padding && (delta-forward*range*.5f).magnitude<=searchRadius+padding &&
            Vector3.Angle(forward,delta)<=angle*.5f+angularPadding;
    }
    public static bool IntersectsSphereSegment(Vector3 start,Vector3 end,Vector3 center,float radius,out float distance)
    {
        distance=0;radius=Mathf.Max(0,radius);Vector3 offset=start-center,segment=end-start;float length=segment.magnitude;
        float c=offset.sqrMagnitude-radius*radius;if(c<=0)return true;if(length<.000001f)return false;
        float b=Vector3.Dot(offset,segment/length),discriminant=b*b-c;if(discriminant<0)return false;
        distance=-b-Mathf.Sqrt(discriminant);return distance>=0&&distance<=length;
    }
}
