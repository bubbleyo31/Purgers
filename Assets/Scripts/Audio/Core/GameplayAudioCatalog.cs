using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Network World Audio 使用的 Cue Catalog。
///
/// RPC 不會傳送 AudioClip 或 ScriptableObject，
/// 只會傳送穩定的 ushort Network ID。
///
/// 每台 Client 再從相同 Catalog 解析成本地 GameplayAudioCue。
/// </summary>
[CreateAssetMenu(
    fileName = "GameplayAudioCatalog",
    menuName = "Purgers/Audio/Gameplay Audio Catalog"
)]
public class GameplayAudioCatalog :
    ScriptableObject
{
    [SerializeField]
    [Tooltip(
        "所有允許透過 NetworkPlayerAudioEmitter 傳送的 GameplayAudioCue。\n\n" +
        "Local Only Cue 不需要放入。\n" +
        "陣列內每個 Cue 的 Network ID 必須大於 0 且不可重複。")]
    private GameplayAudioCue[] networkCues =
        new GameplayAudioCue[0];

    /// <summary>
    /// Runtime ID Lookup。
    /// 不在每次槍聲播放時線性掃描整個陣列。
    /// </summary>
    private Dictionary<ushort, GameplayAudioCue>
        cueByNetworkId;

    private void OnEnable()
    {
        RebuildLookup(
            false
        );
    }

    private void OnValidate()
    {
        RebuildLookup(
            true
        );
    }

    /// <summary>
    /// 依照 RPC 傳來的 Network ID 解析 Cue。
    /// </summary>
    public bool TryGetCue(
        ushort networkId,
        out GameplayAudioCue cue
    )
    {
        cue =
            null;

        if (networkId == 0)
        {
            return false;
        }

        if (cueByNetworkId == null)
        {
            RebuildLookup(
                false
            );
        }

        return
            cueByNetworkId != null &&
            cueByNetworkId.TryGetValue(
                networkId,
                out cue
            ) &&
            cue != null;
    }

    /// <summary>
    /// 驗證呼叫端準備傳送的 Cue 確實已登錄，
    /// 且相同 ID 沒有指向另一個資產。
    /// </summary>
    public bool ContainsCue(
        GameplayAudioCue cue
    )
    {
        if (cue == null ||
            cue.IsNetworkCue == false)
        {
            return false;
        }

        return
            TryGetCue(
                cue.NetworkId,
                out GameplayAudioCue registeredCue
            ) &&
            registeredCue == cue;
    }

    private void RebuildLookup(
        bool logValidationErrors
    )
    {
        if (cueByNetworkId == null)
        {
            cueByNetworkId =
                new Dictionary<
                    ushort,
                    GameplayAudioCue
                >();
        }
        else
        {
            cueByNetworkId.Clear();
        }

        if (networkCues == null)
        {
            return;
        }

        for (int i = 0;
             i < networkCues.Length;
             i++)
        {
            GameplayAudioCue cue =
                networkCues[i];

            if (cue == null)
            {
                continue;
            }

            ushort networkId =
                cue.NetworkId;

            if (networkId == 0)
            {
                if (logValidationErrors)
                {
                    Debug.LogError(
                        $"[Audio Catalog] {cue.name} 的 Network ID 是 0，" +
                        "Local Only Cue 不可加入 Network Catalog。",
                        cue
                    );
                }

                continue;
            }

            if (cueByNetworkId.ContainsKey(
                    networkId
                ))
            {
                if (logValidationErrors)
                {
                    GameplayAudioCue existingCue =
                        cueByNetworkId[networkId];

                    Debug.LogError(
                        $"[Audio Catalog] Network ID {networkId} 重複。" +
                        $"\n第一個：{existingCue.name}" +
                        $"\n重複項：{cue.name}",
                        this
                    );
                }

                continue;
            }

            cueByNetworkId.Add(
                networkId,
                cue
            );
        }
    }
}