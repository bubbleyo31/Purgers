using UnityEngine;

/// <summary>
/// 第一人稱移動粒子特效控制器。
///
/// 目前只負責：
/// 1. 判斷玩家是否處於勾索拉動狀態。
/// 2. 判斷玩家是否處於勾索拋出後滯空狀態。
/// 3. 判斷玩家是否正在使用手動取消勾索慣性。
/// 4. 玩家速度達到門檻後播放速度線。
/// 5. 不符合條件時停止速度線。
///
/// 重要：
/// 這支程式不會修改粒子系統的：
/// Start Speed、Start Lifetime、Start Size、Emission、Length Scale。
///
/// 粒子的外觀完全保留 Inspector 原本設定。
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonMovementEffects : MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中唯一的第一人稱移動特效控制器。
    /// </summary>
    public static FirstPersonMovementEffects Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 粒子引用

    [Header("速度線粒子")]

    [SerializeField]
    [Tooltip("已經調整完成的速度線 ParticleSystem。這支程式只會控制播放與停止，不會修改粒子參數。")]
    private ParticleSystem speedLineParticles;

    #endregion

    // =====================================================================
    #region 顯示條件

    [Header("允許顯示的狀態")]

    [SerializeField]
    [Tooltip("開啟後，勾索已正式附著並拉動玩家時播放速度線。勾索前搖與繩索射出階段不會播放。")]
    private bool showWhileGrapplePulling = true;

    [SerializeField]
    [Tooltip("開啟後，勾索結束後玩家尚未接觸地面時繼續播放速度線。")]
    private bool showWhileGrappleAirborne = true;

    [SerializeField]
    [Tooltip("開啟後，玩家再次按下勾索鍵取消勾索，並正在使用手動取消慣性時播放速度線。")]
    private bool showDuringManualCancelMomentum = true;

    #endregion

    // =====================================================================
    #region 速度門檻

    [Header("速度門檻")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("玩家實際速度達到此數值後才播放速度線。依目前一般移動速度為 20、勾索最高速度為 150，建議先設定為 25 到 35。")]
    private float minimumVisibleSpeed = 30f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("停止速度線使用的速度門檻。此數值應稍低於開始門檻，避免速度剛好在門檻附近時粒子不斷開啟與關閉。例如開始門檻為 30，停止門檻可以設定為 25。")]
    private float stopVisibleSpeed = 25f;

    #endregion

    // =====================================================================
    #region 停止方式

    [Header("停止方式")]

    [SerializeField]
    [Tooltip("開啟後，停止速度線時會立刻清除目前所有粒子。關閉後只停止生成新粒子，已存在的粒子會依原本 Start Lifetime 自然消失。你的粒子生命時間為 2 秒，如果殘留過久可以開啟。")]
    private bool clearParticlesImmediately = false;

    #endregion

    // =====================================================================
    #region 除錯資訊

    [Header("執行時除錯")]

    [SerializeField]
    [Tooltip("開啟後，Inspector 會顯示目前玩家速度與特效判定結果。")]
    private bool showDebugInformation = false;

    [SerializeField]
    [Tooltip("目前讀取到的玩家實際世界速度。此欄位只供執行時觀察，不需要手動修改。")]
    private float debugCurrentSpeed;

    [SerializeField]
    [Tooltip("目前玩家狀態是否允許播放速度線。此欄位只供執行時觀察。")]
    private bool debugStateAllowed;

    [SerializeField]
    [Tooltip("目前速度線是否正在播放。此欄位只供執行時觀察。")]
    private bool debugEffectPlaying;

    #endregion

    // =====================================================================
    #region 執行狀態

    /// <summary>
    /// 目前的本地玩家。
    /// </summary>
    private Player targetPlayer;

    /// <summary>
    /// 目前本地玩家的狀態機。
    /// </summary>
    private PlayerStateMachine targetStateMachine;

    /// <summary>
    /// 目前是否已經由這支程式啟動速度線。
    /// </summary>
    private bool effectPlaying;

    #endregion

    // =====================================================================
    #region Unity 生命週期

    private void Awake()
    {
        RegisterSingleton();

        /*
         * 如果 Inspector 沒有指定粒子，
         * 嘗試從目前物件的子物件尋找。
         */
        if (speedLineParticles == null)
        {
            speedLineParticles =
                GetComponentInChildren<ParticleSystem>(
                    true
                );
        }

        if (speedLineParticles == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonMovementEffects)}] 找不到速度線 ParticleSystem。",
                this
            );

            enabled = false;
            return;
        }

        /*
         * 遊戲開始時確保速度線不會顯示。
         *
         * 這不會修改粒子的 Inspector 參數，
         * 只會停止目前播放。
         */
        speedLineParticles.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        effectPlaying = false;
    }

    private void Update()
    {
        UpdateSpeedLineEffect();

        if (showDebugInformation)
        {
            debugEffectPlaying =
                effectPlaying;
        }
    }

    private void OnDestroy()
    {
        if (Singleton == this)
        {
            Singleton = null;
        }
    }

    #endregion

    // =====================================================================
    #region Singleton 管理

    /// <summary>
    /// 註冊場景中的唯一控制器。
    /// </summary>
    private void RegisterSingleton()
    {
        if (Singleton == null)
        {
            Singleton = this;
            return;
        }

        if (Singleton == this)
            return;

        Debug.LogError(
            $"場景中只能存在一個 {nameof(FirstPersonMovementEffects)}。",
            this
        );

        Destroy(this);
    }

    #endregion

    // =====================================================================
    #region 玩家綁定

    /// <summary>
    /// 指定目前的本地玩家。
    ///
    /// 這個方法應由具有 Input Authority 的 Player 呼叫。
    /// 遠端玩家不應綁定第一人稱速度線。
    /// </summary>
    public void SetTarget(
        Player player
    )
    {
        targetPlayer =
            player;

        targetStateMachine =
            player != null
                ? player.StateMachine
                : null;

        StopSpeedLines(
            true
        );
    }

    /// <summary>
    /// 清除目前的玩家目標。
    ///
    /// 只有傳入玩家就是目前目標時才會清除，
    /// 避免其他遠端玩家被銷毀時影響本地特效。
    /// </summary>
    public void ClearTarget(
        Player player
    )
    {
        if (targetPlayer != player)
            return;

        targetPlayer =
            null;

        targetStateMachine =
            null;

        StopSpeedLines(
            true
        );
    }

    #endregion

    // =====================================================================
    #region 特效判斷

    /// <summary>
    /// 每幀判斷是否應該播放速度線。
    /// </summary>
    private void UpdateSpeedLineEffect()
    {
        if (targetPlayer == null ||
            targetStateMachine == null ||
            speedLineParticles == null)
        {
            StopSpeedLines(
                false
            );

            UpdateDebugValues(
                0f,
                false
            );

            return;
        }

        float currentSpeed =
            targetPlayer.CurrentWorldSpeed;

        bool stateAllowed =
            IsCurrentStateAllowed();

        UpdateDebugValues(
            currentSpeed,
            stateAllowed
        );

        /*
         * 玩家狀態不符合時直接停止。
         */
        if (stateAllowed == false)
        {
            StopSpeedLines(
                false
            );

            return;
        }

        /*
         * 使用不同的開啟與關閉速度門檻。
         *
         * 尚未播放時：
         * 必須達到 Minimum Visible Speed 才播放。
         *
         * 已經播放時：
         * 只有低於 Stop Visible Speed 才停止。
         *
         * 這能避免速度在門檻附近變動時，
         * 粒子每幀反覆 Play、Stop。
         */
        if (effectPlaying)
        {
            if (currentSpeed <=
                stopVisibleSpeed)
            {
                StopSpeedLines(
                    false
                );
            }
        }
        else
        {
            if (currentSpeed >=
                minimumVisibleSpeed)
            {
                PlaySpeedLines();
            }
        }
    }

    /// <summary>
    /// 判斷玩家目前狀態是否允許速度線播放。
    /// </summary>
    private bool IsCurrentStateAllowed()
    {
        /*
         * 勾索已正式附著並正在拉動玩家。
         */
        if (showWhileGrapplePulling &&
            targetPlayer.IsGrapplePulling)
        {
            return true;
        }

        /*
         * 勾索結束後，玩家尚未接觸地面。
         */
        if (showWhileGrappleAirborne &&
            targetStateMachine.CurrentState ==
            PlayerMovementState.GrappleAirborne)
        {
            return true;
        }

        /*
         * 玩家再次按下勾索鍵取消後，
         * 正在使用水平慣性推進。
         */
        if (showDuringManualCancelMomentum &&
            targetPlayer.IsManualCancelMomentumActive)
        {
            return true;
        }

        return false;
    }

    #endregion

    // =====================================================================
    #region 粒子播放控制

    /// <summary>
    /// 播放速度線。
    ///
    /// 不修改任何粒子參數。
    /// </summary>
    private void PlaySpeedLines()
    {
        if (effectPlaying ||
            speedLineParticles == null)
        {
            return;
        }

        speedLineParticles.Play(
            true
        );

        effectPlaying = true;
    }

    /// <summary>
    /// 停止速度線。
    /// </summary>
    /// <param name="forceClear">
    /// true：
    /// 無論 Inspector 設定為何都立即清除粒子。
    /// 適合玩家切換、離線或場景重置。
    ///
    /// false：
    /// 根據 Clear Particles Immediately 決定是否立即清除。
    /// </param>
    private void StopSpeedLines(
        bool forceClear
    )
    {
        if (speedLineParticles == null)
            return;

        if (effectPlaying == false &&
            speedLineParticles.isPlaying == false)
        {
            return;
        }

        bool shouldClear =
            forceClear ||
            clearParticlesImmediately;

        ParticleSystemStopBehavior stopBehavior =
            shouldClear
                ? ParticleSystemStopBehavior
                    .StopEmittingAndClear
                : ParticleSystemStopBehavior
                    .StopEmitting;

        speedLineParticles.Stop(
            true,
            stopBehavior
        );

        effectPlaying = false;
    }

    #endregion

    // =====================================================================
    #region 除錯更新

    /// <summary>
    /// 更新執行時除錯數值。
    /// </summary>
    private void UpdateDebugValues(
        float currentSpeed,
        bool stateAllowed
    )
    {
        if (showDebugInformation == false)
            return;

        debugCurrentSpeed =
            currentSpeed;

        debugStateAllowed =
            stateAllowed;
    }

    #endregion
}