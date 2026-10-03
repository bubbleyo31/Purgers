using UnityEngine;

namespace Purgers.GameFlow.Stage
{
    [DisallowMultipleComponent]
    public sealed class BossSpawnPoint : MonoBehaviour
    {
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.15f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 1.25f);
            Gizmos.DrawLine(
                transform.position,
                transform.position + transform.forward * 2f);
        }
    }
}
