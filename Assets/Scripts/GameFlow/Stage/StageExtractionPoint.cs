using UnityEngine;

namespace Purgers.GameFlow.Stage
{
    [DisallowMultipleComponent]
    public sealed class StageExtractionPoint : MonoBehaviour
    {
        [Tooltip(
            "同一個 Chunk 內不可重複的穩定 ID。Host 與 Client 會依此排序候選點。")]
        [SerializeField] private string pointId = "extraction_01";

        [Tooltip("玩家水平距離小於等於此值時視為進入撤離區。")]
        [SerializeField, Min(0.5f)] private float radius = 8f;

        [Tooltip("玩家與撤離點的垂直距離容許值。")]
        [SerializeField, Min(0.5f)] private float verticalTolerance = 6f;

        [Tooltip(
            "撤離點被本輪選中時才顯示的視覺物件。不可指定本元件所在物件本身。")]
        [SerializeField] private GameObject selectedVisualRoot;

        [Header("視覺輔助線")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color gizmoColor =
            new Color(0.1f, 0.95f, 0.75f, 0.65f);

        public string PointId => pointId;
        public float Radius => Mathf.Max(0.5f, radius);
        public MapChunk OwnerChunk => GetComponentInParent<MapChunk>();

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 center = transform.position;
            float verticalDistance =
                Mathf.Abs(worldPosition.y - center.y);

            if (verticalDistance > Mathf.Max(0.5f, verticalTolerance))
                return false;

            Vector2 horizontalDelta = new Vector2(
                worldPosition.x - center.x,
                worldPosition.z - center.z);

            float safeRadius = Radius;
            return horizontalDelta.sqrMagnitude <= safeRadius * safeRadius;
        }

        public void SetSelected(bool selected)
        {
            if (selectedVisualRoot &&
                selectedVisualRoot != gameObject &&
                selectedVisualRoot.activeSelf != selected)
            {
                selectedVisualRoot.SetActive(selected);
            }
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.5f, radius);
            verticalTolerance = Mathf.Max(0.5f, verticalTolerance);

            if (selectedVisualRoot == gameObject)
            {
                Debug.LogWarning(
                    "[StageExtractionPoint] Selected Visual Root 不可指定元件所在物件本身。",
                    this);
                selectedVisualRoot = null;
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            Color color = gizmoColor;
            color.a = Mathf.Clamp01(color.a);
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, Radius);

            Vector3 top = transform.position +
                          Vector3.up * verticalTolerance;
            Vector3 bottom = transform.position -
                             Vector3.up * verticalTolerance;
            Gizmos.DrawLine(top, bottom);
        }
    }
}
