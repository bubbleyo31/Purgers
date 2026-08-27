using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 一個第一人稱 Animation Event Key 對應的 Local Audio Cue。
/// </summary>
[Serializable]
public class FirstPersonAnimationAudioEntry
{
    [Tooltip(
        "Animation Event 傳入的穩定 Key。\n\n" +
        "例如：Reload_MagOut、Reload_MagIn、Reload_BoltPull。\n" +
        "同一個 FirstPersonAnimationAudioEvents 中不可重複。")]
    public string EventKey;

    [Tooltip(
        "此 Key 播放的 Local First Person GameplayAudioCue。\n\n" +
        "第一人稱細節 Cue 的 Network ID 應保持 0，避免被誤當成 Network World 聲音。")]
    public GameplayAudioCue Cue;

    [Min(0f)]
    [Tooltip(
        "此 Animation Event 額外套用的音量倍率。\n\n" +
        "最終音量 = Cue Volume × Volume Scale。")]
    public float VolumeScale =
        1f;
}


/// <summary>
/// 第一人稱 ViewModel Animation Event 的本地聲音接收器。
///
/// ====================================================================
///
/// 只掛在本地第一人稱 ViewModel Prefab。
///
/// Animation Event：
///
/// Function = PlayLocalOneShot
/// String   = Reload_BoltPull
///
/// ↓
///
/// 本機 GameplayAudioService.PlayLocalOneShot
///
/// 不發 RPC，其他玩家聽不到。
///
/// ====================================================================
///
/// 不可把這支腳本與相同 Animation Event 掛到第三人稱角色模型，
/// 否則每台 Client 都會替遠端玩家播放第一人稱細節聲。
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonAnimationAudioEvents :
    MonoBehaviour
{
    [Header("第一人稱 Animation Audio Events")]

    [SerializeField]
    [Tooltip(
        "Animation Event Key 與 Local Audio Cue 的對照表。\n\n" +
        "換彈中的不同細節應使用不同 Key，讓動畫關鍵幀能分別播放。")]
    private FirstPersonAnimationAudioEntry[] oneShotEntries =
        new FirstPersonAnimationAudioEntry[0];

    [SerializeField]
    [Tooltip(
        "開啟後，Key 不存在、Cue 為空或 GameplayAudioService 不存在時顯示 Warning。\n\n" +
        "建立 Animation Event 階段建議開啟，完成後可關閉。")]
    private bool debugAnimationAudio =
        true;

    private Dictionary<string, FirstPersonAnimationAudioEntry>
        entryByKey;

    private void Awake()
    {
        BuildLookup();
    }

    /// <summary>
    /// Unity Animation Event 正式入口。
    ///
    /// Animation Event 只需傳入 String Key，
    /// 不直接保存 GameplayAudioService 或 Network 元件引用。
    /// </summary>
    public void PlayLocalOneShot(
        string eventKey
    )
    {
        if (string.IsNullOrWhiteSpace(
                eventKey
            ))
        {
            return;
        }

        if (entryByKey == null)
        {
            BuildLookup();
        }

        if (entryByKey.TryGetValue(
                eventKey,
                out FirstPersonAnimationAudioEntry entry
            ) == false ||
            entry == null ||
            entry.Cue == null)
        {
            if (debugAnimationAudio)
            {
                Debug.LogWarning(
                    $"[First Person Audio] 找不到 Animation Event Key 或 Cue：{eventKey}",
                    this
                );
            }

            return;
        }

        GameplayAudioService audioService =
            GameplayAudioService.Instance;

        if (audioService == null)
        {
            if (debugAnimationAudio)
            {
                Debug.LogWarning(
                    "[First Person Audio] 場景中沒有 GameplayAudioService。",
                    this
                );
            }

            return;
        }

        audioService.PlayLocalOneShot(
            entry.Cue,
            Mathf.Max(
                0f,
                entry.VolumeScale
            )
        );
    }

    private void BuildLookup()
    {
        if (entryByKey == null)
        {
            entryByKey =
                new Dictionary<
                    string,
                    FirstPersonAnimationAudioEntry
                >(
                    StringComparer.Ordinal
                );
        }
        else
        {
            entryByKey.Clear();
        }

        if (oneShotEntries == null)
        {
            return;
        }

        for (int i = 0;
             i < oneShotEntries.Length;
             i++)
        {
            FirstPersonAnimationAudioEntry entry =
                oneShotEntries[i];

            if (entry == null ||
                string.IsNullOrWhiteSpace(
                    entry.EventKey
                ))
            {
                continue;
            }

            if (entryByKey.ContainsKey(
                    entry.EventKey
                ))
            {
                Debug.LogError(
                    $"[First Person Audio] Animation Event Key 重複：{entry.EventKey}",
                    this
                );

                continue;
            }

            entryByKey.Add(
                entry.EventKey,
                entry
            );
        }
    }
}