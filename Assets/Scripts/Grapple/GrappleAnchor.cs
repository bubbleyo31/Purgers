using Fusion;
using UnityEngine;

/// <summary>
/// 可被勾索追蹤的網路錨點。
///
/// 這個元件必須位於 NetworkObject 階層內。
/// Player 可以將它保存為 Networked Property，
/// 讓 Host、Client 與回溯模擬都指向相同錨點。
/// </summary>
[DisallowMultipleComponent]
public sealed class GrappleAnchor : NetworkBehaviour
{
    [SerializeField]
    [Tooltip("用來計算勾索局部命中點的座標空間。若留空，使用這個 GrappleAnchor 所在物件的 Transform")]
    private Transform anchorSpace;

    /// <summary>
    /// 實際用來保存與還原局部命中點的 Transform。
    /// </summary>
    public Transform AnchorSpace
    {
        get
        {
            if (anchorSpace != null)
                return anchorSpace;

            return transform;
        }
    }
}