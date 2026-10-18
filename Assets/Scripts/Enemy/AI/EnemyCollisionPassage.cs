using UnityEngine;

/// <summary>沿用指定 PhysicsScene 的膠囊占用／掃掠，不查預設物理場景、不讀 Networked。</summary>
public static class EnemyCollisionPassage
{
    public static bool IsClear(PhysicsScene physics, Vector3 from, Vector3 to,
        EnemyCollisionEnvelope envelope, float skin, int obstacleMask, Collider[] overlapBuffer)
    {
        if (!physics.IsValid() || !envelope.IsValid || !EnemyCollisionEnvelope.Finite(skin) ||
            skin <= 0 || obstacleMask == 0 || overlapBuffer == null || overlapBuffer.Length == 0 ||
            !Finite(from) || !Finite(to)) return false;
        envelope.Endpoints(from, skin, out var bottom, out var top);
        if (physics.OverlapCapsule(bottom, top, envelope.Radius, overlapBuffer,
            obstacleMask, QueryTriggerInteraction.Ignore) > 0) return false;
        var delta = to - from;
        float distance = delta.magnitude;
        return distance < .00001f || !physics.CapsuleCast(bottom, top, envelope.Radius,
            delta / distance, out _, distance + skin, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private static bool Finite(Vector3 value) => EnemyCollisionEnvelope.Finite(value.x) &&
        EnemyCollisionEnvelope.Finite(value.y) && EnemyCollisionEnvelope.Finite(value.z);
}
