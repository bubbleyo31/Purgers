using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 第一人稱玩家命中提示 Hit Marker。
///
/// ------------------------------------------------------------
///
/// 當玩家自己的攻擊被 State Authority
/// 正式確認造成有效傷害後，
/// 在準心中央顯示一個短暫的 X。
///
/// ------------------------------------------------------------
///
/// 這支腳本是純本地 Presentation。
///
/// 它不負責：
///
/// 傷害
/// Raycast
/// Enemy Health
/// Photon RPC
/// 命中判定
///
/// 它只接收：
///
/// CombatHitFeedbackData
///
/// 並決定 Hit Marker 要怎麼顯示。
///
/// ------------------------------------------------------------
///
/// 目前支援：
///
/// Normal Hit
/// Headshot
/// Kill
///
/// 三種不同視覺設定。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public class FirstPersonHitMarker : MonoBehaviour
{
    // =====================================================================
    #region Singleton

    /// <summary>
    /// 場景中唯一的本地 Hit Marker。
    /// </summary>
    public static FirstPersonHitMarker Singleton
    {
        get;
        private set;
    }

    #endregion

    // =====================================================================
    #region UI 引用

    [Header("UI 引用")]

    [SerializeField]
    [Tooltip("Hit Marker 整體 RectTransform。通常就是掛著這支腳本的 HitMarkerRoot。若留空會自動取得。")]
    private RectTransform markerRoot;

    [SerializeField]
    [Tooltip("控制整個 Hit Marker 透明度的 CanvasGroup。若留空會自動取得。")]
    private CanvasGroup canvasGroup;

    [SerializeField]
    [Tooltip("組成 Hit Marker 的所有 UI 圖片陣列。將組成 X 的線條或任何形狀的 Image 全部放進這裡，程式會一次性控制它們的顏色。")]
    private Image[] markerLines = new Image[0];

    #endregion

    // =====================================================================
    #region 普通命中

    [Header("普通命中")]

    [SerializeField]
    [Tooltip("普通有效命中的 Hit Marker 顏色。")]
    private Color normalHitColor =
        Color.white;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("普通命中 Hit Marker 顯示的總時間，單位為秒。建議先從 0.08 到 0.15 秒測試。")]
    private float normalHitDuration =
        0.12f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("普通命中 Hit Marker 出現瞬間的縮放倍率。大於 1 會先稍微放大，再快速縮回正常大小，增加命中的打擊感。")]
    private float normalHitStartScale =
        1.35f;

    #endregion

    // =====================================================================
    #region 近戰命中

    [Header("近戰命中")]

    [SerializeField]
    [Tooltip("開啟後，Damage Type 為 Melee 的命中會使用獨立 Hit Marker 樣式，不會沿用普通步槍命中設定。")]
    private bool useSeparateMeleeStyle =
        true;

    [SerializeField]
    [Tooltip("快速近戰成功命中時 X Hit Marker 的顏色。")]
    private Color meleeHitColor =
        new Color(
            1f,
            0.75f,
            0.25f,
            1f
        );

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("快速近戰 Hit Marker 的顯示總時間。近戰可以比普通槍械稍微停久一點，增加打擊感。")]
    private float meleeHitDuration =
        0.16f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("快速近戰 Hit Marker 出現瞬間的縮放倍率。數值越高，X 出現時會越有衝擊感。")]
    private float meleeHitStartScale =
        1.65f;
    
    #endregion

    // =====================================================================
    #region Tank 輕攻擊命中

    [Header("Tank 輕攻擊命中")]

    [SerializeField]
    [Tooltip("開啟後，Feedback ID 為 TankLightMelee 的有效命中會使用獨立 Hit Marker 樣式。Light1 與 Light2 目前共用這組設定。關閉後會退回一般 Melee Hit Marker。")]
    private bool useSeparateTankLightStyle =
        true;

    [SerializeField]
    [Tooltip("Tank 第一段與第二段輕攻擊成功命中敵人時，X Hit Marker 的顏色。這只影響真正 Damage Confirmed 後的命中提示，揮空不會顯示。")]
    private Color tankLightHitColor =
        new Color(
            1f,
            0.8f,
            0.3f,
            1f
        );

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank 第一段與第二段輕攻擊成功命中時，X Hit Marker 顯示多久，單位為秒。")]
    private float tankLightHitDuration =
        0.14f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank 輕攻擊 X 出現瞬間的起始縮放倍率。數值越大，X 彈出的衝擊感越強。")]
    private float tankLightHitStartScale =
        1.5f;

    #endregion

    // =====================================================================
    #region Tank 重攻擊命中

    [Header("Tank 重攻擊命中")]

    [SerializeField]
    [Tooltip("開啟後，Feedback ID 為 TankHeavyMelee 的有效命中會使用獨立 Hit Marker 樣式。關閉後會退回一般 Melee Hit Marker。")]
    private bool useSeparateTankHeavyStyle =
        true;

    [SerializeField]
    [Tooltip("Tank 第三段 Heavy 成功命中敵人時，X Hit Marker 的顏色。建議可以與 Light 使用相同色系，但更亮或更接近白色，讓 Heavy 看起來更有重量。")]
    private Color tankHeavyHitColor =
        new Color(
            1f,
            0.6f,
            0.15f,
            1f
        );

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank Heavy 成功命中時，X Hit Marker 顯示多久，單位為秒。Heavy 可以比 Light 稍微停久一點。")]
    private float tankHeavyHitDuration =
        0.18f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank Heavy X 出現瞬間的起始縮放倍率。建議比 Light 稍大，讓第三刀的命中感明顯不同。")]
    private float tankHeavyHitStartScale =
        1.8f;

    #endregion
    
    // =====================================================================
    #region Tank Air Strike 命中

    [Header("Tank Air Strike 命中")]

    [SerializeField]
    [Tooltip("開啟後，Tank Enemy Air Dash 成功抵達並以 AOE 造成有效傷害時使用獨立 X Hit Marker。World Dash 不會使用這個樣式，因為 World Dash 沒有傷害。")]
    private bool useSeparateTankAirStrikeStyle =
        true;

    [SerializeField]
    [Tooltip("Tank Air Strike 成功命中時的 X Hit Marker 顏色。這只是第一輪測試顏色，可以直接在 Inspector 調整。")]
    private Color tankAirStrikeHitColor =
        Color.white;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank Air Strike 成功造成有效傷害時，X Hit Marker 顯示多久，單位為秒。")]
    private float tankAirStrikeHitDuration =
        0.2f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("Tank Air Strike X 出現瞬間的起始 Scale。Air Strike 是高傷害技能，因此建議明顯大於普通 Light / Heavy。")]
    private float tankAirStrikeHitStartScale =
        2f;

    #endregion

    // =====================================================================
    #region 暴頭

    [Header("暴頭")]

    [SerializeField]
    [Tooltip("是否讓 Headshot 使用不同於普通命中的 Hit Marker 表現。")]
    private bool useSeparateHeadshotStyle =
        true;

    [SerializeField]
    [Tooltip("暴頭 Hit Marker 顏色。之後有正式 UI 美術時可以再修改。")]
    private Color headshotColor =
        new Color(
            1f,
            0.55f,
            0.1f,
            1f
        );

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("暴頭 Hit Marker 顯示時間，單位為秒。")]
    private float headshotDuration =
        0.15f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("暴頭 Hit Marker 出現瞬間的縮放倍率。")]
    private float headshotStartScale =
        1.55f;

    #endregion

    // =====================================================================
    #region 擊殺

    [Header("擊殺")]

    [SerializeField]
    [Tooltip("是否讓擊殺使用獨立的 Hit Marker 表現。擊殺的優先權高於暴頭。")]
    private bool useSeparateKillStyle =
        true;

    [SerializeField]
    [Tooltip("擊殺 Hit Marker 顏色。")]
    private Color killColor =
        new Color(
            1f,
            0.2f,
            0.2f,
            1f
        );

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("擊殺 Hit Marker 顯示時間，單位為秒。")]
    private float killDuration =
        0.18f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("擊殺 Hit Marker 出現瞬間的縮放倍率。")]
    private float killStartScale =
        1.7f;

    #endregion

    // =====================================================================
    #region 動畫

    [Header("動畫")]

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("整段 Hit Marker 動畫中，有多少比例用來保持較清楚的顯示。剩餘時間會逐漸淡出。例如 0.35 代表前 35% 保持明顯，後 65% 淡出。")]
    private float holdRatio =
        0.3f;

    [SerializeField]
    [Tooltip("Hit Marker 縮放動畫曲線。X 軸是整段生命週期，Y 軸是從起始放大倍率回到正常大小的進度。")]
    private AnimationCurve scaleCurve =
        AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f
        );

    [SerializeField]
    [Tooltip("Hit Marker 淡出曲線。X 軸是淡出階段進度，Y 軸是透明度。預設從完全顯示逐漸下降到完全透明。")]
    private AnimationCurve fadeCurve =
        AnimationCurve.Linear(
            0f,
            1f,
            1f,
            0f
        );

    #endregion

    // =====================================================================
    #region 連續命中

    [Header("連續命中")]

    [SerializeField]
    [Tooltip("開啟後，每一次新的有效命中都會重新播放完整 Hit Marker 動畫。很適合全自動步槍與 Focus 連續射擊。")]
    private bool restartAnimationOnNewHit =
        true;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，每次播放 Hit Marker 時會在 Console 顯示 Normal、Headshot 或 Kill。")]
    private bool debugHitMarker =
        false;

    #endregion

    // =====================================================================
    #region Runtime

    /// <summary>
    /// 目前正在播放的 Hit Marker 動畫。
    /// </summary>
    private Coroutine markerRoutine;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        RegisterSingleton();

        if (markerRoot == null)
        {
            markerRoot =
                GetComponent<RectTransform>();
        }

        if (canvasGroup == null)
        {
            canvasGroup =
                GetComponent<CanvasGroup>();
        }

        HideImmediately();
    }

    private void OnDisable()
    {
        HideImmediately();
    }

    private void OnDestroy()
    {
        if (Singleton == this)
        {
            Singleton =
                null;
        }
    }

    #endregion

    // =====================================================================
    #region Singleton

    private void RegisterSingleton()
    {
        if (Singleton == null)
        {
            Singleton =
                this;

            return;
        }

        if (Singleton == this)
            return;

        Debug.LogError(
            $"場景中只能存在一個 " +
            $"{nameof(FirstPersonHitMarker)}。",
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
    /// 播放一次正式命中提示。
    ///
    /// ------------------------------------------------------------
    ///
    /// 只有 DamageResult 已經正式確認：
    ///
    /// HasEffectiveDamage = true
    ///
    /// 才會進入 Hit Marker。
    ///
    /// ------------------------------------------------------------
    ///
    /// 優先級固定為：
    ///
    /// Kill
    /// ↓
    /// Tank Heavy
    /// ↓
    /// Tank Light
    /// ↓
    /// Headshot
    /// ↓
    /// General Melee
    /// ↓
    /// Normal。
    ///
    /// ------------------------------------------------------------
    ///
    /// 注意：
    ///
    /// Tank 揮空只會播放 Swing Camera Shake，
    /// 不會呼叫這個函式，
    /// 因此不會憑空產生 X。
    /// </summary>
    public void PlayHitMarker(
        CombatHitFeedbackData feedback
    )
    {
        // =============================================================
        // 沒有有效傷害
        // =============================================================

        if (feedback.HasEffectiveDamage ==
            false)
        {
            return;
        }

        Color color;
        float duration;
        float startScale;
        string markerType;

        // =============================================================
        // Feedback Identity
        // =============================================================

        bool isTankLight =
            feedback.FeedbackId ==
            CombatFeedbackId.TankLightMelee;

        bool isTankHeavy =
            feedback.FeedbackId ==
            CombatFeedbackId.TankHeavyMelee;

        bool isMelee =
            feedback.DamageType ==
            DamageType.Melee;

        bool isTankAirStrike =
            feedback.FeedbackId ==
            CombatFeedbackId.TankAirStrike;
        // =============================================================
        // 1. Kill
        // =============================================================

        /*
        * ★ Kill 永遠最高優先級。
        *
        * 不管是：
        *
        * Attack Rifle
        * Attack Quick Melee
        * Tank Light
        * Tank Heavy
        *
        * 只要真正擊殺，
        * 就優先使用 Kill Hit Marker。
        */
        if (feedback.KilledTarget &&
            useSeparateKillStyle)
        {
            color =
                killColor;

            duration =
                killDuration;

            startScale =
                killStartScale;

            markerType =
                "Kill";
        }

        // =============================================================
        // 2. Tank Air Strike
        // =============================================================

        else if (isTankAirStrike &&
                useSeparateTankAirStrikeStyle)
        {
            color =
                tankAirStrikeHitColor;

            duration =
                tankAirStrikeHitDuration;

            startScale =
                tankAirStrikeHitStartScale;

            markerType =
                "Tank Air Strike";
        }

        // =============================================================
        // 2. Tank Heavy
        // =============================================================

        else if (isTankHeavy &&
                useSeparateTankHeavyStyle)
        {
            color =
                tankHeavyHitColor;

            duration =
                tankHeavyHitDuration;

            startScale =
                tankHeavyHitStartScale;

            markerType =
                "Tank Heavy";
        }

        // =============================================================
        // 3. Tank Light
        // =============================================================

        else if (isTankLight &&
                useSeparateTankLightStyle)
        {
            color =
                tankLightHitColor;

            duration =
                tankLightHitDuration;

            startScale =
                tankLightHitStartScale;

            markerType =
                "Tank Light";
        }

        // =============================================================
        // 4. Headshot
        // =============================================================

        else if (feedback.IsHeadshot &&
                useSeparateHeadshotStyle)
        {
            color =
                headshotColor;

            duration =
                headshotDuration;

            startScale =
                headshotStartScale;

            markerType =
                "Headshot";
        }

        // =============================================================
        // 5. General Melee
        // =============================================================

        /*
        * 目前主要會接：
        *
        * AttackQuickMelee。
        *
        * Tank Light / Heavy
        * 如果自己的專屬 Style 被關閉，
        * 也會退回到這裡。
        */
        else if (isMelee &&
                useSeparateMeleeStyle)
        {
            color =
                meleeHitColor;

            duration =
                meleeHitDuration;

            startScale =
                meleeHitStartScale;

            markerType =
                "Melee";
        }

        // =============================================================
        // 6. Normal
        // =============================================================

        else
        {
            color =
                normalHitColor;

            duration =
                normalHitDuration;

            startScale =
                normalHitStartScale;

            markerType =
                "Normal";
        }

        // =============================================================
        // Debug
        // =============================================================

        if (debugHitMarker)
        {
            Debug.Log(
                $"[Hit Marker]" +
                $"\n類型：{markerType}" +
                $"\nFeedback ID：{feedback.FeedbackId}" +
                $"\nDamage Type：{feedback.DamageType}" +
                $"\nApplied Damage：{feedback.AppliedDamage:F2}" +
                $"\nHeadshot：{feedback.IsHeadshot}" +
                $"\nKill：{feedback.KilledTarget}" +
                $"\nSequence：{feedback.Sequence}",
                this
            );
        }

        // =============================================================
        // 正式播放
        // =============================================================

        /*
        * 最後一定要真正啟動動畫。
        *
        * 如果你 Unity 現在版本原本就有這行，
        * 保持一份即可，不要重複呼叫。
        */
        StartMarkerAnimation(
            color,
            duration,
            startScale
        );
    }

    #endregion

    // =====================================================================
    #region 動畫控制

    /// <summary>
    /// 開始播放 Hit Marker。
    /// </summary>
    private void StartMarkerAnimation(
        Color color,
        float duration,
        float startScale
    )
    {
        if (markerRoot == null ||
            canvasGroup == null)
        {
            return;
        }

        /*
         * 全自動步槍高速命中時，
         * 新的一發可以重新啟動動畫。
         */
        if (markerRoutine != null)
        {
            if (restartAnimationOnNewHit == false)
            {
                return;
            }

            StopCoroutine(
                markerRoutine
            );

            markerRoutine =
                null;
        }

        SetMarkerColor(
            color
        );

        markerRoutine =
            StartCoroutine(
                MarkerRoutine(
                    duration,
                    startScale
                )
            );
    }

    /// <summary>
    /// Hit Marker 主動畫。
    ///
    /// 包含：
    ///
    /// 1. 瞬間放大。
    /// 2. 快速縮回。
    /// 3. 保持。
    /// 4. 淡出。
    /// </summary>
    private IEnumerator MarkerRoutine(
        float duration,
        float startScale
    )
    {
        float elapsed =
            0f;

        duration =
            Mathf.Max(
                0.01f,
                duration
            );

        canvasGroup.alpha =
            1f;

        markerRoot.localScale =
            Vector3.one *
            startScale;

        while (elapsed <
               duration)
        {
            elapsed +=
                Time.unscaledDeltaTime;

            float normalizedTime =
                Mathf.Clamp01(
                    elapsed /
                    duration
                );

            // ---------------------------------------------------------
            // Scale
            // ---------------------------------------------------------

            float scaleProgress =
                scaleCurve != null
                    ? scaleCurve.Evaluate(
                        normalizedTime
                    )
                    : normalizedTime;

            float currentScale =
                Mathf.Lerp(
                    startScale,
                    1f,
                    scaleProgress
                );

            markerRoot.localScale =
                Vector3.one *
                currentScale;

            // ---------------------------------------------------------
            // Alpha
            // ---------------------------------------------------------

            if (normalizedTime <=
                holdRatio)
            {
                canvasGroup.alpha =
                    1f;
            }
            else
            {
                float fadeProgress =
                    Mathf.InverseLerp(
                        holdRatio,
                        1f,
                        normalizedTime
                    );

                float alpha =
                    fadeCurve != null
                        ? fadeCurve.Evaluate(
                            fadeProgress
                        )
                        : 1f -
                          fadeProgress;

                canvasGroup.alpha =
                    Mathf.Clamp01(
                        alpha
                    );
            }

            yield return null;
        }

        HideImmediately();

        markerRoutine =
            null;
    }

    #endregion

    // =====================================================================
    #region 顏色

    /// <summary>
    /// 同時設定陣列中所有 Hit Marker 組成圖形的顏色。
    ///
    /// 透過陣列控制的好處：
    /// 未來如果 Hit Marker 從「2條線的交叉」變成「4條獨立的線段」或「加上外圈圓形」，
    /// 都不需要再修改程式碼，只要在 Inspector 中擴充 markerLines 陣列即可。
    /// </summary>
    private void SetMarkerColor(
        Color color
    )
    {
        /*
         * 防呆保護：如果陣列尚未初始化或是 null，直接返回，
         * 避免觸發 NullReferenceException 導致遊戲報錯。
         */
        if (markerLines == null)
            return;

        /*
         * 走訪陣列中所有的 UI Image。
         * 使用傳統 for 迴圈效能會比 foreach 稍微好一點，
         * 特別是對於在戰鬥中會頻繁觸發的 Hit Marker 來說是個好習慣。
         */
        for (int i = 0; i < markerLines.Length; i++)
        {
            // 確保陣列內的元素沒有遺失（例如物件被意外刪除）
            if (markerLines[i] != null)
            {
                markerLines[i].color =
                    color;
            }
        }
    }

    #endregion

    // =====================================================================
    #region Hide

    /// <summary>
    /// 立即隱藏 Hit Marker。
    /// </summary>
    private void HideImmediately()
    {
        if (markerRoutine != null)
        {
            StopCoroutine(
                markerRoutine
            );

            markerRoutine =
                null;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha =
                0f;
        }

        if (markerRoot != null)
        {
            markerRoot.localScale =
                Vector3.one;
        }
    }

    #endregion
}