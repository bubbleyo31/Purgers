using System.Collections;
using UnityEngine;

/// <summary>
/// 第一人稱「攻擊成功命中」攝影機震動。
///
/// 這支腳本只負責視覺表現。
///
/// 不知道：
///
/// Damage
/// AttackRifle
/// Photon Fusion
/// Enemy
/// WeaponHitZone
///
/// 它唯一知道的是：
///
/// 「有人要求我播放一次命中震動。」
///
/// ------------------------------------------------------------
///
/// 建議掛在 CameraRig 上。
///
/// 並指定：
///
/// CameraEffectsRoot
///
/// 作為真正被震動的 Transform。
///
/// ------------------------------------------------------------
///
/// Hierarchy 建議：
///
/// CameraRig
/// └─ CameraEffectsRoot
///    ├─ WorldCamera
///    ├─ WeaponCamera
///    └─ ViewModelRoot
///
/// ------------------------------------------------------------
///
/// 為什麼不直接震 CameraRig？
///
/// 因為 CameraRig 目前已經由 CameraFollow 控制。
///
/// 如果 CameraFollow 與 Camera Shake
/// 同時修改 CameraRig Transform，
/// 高速移動時很容易重新產生抖動或互相覆寫。
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonHitCameraShake : MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中的本地第一人稱命中震動控制器。
    ///
    /// CameraRig 是本地場景物件，
/// 因此只需要一份。
    /// </summary>
    public static FirstPersonHitCameraShake Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region 核心引用

    [Header("核心引用")]

    [SerializeField]
    [Tooltip("真正接受 Camera Shake 的視覺根節點。建議建立 CameraEffectsRoot，並把 World Camera、Weapon Camera、ViewModelRoot 都放在它下面。不要直接指定 CameraRig，避免與 CameraFollow 同時修改同一個 Transform。")]
    private Transform cameraEffectsRoot;

    #endregion

    // =====================================================================
    #region 一般命中震動

    [Header("一般命中震動")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("普通有效命中時，Camera Shake 持續時間。這只是攻擊命中的手感回饋，建議保持非常短。可以先從 0.06 到 0.1 秒測試。")]
    private float normalHitDuration =
        0.07f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("普通有效命中時的位置震動強度，單位為 Unity 世界單位。數值應保持很小，第一人稱遊戲建議先從 0.005 到 0.02 測試。")]
    private float normalHitPositionAmplitude =
        0.008f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("普通有效命中時的旋轉震動強度，單位為角度。建議先從 0.1 到 0.4 度測試，避免玩家高速移動時畫面過度晃動。")]
    private float normalHitRotationAmplitude =
        0.18f;

    #endregion

    // =====================================================================
    #region 近戰命中震動

    [Header("近戰命中震動")]

    [SerializeField]
    [Tooltip("開啟後，Damage Type 為 Melee 的命中會使用獨立的 Camera Shake 數值，不會使用普通步槍命中震動。")]
    private bool useSeparateMeleeShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("快速近戰成功造成有效傷害時，Camera Shake 持續多久。建議近戰比槍械稍微重一些，例如 0.08 到 0.14 秒。")]
    private float meleeHitDuration =
        0.10f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("快速近戰命中時的位置震動強度。這是 CameraEffectsRoot 的 Local Position Offset，數值不要太大。建議先從 0.01 到 0.025 測試。")]
    private float meleeHitPositionAmplitude =
        0.018f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("快速近戰命中時的旋轉震動強度，單位為角度。建議先從 0.3 到 0.7 度測試。")]
    private float meleeHitRotationAmplitude =
        0.45f;

    #endregion

    // =====================================================================
    #region Tank 近戰命中震動

    [Header("Tank 輕攻擊震動")]

    [SerializeField]
    [Tooltip("開啟後，Combat Feedback ID 為 TankLightMelee 的命中會使用 Tank 專屬輕攻擊震動，而不是一般 Melee 震動。Light1 與 Light2 目前共用這組設定。")]
    private bool useSeparateTankLightShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第一、二段輕攻擊成功造成有效傷害時的 Camera Shake 持續時間，單位為秒。目前設計值為 0.2 秒。")]
    private float tankLightDuration =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第一、二段輕攻擊命中時的位置震動強度。這會作用在 CameraEffectsRoot 的 Local Position Offset。目前設計值為 0.125。")]
    private float tankLightPositionAmplitude =
        0.125f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第一、二段輕攻擊命中時的旋轉震動強度，單位為角度。目前設計值為 2。")]
    private float tankLightRotationAmplitude =
        2f;


    [Header("Tank 重攻擊震動")]

    [SerializeField]
    [Tooltip("開啟後，Combat Feedback ID 為 TankHeavyMelee 的命中會使用 Tank 專屬重攻擊震動，而不是一般 Melee 震動。")]
    private bool useSeparateTankHeavyShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第三段 Heavy 成功造成有效傷害時的 Camera Shake 持續時間，單位為秒。目前設計值為 0.2 秒。")]
    private float tankHeavyDuration =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第三段 Heavy 命中時的位置震動強度。目前設計值為 0.2，因此應該比 Light 的 0.125 更有重量感。")]
    private float tankHeavyPositionAmplitude =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 第三段 Heavy 命中時的旋轉震動強度，單位為角度。目前設計值為 2。")]
    private float tankHeavyRotationAmplitude =
        2f;


    [Header("Tank 擊殺震動")]

    [SerializeField]
    [Tooltip("開啟後，Tank Light 或 Heavy 造成擊殺時會使用 Tank 專屬 Kill Shake。Kill 的判定優先級高於 Light 與 Heavy，所以 Heavy 擊殺不會播放 Heavy Shake，而是播放這組 Tank Kill Shake。")]
    private bool useSeparateTankKillShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 近戰造成擊殺時的 Camera Shake 持續時間，單位為秒。目前設計值為 0.2 秒。")]
    private float tankKillDuration =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 近戰造成擊殺時的位置震動強度。目前設計值為 0.4，優先級與強度都高於 Tank Light 與 Heavy。")]
    private float tankKillPositionAmplitude =
        0.4f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 近戰造成擊殺時的旋轉震動強度，單位為角度。目前設計值為 4。")]
    private float tankKillRotationAmplitude =
        4f;

    #endregion

    // =====================================================================
    #region Tank Air Dash 震動

    [Header("Tank Enemy Air Dash 發動震動")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 在 GrappleAirborne 成功鎖定 Enemy 並正式開始特殊衝刺時的 Camera Shake 持續時間。這是衝刺本身的回饋，不要求最後命中。")]
    private float tankEnemyAirDashDuration =
        0.15f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Enemy Air Dash 正式發動時的位置震動強度。這只是第一輪測試數值，之後可以依速度感調整。")]
    private float tankEnemyAirDashPositionAmplitude =
        0.18f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Enemy Air Dash 正式發動時的旋轉震動強度，單位為角度。")]
    private float tankEnemyAirDashRotationAmplitude =
        1.5f;


    [Header("Tank World Air Dash 發動震動")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank 對牆壁、地面等 World Target 成功發動 Air Dash 時的 Camera Shake 持續時間。World Dash 沒有攻擊傷害，所以建議比 Enemy Dash 稍弱。")]
    private float tankWorldAirDashDuration =
        0.12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank World Air Dash 發動時的位置震動強度。")]
    private float tankWorldAirDashPositionAmplitude =
        0.12f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank World Air Dash 發動時的旋轉震動強度，單位為角度。")]
    private float tankWorldAirDashRotationAmplitude =
        1f;


    [Header("Tank Air Strike 命中震動")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Enemy Air Dash 成功抵達並用 AOE 命中至少一個敵人時的 Camera Shake 持續時間。這是『撞擊命中』回饋，不是衝刺發動回饋。")]
    private float tankAirStrikeHitDuration =
        0.2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Air Strike AOE 成功造成有效傷害時的位置震動強度。建議明顯強於普通 Heavy。")]
    private float tankAirStrikeHitPositionAmplitude =
        0.3f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("Tank Air Strike AOE 成功造成有效傷害時的旋轉震動強度，單位為角度。")]
    private float tankAirStrikeHitRotationAmplitude =
        3f;

    #endregion

    // =====================================================================
    #region 暴頭震動

    [Header("暴頭震動")]

    [SerializeField]
    [Tooltip("開啟後，暴頭可以使用比普通命中稍微明顯的 Camera Shake。關閉時暴頭與普通命中使用完全相同數值。")]
    private bool useSeparateHeadshotShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("暴頭確認時的震動持續時間。建議只比普通命中稍長，不要做成受傷或爆炸等級的大幅震動。")]
    private float headshotDuration =
        0.09f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("暴頭命中時的位置震動強度。")]
    private float headshotPositionAmplitude =
        0.012f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("暴頭命中時的旋轉震動強度，單位為角度。")]
    private float headshotRotationAmplitude =
        0.28f;

    #endregion

    // =====================================================================
    #region 擊殺震動

    [Header("擊殺震動")]

    [SerializeField]
    [Tooltip("開啟後，造成擊殺時可以再使用較明顯的命中震動。優先權為 Kill 高於 Headshot 高於 Normal。")]
    private bool useSeparateKillShake =
        true;

    [SerializeField]
    [Min(0f)]
    [Tooltip("造成擊殺時的震動持續時間。")]
    private float killDuration =
        0.11f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("造成擊殺時的位置震動強度。")]
    private float killPositionAmplitude =
        0.015f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("造成擊殺時的旋轉震動強度，單位為角度。")]
    private float killRotationAmplitude =
        0.35f;

    #endregion

    // =====================================================================
    #region 震動表現

    [Header("震動表現")]

    [SerializeField]
    [Min(1f)]
    [Tooltip("震動雜訊更新速度。數值越高，Camera Shake 越快速、越銳利；數值越低則比較像緩慢晃動。命中回饋通常需要較高頻率，建議先測試 30 到 50。")]
    private float shakeFrequency =
        40f;

    [SerializeField]
    [Tooltip("震動強度隨時間衰減的曲線。X 軸為震動生命週期，Y 軸為強度倍率。預設從 1 快速衰減到 0。")]
    private AnimationCurve shakeFalloff =
        AnimationCurve.EaseInOut(
            0f,
            1f,
            1f,
            0f
        );

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，每次收到命中震動要求時會在 Console 顯示這次使用的是 Normal、Headshot 或 Kill，以及震動參數。")]
    private bool debugShake =
        false;

    #endregion

    // =====================================================================
    #region Runtime

    /// <summary>
    /// CameraEffectsRoot 原本的 Local Position。
    ///
    /// Camera Shake 永遠在這個基準上增加 Offset，
    /// 不會永久改變攝影機位置。
    /// </summary>
    private Vector3 baseLocalPosition;

    /// <summary>
    /// CameraEffectsRoot 原本的 Local Rotation。
    /// </summary>
    private Quaternion baseLocalRotation;

    /// <summary>
    /// 目前正在執行的 Shake Coroutine。
    /// </summary>
    private Coroutine currentShakeRoutine;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        RegisterSingleton();

        if (cameraEffectsRoot == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonHitCameraShake)}] " +
                $"尚未指定 CameraEffectsRoot。",
                this
            );

            return;
        }

        /*
         * 紀錄正常狀態。
         *
         * CameraEffectsRoot 建議：
         *
         * Local Position = 0,0,0
         * Local Rotation = 0,0,0
         *
         * 但即使不是零，
         * 這支腳本仍然會保存真正的基準值。
         */
        baseLocalPosition =
            cameraEffectsRoot.localPosition;

        baseLocalRotation =
            cameraEffectsRoot.localRotation;
    }

    private void OnDisable()
    {
        /*
         * 如果物件在震動途中被關閉，
         * 一定要恢復基準 Transform。
         */
        ResetShakeTransform();
    }

    private void OnDestroy()
    {
        ResetShakeTransform();

        if (Singleton == this)
        {
            Singleton = null;
        }
    }

    #endregion

    // =====================================================================
    #region Singleton

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
            $"場景中只能存在一個 " +
            $"{nameof(FirstPersonHitCameraShake)}。",
            this
        );

        Destroy(
            this
        );
    }

    #endregion

    // =====================================================================
    #region 對外接口

    /// <summary>
    /// 播放一次正式有效命中的 Camera Shake。
    ///
    /// ====================================================================
    ///
    /// 現在的優先級：
    ///
    /// 1. Kill
    /// ↓
    /// 2. 攻擊專屬 Feedback ID
    /// ↓
    /// 3. Headshot
    /// ↓
    /// 4. DamageType Melee Fallback
    /// ↓
    /// 5. Normal
    ///
    /// ====================================================================
    ///
    /// 特別注意：
    ///
    /// Kill 永遠高於 Tank Light / Heavy。
    ///
    /// 所以：
    ///
    /// Tank Heavy
    /// +
    /// KilledTarget = true
    ///
    /// 不會播放 Tank Heavy Shake。
    ///
    /// 而是播放 Tank Kill Shake。
    /// </summary>
    public void PlayHitShake(
        CombatHitFeedbackData feedback
    )
    {
        // =============================================================
        // 沒有有效傷害
        // =============================================================

        /*
        * 例如：
        *
        * 無敵
        * Fully Blocked
        * AppliedDamage = 0
        *
        * 都不應該播放攻擊成功震動。
        */
        if (feedback.HasEffectiveDamage ==
            false)
        {
            return;
        }

        float duration;
        float positionAmplitude;
        float rotationAmplitude;
        string shakeType;

        // =============================================================
        // 判斷 Tank 攻擊身份
        // =============================================================

        bool isTankLight =
            feedback.FeedbackId ==
            CombatFeedbackId.TankLightMelee;

        bool isTankHeavy =
            feedback.FeedbackId ==
            CombatFeedbackId.TankHeavyMelee;

        bool isTankAirStrike =
            feedback.FeedbackId ==
            CombatFeedbackId.TankAirStrike;

        /*
        * 「Tank 攻擊造成的傷害」
        *
        * 用於 Tank 專屬 Kill Shake。
        */
        bool isTankDamage =
            isTankLight ||
            isTankHeavy ||
            isTankAirStrike;

        // =============================================================
        // 1. Tank Kill
        // =============================================================

        /*
        * ★ 最高優先級。
        *
        * Tank Light Kill
        * Tank Heavy Kill
        *
        * 都統一使用 Tank Kill Shake。
        *
        * Heavy 不可以蓋掉 Kill。
        */
        if (feedback.KilledTarget &&
            isTankDamage &&
            useSeparateTankKillShake)
        {
            duration =
                tankKillDuration;

            positionAmplitude =
                tankKillPositionAmplitude;

            rotationAmplitude =
                tankKillRotationAmplitude;

            shakeType =
                "Tank Kill";
        }

        // =============================================================
        // 2. 其他攻擊 Kill
        // =============================================================

        /*
        * Attack Rifle
        * Attack Quick Melee
        * 以及未來其他普通攻擊
        *
        * 造成擊殺時仍然沿用目前共用 Kill Shake。
        *
        * 這也維持你之前定下來的：
        *
        * Kill 優先於所有普通命中回饋。
        */
        else if (feedback.KilledTarget &&
                useSeparateKillShake)
        {
            duration =
                killDuration;

            positionAmplitude =
                killPositionAmplitude;

            rotationAmplitude =
                killRotationAmplitude;

            shakeType =
                "Kill";
        }

        // =============================================================
        // Tank Air Strike Hit
        // =============================================================

        else if (isTankAirStrike)
        {
            duration =
                tankAirStrikeHitDuration;

            positionAmplitude =
                tankAirStrikeHitPositionAmplitude;

            rotationAmplitude =
                tankAirStrikeHitRotationAmplitude;

            shakeType =
                "Tank Air Strike";
        }

        // =============================================================
        // Tank 普通命中不再額外 Shake
        // =============================================================

        else if (isTankLight ||
                isTankHeavy)
        {
            /*
            * Light / Heavy 的普通 Shake
            * 已經在揮刀 Active 時播放。
            *
            * 所以命中不能再震第二次。
            */
            return;
        }

        // =============================================================
        // 5. Headshot
        // =============================================================

        /*
        * Headshot 放在 Tank 專屬攻擊後面。
        *
        * Tank 普通近戰目前本身不會產生 Headshot，
        * 但這樣的優先順序可以避免未來其他攻擊身份
        * 被 Headshot 規則意外覆蓋。
        */
        else if (feedback.IsHeadshot &&
                useSeparateHeadshotShake)
        {
            duration =
                headshotDuration;

            positionAmplitude =
                headshotPositionAmplitude;

            rotationAmplitude =
                headshotRotationAmplitude;

            shakeType =
                "Headshot";
        }

        // =============================================================
        // 6. 舊 Melee Fallback
        // =============================================================

        /*
        * 這裡主要保留給：
        *
        * AttackQuickMelee
        *
        * 或任何還沒有獨立 CombatFeedbackId
        * 視覺設定的 Melee。
        */
        else if (feedback.DamageType ==
                    DamageType.Melee &&
                useSeparateMeleeShake)
        {
            duration =
                meleeHitDuration;

            positionAmplitude =
                meleeHitPositionAmplitude;

            rotationAmplitude =
                meleeHitRotationAmplitude;

            shakeType =
                "Melee";
        }

        // =============================================================
        // 7. Normal
        // =============================================================

        else
        {
            duration =
                normalHitDuration;

            positionAmplitude =
                normalHitPositionAmplitude;

            rotationAmplitude =
                normalHitRotationAmplitude;

            shakeType =
                "Normal";
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugShake)
        {
            Debug.Log(
                $"[Hit Camera Shake]" +
                $"\n類型：{shakeType}" +
                $"\nFeedback ID：{feedback.FeedbackId}" +
                $"\nDamage Type：{feedback.DamageType}" +
                $"\nKill：{feedback.KilledTarget}" +
                $"\nApplied Damage：{feedback.AppliedDamage:F2}" +
                $"\nDuration：{duration:F3}" +
                $"\nPosition Amplitude：{positionAmplitude:F4}" +
                $"\nRotation Amplitude：{rotationAmplitude:F3}",
                this
            );
        }

        // =============================================================
        // 正式播放
        // =============================================================

        PlayShake(
            duration,
            positionAmplitude,
            rotationAmplitude
        );
    }

    #endregion

    // =====================================================================
    #region Shake 核心

    /// <summary>
    /// 播放指定參數的 Camera Shake。
    ///
    /// 如果短時間連續命中：
    ///
    /// 舊 Shake 會被停止，
    /// 新 Shake 立即接手。
    ///
    /// 目前先採用這個簡單規則，
    /// 避免全自動武器每發都累加震動後
    /// Camera 振幅失控。
    /// </summary>
    private void PlayShake(
        float duration,
        float positionAmplitude,
        float rotationAmplitude
    )
    {
        if (cameraEffectsRoot == null)
            return;

        if (duration <= 0f)
            return;

        if (currentShakeRoutine != null)
        {
            StopCoroutine(
                currentShakeRoutine
            );

            currentShakeRoutine =
                null;
        }

        /*
         * 每次新震動開始前，
         * 先確保回到基準位置。
         */
        ResetShakeTransform();

        currentShakeRoutine =
            StartCoroutine(
                ShakeRoutine(
                    duration,
                    positionAmplitude,
                    rotationAmplitude
                )
            );
    }

    /// <summary>
    /// 實際 Camera Shake Coroutine。
    /// </summary>
    private IEnumerator ShakeRoutine(
        float duration,
        float positionAmplitude,
        float rotationAmplitude
    )
    {
        float elapsed =
            0f;

        /*
         * 每次震動給一組不同 Noise Seed，
         * 避免每發子彈的 Camera Shake
         * 看起來都完全一樣。
         */
        float seedX =
            Random.Range(
                0f,
                1000f
            );

        float seedY =
            Random.Range(
                0f,
                1000f
            );

        float seedRotation =
            Random.Range(
                0f,
                1000f
            );

        while (elapsed <
               duration)
        {
            elapsed +=
                Time.deltaTime;

            float normalizedTime =
                Mathf.Clamp01(
                    elapsed /
                    duration
                );

            // ---------------------------------------------------------
            // 衰減
            // ---------------------------------------------------------

            float strength =
                shakeFalloff != null
                    ? shakeFalloff.Evaluate(
                        normalizedTime
                    )
                    : 1f - normalizedTime;

            // ---------------------------------------------------------
            // Noise
            // ---------------------------------------------------------

            float noiseTime =
                Time.unscaledTime *
                shakeFrequency;

            float noiseX =
                Mathf.PerlinNoise(
                    seedX,
                    noiseTime
                ) *
                2f -
                1f;

            float noiseY =
                Mathf.PerlinNoise(
                    seedY,
                    noiseTime
                ) *
                2f -
                1f;

            float noiseRotation =
                Mathf.PerlinNoise(
                    seedRotation,
                    noiseTime
                ) *
                2f -
                1f;

            // ---------------------------------------------------------
            // Position Offset
            // ---------------------------------------------------------

            /*
             * 命中震動目前只做 X / Y。
             *
             * 不使用 Z，
             * 避免 Camera 前後推動造成
             * 牆壁穿模或強烈暈動。
             */
            Vector3 positionOffset =
                new Vector3(
                    noiseX,
                    noiseY,
                    0f
                ) *
                positionAmplitude *
                strength;

            // ---------------------------------------------------------
            // Rotation Offset
            // ---------------------------------------------------------

            /*
             * 目前主要使用：
             *
             * Pitch
             * +
             * 一點 Roll。
             *
             * 不修改玩家真正 KCC Look，
             * 所以：
             *
             * 準心 Gameplay Direction
             * 不會因 Camera Shake 改變。
             */
            Vector3 rotationOffset =
                new Vector3(
                    noiseY *
                    rotationAmplitude,

                    0f,

                    noiseRotation *
                    rotationAmplitude *
                    0.5f
                ) *
                strength;

            // ---------------------------------------------------------
            // 套用
            // ---------------------------------------------------------

            cameraEffectsRoot.localPosition =
                baseLocalPosition +
                positionOffset;

            cameraEffectsRoot.localRotation =
                baseLocalRotation *
                Quaternion.Euler(
                    rotationOffset
                );

            yield return null;
        }

        // -------------------------------------------------------------
        // 完成
        // -------------------------------------------------------------

        ResetShakeTransform();

        currentShakeRoutine =
            null;
    }

    #endregion

    // =====================================================================
    #region Reset

    /// <summary>
    /// 將 CameraEffectsRoot 恢復正常狀態。
    /// </summary>
    private void ResetShakeTransform()
    {
        if (cameraEffectsRoot == null)
            return;

        cameraEffectsRoot.localPosition =
            baseLocalPosition;

        cameraEffectsRoot.localRotation =
            baseLocalRotation;
    }

    #endregion

    /// <summary>
    /// Editor / Play Mode 用的獨立 Camera Shake 測試。
    ///
    /// 這個測試完全不經過：
    ///
    /// AttackRifle
    /// AttackQuickMelee
    /// DamageResult
    /// PlayerCombatFeedbackRelay
    /// RPC
    ///
    /// 目的只有一個：
    ///
    /// 確認 FirstPersonHitCameraShake
    /// 本身是否真的能讓 CameraEffectsRoot 產生可見震動。
    ///
    /// 使用方式：
    ///
    /// Play Mode
    /// ↓
    /// Inspector 選取掛有 FirstPersonHitCameraShake 的 CameraRig
    /// ↓
    /// Component 右上角 ⋮
    /// ↓
    /// 選擇「測試強烈命中震動」
    ///
    /// 這裡故意使用非常明顯的數值，
    /// 所以如果 CameraEffectsRoot 設定正確，
    /// 一定看得出震動。
    /// </summary>
    [ContextMenu("測試強烈命中震動")]
    private void DebugPlayStrongShake()
    {
        if (cameraEffectsRoot == null)
        {
            Debug.LogError(
                $"[{nameof(FirstPersonHitCameraShake)}] " +
                $"無法測試震動，Camera Effects Root 沒有指定。",
                this
            );

            return;
        }

        Debug.Log(
            $"[{nameof(FirstPersonHitCameraShake)}] " +
            $"開始獨立測試 Camera Shake。" +
            $"\nCamera Effects Root：{cameraEffectsRoot.name}" +
            $"\nLocal Position：{cameraEffectsRoot.localPosition}" +
            $"\nLocal Rotation：{cameraEffectsRoot.localEulerAngles}",
            cameraEffectsRoot
        );

        /*
        * 故意使用很大的測試值。
        *
        * 正式遊戲不要使用這麼強。
        */
        PlayShake(
            duration: 0.4f,
            positionAmplitude: 0.05f,
            rotationAmplitude: 2f
        );
    }

    /// <summary>
    /// 收到「本地玩家真的執行了一次攻擊」。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// 這不是 Hit Confirm。
    ///
    /// 所以這裡完全不處理：
    ///
    /// Kill
    /// Headshot
    /// Applied Damage
    /// Hit Marker。
    ///
    /// ------------------------------------------------------------
    ///
    /// 目前只讓 Tank Light / Heavy
    /// 即使揮空也能產生武器重量感。
    /// </summary>
    public void PlayAttackShake(
        CombatFeedbackId feedbackId
    )
    {
        switch (feedbackId)
        {
            // =========================================================
            // Tank Light
            // =========================================================

            case CombatFeedbackId.TankLightMelee:
            {
                if (useSeparateTankLightShake ==
                    false)
                {
                    return;
                }

                if (debugShake)
                {
                    Debug.Log(
                        $"[Attack Camera Shake]" +
                        $"\n類型：Tank Light Swing" +
                        $"\nFeedback ID：{feedbackId}" +
                        $"\nDuration：{tankLightDuration:F3}" +
                        $"\nPosition Amplitude：{tankLightPositionAmplitude:F4}" +
                        $"\nRotation Amplitude：{tankLightRotationAmplitude:F3}",
                        this
                    );
                }

                PlayShake(
                    tankLightDuration,
                    tankLightPositionAmplitude,
                    tankLightRotationAmplitude
                );

                break;
            }

            // =========================================================
            // Tank Heavy
            // =========================================================

            case CombatFeedbackId.TankHeavyMelee:
            {
                if (useSeparateTankHeavyShake ==
                    false)
                {
                    return;
                }

                if (debugShake)
                {
                    Debug.Log(
                        $"[Attack Camera Shake]" +
                        $"\n類型：Tank Heavy Swing" +
                        $"\nFeedback ID：{feedbackId}" +
                        $"\nDuration：{tankHeavyDuration:F3}" +
                        $"\nPosition Amplitude：{tankHeavyPositionAmplitude:F4}" +
                        $"\nRotation Amplitude：{tankHeavyRotationAmplitude:F3}",
                        this
                    );
                }

                PlayShake(
                    tankHeavyDuration,
                    tankHeavyPositionAmplitude,
                    tankHeavyRotationAmplitude
                );

                break;
            }

            // =========================================================
            // Tank Enemy Dash
            // =========================================================

            case CombatFeedbackId.TankAirDashEnemy:
            {
                if (debugShake)
                {
                    Debug.Log(
                        $"[Attack Camera Shake]" +
                        $"\n類型：Tank Enemy Air Dash" +
                        $"\nFeedback ID：{feedbackId}" +
                        $"\nDuration：{tankEnemyAirDashDuration:F3}" +
                        $"\nPosition Amplitude：{tankEnemyAirDashPositionAmplitude:F3}" +
                        $"\nRotation Amplitude：{tankEnemyAirDashRotationAmplitude:F3}",
                        this
                    );
                }

                PlayShake(
                    tankEnemyAirDashDuration,
                    tankEnemyAirDashPositionAmplitude,
                    tankEnemyAirDashRotationAmplitude
                );

                break;
            }

            case CombatFeedbackId.TankAirDashWorld:
            {
                if (debugShake)
                {
                    Debug.Log(
                        $"[Attack Camera Shake]" +
                        $"\n類型：Tank World Air Dash" +
                        $"\nFeedback ID：{feedbackId}" +
                        $"\nDuration：{tankWorldAirDashDuration:F3}" +
                        $"\nPosition Amplitude：{tankWorldAirDashPositionAmplitude:F3}" +
                        $"\nRotation Amplitude：{tankWorldAirDashRotationAmplitude:F3}",
                        this
                    );
                }

                PlayShake(
                    tankWorldAirDashDuration,
                    tankWorldAirDashPositionAmplitude,
                    tankWorldAirDashRotationAmplitude
                );

                break;
            }
        }
    }
}