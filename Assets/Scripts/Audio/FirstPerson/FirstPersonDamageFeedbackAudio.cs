using Fusion;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 只為本機攻擊玩家播放的傷害結果回饋聲。
///
/// ====================================================================
///
/// 正式資料流程：
///
/// Damage Source
/// ↓
/// DamageResult
/// ↓
/// PlayerCombatFeedbackRelay
/// ↓
/// State Authority RPC
/// ↓
/// 造成傷害的 Player Input Authority
/// ↓
/// LocalHitConfirmed
/// ↓
/// 本腳本。
///
/// ====================================================================
///
/// 音效優先級固定為：
///
/// Kill
/// >
/// Headshot
/// >
/// Normal Hit。
///
/// 同一個 DamageResult 永遠只會播放其中一個 Cue。
///
/// ====================================================================
///
/// AOE / 多目標處理：
///
/// 同一個 FeedbackId + Sequence
/// 如果在同一個畫面幀收到多個 DamageResult，
/// 本腳本會先暫存並挑選最高優先結果，
/// 到 LateUpdate 才真正播放一次。
///
/// 例如 Tank 一刀同時：
///
/// 命中 3 隻
/// 暴頭 1 隻
/// 擊殺 1 隻
///
/// 最後只播放一次 Kill Cue。
///
/// ====================================================================
///
/// 這三個 Cue 都必須是 Local Only：
///
/// Network ID = 0。
///
/// 不可加入 GameplayAudioCatalog，
/// 也不會透過 NetworkPlayerAudioEmitter 再次廣播。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerCombatFeedbackRelay))]
public class FirstPersonDamageFeedbackAudio :
    NetworkBehaviour
{
    // =====================================================================
    #region Player Feedback Reference


    [Header("玩家戰鬥回饋")]


    [SerializeField]
    [Tooltip(
        "同一個 Player Network Prefab Root 上的 PlayerCombatFeedbackRelay。\n\n" +
        "Relay 只會把 State Authority 正式確認的 DamageResult 傳回造成傷害的玩家本人。\n\n" +
        "若留空，Awake 時會自動從同一個 GameObject 取得。")]
    private PlayerCombatFeedbackRelay
        feedbackRelay;


    #endregion


    // =====================================================================
    #region Local Feedback Cues


    [Header("Local 傷害回饋 Cue")]


    [SerializeField]
    [Tooltip(
        "普通有效命中的本機 2D 回饋聲。\n\n" +
        "只有 HasEffectiveDamage = true，且這次結果不是 Kill、也不是 Headshot 時才會播放。\n\n" +
        "此 Cue 必須使用 Network ID = 0，不可加入 GameplayAudioCatalog。")]
    private GameplayAudioCue
        normalHitCue;


    [SerializeField]
    [Tooltip(
        "暴頭有效命中的本機 2D 回饋聲。\n\n" +
        "當 IsHeadshot = true 且 KilledTarget = false 時播放。\n\n" +
        "如果同一次傷害同時是 Headshot + Kill，Kill 優先，本 Cue 不會播放。\n\n" +
        "此 Cue 必須使用 Network ID = 0。")]
    private GameplayAudioCue
        headshotCue;


    [SerializeField]
    [Tooltip(
        "擊殺目標時的本機 2D 回饋聲。\n\n" +
        "KilledTarget = true 時擁有最高優先級。\n\n" +
        "即使這一擊同時是暴頭，也只播放 Kill Cue，不會再疊加 Headshot 或 Normal Hit。\n\n" +
        "此 Cue 必須使用 Network ID = 0。")]
    private GameplayAudioCue
        killCue;


    #endregion


    // =====================================================================
    #region Volume Scale


    [Header("Local 回饋音量倍率")]


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "普通命中 Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。\n\n" +
        "這只影響本機命中回饋，不影響世界槍聲或其他玩家。")]
    private float normalHitVolumeScale =
        1f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "暴頭 Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。")]
    private float headshotVolumeScale =
        1f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "擊殺 Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量。\n" +
        "0 = 靜音。")]
    private float killVolumeScale =
        1f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("除錯設定")]


    [SerializeField]
    [Tooltip(
        "開啟後，每次本機真正播放傷害回饋聲時，會顯示選到的是 Normal Hit、Headshot 或 Kill，以及 FeedbackId、Sequence 與 Applied Damage。\n\n" +
        "完成多人測試後建議關閉，避免高射速武器產生大量 Console 訊息。")]
    private bool debugFeedbackAudio =
        true;


    #endregion


    // =====================================================================
    #region Pending Feedback


    /// <summary>
    /// 同一個攻擊身份與 Sequence 在本畫面幀中
    /// 最後留下的最高優先回饋。
    ///
    /// Key 格式：
    ///
    /// 高 32 位：CombatFeedbackId。
    /// 低 32 位：Sequence。
    /// </summary>
    private readonly Dictionary<long, CombatHitFeedbackData>
        pendingFeedbackByAttack =
            new Dictionary<long, CombatHitFeedbackData>(8);


    /// <summary>
    /// 保留本畫面幀第一次收到各攻擊的順序。
    ///
    /// Dictionary 不負責表現順序；
    /// 使用獨立 List 可以讓同一幀真的收到兩次不同攻擊時，
    /// 仍依到達順序播放。
    /// </summary>
    private readonly List<long>
        pendingAttackOrder =
            new List<long>(8);


    /// <summary>
    /// 是否已訂閱 LocalHitConfirmed。
    /// </summary>
    private bool isSubscribed;


    #endregion


    // =====================================================================
    #region Unity / Fusion


    private void Awake()
    {
        if (feedbackRelay == null)
        {
            feedbackRelay =
                GetComponent<
                    PlayerCombatFeedbackRelay
                >();
        }
    }


    public override void Spawned()
    {
        /*
         * 遠端玩家不需要訂閱。
         *
         * 這可確保命中、暴頭、擊殺提示
         * 只會在真正輸出傷害的玩家本人裝置播放。
         */
        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            return;
        }


        Subscribe();
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        Unsubscribe();
    }


    private void OnDestroy()
    {
        Unsubscribe();
    }


    /// <summary>
    /// 等本畫面幀所有 LocalHitConfirmed 都進來後，
    /// 才為每個攻擊播放一次最高優先音效。
    /// </summary>
    private void LateUpdate()
    {
        if (isSubscribed == false ||
            pendingAttackOrder.Count <= 0)
        {
            return;
        }


        for (int i = 0;
            i < pendingAttackOrder.Count;
            i++)
        {
            long attackKey =
                pendingAttackOrder[i];


            if (pendingFeedbackByAttack.TryGetValue(
                    attackKey,
                    out CombatHitFeedbackData feedback
                ) == false)
            {
                continue;
            }


            PlaySelectedFeedback(
                feedback
            );
        }


        ClearPendingFeedback();
    }


    #endregion


    // =====================================================================
    #region Subscription


    private void Subscribe()
    {
        if (isSubscribed)
        {
            return;
        }


        if (feedbackRelay == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonDamageFeedbackAudio)}] " +
                $"找不到 {nameof(PlayerCombatFeedbackRelay)}，" +
                $"無法接收本地傷害結果。",
                this
            );


            return;
        }


        /*
         * 先 -= 再 +=，避免未來某個流程意外重複 Subscribe。
         */
        feedbackRelay.LocalHitConfirmed -=
            OnLocalHitConfirmed;


        feedbackRelay.LocalHitConfirmed +=
            OnLocalHitConfirmed;


        isSubscribed =
            true;
    }


    private void Unsubscribe()
    {
        if (feedbackRelay != null)
        {
            feedbackRelay.LocalHitConfirmed -=
                OnLocalHitConfirmed;
        }


        isSubscribed =
            false;


        ClearPendingFeedback();
    }


    #endregion


    // =====================================================================
    #region Feedback Selection


    /// <summary>
    /// 收到 State Authority 傳回給本機攻擊者的正式傷害結果。
    /// </summary>
    private void OnLocalHitConfirmed(
        CombatHitFeedbackData feedback
    )
    {
        /*
         * Accepted 不一定等於真的扣到 HP。
         *
         * 完全無敵、完全格擋或最後 Applied Damage = 0
         * 不播放普通傷害命中聲。
         *
         * 未來若要做 Shield Block Sound，
         * 應建立獨立的 Block Feedback，不要冒充 Normal Hit。
         */
        if (feedback.HasEffectiveDamage == false)
        {
            return;
        }


        long attackKey =
            BuildAttackKey(
                feedback.FeedbackId,
                feedback.Sequence
            );


        if (pendingFeedbackByAttack.TryGetValue(
                attackKey,
                out CombatHitFeedbackData currentFeedback
            ))
        {
            /*
             * 同一攻擊已經有暫存結果。
             * 只有新結果優先級更高時才覆蓋。
             *
             * Kill > Headshot > Normal Hit。
             */
            if (GetPriority(feedback) >
                GetPriority(currentFeedback))
            {
                pendingFeedbackByAttack[attackKey] =
                    feedback;
            }


            return;
        }


        pendingFeedbackByAttack.Add(
            attackKey,
            feedback
        );


        pendingAttackOrder.Add(
            attackKey
        );
    }


    /// <summary>
    /// 將攻擊身份與 Sequence 合成單一暫存 Key。
    /// </summary>
    private static long BuildAttackKey(
        CombatFeedbackId feedbackId,
        int sequence
    )
    {
        return
            ((long)(byte)feedbackId << 32) |
            (uint)sequence;
    }


    /// <summary>
    /// 回傳傷害結果的音效優先級。
    /// </summary>
    private static int GetPriority(
        CombatHitFeedbackData feedback
    )
    {
        if (feedback.KilledTarget)
        {
            return 3;
        }


        if (feedback.IsHeadshot)
        {
            return 2;
        }


        return 1;
    }


    /// <summary>
    /// 按固定優先級選出並播放唯一一個 Local Cue。
    /// </summary>
    private void PlaySelectedFeedback(
        CombatHitFeedbackData feedback
    )
    {
        GameplayAudioCue selectedCue;
        float selectedVolumeScale;
        string selectedType;


        // =============================================================
        // 1. Kill
        // =============================================================

        if (feedback.KilledTarget)
        {
            selectedCue =
                killCue;


            selectedVolumeScale =
                killVolumeScale;


            selectedType =
                "Kill";
        }


        // =============================================================
        // 2. Headshot
        // =============================================================

        else if (feedback.IsHeadshot)
        {
            selectedCue =
                headshotCue;


            selectedVolumeScale =
                headshotVolumeScale;


            selectedType =
                "Headshot";
        }


        // =============================================================
        // 3. Normal Hit
        // =============================================================

        else
        {
            selectedCue =
                normalHitCue;


            selectedVolumeScale =
                normalHitVolumeScale;


            selectedType =
                "Normal Hit";
        }


        if (selectedCue == null)
        {
            if (debugFeedbackAudio)
            {
                Debug.LogWarning(
                    $"[{nameof(FirstPersonDamageFeedbackAudio)}] " +
                    $"{selectedType} 沒有指定 GameplayAudioCue，" +
                    $"本次不播放。" +
                    $"\nFeedback ID：{feedback.FeedbackId}" +
                    $"\nSequence：{feedback.Sequence}",
                    this
                );
            }


            return;
        }


        GameplayAudioService audioService =
            GameplayAudioService.Instance;


        if (audioService == null)
        {
            if (debugFeedbackAudio)
            {
                Debug.LogWarning(
                    $"[{nameof(FirstPersonDamageFeedbackAudio)}] " +
                    $"GameplayAudioService.Instance = NULL，" +
                    $"無法播放 {selectedType}。",
                    this
                );
            }


            return;
        }


        bool played =
            audioService.PlayLocalOneShot(
                selectedCue,
                selectedVolumeScale
            );


        if (debugFeedbackAudio)
        {
            Debug.Log(
                $"[Damage Feedback Audio]" +
                $"\nType：{selectedType}" +
                $"\nPlayed：{played}" +
                $"\nFeedback ID：{feedback.FeedbackId}" +
                $"\nSequence：{feedback.Sequence}" +
                $"\nHeadshot：{feedback.IsHeadshot}" +
                $"\nKill：{feedback.KilledTarget}" +
                $"\nApplied Damage：{feedback.AppliedDamage:F2}",
                this
            );
        }
    }


    private void ClearPendingFeedback()
    {
        pendingFeedbackByAttack.Clear();
        pendingAttackOrder.Clear();
    }


    #endregion
}
