using System.Collections.Generic;
using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    /// <summary>只處理幾何、地板取樣與輪替規則，不決定網路結果。</summary>
    public static class EnemyEncounterRules
    {
        public static bool TryProjectGroundSpawn(
            PhysicsScene physicsScene,
            Vector3 navPosition,
            LayerMask groundMask,
            float maximumGroundOffset,
            out Vector3 groundedPosition)
        {
            groundedPosition = navPosition;
            if (!physicsScene.IsValid() || groundMask.value == 0 || maximumGroundOffset <= 0f)
                return false;

            if (!physicsScene.Raycast(
                    navPosition + Vector3.up * 0.1f,
                    Vector3.down,
                    out RaycastHit support,
                    maximumGroundOffset + 0.1f,
                    groundMask,
                    QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(navPosition.y - support.point.y) > maximumGroundOffset)
                return false;

            groundedPosition.y = support.point.y;
            return true;
        }

        public static bool TryCrossGate(
            Matrix4x4 gateLocalToWorld,
            Vector3 previousWorld,
            Vector3 currentWorld,
            float width,
            float height,
            out float crossingFraction,
            bool bidirectional = false)
        {
            crossingFraction = 0f;
            if (width <= 0f || height <= 0f)
                return false;

            Matrix4x4 worldToGate = gateLocalToWorld.inverse;
            Vector3 previous = worldToGate.MultiplyPoint3x4(previousWorld);
            Vector3 current = worldToGate.MultiplyPoint3x4(currentWorld);
            bool forward = previous.z < -0.0001f && current.z >= 0f;
            bool reverse = bidirectional && previous.z > 0.0001f && current.z <= 0f;
            if (!forward && !reverse)
                return false;

            float denominator = current.z - previous.z;
            if (Mathf.Abs(denominator) <= 0.0001f)
                return false;

            crossingFraction = -previous.z / denominator;
            Vector3 point = Vector3.Lerp(previous, current, crossingFraction);
            return Mathf.Abs(point.x) <= width * 0.5f &&
                   Mathf.Abs(point.y) <= height * 0.5f;
        }
    }

    /// <summary>最近成功生成區域的固定容量 FIFO。失敗的嘗試不呼叫 MarkSuccessful。</summary>
    public sealed class RecentZoneLock<T>
    {
        private readonly int capacity;
        private readonly Queue<T> order = new Queue<T>();
        private readonly HashSet<T> locked = new HashSet<T>();

        public RecentZoneLock(int capacity)
        {
            this.capacity = Mathf.Max(1, capacity);
        }

        public bool IsLocked(T zone) => locked.Contains(zone);

        public T MarkSuccessful(T zone)
        {
            if (!locked.Add(zone))
                return default;

            T released = default;
            if (order.Count >= capacity)
            {
                released = order.Dequeue();
                locked.Remove(released);
            }

            order.Enqueue(zone);
            return released;
        }
    }
}
