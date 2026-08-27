using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;


/// <summary>
/// 每台遊戲 Client 的 Gameplay SFX 播放服務。
///
/// ====================================================================
///
/// 負責：
///
/// 1. Local First Person OneShot。
/// 2. Local First Person Loop。
/// 3. Network RPC 接收後的 World 3D OneShot。
/// 4. 重用 AudioSource Pool，避免自動武器反覆 Instantiate / Destroy。
///
/// ====================================================================
///
/// 不負責：
///
/// 1. 決定一發子彈是否真的成立。
/// 2. 發送 Fusion RPC。
/// 3. 管理 BGM。
/// 4. 修改 Gameplay State。
/// </summary>
[DisallowMultipleComponent]
public class GameplayAudioService :
    MonoBehaviour
{
    public static GameplayAudioService Instance
    {
        get;
        private set;
    }

    // =====================================================================
    #region Lifecycle

    [Header("生命週期")]

    [SerializeField]
    [Tooltip(
        "開啟後，GameplayAudioService 會脫離父物件並使用 DontDestroyOnLoad。\n\n" +
        "如果 Menu 與 Gameplay 共用同一個 AudioMixer，且希望切場景後保留服務，可以開啟。\n" +
        "若每個 Gameplay Scene 都會建立自己的 Audio Service，則關閉。")]
    private bool persistAcrossScenes =
        true;

    #endregion

    // =====================================================================
    #region Mixer Fallback

    [Header("Mixer Fallback")]

    [SerializeField]
    [Tooltip(
        "Local First Person Cue 沒有指定 Output Mixer Group 時使用的群組。\n\n" +
        "建議建立 SFX/FirstPerson Group，讓玩家未來能獨立控制第一人稱細節音量。")]
    private AudioMixerGroup localFirstPersonFallbackGroup;

    [SerializeField]
    [Tooltip(
        "Network World Cue 沒有指定 Output Mixer Group 時使用的群組。\n\n" +
        "建議建立 SFX/World Group，供槍聲、受傷聲與世界撞擊使用。")]
    private AudioMixerGroup worldSFXFallbackGroup;

    #endregion

    // =====================================================================
    #region OneShot Pool

    [Header("OneShot AudioSource Pool")]

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "Awake 時預先建立多少個 OneShot AudioSource。\n\n" +
        "會同時供 Local 與 World OneShot 使用。" +
        "第一輪可使用 16；多人與全自動武器較密集時可提高。")]
    private int initialOneShotVoices =
        16;

    [SerializeField]
    [Min(1)]
    [Tooltip(
        "OneShot Pool 允許擴充到的最高 AudioSource 數量。\n\n" +
        "所有 Voice 都正在播放且已達上限時，新聲音會被捨棄，" +
        "不會硬切斷一個仍在播放的重要聲音。\n\n" +
        "第一輪可使用 48 或 64。")]
    private int maximumOneShotVoices =
        64;

    [SerializeField]
    [Tooltip(
        "開啟後，OneShot Pool 滿載而捨棄新聲音時顯示 Warning。\n\n" +
        "壓力測試時可開啟；正式遊戲建議關閉，避免極端戰鬥洗滿 Console。")]
    private bool warnWhenOneShotPoolExhausted =
        false;

    #endregion

    // =====================================================================
    #region Runtime State

    private Transform runtimeAudioRoot;

    private readonly List<AudioSource>
        oneShotVoices =
            new List<AudioSource>(64);

    private readonly List<AudioSource>
        localLoopVoices =
            new List<AudioSource>(8);

    private readonly Dictionary<int, ActiveLocalLoop>
        activeLocalLoops =
            new Dictionary<int, ActiveLocalLoop>();

    private int nextLocalLoopHandle =
        1;

    /// <summary>
    /// Local Loop 需要記住 Cue 基礎音量與 Pitch，
    /// 才能讓外部系統用 Scale／Multiplier 安全調整。
    /// </summary>
    private sealed class ActiveLocalLoop
    {
        public AudioSource Source;
        public float BaseVolume;
        public float BasePitch;
    }

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (Instance != null &&
            Instance != this)
        {
            Debug.LogWarning(
                "[Gameplay Audio] 場景中出現第二個 GameplayAudioService，刪除後建立的重複物件。",
                this
            );

            Destroy(
                gameObject
            );

            return;
        }

        Instance =
            this;

        if (persistAcrossScenes)
        {
            if (transform.parent != null)
            {
                transform.SetParent(
                    null,
                    true
                );
            }

            DontDestroyOnLoad(
                gameObject
            );
        }

        CreateRuntimeAudioRoot();

        int safeMaximum =
            Mathf.Max(
                1,
                maximumOneShotVoices
            );

        int preloadCount =
            Mathf.Clamp(
                initialOneShotVoices,
                1,
                safeMaximum
            );

        for (int i = 0;
             i < preloadCount;
             i++)
        {
            oneShotVoices.Add(
                CreateAudioSource(
                    $"OneShotVoice_{i:00}"
                )
            );
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            StopAllLocalLoops();

            Instance =
                null;
        }
    }

    private void OnValidate()
    {
        initialOneShotVoices =
            Mathf.Max(
                1,
                initialOneShotVoices
            );

        maximumOneShotVoices =
            Mathf.Max(
                initialOneShotVoices,
                maximumOneShotVoices
            );
    }

    #endregion

    // =====================================================================
    #region Local OneShot

    /// <summary>
    /// 只在這台 Client 播放一次第一人稱 2D 聲音。
    ///
    /// 不發 RPC，不會讓其他玩家聽到。
    /// 適合由第一人稱 ViewModel Animation Event 呼叫。
    /// </summary>
    public bool PlayLocalOneShot(
        GameplayAudioCue cue,
        float volumeScale = 1f
    )
    {
        if (cue == null ||
            cue.TryCreatePlaybackSelection(
                out byte variantIndex,
                out float selectedPitch
            ) == false)
        {
            return false;
        }

        AudioClip clip =
            cue.GetClip(
                variantIndex
            );

        if (clip == null)
        {
            return false;
        }

        AudioSource source =
            AcquireOneShotVoice();

        if (source == null)
        {
            return false;
        }

        ConfigureSource(
            source,
            cue,
            clip,
            Vector3.zero,
            true,
            false,
            selectedPitch,
            volumeScale
        );

        source.Play();

        return true;
    }

    #endregion

    // =====================================================================
    #region World OneShot

    /// <summary>
    /// 播放已由 NetworkPlayerAudioEmitter 解決完成的世界 3D OneShot。
    ///
    /// variantIndex 與 selectedPitch 必須來自 State Authority RPC，
    /// 接收端不可重新 Random。
    /// </summary>
    public bool PlayWorldOneShot(
        GameplayAudioCue cue,
        byte variantIndex,
        float selectedPitch,
        Vector3 worldPosition,
        float volumeScale = 1f
    )
    {
        if (cue == null)
        {
            return false;
        }

        AudioClip clip =
            cue.GetClip(
                variantIndex
            );

        if (clip == null)
        {
            return false;
        }

        AudioSource source =
            AcquireOneShotVoice();

        if (source == null)
        {
            return false;
        }

        ConfigureSource(
            source,
            cue,
            clip,
            worldPosition,
            false,
            false,
            selectedPitch,
            volumeScale
        );

        source.Play();

        return true;
    }

    #endregion

    // =====================================================================
    #region Local Loop

    /// <summary>
    /// 開始一個只在這台 Client 播放的第一人稱 2D Loop。
    ///
    /// 成功時回傳大於 0 的 Handle；失敗回傳 -1。
    /// 呼叫端必須保存 Handle，結束狀態時停止對應 Loop。
    /// </summary>
    public int StartLocalLoop(
        GameplayAudioCue cue,
        float volumeScale = 1f
    )
    {
        if (cue == null ||
            cue.TryCreatePlaybackSelection(
                out byte variantIndex,
                out float selectedPitch
            ) == false)
        {
            return -1;
        }

        AudioClip clip =
            cue.GetClip(
                variantIndex
            );

        if (clip == null)
        {
            return -1;
        }

        AudioSource source =
            AcquireLocalLoopVoice();

        if (source == null)
        {
            return -1;
        }

        ConfigureSource(
            source,
            cue,
            clip,
            Vector3.zero,
            true,
            true,
            selectedPitch,
            volumeScale
        );

        int handle =
            CreateNextLocalLoopHandle();

        if (handle < 0)
        {
            ResetAudioSource(
                source
            );

            return -1;
        }

        activeLocalLoops.Add(
            handle,
            new ActiveLocalLoop
            {
                Source = source,
                BaseVolume = cue.Volume,
                BasePitch = selectedPitch
            }
        );

        source.Play();

        return handle;
    }

    public void StopLocalLoop(
        int handle
    )
    {
        if (activeLocalLoops.TryGetValue(
                handle,
                out ActiveLocalLoop activeLoop
            ) == false)
        {
            return;
        }

        if (activeLoop != null &&
            activeLoop.Source != null)
        {
            ResetAudioSource(
                activeLoop.Source
            );
        }

        activeLocalLoops.Remove(
            handle
        );
    }

    public void SetLocalLoopVolumeScale(
        int handle,
        float volumeScale
    )
    {
        if (activeLocalLoops.TryGetValue(
                handle,
                out ActiveLocalLoop activeLoop
            ) == false ||
            activeLoop == null ||
            activeLoop.Source == null)
        {
            return;
        }

        activeLoop.Source.volume =
            Mathf.Clamp01(
                activeLoop.BaseVolume *
                Mathf.Max(
                    0f,
                    volumeScale
                )
            );
    }

    public void SetLocalLoopPitchMultiplier(
        int handle,
        float pitchMultiplier
    )
    {
        if (activeLocalLoops.TryGetValue(
                handle,
                out ActiveLocalLoop activeLoop
            ) == false ||
            activeLoop == null ||
            activeLoop.Source == null)
        {
            return;
        }

        activeLoop.Source.pitch =
            Mathf.Clamp(
                activeLoop.BasePitch *
                Mathf.Max(
                    0.01f,
                    pitchMultiplier
                ),
                0.01f,
                3f
            );
    }

    public bool IsLocalLoopPlaying(
        int handle
    )
    {
        return
            activeLocalLoops.TryGetValue(
                handle,
                out ActiveLocalLoop activeLoop
            ) &&
            activeLoop != null &&
            activeLoop.Source != null &&
            activeLoop.Source.isPlaying;
    }

    public void StopAllLocalLoops()
    {
        if (activeLocalLoops.Count == 0)
        {
            return;
        }

        int[] handles =
            new int[activeLocalLoops.Count];

        activeLocalLoops.Keys.CopyTo(
            handles,
            0
        );

        for (int i = 0;
             i < handles.Length;
             i++)
        {
            StopLocalLoop(
                handles[i]
            );
        }
    }

    #endregion

    // =====================================================================
    #region Source Pool

    private AudioSource AcquireOneShotVoice()
    {
        for (int i = 0;
             i < oneShotVoices.Count;
             i++)
        {
            AudioSource source =
                oneShotVoices[i];

            if (source != null &&
                source.isPlaying == false)
            {
                ResetAudioSource(
                    source
                );

                return source;
            }
        }

        int safeMaximum =
            Mathf.Max(
                1,
                maximumOneShotVoices
            );

        if (oneShotVoices.Count <
            safeMaximum)
        {
            AudioSource newSource =
                CreateAudioSource(
                    $"OneShotVoice_{oneShotVoices.Count:00}"
                );

            oneShotVoices.Add(
                newSource
            );

            return newSource;
        }

        if (warnWhenOneShotPoolExhausted)
        {
            Debug.LogWarning(
                "[Gameplay Audio] OneShot Pool 已滿，新聲音被捨棄。" +
                $"\nMaximum Voices：{safeMaximum}",
                this
            );
        }

        return null;
    }

    private AudioSource AcquireLocalLoopVoice()
    {
        for (int i = 0;
             i < localLoopVoices.Count;
             i++)
        {
            AudioSource source =
                localLoopVoices[i];

            if (source != null &&
                source.isPlaying == false &&
                IsLoopVoiceActive(
                    source
                ) == false)
            {
                ResetAudioSource(
                    source
                );

                return source;
            }
        }

        AudioSource newSource =
            CreateAudioSource(
                $"LocalLoopVoice_{localLoopVoices.Count:00}"
            );

        localLoopVoices.Add(
            newSource
        );

        return newSource;
    }

    private bool IsLoopVoiceActive(
        AudioSource source
    )
    {
        foreach (KeyValuePair<int, ActiveLocalLoop> pair
                 in activeLocalLoops)
        {
            ActiveLocalLoop activeLoop =
                pair.Value;

            if (activeLoop != null &&
                activeLoop.Source == source)
            {
                return true;
            }
        }

        return false;
    }

    private int CreateNextLocalLoopHandle()
    {
        int attempts =
            0;

        while (attempts <
               int.MaxValue)
        {
            int candidate =
                nextLocalLoopHandle++;

            if (nextLocalLoopHandle <= 0)
            {
                nextLocalLoopHandle =
                    1;
            }

            if (candidate > 0 &&
                activeLocalLoops.ContainsKey(
                    candidate
                ) == false)
            {
                return candidate;
            }

            attempts++;
        }

        return -1;
    }

    #endregion

    // =====================================================================
    #region AudioSource Configuration

    private void ConfigureSource(
        AudioSource source,
        GameplayAudioCue cue,
        AudioClip clip,
        Vector3 position,
        bool forceLocal2D,
        bool loop,
        float selectedPitch,
        float volumeScale
    )
    {
        source.transform.position =
            position;

        source.clip =
            clip;

        source.loop =
            loop;

        source.playOnAwake =
            false;

        source.volume =
            Mathf.Clamp01(
                cue.Volume *
                Mathf.Max(
                    0f,
                    volumeScale
                )
            );

        source.pitch =
            Mathf.Clamp(
                selectedPitch,
                0.01f,
                3f
            );

        source.spatialBlend =
            forceLocal2D
                ? 0f
                : cue.WorldSpatialBlend;

        source.rolloffMode =
            cue.WorldRolloffMode;

        source.minDistance =
            cue.WorldMinimumDistance;

        source.maxDistance =
            cue.WorldMaximumDistance;

        source.dopplerLevel =
            forceLocal2D
                ? 0f
                : cue.WorldDopplerLevel;

        source.outputAudioMixerGroup =
            cue.OutputMixerGroup != null
                ? cue.OutputMixerGroup
                : forceLocal2D
                    ? localFirstPersonFallbackGroup
                    : worldSFXFallbackGroup;
    }

    private AudioSource CreateAudioSource(
        string objectName
    )
    {
        CreateRuntimeAudioRoot();

        GameObject sourceObject =
            new GameObject(
                objectName
            );

        sourceObject.transform.SetParent(
            runtimeAudioRoot,
            false
        );

        AudioSource source =
            sourceObject.AddComponent<AudioSource>();

        ResetAudioSource(
            source
        );

        return source;
    }

    private void ResetAudioSource(
        AudioSource source
    )
    {
        if (source == null)
        {
            return;
        }

        source.Stop();

        source.clip =
            null;

        source.loop =
            false;

        source.playOnAwake =
            false;

        source.volume =
            1f;

        source.pitch =
            1f;

        source.spatialBlend =
            0f;

        source.dopplerLevel =
            0f;

        source.outputAudioMixerGroup =
            null;
    }

    private void CreateRuntimeAudioRoot()
    {
        if (runtimeAudioRoot != null)
        {
            return;
        }

        GameObject rootObject =
            new GameObject(
                "RuntimeAudioVoices"
            );

        rootObject.transform.SetParent(
            transform,
            false
        );

        runtimeAudioRoot =
            rootObject.transform;
    }

    #endregion
}