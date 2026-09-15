using UnityEngine;

public enum EnemyPatrolPointQueryResult : byte
{
    Valid = 0,
    IndexOutOfRange = 1,
    MissingTransform = 2,
    BeyondMaximumDistance = 3
}

/// <summary>
/// 場景中的人工巡邏節點集合。
///
/// 巡邏點不再需要逐一手動指定。
/// 設計者只需要指定 Patrol Points Parent，
/// 系統就會按照 Hierarchy 順序，自動取得它的所有直接子物件作為巡邏點。
///
/// Brain 依序檢查候選節點，再由地面／飛行 Navigator
/// 確認該位置實際上是否可以抵達。
///
/// 這個元件：
/// - 不移動敵人
/// - 不控制巡邏流程
/// - 不保存任何敵人的 Runtime 巡邏狀態
/// - 只負責提供場景中的巡邏節點資料
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyPatrolArea : MonoBehaviour
{
    [Header("巡邏區域")]

    [SerializeField, Tooltip(
        "同 Unity Scene 的唯一 ID。\n" +
        "地面／飛行可以各建立一組，例如 ground_room_a / air_room_a。\n" +
        "敵人 Prefab 使用相同 ID 尋找對應的 Patrol Area。")]
    private string areaId = "default";


    [SerializeField, Tooltip(
        "巡邏節點的父物件。\n\n" +
        "系統會自動取得這個物件底下的所有『直接子物件』作為巡邏節點，" +
        "不需要再逐一拖進陣列。\n\n" +
        "巡邏點順序會依照 Hierarchy 的 Sibling Index 決定。\n" +
        "例如：\n" +
        "Point_00\n" +
        "Point_01\n" +
        "Point_02\n\n" +
        "注意：這個父物件不要放進 Enemy Root，否則巡邏點會跟著敵人一起移動。")]
    private Transform patrolPointsParent;


    /// <summary>
    /// 自動從 patrolPointsParent 建立的巡邏點快取。
    /// 不需要由 Inspector 手動設定。
    /// </summary>
    private Transform[] patrolPoints = System.Array.Empty<Transform>();


    [SerializeField, Min(0f), Tooltip(
        "候選巡邏節點距離敵人的最大三維直線距離，單位為公尺。\n" +
        "0 = 不限制距離。\n\n" +
        "這個設定只是過濾人工巡邏節點，" +
        "並不是隨機取點半徑。")]
    private float maximumPointDistance = 15f;


    [SerializeField, Tooltip(
        "是否在 Scene 視窗顯示人工巡邏節點 Gizmos。\n" +
        "小球只是編輯器視覺提示，不是碰撞器，也不是隨機取點範圍。")]
    private bool drawGizmos = true;


    [SerializeField, Tooltip(
        "開啟後，即使沒有選取 EnemyPatrolArea 也會顯示巡邏節點。\n" +
        "關閉後只有選取此物件時才會顯示。")]
    private bool drawGizmosWhenNotSelected = true;


    [SerializeField, Min(0.01f), Tooltip(
        "每個巡邏節點 Gizmos 小球的半徑，單位為公尺。")]
    private float patrolPointGizmoRadius = 0.25f;


    [SerializeField, Tooltip(
        "巡邏節點、節點連線與文字標籤使用的 Gizmos 顏色。")]
    private Color patrolPointGizmoColor = Color.cyan;


    [SerializeField, Tooltip(
        "開啟後會按照 Hierarchy / 陣列順序連接巡邏點。\n" +
        "方便檢查節點是否放錯樓層、順序錯誤或距離過遠。\n\n" +
        "這條線只供除錯使用，不代表 AI 必須依序巡邏。")]
    private bool drawPointConnections = true;


    public string AreaId => areaId;

    public int PointCount
    {
        get
        {
            EnsurePatrolPointsCache();
            return patrolPoints.Length;
        }
    }


    /// <summary>
    /// 嘗試取得指定 Index 的巡邏點。
    /// </summary>
    public bool TryGetPoint(
        int index,
        Vector3 requesterPosition,
        out Vector3 point)
    {
        return QueryPoint(
            index,
            requesterPosition,
            out point,
            out _,
            out _,
            out _
        ) == EnemyPatrolPointQueryResult.Valid;
    }


    /// <summary>
    /// 回傳單一巡邏點的詳細過濾結果，
    /// 供 Brain 產生可採取行動的診斷訊息。
    ///
    /// 這個查詢不負責：
    /// - NavMesh 路徑檢查
    /// - 飛行通道檢查
    ///
    /// 那些責任仍然交給 Navigator。
    /// </summary>
    public EnemyPatrolPointQueryResult QueryPoint(
        int index,
        Vector3 requesterPosition,
        out Vector3 point,
        out string pointName,
        out float distance,
        out float allowedMaximumDistance)
    {
        // 確保父物件的子節點已經被建立成快取。
        EnsurePatrolPointsCache();

        point = requesterPosition;
        pointName = $"Element {index}";
        distance = 0f;
        allowedMaximumDistance = maximumPointDistance;

        if (index < 0 || index >= patrolPoints.Length)
        {
            return EnemyPatrolPointQueryResult.IndexOutOfRange;
        }

        Transform pointTransform = patrolPoints[index];

        if (pointTransform == null)
        {
            return EnemyPatrolPointQueryResult.MissingTransform;
        }

        pointName = pointTransform.name;
        point = pointTransform.position;

        distance = Vector3.Distance(
            requesterPosition,
            point
        );

        if (maximumPointDistance > 0f &&
            distance > maximumPointDistance)
        {
            return EnemyPatrolPointQueryResult.BeyondMaximumDistance;
        }

        return EnemyPatrolPointQueryResult.Valid;
    }


    /// <summary>
    /// 從 Patrol Points Parent 重新建立巡邏節點快取。
    ///
    /// 目前只取得「直接子物件」。
    ///
    /// 例如：
    ///
    /// PatrolPointsRoot
    /// ├─ Point_00     ← 會抓
    /// ├─ Point_01     ← 會抓
    /// └─ Group
    ///    └─ Point_02  ← 不會抓
    ///
    /// 順序完全按照 Hierarchy 的 Sibling Index。
    /// </summary>
    private void RebuildPatrolPointsCache()
    {
        if (patrolPointsParent == null)
        {
            patrolPoints = System.Array.Empty<Transform>();
            return;
        }

        int childCount = patrolPointsParent.childCount;

        patrolPoints = new Transform[childCount];

        for (int index = 0; index < childCount; index++)
        {
            patrolPoints[index] = patrolPointsParent.GetChild(index);
        }
    }


    /// <summary>
    /// 確保 Runtime 使用巡邏點以前，快取已經存在。
    ///
    /// 這層保護可以避免其他 Enemy Script
    /// 在 EnemyPatrolArea.Awake() 之前呼叫 QueryPoint 時，
    /// patrolPoints 尚未初始化。
    /// </summary>
    private void EnsurePatrolPointsCache()
    {
        if (patrolPointsParent == null)
        {
            if (patrolPoints.Length != 0)
            {
                patrolPoints = System.Array.Empty<Transform>();
            }

            return;
        }

        // 子物件數量改變時重新建立。
        if (patrolPoints == null ||
            patrolPoints.Length != patrolPointsParent.childCount)
        {
            RebuildPatrolPointsCache();
            return;
        }

        // 檢查 Hierarchy 順序或物件是否改變。
        //
        // 因為即使 Child Count 沒變，
        // 設計者仍可能交換 Point_00 / Point_01 的 Hierarchy 順序。
        for (int index = 0; index < patrolPointsParent.childCount; index++)
        {
            if (patrolPoints[index] != patrolPointsParent.GetChild(index))
            {
                RebuildPatrolPointsCache();
                return;
            }
        }
    }


    private void Awake()
    {
        RebuildPatrolPointsCache();
    }


    private void OnValidate()
    {
        maximumPointDistance =
            Mathf.Max(0f, maximumPointDistance);

        patrolPointGizmoRadius =
            Mathf.Max(0.01f, patrolPointGizmoRadius);

        // Inspector 修改 Parent 或 Hierarchy 時，
        // 直接刷新巡邏點資料。
        RebuildPatrolPointsCache();
    }


    private void OnDrawGizmos()
    {
        if (drawGizmosWhenNotSelected)
        {
            DrawPatrolGizmos();
        }
    }


    private void OnDrawGizmosSelected()
    {
        if (!drawGizmosWhenNotSelected)
        {
            DrawPatrolGizmos();
        }
    }


    private void DrawPatrolGizmos()
    {
        if (!drawGizmos)
            return;

        // Scene 編輯期間也同步父物件中的巡邏點。
        EnsurePatrolPointsCache();

        if (patrolPoints == null)
            return;

        Gizmos.color = patrolPointGizmoColor;

        Transform previous = null;

        for (int index = 0; index < patrolPoints.Length; index++)
        {
            Transform point = patrolPoints[index];

            if (point == null)
                continue;

            Gizmos.DrawWireSphere(
                point.position,
                patrolPointGizmoRadius
            );

            if (drawPointConnections &&
                previous != null)
            {
                Gizmos.DrawLine(
                    previous.position,
                    point.position
                );
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color =
                patrolPointGizmoColor;

            UnityEditor.Handles.Label(
                point.position +
                Vector3.up * (patrolPointGizmoRadius + 0.08f),

                $"{areaId} [{index}]"
            );
#endif

            previous = point;
        }
    }
}