using UnityEngine;

/// <summary>保留舊元件 GUID 的相容入口；畫面效果已統一交給 LGG Owner，不再建立 Canvas 色罩。</summary>
public sealed class PlayerBulletTimeOverlay : MonoBehaviour
{
    public void Bind(Player owner) => PlayerAbilityColorGrading.BindLocal(owner);
}
