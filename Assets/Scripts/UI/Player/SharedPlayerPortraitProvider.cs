using Fusion;
using UnityEngine;

[CreateAssetMenu(menuName = "Purgers/UI/共用玩家頭像")]
public sealed class SharedPlayerPortraitProvider : PlayerPortraitProvider
{
    [SerializeField, Tooltip("目前所有玩家共用的頭像；未來可將 HUD 的 Provider 換成玩家選圖或 Steam 快取來源。")]
    private Sprite portrait;
    public override Sprite GetPortrait(NetworkRunner runner, PlayerRef player) => portrait;
}
