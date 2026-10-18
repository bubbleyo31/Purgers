using Fusion;
using UnityEngine;

/// <summary>頭像查詢接口。未來選圖／Steam provider 自行快取圖片與帳號對應；UI 不等待網路、不使用名稱作身分鍵。</summary>
public abstract class PlayerPortraitProvider : ScriptableObject
{
    public abstract Sprite GetPortrait(NetworkRunner runner, PlayerRef player);
}
