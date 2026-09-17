using UnityEngine;

public enum ConnectorSide
{
    North = 0,
    East = 1,
    South = 2,
    West = 3
}

public enum ConnectorPrototypeRole
{
    None = 0,
    Entrance = 1,
    Exit = 2
}

[DisallowMultipleComponent]
public sealed class MapConnector : MonoBehaviour
{
    [Tooltip(
        "此 Connector 在 Map Chunk 局部座標中的固定方向。" +
        "這不是目前流程中的入口或出口角色。" +
        "Connector 的 Transform.forward 必須朝向地圖外側。")]
    [SerializeField]
    private ConnectorSide side;

    [Header("Prototype Runtime Anchors")]

    [Tooltip(
        "玩家從此 Connector 出生時使用的位置與朝向。" +
        "請放在地圖內側，Transform.forward 應朝向地圖內部。")]
    [SerializeField]
    private Transform playerSpawnPoint;

    [Tooltip(
        "此 Connector 未與另一個 Chunk 接合時，阻擋屋 Prefab 使用的位置與朝向。" +
        "留空時使用 Connector 本身；若建築 Pivot 或朝向不同，建議建立子物件作為專用錨點。")]
    [SerializeField]
    private Transform blockerSpawnPoint;

    [Tooltip(
        "阻擋屋相對於 Blocker Spawn Point 的局部位置偏移。" +
        "X 是錨點右方、Y 是上方、Z 是錨點前方；" +
        "因此 Chunk 旋轉後仍會維持一致的局部方向語意。")]
    [SerializeField]
    private Vector3 blockerLocalPositionOffset;

    [Header("Blocker Gizmos")]

    [Tooltip(
        "在 Scene 與 Prefab 編輯視窗顯示阻擋屋的實際生成位置、預覽範圍與朝向。")]
    [SerializeField]
    private bool drawBlockerGizmo = true;

    [Tooltip(
        "阻擋屋 Gizmo 預覽框尺寸，只影響可視化，不會縮放實際 Prefab。")]
    [SerializeField]
    private Vector3 blockerGizmoSize =
        new Vector3(2f, 2f, 2f);

    [Tooltip(
        "阻擋屋生成位置、偏移連線與朝向使用的 Gizmo 顏色。")]
    [SerializeField]
    private Color blockerGizmoColor =
        new Color(1f, 0.55f, 0f, 1f);

    [Tooltip(
        "此 Connector 被選為入口時顯示的物件。" +
        "建議使用綠色、不含碰撞器的簡單標記。")]
    [SerializeField]
    private GameObject entrancePreview;

    [Tooltip(
        "此 Connector 被選為出口時顯示的物件。" +
        "建議使用紅色或橘色、不含碰撞器的簡單標記。")]
    [SerializeField]
    private GameObject exitPreview;

    public Transform PlayerSpawnPoint => playerSpawnPoint;

    public Transform BlockerSpawnPoint =>
        blockerSpawnPoint != null
            ? blockerSpawnPoint
            : transform;

    public Pose BlockerSpawnPose
    {
        get
        {
            Transform anchor = BlockerSpawnPoint;

            return new Pose(
                anchor.TransformPoint(
                    blockerLocalPositionOffset),
                anchor.rotation);
        }
    }

    public ConnectorSide Side => side;

    public void SetPrototypeRole(ConnectorPrototypeRole role)
    {
        if (entrancePreview != null)
        {
            entrancePreview.SetActive(
                role == ConnectorPrototypeRole.Entrance);
        }

        if (exitPreview != null)
        {
            exitPreview.SetActive(
                role == ConnectorPrototypeRole.Exit);
        }
    }

    private void OnDrawGizmos()
    {
        // Connector：青色，Forward 朝地圖外側。
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.2f);
        Gizmos.DrawRay(
            transform.position,
            transform.forward * 2f);

        if (playerSpawnPoint != null)
        {
            // Spawn Point：黃色，Forward 朝地圖內側。
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(
                playerSpawnPoint.position,
                0.15f);

            Gizmos.DrawRay(
                playerSpawnPoint.position,
                playerSpawnPoint.forward * 1.5f);
        }

        DrawBlockerGizmo();
    }

    private void DrawBlockerGizmo()
    {
        if (!drawBlockerGizmo)
            return;

        Transform anchor = BlockerSpawnPoint;
        Pose pose = BlockerSpawnPose;
        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;

        Gizmos.color = blockerGizmoColor;
        Gizmos.DrawLine(
            anchor.position,
            pose.position);

        Gizmos.DrawSphere(
            pose.position,
            0.15f);

        Gizmos.DrawRay(
            pose.position,
            pose.rotation * Vector3.forward * 1.5f);

        Gizmos.matrix = Matrix4x4.TRS(
            pose.position,
            pose.rotation,
            Vector3.one);

        Gizmos.DrawWireCube(
            Vector3.zero,
            blockerGizmoSize);

        Gizmos.matrix = previousMatrix;

#if UNITY_EDITOR
        UnityEditor.Handles.color =
            blockerGizmoColor;

        UnityEditor.Handles.Label(
            pose.position + Vector3.up *
            (blockerGizmoSize.y * 0.5f + 0.2f),
            $"Blocker {side}  Offset {blockerLocalPositionOffset}");
#endif

        Gizmos.color = previousColor;
    }

    private void OnValidate()
    {
        blockerGizmoSize = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(blockerGizmoSize.x)),
            Mathf.Max(0.01f, Mathf.Abs(blockerGizmoSize.y)),
            Mathf.Max(0.01f, Mathf.Abs(blockerGizmoSize.z)));
    }
}
