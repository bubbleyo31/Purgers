using System;
using UnityEngine;
using UnityEngine.Audio;


/// <summary>
/// 世界聲音透過 Fusion RPC 傳送時使用的可靠度。
/// </summary>
public enum GameplayAudioNetworkDelivery : byte
{
    /// <summary>
    /// 適合高頻、短暫且過期後不應補播的聲音。
    ///
    /// 例如：
    /// 自動武器槍聲、一般受傷聲、普通撞擊聲。
    ///
    /// 少量封包遺失時可能漏掉一次聲音，
    /// 但不會因網路壅塞延遲補播一串過期槍聲。
    /// </summary>
    Unreliable = 0,

    /// <summary>
    /// 適合低頻且必須送達的重要聲音。
    ///
    /// 例如：
    /// 關卡重要事件、Boss 階段音效、極低頻關鍵提示。
    ///
    /// 不建議拿來傳送全自動槍聲。
    /// </summary>
    Reliable = 1
}


/// <summary>
/// 一個可重用的 Gameplay Audio Cue 資產。
///
/// ====================================================================
///
/// Audio Cue 保存：
///
/// 音檔變體
/// 音量
/// Pitch 隨機範圍
/// Mixer Group
/// 3D 空間參數
/// 網路 ID
/// 網路可靠度。
///
/// ====================================================================
///
/// Network ID 規則：
///
/// 0
/// → Local Only Cue。
/// → 可以用於第一人稱動畫細節與本地 Loop。
/// → NetworkPlayerAudioEmitter 會拒絕傳送。
///
/// 1～65535
/// → 可以加入 GameplayAudioCatalog，透過 RPC 傳送。
/// → 所有 Client 必須使用相同 Catalog Asset。
/// </summary>
[CreateAssetMenu(
    fileName = "GameplayAudioCue_New",
    menuName = "Purgers/Audio/Gameplay Audio Cue"
)]
public class GameplayAudioCue :
    ScriptableObject
{
    // =====================================================================
    #region Identity / Network

    [Header("識別與網路")]

    [SerializeField]
    [Range(0, 65535)]
    [Tooltip(
        "此 Cue 的穩定網路 ID。\n\n" +
        "0 = Local Only，不允許透過 RPC 傳送。\n" +
        "1～65535 = Network World Cue，必須加入 GameplayAudioCatalog。\n\n" +
        "同一份 Catalog 中不可有重複 ID。" +
        "已經發布或存檔後不要任意改動既有 ID，避免不同版本解析成錯誤聲音。")]
    private int networkId;

    [SerializeField]
    [Tooltip(
        "這個 Cue 透過 Fusion 傳送時使用的 RPC 可靠度。\n\n" +
        "自動武器、普通受傷與一般撞擊建議 Unreliable；" +
        "低頻且必須送達的重要事件才使用 Reliable。")]
    private GameplayAudioNetworkDelivery networkDelivery =
        GameplayAudioNetworkDelivery.Unreliable;

    #endregion

    // =====================================================================
    #region Clips

    [Header("音檔變體")]

    [SerializeField]
    [Tooltip(
        "此事件可隨機選擇的 AudioClip。\n\n" +
        "例如槍聲可放入 3～6 個近似變體，降低連射重複感。\n" +
        "Network World 播放時會由 State Authority 選定變體索引，" +
        "所有 Client 播放同一個變體。\n\n" +
        "單一 Cue 最多使用前 256 個 Clip。")]
    private AudioClip[] clips =
        new AudioClip[0];

    #endregion

    // =====================================================================
    #region Volume / Pitch

    [Header("音量與 Pitch")]

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "此 Cue 的基礎音量，範圍 0～1。\n\n" +
        "播放端仍可額外傳入 Volume Scale，但最終值會限制在 0～1。")]
    private float volume =
        1f;

    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip(
        "每次播放時允許隨機選擇的最低 Pitch。\n\n" +
        "不需要隨機時，請讓 Min 與 Max 相同，例如都設為 1。")]
    private float minimumPitch =
        1f;

    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip(
        "每次播放時允許隨機選擇的最高 Pitch。\n\n" +
        "Network World 播放時由 State Authority 選定 Pitch，" +
        "所有 Client 使用相同數值。")]
    private float maximumPitch =
        1f;

    [SerializeField]
    [Tooltip(
        "此 Cue 優先使用的 AudioMixerGroup。\n\n" +
        "若留空，GameplayAudioService 會依 Local／World 使用對應的 Fallback Mixer Group。")]
    private AudioMixerGroup outputMixerGroup;

    #endregion

    // =====================================================================
    #region World 3D

    [Header("World 3D 設定")]

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "World 播放時的 3D 空間混合比例。\n\n" +
        "0 = 完全 2D。\n" +
        "1 = 完全 3D。\n\n" +
        "槍聲、受傷聲與世界撞擊聲通常設定為 1。" +
        "Local First Person API 會強制使用 0，不受此欄影響。")]
    private float worldSpatialBlend =
        1f;

    [SerializeField]
    [Tooltip(
        "World 3D 聲音的距離衰減模式。\n\n" +
        "一般槍聲與角色聲音建議使用 Logarithmic。")]
    private AudioRolloffMode worldRolloffMode =
        AudioRolloffMode.Logarithmic;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "World 3D 聲音在此距離內維持接近完整音量。\n\n" +
        "單位為 Unity 世界單位。角色槍聲可先從 3～6 測試。")]
    private float worldMinimumDistance =
        4f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "World 3D 聲音超過此距離後不再聽見。\n\n" +
        "必須大於或等於 World Minimum Distance。" +
        "槍聲可依地圖尺度先從 40～80 測試。")]
    private float worldMaximumDistance =
        60f;

    [SerializeField]
    [Range(0f, 5f)]
    [Tooltip(
        "World 3D 聲音的 Doppler Level。\n\n" +
        "高速玩家與鈎索可能讓 Doppler 過度明顯；" +
        "短促槍聲第一輪建議設為 0，需要都卜勒效果時再逐步提高。")]
    private float worldDopplerLevel =
        0f;

    #endregion

    // =====================================================================
    #region Runtime Random

    /// <summary>
    /// Audio Presentation 專用 Random。
    ///
    /// 不使用 UnityEngine.Random，避免聲音變體選擇改變
    /// 其他 Gameplay 系統可能使用的 Unity 全域 Random State。
    ///
    /// Network World Cue 仍然只由 State Authority 選一次，
    /// 再把結果送給所有 Client。
    /// </summary>
    [NonSerialized]
    private System.Random playbackRandom;

    #endregion

    // =====================================================================
    #region Public Data

    public ushort NetworkId =>
        (ushort)Mathf.Clamp(
            networkId,
            0,
            ushort.MaxValue
        );

    public bool IsNetworkCue =>
        NetworkId != 0;

    public GameplayAudioNetworkDelivery
        NetworkDelivery =>
            networkDelivery;

    public float Volume =>
        Mathf.Clamp01(
            volume
        );

    public AudioMixerGroup OutputMixerGroup =>
        outputMixerGroup;

    public float WorldSpatialBlend =>
        Mathf.Clamp01(
            worldSpatialBlend
        );

    public AudioRolloffMode WorldRolloffMode =>
        worldRolloffMode;

    public float WorldMinimumDistance =>
        Mathf.Max(
            0.01f,
            worldMinimumDistance
        );

    public float WorldMaximumDistance =>
        Mathf.Max(
            WorldMinimumDistance,
            worldMaximumDistance
        );

    public float WorldDopplerLevel =>
        Mathf.Clamp(
            worldDopplerLevel,
            0f,
            5f
        );

    #endregion

    // =====================================================================
    #region Playback Selection

    /// <summary>
    /// 為一次新播放選出 Clip 變體與 Pitch。
    ///
    /// Local 播放：由本機呼叫一次。
    /// Network World 播放：只由 State Authority 呼叫，
    /// 再把選定結果透過 RPC 傳給所有 Client。
    /// </summary>
    public bool TryCreatePlaybackSelection(
        out byte variantIndex,
        out float selectedPitch
    )
    {
        variantIndex =
            0;

        selectedPitch =
            1f;

        int validCount =
            GetUsableClipCount();

        if (validCount <= 0)
        {
            return false;
        }

        System.Random random =
            GetPlaybackRandom();

        int randomStartIndex =
            random.Next(
                0,
                validCount
            );

        int selectedIndex =
            -1;

        /*
         * Inspector 陣列中若有空 Clip，
         * 不應讓整次播放隨機失敗。
         *
         * 從隨機起點繞一圈，尋找第一個有效 Clip。
         */
        for (int offset = 0;
             offset < validCount;
             offset++)
        {
            int candidateIndex =
                (randomStartIndex + offset) %
                validCount;

            if (clips[candidateIndex] != null)
            {
                selectedIndex =
                    candidateIndex;

                break;
            }
        }

        if (selectedIndex < 0)
        {
            return false;
        }

        variantIndex =
            (byte)selectedIndex;

        float safeMinimumPitch =
            Mathf.Clamp(
                Mathf.Min(
                    minimumPitch,
                    maximumPitch
                ),
                0.01f,
                3f
            );

        float safeMaximumPitch =
            Mathf.Clamp(
                Mathf.Max(
                    minimumPitch,
                    maximumPitch
                ),
                safeMinimumPitch,
                3f
            );

        double pitchT =
            random.NextDouble();

        selectedPitch =
            Mathf.Lerp(
                safeMinimumPitch,
                safeMaximumPitch,
                (float)pitchT
            );

        return GetClip(
                variantIndex
            ) != null;
    }

    /// <summary>
    /// 依照已選定的索引取得 Clip。
    ///
    /// RPC 接收端只能解析 State Authority 傳來的索引，
    /// 不可再次 Random，否則每名玩家會聽到不同變體。
    /// </summary>
    public AudioClip GetClip(
        byte variantIndex
    )
    {
        int validCount =
            GetUsableClipCount();

        int index =
            variantIndex;

        if (index < 0 ||
            index >= validCount)
        {
            return null;
        }

        return clips[index];
    }

    private int GetUsableClipCount()
    {
        if (clips == null)
        {
            return 0;
        }

        return Mathf.Min(
            clips.Length,
            byte.MaxValue + 1
        );
    }

    private System.Random GetPlaybackRandom()
    {
        if (playbackRandom == null)
        {
            int seed =
                unchecked(
                    Environment.TickCount *
                    397 ^
                    GetInstanceID()
                );

            playbackRandom =
                new System.Random(
                    seed
                );
        }

        return playbackRandom;
    }

    #endregion

    // =====================================================================
    #region Validation

    private void OnValidate()
    {
        networkId =
            Mathf.Clamp(
                networkId,
                0,
                ushort.MaxValue
            );

        minimumPitch =
            Mathf.Clamp(
                minimumPitch,
                0.01f,
                3f
            );

        maximumPitch =
            Mathf.Clamp(
                maximumPitch,
                0.01f,
                3f
            );

        if (maximumPitch <
            minimumPitch)
        {
            maximumPitch =
                minimumPitch;
        }

        worldMinimumDistance =
            Mathf.Max(
                0.01f,
                worldMinimumDistance
            );

        worldMaximumDistance =
            Mathf.Max(
                worldMinimumDistance,
                worldMaximumDistance
            );
    }

    #endregion
}