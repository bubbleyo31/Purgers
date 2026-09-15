using UnityEngine;

/// <summary>
/// 為當前物件及其所有子物件中，帶有網格(Mesh)的節點自動添加 MeshCollider。
/// 包含詳盡的防呆與效能優化機制。
/// </summary>
public class AutoAddMeshCollider : MonoBehaviour
{
    [Tooltip("若勾選，則會在遊戲啟動 (Awake) 時自動執行。若為了極致節省資源，建議取消勾選，並在編輯器中按右鍵手動生成。")]
    public bool addOnAwake = false;

    private void Awake()
    {
        // 根據選項決定是否在遊戲運行一開始就自動配置
        if (addOnAwake)
        {
            AddColliders();
        }
    }

    /// <summary>
    /// 給所有子物件上 MeshCollider 並關閉 Convex。
    /// 加入了 ContextMenu，讓你可以在 Inspector 中對著此腳本點擊右鍵，選擇該指令直接在編輯器中執行。
    /// </summary>
    [ContextMenu("執行添加 Mesh Collider (Add Colliders)")]
    public void AddColliders()
    {
        // 【優化點 1】僅獲取帶有 MeshFilter 的物件 (true 代表包含隱藏物件)
        // 這樣可以避免遍歷整棵樹狀結構中的大量空物件，大幅節省效能
        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);

        // 使用 for 迴圈取代 foreach，在某些 Unity 舊版本中能微幅減少記憶體垃圾 (GC)
        for (int i = 0; i < meshFilters.Length; i++)
        {
            GameObject targetObj = meshFilters[i].gameObject;

            // 【優化點 2】檢查是否已經存在任何 Collider，避免重複添加浪費記憶體
            // TryGetComponent 比一般的 GetComponent 效能更好，且不會產生警告
            if (!targetObj.TryGetComponent<Collider>(out _))
            {
                // 添加 MeshCollider 元件
                MeshCollider meshCollider = targetObj.AddComponent<MeshCollider>();
                
                // 根據需求：強制關閉 Convex (凸面) 屬性，呈現真實凹凸網格
                meshCollider.convex = false;
                
                // 【優化點 3】直接將該物件的 sharedMesh 賦予給碰撞器
                // 這樣可以避免 Unity 在底層自動查找網格的額外開銷，提升初始化速度
                meshCollider.sharedMesh = meshFilters[i].sharedMesh;
            }
        }
        
        Debug.Log($"[AutoAddMeshCollider] 執行完畢！共檢查了 {meshFilters.Length} 個網格物件。");
    }
}