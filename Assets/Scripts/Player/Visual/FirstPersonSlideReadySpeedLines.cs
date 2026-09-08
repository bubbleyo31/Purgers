using UnityEngine;


/// <summary>
/// 第一人稱「已達可滑鏟速度」提示速度線。
///
/// ====================================================================
///
/// 這支腳本與 FirstPersonMovementEffects 完全獨立：
///
/// 1. 使用另一個 ParticleSystem。
/// 2. 使用自己的顯示與隱藏速度門檻。
/// 3. 不讀取、不播放、不停止舊速度線。
/// 4. 只綁定本機 Input Authority 玩家。
///
/// ====================================================================
///
/// 淡入淡出採用「粒子發射密度」：
///
/// Fade = 0
/// → Rate Over Time / Distance 為 0。
///
/// Fade = 1
/// → 恢復 ParticleSystem 原本在 Inspector 設定的發射倍率。
///
/// 因此這支腳本不會改動粒子的 Start Speed、Start Lifetime、
/// Start Size、Shape、Noise 或 Renderer 材質。
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonSlideReadySpeedLines :
    MonoBehaviour
{
    // =====================================================================
    #region Singleton


    /// <summary>
    /// 場景中唯一的本機滑鏟速度提示控制器。
    /// </summary>
    public static FirstPersonSlideReadySpeedLines Singleton
    {
        get;
        private set;
    }


    #endregion


    // =====================================================================
    #region Particle Reference


    [Header("獨立滑鏟速度線")]


    [SerializeField]
    [Tooltip(
        "專門用來提示『已達可滑鏟速度』的新 ParticleSystem。\n\n" +
        "不可指定 FirstPersonMovementEffects 正在使用的舊速度線；" +
        "請建立一個全新的 Particle System 並拖進來。\n\n" +
        "本腳本不會自動搜尋子物件，避免誤抓到舊速度線。")]
    private ParticleSystem slideReadySpeedLineParticles;


    #endregion


    // =====================================================================
    #region Independent Speed Thresholds


    [Header("獨立速度門檻")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "玩家水平實際速度達到此數值時，開始淡入滑鏟提示速度線。\n\n" +
        "這是本視覺元件自己的門檻，不會讀取：\n" +
        "- FirstPersonMovementEffects 的 Minimum Visible Speed。\n" +
        "- PlayerSlideController 的 Minimum Entry Speed Multiplier。\n\n" +
        "請依遊戲實際可滑鏟速度手動調整。")]
    private float showAtHorizontalSpeed =
        8f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "速度線已經顯示後，水平速度低於此數值才開始淡出。\n\n" +
        "此值應略低於 Show At Horizontal Speed，形成遲滯區間，" +
        "避免速度在門檻附近波動時每幀反覆淡入淡出。\n\n" +
        "例如顯示門檻為 8，可先將隱藏門檻設為 7.25。")]
    private float hideBelowHorizontalSpeed =
        7.25f;


    #endregion


    // =====================================================================
    #region Fade Settings


    [Header("淡入淡出")]


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "從完全隱藏到完整發射密度需要的秒數。\n\n" +
        "0 = 瞬間顯示。\n" +
        "第一輪建議 0.15～0.25 秒。")]
    private float fadeInDuration =
        0.2f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "從完整發射密度降到停止發射需要的秒數。\n\n" +
        "0 = 瞬間停止發射。\n" +
        "第一輪建議 0.2～0.35 秒。")]
    private float fadeOutDuration =
        0.25f;


    [SerializeField]
    [Tooltip(
        "淡出完成後是否立即清除仍存活的粒子。\n\n" +
        "關閉：停止發射後，舊粒子依 Start Lifetime 自然消失，較柔和。\n" +
        "開啟：淡出完成後立刻清空，適合粒子 Lifetime 很長的設定。")]
    private bool clearParticlesWhenFullyHidden =
        false;


    #endregion


    // =====================================================================
    #region Debug


    [Header("執行時除錯")]


    [SerializeField]
    [Tooltip(
        "開啟後在 Inspector 顯示目前水平速度、門檻狀態與 Fade 值。\n\n" +
        "正式 Build 建議關閉。")]
    private bool showDebugInformation =
        false;


    [SerializeField]
    [Tooltip(
        "目前綁定玩家的水平實際速度。\n\n" +
        "此欄位只供執行時觀察，不需要手動修改。")]
    private float debugHorizontalSpeed;


    [SerializeField]
    [Tooltip(
        "目前是否已跨過獨立的可滑鏟速度門檻。\n\n" +
        "此欄位只供執行時觀察。")]
    private bool debugSpeedReady;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip(
        "目前粒子發射密度淡入淡出比例。\n\n" +
        "0 = 不發射；1 = 使用原始完整發射倍率。")]
    private float debugFade;


    #endregion


    // =====================================================================
    #region Runtime State


    /// <summary>
    /// 目前具有 Input Authority 的本機玩家。
    /// </summary>
    private Player targetPlayer;


    /// <summary>
    /// 經過顯示／隱藏雙門檻後的穩定狀態。
    /// </summary>
    private bool speedReady;


    /// <summary>
    /// 目前發射密度比例，範圍 0～1。
    /// </summary>
    private float currentFade;


    /// <summary>
    /// ParticleSystem 原始 Rate Over Time Multiplier。
    /// Fade = 1 時必須完整還原此值。
    /// </summary>
    private float originalRateOverTimeMultiplier;


    /// <summary>
    /// ParticleSystem 原始 Rate Over Distance Multiplier。
    /// 同時支援用距離發射的速度線設定。
    /// </summary>
    private float originalRateOverDistanceMultiplier;


    /// <summary>
    /// 是否已成功保存粒子原始發射倍率。
    /// </summary>
    private bool emissionSettingsCaptured;


    #endregion


    // =====================================================================
    #region Unity Lifecycle


    private void Awake()
    {
        RegisterSingleton();

        if (slideReadySpeedLineParticles == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonSlideReadySpeedLines)}] " +
                "尚未指定獨立的 Slide Ready Speed Line ParticleSystem。",
                this
            );

            enabled =
                false;

            return;
        }

        CaptureOriginalEmissionSettings();

        currentFade =
            0f;

        ApplyEmissionFade(
            0f
        );

        slideReadySpeedLineParticles.Stop(
            true,
            ParticleSystemStopBehavior
                .StopEmittingAndClear
        );
    }


    private void Update()
    {
        UpdateSpeedReadyState();
        UpdateFade();
        UpdateDebugInformation();
    }


    private void OnDisable()
    {
        ForceHideAndClear();
    }


    private void OnDestroy()
    {
        if (Singleton == this)
        {
            Singleton =
                null;
        }
    }


    private void OnValidate()
    {
        showAtHorizontalSpeed =
            Mathf.Max(
                0f,
                showAtHorizontalSpeed
            );

        hideBelowHorizontalSpeed =
            Mathf.Clamp(
                hideBelowHorizontalSpeed,
                0f,
                showAtHorizontalSpeed
            );

        fadeInDuration =
            Mathf.Max(
                0f,
                fadeInDuration
            );

        fadeOutDuration =
            Mathf.Max(
                0f,
                fadeOutDuration
            );
    }


    #endregion


    // =====================================================================
    #region Singleton Management


    private void RegisterSingleton()
    {
        if (Singleton == null)
        {
            Singleton =
                this;

            return;
        }

        if (Singleton == this)
        {
            return;
        }

        Debug.LogError(
            $"場景中只能存在一個 " +
            $"{nameof(FirstPersonSlideReadySpeedLines)}。",
            this
        );

        enabled =
            false;
    }


    #endregion


    // =====================================================================
    #region Local Player Binding


    /// <summary>
    /// 綁定目前的本機玩家。
    ///
    /// 由具有 Input Authority 的 PlayerLocalView 呼叫。
    /// </summary>
    public void SetTarget(
        Player player
    )
    {
        targetPlayer =
            player;

        ResetVisualState(
            true
        );
    }


    /// <summary>
    /// 只有傳入玩家就是目前綁定目標時才解除，
    /// 避免其他 Proxy Despawn 誤清除本機畫面。
    /// </summary>
    public void ClearTarget(
        Player player
    )
    {
        if (targetPlayer != player)
        {
            return;
        }

        targetPlayer =
            null;

        ResetVisualState(
            true
        );
    }


    #endregion


    // =====================================================================
    #region Speed Evaluation


    /// <summary>
    /// 使用本元件自己的雙門檻更新可滑鏟速度提示狀態。
    ///
    /// 只讀取水平速度，不使用 CurrentWorldSpeed，
    /// 因此垂直跳躍或高速墜落不會單獨觸發速度線。
    /// </summary>
    private void UpdateSpeedReadyState()
    {
        float horizontalSpeed =
            targetPlayer != null &&
            targetPlayer.Movement != null
                ? targetPlayer.Movement.HorizontalSpeed
                : 0f;

        if (targetPlayer == null)
        {
            speedReady =
                false;
        }
        else if (speedReady)
        {
            if (horizontalSpeed <=
                hideBelowHorizontalSpeed)
            {
                speedReady =
                    false;
            }
        }
        else if (horizontalSpeed >=
                 showAtHorizontalSpeed)
        {
            speedReady =
                true;
        }

        if (showDebugInformation)
        {
            debugHorizontalSpeed =
                horizontalSpeed;
        }
    }


    #endregion


    // =====================================================================
    #region Particle Fade


    /// <summary>
    /// 將目前 Fade 朝目標值平滑移動，並同步控制粒子發射密度。
    /// </summary>
    private void UpdateFade()
    {
        if (slideReadySpeedLineParticles == null ||
            emissionSettingsCaptured == false)
        {
            return;
        }

        float targetFade =
            speedReady
                ? 1f
                : 0f;

        if (targetFade > 0f &&
            slideReadySpeedLineParticles.isPlaying == false)
        {
            slideReadySpeedLineParticles.Play(
                true
            );
        }

        float duration =
            targetFade > currentFade
                ? Mathf.Max(
                    0f,
                    fadeInDuration
                )
                : Mathf.Max(
                    0f,
                    fadeOutDuration
                );

        currentFade =
            duration <= 0.0001f
                ? targetFade
                : Mathf.MoveTowards(
                    currentFade,
                    targetFade,
                    Time.deltaTime /
                    duration
                );

        ApplyEmissionFade(
            currentFade
        );

        if (speedReady == false &&
            currentFade <= 0.0001f)
        {
            StopParticlesAtFullyHidden();
        }
    }


    /// <summary>
    /// 保存新粒子在 Inspector 設定的原始發射倍率。
    /// </summary>
    private void CaptureOriginalEmissionSettings()
    {
        if (slideReadySpeedLineParticles == null)
        {
            return;
        }

        ParticleSystem.EmissionModule emission =
            slideReadySpeedLineParticles.emission;

        originalRateOverTimeMultiplier =
            emission.rateOverTimeMultiplier;

        originalRateOverDistanceMultiplier =
            emission.rateOverDistanceMultiplier;

        emissionSettingsCaptured =
            true;
    }


    /// <summary>
    /// 套用 0～1 的發射密度比例。
    ///
    /// 同時調整 Rate Over Time 與 Rate Over Distance，
    /// 但不改變兩者原始 Curve 的形狀。
    /// </summary>
    private void ApplyEmissionFade(
        float fade
    )
    {
        if (slideReadySpeedLineParticles == null ||
            emissionSettingsCaptured == false)
        {
            return;
        }

        float clampedFade =
            Mathf.Clamp01(
                fade
            );

        ParticleSystem.EmissionModule emission =
            slideReadySpeedLineParticles.emission;

        emission.rateOverTimeMultiplier =
            originalRateOverTimeMultiplier *
            clampedFade;

        emission.rateOverDistanceMultiplier =
            originalRateOverDistanceMultiplier *
            clampedFade;
    }


    /// <summary>
    /// Fade 完全歸零後才停止粒子。
    /// </summary>
    private void StopParticlesAtFullyHidden()
    {
        if (slideReadySpeedLineParticles == null ||
            slideReadySpeedLineParticles.isPlaying == false)
        {
            return;
        }

        ParticleSystemStopBehavior stopBehavior =
            clearParticlesWhenFullyHidden
                ? ParticleSystemStopBehavior
                    .StopEmittingAndClear
                : ParticleSystemStopBehavior
                    .StopEmitting;

        slideReadySpeedLineParticles.Stop(
            true,
            stopBehavior
        );
    }


    /// <summary>
    /// 玩家切換、死亡或物件停用時立即清除本機畫面。
    /// </summary>
    private void ForceHideAndClear()
    {
        speedReady =
            false;

        currentFade =
            0f;

        ApplyEmissionFade(
            0f
        );

        if (slideReadySpeedLineParticles != null)
        {
            slideReadySpeedLineParticles.Stop(
                true,
                ParticleSystemStopBehavior
                    .StopEmittingAndClear
            );
        }
    }


    private void ResetVisualState(
        bool clearParticles
    )
    {
        speedReady =
            false;

        currentFade =
            0f;

        ApplyEmissionFade(
            0f
        );

        if (clearParticles &&
            slideReadySpeedLineParticles != null)
        {
            slideReadySpeedLineParticles.Stop(
                true,
                ParticleSystemStopBehavior
                    .StopEmittingAndClear
            );
        }
    }


    #endregion


    // =====================================================================
    #region Debug Update


    private void UpdateDebugInformation()
    {
        if (showDebugInformation == false)
        {
            return;
        }

        debugSpeedReady =
            speedReady;

        debugFade =
            currentFade;
    }


    #endregion
}
