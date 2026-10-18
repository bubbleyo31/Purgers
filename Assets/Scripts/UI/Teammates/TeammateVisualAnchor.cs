using UnityEngine;

/// <summary>人工指定第三人稱身體與頭頂；不自動擷取第一人稱手、武器或特效。</summary>
[DisallowMultipleComponent]
public sealed class TeammateVisualAnchor : MonoBehaviour
{
    [Header("隊友外觀引用")]
    [Tooltip("名條追蹤點；建議放在頭頂。留空使用 HUD 設定的 Root 高度。")] public Transform headAnchor;
    [Tooltip("只放第三人稱身體的 MeshRenderer／SkinnedMeshRenderer。不要加入手部、武器、粒子或第一人稱物件。")] public Renderer[] bodyRenderers=System.Array.Empty<Renderer>();
}
