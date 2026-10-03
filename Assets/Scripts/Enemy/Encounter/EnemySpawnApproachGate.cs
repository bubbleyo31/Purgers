using UnityEngine;

namespace Purgers.Enemy.Encounter
{
    /// <summary>無 Collider 的定向門檻；預設單向，可選雙向穿越並回傳本次進入方向。</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnApproachGate : MonoBehaviour
    {
        [Header("啟動門檻（局部平面）")]
        [SerializeField, Min(0.1f), Tooltip("門檻左右寬度，公尺；Transform.forward 必須朝向預期的怪物出生區。")]
        private float width = 12f;

        [SerializeField, Min(0.1f), Tooltip("門檻上下高度，公尺；高速玩家跨越時會用前後兩個 Fusion Tick 位置做線段穿越判定。")]
        private float height = 8f;

        [SerializeField, Tooltip("只影響 Scene／Prefab 視窗的門檻預覽，不參與物理觸發。")]
        private Color gizmoColor = new Color(1f, 0.75f, 0.1f, 0.8f);

        [SerializeField, InspectorName("雙向穿越"), Tooltip("預設關閉。開啟後，正反兩面穿越都可提出生成機會；前方檢查依本次穿越方向，反向使用 -Transform.forward。仍受遮擋、預算與同一 Zone 輪替鎖定限制。出生盒需在該方向前方有合法點。")]
        private bool bidirectional;

        public bool TryCross(Vector3 previous, Vector3 current, out float fraction) =>
            EnemyEncounterRules.TryCrossGate(
                Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one),
                previous,
                current,
                width,
                height,
                out fraction,
                bidirectional);

        public bool TryCross(Vector3 previous, Vector3 current, out float fraction, out Vector3 approachForward)
        {
            approachForward = Vector3.zero;
            if (!TryCross(previous, current, out fraction)) return false;
            approachForward = Vector3.Dot(current - previous, transform.forward) >= 0f
                ? transform.forward : -transform.forward;
            return true;
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.1f, width);
            height = Mathf.Max(0.1f, height);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = gizmoColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, 0.02f));
            Gizmos.DrawRay(Vector3.zero, Vector3.forward * 3f);
            if (bidirectional) Gizmos.DrawRay(Vector3.zero, Vector3.back * 3f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
