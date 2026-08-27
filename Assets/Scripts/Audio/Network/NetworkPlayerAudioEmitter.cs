using Fusion;
using UnityEngine;


/// <summary>
/// Player NetworkObject 的世界聲音網路出口。
///
/// ====================================================================
///
/// 只處理其他玩家也必須聽見的 World OneShot：
///
/// 槍聲
/// 受傷聲
/// 世界撞擊
/// 爆炸
/// 重要能力瞬間音效。
///
/// ====================================================================
///
/// 權限規則：
///
/// 只有 State Authority 可以正式發出聲音 RPC。
///
/// 原因：
///
/// 1. 防止 Prediction / Resimulation 重複播放。
/// 2. 防止 Input Authority 自行宣稱不存在的槍聲或受傷聲。
/// 3. 聲音只在 Gameplay 事件正式成立後送出。
///
/// ====================================================================
///
/// 本腳本不處理第一人稱換彈細節。
/// 那些聲音不可呼叫這裡，應直接走 Local API。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerAudioEmitter :
    NetworkBehaviour
{
    // =====================================================================
    #region References

    [Header("Network Audio Catalog")]

    [SerializeField]
    [Tooltip(
        "所有 Client 共用的 GameplayAudioCatalog。\n\n" +
        "RPC 只傳 Network ID；接收端用這個 Catalog 解析成真正 Cue。\n" +
        "所有 Player Prefab 必須指定同一份 Catalog Asset。")]
    private GameplayAudioCatalog audioCatalog;

    [SerializeField]
    [Tooltip(
        "沒有另外傳入世界位置時，World OneShot 使用的預設 Transform。\n\n" +
        "可以指定 Player 胸口或共用 Audio Origin；留空則使用 Player Root Transform。\n" +
        "槍聲正式接入時仍建議傳入 Gameplay Muzzle Position。")]
    private Transform defaultWorldAudioOrigin;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip(
        "開啟後，世界聲音因為 Cue 未登錄、權限錯誤或接收端無法解析時顯示資訊。\n\n" +
        "測試階段可開啟；全自動武器正式接入後建議關閉。")]
    private bool debugNetworkAudio =
        false;

    #endregion

    // =====================================================================
    #region Fusion

    public override void Spawned()
    {
        if (defaultWorldAudioOrigin == null)
        {
            defaultWorldAudioOrigin =
                transform;
        }

        if (audioCatalog == null)
        {
            Debug.LogError(
                "[Network Audio] Player 尚未指定 GameplayAudioCatalog。",
                this
            );
        }
    }

    #endregion

    // =====================================================================
    #region Public State Authority API

    /// <summary>
    /// 在預設 Player Audio Origin 播放世界 OneShot。
    ///
    /// 只能由已確認 Gameplay 事件成立的 State Authority 呼叫。
    /// </summary>
    public bool PlayWorldOneShotFromStateAuthority(
        GameplayAudioCue cue,
        float volumeScale = 1f
    )
    {
        Vector3 position =
            defaultWorldAudioOrigin != null
                ? defaultWorldAudioOrigin.position
                : transform.position;

        return PlayWorldOneShotFromStateAuthority(
            cue,
            position,
            volumeScale
        );
    }

    /// <summary>
    /// 在指定世界位置播放 Network World OneShot。
    ///
    /// 位置使用事件發生瞬間的 Snapshot；
    /// 玩家之後繼續移動不會把已發出的短音拖著走。
    /// </summary>
    public bool PlayWorldOneShotFromStateAuthority(
        GameplayAudioCue cue,
        Vector3 worldPosition,
        float volumeScale = 1f
    )
    {
        if (Object == null ||
            Object.IsValid == false ||
            Object.HasStateAuthority == false)
        {
            if (debugNetworkAudio)
            {
                Debug.LogWarning(
                    "[Network Audio] 非 State Authority 嘗試發送世界聲音，已拒絕。",
                    this
                );
            }

            return false;
        }

        if (cue == null ||
            cue.IsNetworkCue == false)
        {
            if (debugNetworkAudio)
            {
                Debug.LogWarning(
                    "[Network Audio] Cue 為空或 Network ID = 0，無法透過 RPC 傳送。",
                    this
                );
            }

            return false;
        }

        if (audioCatalog == null ||
            audioCatalog.ContainsCue(
                cue
            ) == false)
        {
            if (debugNetworkAudio)
            {
                Debug.LogWarning(
                    $"[Network Audio] Cue 尚未正確登錄在 Catalog。" +
                    $"\nCue：{cue.name}" +
                    $"\nNetwork ID：{cue.NetworkId}",
                    cue
                );
            }

            return false;
        }

        if (cue.TryCreatePlaybackSelection(
                out byte variantIndex,
                out float selectedPitch
            ) == false)
        {
            return false;
        }

        float safeVolumeScale =
            Mathf.Clamp(
                volumeScale,
                0f,
                4f
            );

        if (cue.NetworkDelivery ==
            GameplayAudioNetworkDelivery.Reliable)
        {
            RPC_PlayWorldOneShotReliable(
                cue.NetworkId,
                variantIndex,
                selectedPitch,
                worldPosition,
                safeVolumeScale
            );
        }
        else
        {
            RPC_PlayWorldOneShotUnreliable(
                cue.NetworkId,
                variantIndex,
                selectedPitch,
                worldPosition,
                safeVolumeScale
            );
        }

        return true;
    }

    #endregion

    // =====================================================================
    #region RPC

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Unreliable,
        TickAligned = false,
        InvokeLocal = true
    )]
    private void RPC_PlayWorldOneShotUnreliable(
        ushort cueNetworkId,
        byte variantIndex,
        float selectedPitch,
        Vector3 worldPosition,
        float volumeScale
    )
    {
        PlayReceivedWorldOneShot(
            cueNetworkId,
            variantIndex,
            selectedPitch,
            worldPosition,
            volumeScale
        );
    }

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All,
        Channel = RpcChannel.Reliable,
        TickAligned = false,
        InvokeLocal = true
    )]
    private void RPC_PlayWorldOneShotReliable(
        ushort cueNetworkId,
        byte variantIndex,
        float selectedPitch,
        Vector3 worldPosition,
        float volumeScale
    )
    {
        PlayReceivedWorldOneShot(
            cueNetworkId,
            variantIndex,
            selectedPitch,
            worldPosition,
            volumeScale
        );
    }

    private void PlayReceivedWorldOneShot(
        ushort cueNetworkId,
        byte variantIndex,
        float selectedPitch,
        Vector3 worldPosition,
        float volumeScale
    )
    {
        if (audioCatalog == null ||
            audioCatalog.TryGetCue(
                cueNetworkId,
                out GameplayAudioCue cue
            ) == false)
        {
            if (debugNetworkAudio)
            {
                Debug.LogWarning(
                    $"[Network Audio] 接收端無法解析 Cue Network ID：{cueNetworkId}。",
                    this
                );
            }

            return;
        }

        GameplayAudioService audioService =
            GameplayAudioService.Instance;

        /*
         * Dedicated Server 可能完全沒有 Audio Service，
         * 這不是錯誤；它不需要實際播放聲音。
         */
        if (audioService == null)
        {
            return;
        }

        audioService.PlayWorldOneShot(
            cue,
            variantIndex,
            selectedPitch,
            worldPosition,
            volumeScale
        );
    }

    #endregion
}