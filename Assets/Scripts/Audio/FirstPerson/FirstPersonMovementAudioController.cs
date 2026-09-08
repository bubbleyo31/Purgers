using Fusion;
using UnityEngine;


/// <summary>
/// 本機玩家的第一人稱移動聲音控制器。
///
/// ====================================================================
///
/// 本腳本只會在 Player NetworkObject.HasInputAuthority 的裝置播放：
///
/// - Walk Footstep OneShot。
/// - Run Footstep OneShot。
/// - Ground Jump OneShot。
/// - Double Jump OneShot。
/// - Landing OneShot。
/// - Slide Start / Loop / End。
/// - Grapple Wind Loop。
///
/// ====================================================================
///
/// 所有聲音都使用 GameplayAudioService 的 Local API：
///
/// PlayLocalOneShot()
/// StartLocalLoop()
///
/// 不發送 Fusion RPC，也不經過 NetworkPlayerAudioEmitter，
/// 因此其他玩家不會聽見這些第一人稱細節聲。
///
/// ====================================================================
///
/// 此元件掛在 Player Network Prefab Root。
/// 所有 Cue 必須設為 Local Only：Network ID = 0。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerSlideController))]
[RequireComponent(typeof(PlayerGrapple))]
public class FirstPersonMovementAudioController :
    NetworkBehaviour
{
    // =====================================================================
    #region Core References


    [Header("玩家模組引用")]


    [SerializeField]
    [Tooltip(
        "同一個 Player Network Prefab Root 上的 Player。\n\n" +
        "用來確認本機玩家與取得共用狀態；留空會自動取得。")]
    private Player player;


    [SerializeField]
    [Tooltip(
        "同一個 Player Root 上的 PlayerMovement。\n\n" +
        "用來讀取 Grounded、Horizontal Speed 與 World Speed；" +
        "留空會自動取得。")]
    private PlayerMovement movement;


    [SerializeField]
    [Tooltip(
        "同一個 Player Root 上的 PlayerStateMachine。\n\n" +
        "用來分辨 Walk、Run、Jump、DoubleJump 與 GrappleAirborne；" +
        "留空會自動取得。")]
    private PlayerStateMachine stateMachine;


    [SerializeField]
    [Tooltip(
        "同一個 Player Root 上的 PlayerSlideController。\n\n" +
        "用來偵測滑鏟開始、持續與結束；留空會自動取得。")]
    private PlayerSlideController slideController;


    [SerializeField]
    [Tooltip(
        "同一個 Player Root 上的 PlayerGrapple。\n\n" +
        "用來判斷普通鈎索拉動與鈎索釋放 Momentum；" +
        "Support 拉取目標的 Tether 不會被當成玩家自己的鈎索風聲。\n\n" +
        "留空會自動取得。")]
    private PlayerGrapple grapple;


    #endregion


    // =====================================================================
    #region Footstep Cues


    [Header("走路與跑步腳步聲（Local OneShot）")]


    [SerializeField]
    [Tooltip(
        "玩家處於 Walk、接觸地面且沒有滑鏟時播放的本機腳步 Cue。\n\n" +
        "可在同一個 GameplayAudioCue 放入多個腳步 AudioClip，" +
        "由 Cue 自己隨機選擇變體與 Pitch。\n\n" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue walkFootstepCue;


    [SerializeField]
    [Tooltip(
        "玩家處於 Run、接觸地面且沒有滑鏟時播放的本機腳步 Cue。\n\n" +
        "可以與 Walk 使用不同 Clip、音量與 Pitch；" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue runFootstepCue;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "Walk 狀態下兩次本機腳步聲之間的秒數。\n\n" +
        "這是第一人稱節奏，不依賴第三人稱 Animator Event。\n" +
        "第一輪建議 0.42～0.55 秒。")]
    private float walkFootstepInterval =
        0.48f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "Run 狀態下兩次本機腳步聲之間的秒數。\n\n" +
        "應短於 Walk 間隔；第一輪建議 0.28～0.36 秒。")]
    private float runFootstepInterval =
        0.32f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "剛從 Idle／空中進入 Walk 或 Run 後，第一個腳步聲等待多久。\n\n" +
        "小於完整步伐間隔能讓起步反應更快；第一輪建議 0.08～0.15 秒。")]
    private float firstFootstepDelay =
        0.1f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "水平速度低於此值時，即使狀態暫時仍是 Walk／Run，" +
        "也不播放腳步聲。\n\n" +
        "用來過濾 KCC 停止附近的小幅速度抖動。")]
    private float minimumFootstepHorizontalSpeed =
        0.5f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Walk 與 Run 腳步 Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量；0 = 靜音。")]
    private float footstepVolumeScale =
        1f;


    #endregion


    // =====================================================================
    #region Jump And Landing Cues


    [Header("跳躍與落地（Local OneShot）")]


    [SerializeField]
    [Tooltip(
        "玩家進入 Ground Jump 狀態時播放一次。\n\n" +
        "普通跳躍與成功的 Slide Jump 都屬於 Ground Jump；" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue groundJumpCue;


    [SerializeField]
    [Tooltip(
        "玩家成功執行二段跳時播放一次。\n\n" +
        "可留空；必須使用 Network ID = 0。")]
    private GameplayAudioCue doubleJumpCue;


    [SerializeField]
    [Tooltip(
        "玩家從空中重新接觸地面時播放一次。\n\n" +
        "包含普通落地、鏟跳落地與鈎索後落地；" +
        "可留空，必須使用 Network ID = 0。")]
    private GameplayAudioCue landingCue;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Ground Jump 與 Double Jump Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量；0 = 靜音。")]
    private float jumpVolumeScale =
        1f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Landing Cue 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量；0 = 靜音。")]
    private float landingVolumeScale =
        1f;


    #endregion


    // =====================================================================
    #region Slide Cues


    [Header("滑鏟（Local OneShot / Loop）")]


    [SerializeField]
    [Tooltip(
        "IsSliding 從 false 變成 true 時播放一次的起鏟聲。\n\n" +
        "例如衣物摩擦、腳踩地或起鏟撞擊；可留空。\n" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue slideStartCue;


    [SerializeField]
    [Tooltip(
        "IsSliding 維持 true 時播放的循環摩擦聲。\n\n" +
        "AudioClip 必須能無縫循環；GameplayAudioService 會強制以 Local 2D Loop 播放。\n" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue slideLoopCue;


    [SerializeField]
    [Tooltip(
        "IsSliding 從 true 變成 false 時播放一次的收尾聲。\n\n" +
        "跳出滑鏟、放開蹲下或速度不足結束都會觸發；可留空。\n" +
        "必須使用 Network ID = 0。")]
    private GameplayAudioCue slideEndCue;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "Slide Loop 從靜音淡入完整音量所需秒數。\n\n" +
        "第一輪建議 0.05～0.12 秒。")]
    private float slideLoopFadeInDuration =
        0.08f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "滑鏟結束後，Slide Loop 淡出至停止所需秒數。\n\n" +
        "第一輪建議 0.08～0.18 秒。")]
    private float slideLoopFadeOutDuration =
        0.12f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "滑鏟速度到達此數值時，Slide Loop Pitch 使用 Maximum Pitch。\n\n" +
        "速度低於此值時會在 Minimum 與 Maximum Pitch 間內插。")]
    private float slideFullPitchSpeed =
        20f;


    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip("低速滑鏟時的 Slide Loop Pitch 倍率。")]
    private float slideMinimumPitchMultiplier =
        0.9f;


    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip("速度到達 Slide Full Pitch Speed 時的 Slide Loop Pitch 倍率。")]
    private float slideMaximumPitchMultiplier =
        1.2f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Slide Start 與 Slide End OneShot 的額外音量倍率。\n\n" +
        "1 = 使用 Cue 原始音量；0 = 靜音。")]
    private float slideOneShotVolumeScale =
        1f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "Slide Loop 完整淡入後的額外音量倍率。\n\n" +
        "實際音量仍會乘上目前 Fade。")]
    private float slideLoopVolumeScale =
        1f;


    #endregion


    // =====================================================================
    #region Grapple Wind Cue


    [Header("鈎索風聲（Local Loop）")]


    [SerializeField]
    [Tooltip(
        "玩家被普通鈎索拉動、處於鈎索 Release Momentum，" +
        "或 GrappleAirborne 時播放的本機風聲 Loop。\n\n" +
        "Support Tether 只拉別人、不拉自己時不會播放。\n" +
        "AudioClip 必須能無縫循環，Cue 必須使用 Network ID = 0。")]
    private GameplayAudioCue grappleWindLoopCue;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "鈎索風聲開始產生音量的最低世界速度。\n\n" +
        "低於此速度時即使仍在 GrappleAirborne，也會淡出至靜音。")]
    private float grappleWindStartSpeed =
        8f;


    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "鈎索風聲到達完整音量與最高 Pitch 的世界速度。\n\n" +
        "必須高於 Grapple Wind Start Speed；第一輪可依速度表測試 25～40。")]
    private float grappleWindFullSpeed =
        30f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "鈎索風聲從目前音量淡入目標音量所需的基準秒數。\n\n" +
        "第一輪建議 0.15～0.3 秒。")]
    private float grappleWindFadeInDuration =
        0.2f;


    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "鈎索狀態或速度結束後，風聲淡出至停止所需的基準秒數。\n\n" +
        "第一輪建議 0.25～0.5 秒。")]
    private float grappleWindFadeOutDuration =
        0.35f;


    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip("鈎索風聲剛跨過最低速度時的 Pitch 倍率。")]
    private float grappleWindMinimumPitchMultiplier =
        0.85f;


    [SerializeField]
    [Range(0.01f, 3f)]
    [Tooltip("鈎索速度到達 Grapple Wind Full Speed 時的 Pitch 倍率。")]
    private float grappleWindMaximumPitchMultiplier =
        1.25f;


    [SerializeField]
    [Range(0f, 4f)]
    [Tooltip(
        "鈎索風聲到達完整速度時的額外音量倍率。\n\n" +
        "實際音量會再乘上依速度計算的 0～1 比例。")]
    private float grappleWindVolumeScale =
        1f;


    #endregion


    // =====================================================================
    #region Debug


    [Header("執行時除錯")]


    [SerializeField]
    [Tooltip(
        "開啟後在 Console 顯示 OneShot、Loop Start／Stop 與重要狀態變化。\n\n" +
        "正式 Build 建議關閉。")]
    private bool debugMovementAudio =
        false;


    [SerializeField]
    [Tooltip("目前是否已啟用本機播放。只供執行時觀察。")]
    private bool debugLocalPlaybackActive;


    [SerializeField]
    [Tooltip("目前狀態機移動狀態。只供執行時觀察。")]
    private PlayerMovementState debugMovementState;


    [SerializeField]
    [Tooltip("目前水平速度。只供執行時觀察。")]
    private float debugHorizontalSpeed;


    [SerializeField]
    [Tooltip("目前世界速度。只供執行時觀察。")]
    private float debugWorldSpeed;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("目前 Slide Loop 淡入淡出比例。只供執行時觀察。")]
    private float debugSlideLoopFade;


    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("目前 Grapple Wind 依速度計算後的音量比例。只供執行時觀察。")]
    private float debugGrappleWindFade;


    #endregion


    // =====================================================================
    #region Runtime State


    private const int InvalidLoopHandle =
        -1;


    private GameplayAudioService audioService;

    private bool localPlaybackActive;

    private bool previousGrounded;

    private bool previousSliding;

    private bool previousHasUsedDoubleJump;

    private PlayerMovementState previousMovementState;

    private PlayerMovementState previousFootstepState;

    private float footstepTimer;

    private int slideLoopHandle =
        InvalidLoopHandle;

    private float slideLoopFade;

    private int grappleWindLoopHandle =
        InvalidLoopHandle;

    private float grappleWindFade;


    #endregion


    // =====================================================================
    #region Unity And Fusion


    private void Awake()
    {
        CacheReferences();
    }


    public override void Spawned()
    {
        CacheReferences();

        if (Object == null ||
            Object.HasInputAuthority == false)
        {
            localPlaybackActive =
                false;

            return;
        }

        localPlaybackActive =
            true;

        ResetRuntimeState();

        if (debugMovementAudio)
        {
            Debug.Log(
                "[Local Movement Audio] 已綁定本機 Input Authority 玩家。",
                this
            );
        }
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        ShutdownLocalPlayback();
    }


    private void OnDestroy()
    {
        ShutdownLocalPlayback();
    }


    private void Update()
    {
        if (localPlaybackActive == false ||
            movement == null ||
            stateMachine == null ||
            slideController == null ||
            grapple == null)
        {
            return;
        }

        ResolveAudioService();

        PlayerMovementState currentState =
            stateMachine.CurrentState;

        bool grounded =
            movement.IsGrounded;

        bool sliding =
            slideController.IsSliding;

        bool hasUsedDoubleJump =
            stateMachine.HasUsedDoubleJump;

        float horizontalSpeed =
            movement.HorizontalSpeed;

        float worldSpeed =
            movement.CurrentWorldSpeed;

        UpdateJumpAndLandingOneShots(
            currentState,
            grounded,
            hasUsedDoubleJump
        );

        UpdateFootsteps(
            currentState,
            grounded,
            sliding,
            horizontalSpeed
        );

        UpdateSlideAudio(
            sliding,
            slideController.CurrentSlideSpeed
        );

        UpdateGrappleWindAudio(
            currentState,
            worldSpeed
        );

        previousMovementState =
            currentState;

        previousGrounded =
            grounded;

        previousSliding =
            sliding;

        previousHasUsedDoubleJump =
            hasUsedDoubleJump;

        UpdateDebugValues(
            currentState,
            horizontalSpeed,
            worldSpeed
        );
    }


    private void OnValidate()
    {
        walkFootstepInterval =
            Mathf.Max(
                0.01f,
                walkFootstepInterval
            );

        runFootstepInterval =
            Mathf.Max(
                0.01f,
                runFootstepInterval
            );

        firstFootstepDelay =
            Mathf.Max(
                0f,
                firstFootstepDelay
            );

        minimumFootstepHorizontalSpeed =
            Mathf.Max(
                0f,
                minimumFootstepHorizontalSpeed
            );

        slideLoopFadeInDuration =
            Mathf.Max(
                0f,
                slideLoopFadeInDuration
            );

        slideLoopFadeOutDuration =
            Mathf.Max(
                0f,
                slideLoopFadeOutDuration
            );

        slideFullPitchSpeed =
            Mathf.Max(
                0.01f,
                slideFullPitchSpeed
            );

        grappleWindStartSpeed =
            Mathf.Max(
                0f,
                grappleWindStartSpeed
            );

        grappleWindFullSpeed =
            Mathf.Max(
                grappleWindStartSpeed +
                    0.01f,
                grappleWindFullSpeed
            );

        grappleWindFadeInDuration =
            Mathf.Max(
                0f,
                grappleWindFadeInDuration
            );

        grappleWindFadeOutDuration =
            Mathf.Max(
                0f,
                grappleWindFadeOutDuration
            );
    }


    #endregion


    // =====================================================================
    #region Jump And Landing


    private void UpdateJumpAndLandingOneShots(
        PlayerMovementState currentState,
        bool grounded,
        bool hasUsedDoubleJump
    )
    {
        if (currentState != previousMovementState)
        {
            if (currentState ==
                PlayerMovementState.Jump)
            {
                PlayLocalOneShot(
                    groundJumpCue,
                    jumpVolumeScale,
                    "Ground Jump"
                );
            }
        }

        /*
         * 不直接依賴 CurrentState == DoubleJump。
         *
         * 玩家在 GrappleAirborne 中使用二段跳時，
         * 狀態機會刻意維持 GrappleAirborne，讓職業空中能力仍然合法。
         * HasUsedDoubleJump 的上升沿才能涵蓋普通與 Grapple 系二段跳，
         * 並避免同一次跳躍重複播放。
         */
        if (previousHasUsedDoubleJump == false &&
            hasUsedDoubleJump)
        {
            PlayLocalOneShot(
                doubleJumpCue,
                jumpVolumeScale,
                "Double Jump"
            );
        }

        if (previousGrounded == false &&
            grounded)
        {
            PlayLocalOneShot(
                landingCue,
                landingVolumeScale,
                "Landing"
            );
        }
    }


    #endregion


    // =====================================================================
    #region Footsteps


    private void UpdateFootsteps(
        PlayerMovementState currentState,
        bool grounded,
        bool sliding,
        float horizontalSpeed
    )
    {
        bool walking =
            currentState ==
            PlayerMovementState.Walk;

        bool running =
            currentState ==
            PlayerMovementState.Run;

        bool canPlayFootsteps =
            grounded &&
            sliding == false &&
            (walking || running) &&
            horizontalSpeed >=
                minimumFootstepHorizontalSpeed;

        if (canPlayFootsteps == false)
        {
            footstepTimer =
                Mathf.Max(
                    0f,
                    firstFootstepDelay
                );

            previousFootstepState =
                currentState;

            return;
        }

        if (previousFootstepState !=
            currentState)
        {
            footstepTimer =
                Mathf.Max(
                    0f,
                    firstFootstepDelay
                );
        }

        previousFootstepState =
            currentState;

        footstepTimer -=
            Time.deltaTime;

        if (footstepTimer > 0f)
        {
            return;
        }

        GameplayAudioCue selectedCue =
            running
                ? runFootstepCue
                : walkFootstepCue;

        PlayLocalOneShot(
            selectedCue,
            footstepVolumeScale,
            running
                ? "Run Footstep"
                : "Walk Footstep"
        );

        footstepTimer =
            running
                ? Mathf.Max(
                    0.01f,
                    runFootstepInterval
                )
                : Mathf.Max(
                    0.01f,
                    walkFootstepInterval
                );
    }


    #endregion


    // =====================================================================
    #region Slide Audio


    private void UpdateSlideAudio(
        bool sliding,
        float slideSpeed
    )
    {
        if (sliding &&
            previousSliding == false)
        {
            PlayLocalOneShot(
                slideStartCue,
                slideOneShotVolumeScale,
                "Slide Start"
            );
        }
        else if (sliding == false &&
                 previousSliding)
        {
            PlayLocalOneShot(
                slideEndCue,
                slideOneShotVolumeScale,
                "Slide End"
            );
        }

        float targetFade =
            sliding
                ? 1f
                : 0f;

        slideLoopFade =
            MoveFade(
                slideLoopFade,
                targetFade,
                slideLoopFadeInDuration,
                slideLoopFadeOutDuration
            );

        if (targetFade > 0f)
        {
            EnsureSlideLoopStarted();
        }

        if (slideLoopHandle >
            InvalidLoopHandle &&
            audioService != null)
        {
            audioService.SetLocalLoopVolumeScale(
                slideLoopHandle,
                slideLoopFade *
                Mathf.Max(
                    0f,
                    slideLoopVolumeScale
                )
            );

            float speedT =
                Mathf.Clamp01(
                    slideSpeed /
                    Mathf.Max(
                        0.01f,
                        slideFullPitchSpeed
                    )
                );

            float pitchMultiplier =
                Mathf.Lerp(
                    slideMinimumPitchMultiplier,
                    slideMaximumPitchMultiplier,
                    speedT
                );

            audioService.SetLocalLoopPitchMultiplier(
                slideLoopHandle,
                pitchMultiplier
            );
        }

        if (sliding == false &&
            slideLoopFade <= 0.0001f)
        {
            StopSlideLoop();
        }
    }


    private void EnsureSlideLoopStarted()
    {
        if (audioService == null ||
            slideLoopCue == null)
        {
            return;
        }

        if (slideLoopHandle >
                InvalidLoopHandle &&
            audioService.IsLocalLoopPlaying(
                slideLoopHandle
            ))
        {
            return;
        }

        if (slideLoopHandle >
            InvalidLoopHandle)
        {
            audioService.StopLocalLoop(
                slideLoopHandle
            );

            slideLoopHandle =
                InvalidLoopHandle;
        }

        slideLoopHandle =
            audioService.StartLocalLoop(
                slideLoopCue,
                0f
            );

        if (debugMovementAudio &&
            slideLoopHandle >
                InvalidLoopHandle)
        {
            Debug.Log(
                "[Local Movement Audio] Slide Loop Start。",
                this
            );
        }
    }


    private void StopSlideLoop()
    {
        if (slideLoopHandle <=
            InvalidLoopHandle)
        {
            return;
        }

        if (audioService != null)
        {
            audioService.StopLocalLoop(
                slideLoopHandle
            );
        }

        slideLoopHandle =
            InvalidLoopHandle;

        if (debugMovementAudio)
        {
            Debug.Log(
                "[Local Movement Audio] Slide Loop Stop。",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Grapple Wind Audio


    private void UpdateGrappleWindAudio(
        PlayerMovementState currentState,
        float worldSpeed
    )
    {
        bool grappleWindStateActive =
            grapple.IsNormalPlayerPullAttached ||
            grapple.IsReleaseMomentumActive ||
            currentState ==
                PlayerMovementState.GrappleAirborne;

        float speedT =
            grappleWindStateActive
                ? Mathf.InverseLerp(
                    grappleWindStartSpeed,
                    Mathf.Max(
                        grappleWindStartSpeed +
                            0.01f,
                        grappleWindFullSpeed
                    ),
                    worldSpeed
                )
                : 0f;

        grappleWindFade =
            MoveFade(
                grappleWindFade,
                speedT,
                grappleWindFadeInDuration,
                grappleWindFadeOutDuration
            );

        if (speedT > 0.0001f)
        {
            EnsureGrappleWindLoopStarted();
        }

        if (grappleWindLoopHandle >
                InvalidLoopHandle &&
            audioService != null)
        {
            audioService.SetLocalLoopVolumeScale(
                grappleWindLoopHandle,
                grappleWindFade *
                Mathf.Max(
                    0f,
                    grappleWindVolumeScale
                )
            );

            float pitchMultiplier =
                Mathf.Lerp(
                    grappleWindMinimumPitchMultiplier,
                    grappleWindMaximumPitchMultiplier,
                    Mathf.Clamp01(
                        speedT
                    )
                );

            audioService.SetLocalLoopPitchMultiplier(
                grappleWindLoopHandle,
                pitchMultiplier
            );
        }

        if (speedT <= 0.0001f &&
            grappleWindFade <= 0.0001f)
        {
            StopGrappleWindLoop();
        }
    }


    private void EnsureGrappleWindLoopStarted()
    {
        if (audioService == null ||
            grappleWindLoopCue == null)
        {
            return;
        }

        if (grappleWindLoopHandle >
                InvalidLoopHandle &&
            audioService.IsLocalLoopPlaying(
                grappleWindLoopHandle
            ))
        {
            return;
        }

        if (grappleWindLoopHandle >
            InvalidLoopHandle)
        {
            audioService.StopLocalLoop(
                grappleWindLoopHandle
            );

            grappleWindLoopHandle =
                InvalidLoopHandle;
        }

        grappleWindLoopHandle =
            audioService.StartLocalLoop(
                grappleWindLoopCue,
                0f
            );

        if (debugMovementAudio &&
            grappleWindLoopHandle >
                InvalidLoopHandle)
        {
            Debug.Log(
                "[Local Movement Audio] Grapple Wind Loop Start。",
                this
            );
        }
    }


    private void StopGrappleWindLoop()
    {
        if (grappleWindLoopHandle <=
            InvalidLoopHandle)
        {
            return;
        }

        if (audioService != null)
        {
            audioService.StopLocalLoop(
                grappleWindLoopHandle
            );
        }

        grappleWindLoopHandle =
            InvalidLoopHandle;

        if (debugMovementAudio)
        {
            Debug.Log(
                "[Local Movement Audio] Grapple Wind Loop Stop。",
                this
            );
        }
    }


    #endregion


    // =====================================================================
    #region Audio Service And Playback


    private void ResolveAudioService()
    {
        GameplayAudioService currentService =
            GameplayAudioService.Instance;

        if (audioService == currentService)
        {
            return;
        }

        /*
         * 場景切換時 Audio Service 可能被替換。
         * 先讓舊 Service 回收本控制器持有的 Loop，
         * 再切換到新 Instance，避免留下無主 AudioSource。
         */
        StopAllOwnedLoops();

        audioService =
            currentService;
    }


    private bool PlayLocalOneShot(
        GameplayAudioCue cue,
        float volumeScale,
        string debugName
    )
    {
        if (cue == null ||
            audioService == null)
        {
            return false;
        }

        bool played =
            audioService.PlayLocalOneShot(
                cue,
                Mathf.Max(
                    0f,
                    volumeScale
                )
            );

        if (debugMovementAudio)
        {
            Debug.Log(
                $"[Local Movement Audio] {debugName}" +
                $"\nPlayed：{played}",
                this
            );
        }

        return played;
    }


    private static float MoveFade(
        float current,
        float target,
        float fadeInDuration,
        float fadeOutDuration
    )
    {
        float clampedTarget =
            Mathf.Clamp01(
                target
            );

        float duration =
            clampedTarget > current
                ? Mathf.Max(
                    0f,
                    fadeInDuration
                )
                : Mathf.Max(
                    0f,
                    fadeOutDuration
                );

        if (duration <= 0.0001f)
        {
            return clampedTarget;
        }

        return Mathf.MoveTowards(
            current,
            clampedTarget,
            Time.deltaTime /
            duration
        );
    }


    #endregion


    // =====================================================================
    #region State Management


    private void CacheReferences()
    {
        if (player == null)
        {
            player =
                GetComponent<Player>();
        }

        if (movement == null)
        {
            movement =
                GetComponent<PlayerMovement>();
        }

        if (stateMachine == null)
        {
            stateMachine =
                GetComponent<PlayerStateMachine>();
        }

        if (slideController == null)
        {
            slideController =
                GetComponent<PlayerSlideController>();
        }

        if (grapple == null)
        {
            grapple =
                GetComponent<PlayerGrapple>();
        }
    }


    private void ResetRuntimeState()
    {
        ResolveAudioService();

        previousGrounded =
            movement != null &&
            movement.IsGrounded;

        previousSliding =
            slideController != null &&
            slideController.IsSliding;

        previousHasUsedDoubleJump =
            stateMachine != null &&
            stateMachine.HasUsedDoubleJump;

        previousMovementState =
            stateMachine != null
                ? stateMachine.CurrentState
                : PlayerMovementState.Idle;

        previousFootstepState =
            previousMovementState;

        footstepTimer =
            Mathf.Max(
                0f,
                firstFootstepDelay
            );

        slideLoopFade =
            0f;

        grappleWindFade =
            0f;

        slideLoopHandle =
            InvalidLoopHandle;

        grappleWindLoopHandle =
            InvalidLoopHandle;
    }


    private void ShutdownLocalPlayback()
    {
        StopAllOwnedLoops();

        localPlaybackActive =
            false;

        slideLoopFade =
            0f;

        grappleWindFade =
            0f;
    }


    private void StopAllOwnedLoops()
    {
        GameplayAudioService service =
            audioService;

        if (service != null)
        {
            if (slideLoopHandle >
                InvalidLoopHandle)
            {
                service.StopLocalLoop(
                    slideLoopHandle
                );
            }

            if (grappleWindLoopHandle >
                InvalidLoopHandle)
            {
                service.StopLocalLoop(
                    grappleWindLoopHandle
                );
            }
        }

        slideLoopHandle =
            InvalidLoopHandle;

        grappleWindLoopHandle =
            InvalidLoopHandle;
    }


    private void UpdateDebugValues(
        PlayerMovementState currentState,
        float horizontalSpeed,
        float worldSpeed
    )
    {
        if (debugMovementAudio == false)
        {
            return;
        }

        debugLocalPlaybackActive =
            localPlaybackActive;

        debugMovementState =
            currentState;

        debugHorizontalSpeed =
            horizontalSpeed;

        debugWorldSpeed =
            worldSpeed;

        debugSlideLoopFade =
            slideLoopFade;

        debugGrappleWindFade =
            grappleWindFade;
    }


    #endregion
}
