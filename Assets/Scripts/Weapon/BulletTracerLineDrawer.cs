using System.Collections;
using UnityEngine;

/// <summary>
/// 使用 LineRenderer 視覺化 Hitscan 武器的彈道。
///
/// 這不是實際 Projectile。
///
/// 真正傷害與命中仍然由 AttackRifle 的 Hitscan 決定。
///
/// 這支腳本只負責：
///
/// 1. 顯示子彈飛行方向。
/// 2. 顯示 Focus Auto Lock 修正後的彈道。
/// 3. 讓 Hitscan 看起來像高速 Projectile。
/// 4. 使用固定長度的曳光線段，而不是整條射線全部亮起。
/// 5. Hitscan 命中物體後，曳光線仍保持原速度繼續穿過命中點。
/// </summary>
[DisallowMultipleComponent]
public class BulletTracerLineDrawer : MonoBehaviour
{
    // =====================================================================
    #region 基本開關

    [Header("基本開關")]

    [SerializeField]
    [Tooltip("是否啟用彈道 LineRenderer 視覺化。關閉後 DrawTracer 會直接忽略，不會產生任何彈道。")]
    private bool enableTracer = true;

    #endregion

    // =====================================================================
    #region 外觀設定

    [Header("外觀設定")]

    [SerializeField]
    [Tooltip("彈道使用的材質。留空時使用支援顏色與透明度的 Sprites/Default。自訂 Shader 必須支援 LineRenderer 頂點色與透明混合；材質顏色及貼圖會再乘上彈道顏色。")]
    private Material tracerMaterial;

    [SerializeField]
    [Tooltip("彈道基礎色與 Alpha（0～1），會乘上 Tracer Gradient 的各色鍵與透明度鍵。要直接使用漸層原色，請設為白色且 Alpha 為 1；下一發生效。")]
    private Color tracerColor =
        new Color(
            1f,
            0.85f,
            0.25f,
            1f
        );

    [SerializeField]
    [Tooltip("沿曳光線從尾端（左、0）到前端（右、1）的顏色與透明度漸層。色鍵調 RGB、透明度鍵調 Alpha（0～1）；預設全白且完全不透明，保留舊 Prefab 顏色。與 Tracer Color 相乘，下一發生效。")]
    private Gradient tracerGradient = new Gradient();

    [SerializeField]
    [Tooltip("讀取此職業 Runtime 所屬玩家的鈎索動能。NormalizedEnergy 達到 1 時，新發射線段使用滿能量顏色；未滿、來源無效或尚未 Spawn 時使用 Tracer Color。")]
    private bool useMaximumEnergyColor = true;

    [SerializeField]
    [Tooltip("動能滿值時取代 Tracer Color 的基礎 RGBA（0～1），仍乘上 Tracer Gradient。預設青色；每發生成時取樣，隱藏延遲與飛行途中不重新換色。")]
    private Color maximumEnergyTracerColor = new Color(0.2f, 0.8f, 1f, 1f);

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("彈道線段起點寬度。這是曳光線尾端的粗細，單位為 Unity 世界單位。")]
    private float startWidth = 0.025f;

    [SerializeField]
    [Min(0.001f)]
    [Tooltip("彈道線段終點寬度。這是曳光線前端的粗細，單位為 Unity 世界單位。")]
    private float endWidth = 0.012f;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("曳光彈實際顯示的線段長度，單位為 Unity 世界單位。這個數值不會改變真正 Hitscan 射程，只影響視覺。")]
    private float tracerLength = 2f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("曳光彈完成整段視覺飛行後，完整保持顯示多少秒才開始淡出。若曳光線已經因為穿過命中點而隱藏，這個設定不會再生效。")]
    private float visibleDuration = 0.03f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("曳光彈完成整段視覺飛行後，淡出到完全消失需要多少秒。若曳光線已經因為穿過命中點而隱藏，這個設定不會再生效。")]
    private float fadeDuration = 0.04f;

    #endregion

    // =====================================================================
    #region 飛行設定

    [Header("飛行設定")]

    [SerializeField]
    [Min(0f)]
    [Tooltip("生成後先隱藏多少遊戲秒。線段照常飛行，但隱藏期間的路段永遠不畫；從時間到達時的前端位置開始長出，直到 Tracer Length。0 從槍口開始。若整段飛行都在隱藏期內，停留與淡出也不補畫。")]
    private float displayDelay = 0f;

    [SerializeField]
    [Tooltip("開啟後，曳光彈會沿著射擊方向高速飛行。關閉後會直接把曳光線放到最終視覺位置。")]
    private bool animateTravel = true;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip("曳光彈視覺飛行速度，單位為 Unity 世界單位每秒。這不會影響真正 Hitscan 的命中時間與傷害。")]
    private float travelSpeed = 250f;

    [SerializeField]
    [Min(0f)]
    [Tooltip("曳光彈到達 Hitscan 命中點後，視覺上至少還要繼續往前飛多少距離。系統會自動確保這個距離足以讓整條曳光線穿過命中點。")]
    private float passThroughDistance = 3f;

    [SerializeField]
    [Tooltip("開啟後，整條曳光線都穿過 Hitscan 命中點之後，會關閉 LineRenderer 顯示。但內部飛行計算仍然會繼續完成，不會在命中點停止。")]
    private bool hideAfterPassingHitPoint = true;

    #endregion

    // =====================================================================
    #region 階層設定

    [Header("階層設定")]

    [SerializeField]
    [Tooltip("產生出的暫時彈道物件要掛在哪個父物件底下。留空時會直接生成在場景根節點。")]
    private Transform tracerRoot;

    #endregion

    // =====================================================================
    #region 除錯

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，每次產生彈道時會在 Console 印出射擊起點、命中點、Hitscan 距離與視覺飛行距離。")]
    private bool debugTracer = false;

    #endregion

    // =====================================================================
    #region Runtime

    /// <summary>
    /// 沒有指定材質時建立的 Runtime Material。
    /// </summary>
    private Material runtimeMaterial;

    private PlayerProfessionRuntime professionRuntime;

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (tracerMaterial == null)
        {
            Shader shader =
                Shader.Find(
                    "Sprites/Default"
                );

            if (shader != null)
            {
                runtimeMaterial =
                    new Material(
                        shader
                    );
            }
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            Destroy(
                runtimeMaterial
            );
        }
    }

    #endregion

    // =====================================================================
    #region 對外接口

    /// <summary>
    /// 顯示一發 Hitscan 的曳光彈。
    ///
    /// start：
    /// 通常是 ViewModel 的 MuzzlePoint。
    ///
    /// end：
    /// 真正 Hitscan 的命中點。
    /// 如果沒有命中，就是 Max Shot Distance 的終點。
    ///
    /// 注意：
    /// end 只代表 Hitscan 結果。
    /// 它不再代表曳光線視覺動畫一定要停止的位置。
    /// </summary>
    public void DrawTracer(
        Vector3 start,
        Vector3 end
    )
    {
        if (enableTracer == false)
            return;

        float distance =
            Vector3.Distance(
                start,
                end
            );

        /*
         * 起點和終點幾乎相同時，
         * 不建立 LineRenderer。
         */
        if (distance <= 0.0001f)
            return;

        if (debugTracer)
        {
            Debug.Log(
                $"[BulletTracer] 產生彈道。" +
                $"\n起點：{start}" +
                $"\nHitscan 終點：{end}" +
                $"\nHitscan 距離：{distance:F2}" +
                $"\nTracer Length：{tracerLength:F2}" +
                $"\nPass Through Distance：{passThroughDistance:F2}",
                this
            );
        }

        StartCoroutine(
            PlayTracerRoutine(
                start,
                end
            )
        );
    }

    #endregion

    // =====================================================================
    #region 彈道動畫

    /// <summary>
    /// 播放固定長度的曳光線。
    ///
    /// Hitscan：
    ///
    /// Muzzle ------------------------ HitPoint
    ///
    /// 真正傷害在 HitPoint 立即發生。
    ///
    ///
    /// Tracer：
    ///
    /// Muzzle ---------> Tracer
    ///
    ///                  HitPoint
    ///                     │
    /// ------------------->│
    ///
    ///                     │ --------->
    ///
    /// 曳光線不會因為 HitPoint 而停止。
    ///
    /// 當整條 Tracer 都通過 HitPoint 後，
    /// 才會關閉 LineRenderer 顯示。
    ///
    /// 但 Coroutine 的飛行距離仍然會繼續完成，
    /// 所以邏輯上沒有在碰撞點瞬間停止。
    /// </summary>
    private IEnumerator PlayTracerRoutine(
        Vector3 start,
        Vector3 end
    )
    {
        // -------------------------------------------------------------
        // 建立 LineRenderer
        // -------------------------------------------------------------

        GameObject tracerObject =
            new GameObject(
                "BulletTracerLine"
            );

        if (tracerRoot != null)
        {
            tracerObject.transform.SetParent(
                tracerRoot,
                true
            );
        }

        LineRenderer line =
            tracerObject.AddComponent<LineRenderer>();

        SetupLineRenderer(
            line
        );

        // 位置與生命期照常更新；顯示延遲另決定可繪製路段的固定起點。
        float effectiveDisplayDelay = Mathf.Max(0f, displayDelay);
        if (effectiveDisplayDelay > 0f)
            line.enabled = false;

        // -------------------------------------------------------------
        // 計算 Hitscan 路徑
        // -------------------------------------------------------------

        Vector3 path =
            end -
            start;

        float totalDistance =
            path.magnitude;

        /*
         * 理論上 DrawTracer 前面已經排除距離為 0 的狀況，
         * 這裡再保護一次，避免除以 0。
         */
        if (totalDistance <= 0.0001f)
        {
            Destroy(
                tracerObject
            );

            yield break;
        }

        Vector3 direction =
            path /
            totalDistance;

        // -------------------------------------------------------------
        // 計算實際 Tracer 長度
        // -------------------------------------------------------------

        /*
         * 如果敵人離槍口比 tracerLength 還近，
         * Tracer 不會反向延伸到槍口後方。
         *
         * 例如：
         *
         * tracerLength = 2m
         * 敵人距離 = 1m
         *
         * 那麼有效長度就是 1m。
         */
        float validTracerLength =
            Mathf.Min(
                Mathf.Max(
                    0.01f,
                    tracerLength
                ),
                totalDistance
            );

        // -------------------------------------------------------------
        // 計算穿透後的額外視覺距離
        // -------------------------------------------------------------

        /*
         * 這裡很重要。
         *
         * 如果：
         *
         * tracerLength = 2m
         * passThroughDistance = 0.5m
         *
         * 那 Tracer 的 Head 只往 HitPoint 後面跑 0.5m，
         * 但 Tail 還沒有通過 HitPoint。
         *
         * 所以系統會自動確保：
         *
         * effectivePassThroughDistance
         * 至少 >= validTracerLength
         *
         * 如此才能保證整條 Tracer 都有機會穿過 HitPoint。
         */
        float effectivePassThroughDistance =
            Mathf.Max(
                passThroughDistance,
                validTracerLength
            );

        /*
         * totalDistance：
         * 真正 Hitscan 距離。
         *
         * visualTravelDistance：
         * Tracer 視覺動畫實際移動的完整距離。
         *
         *
         *            Hitscan End
         *                │
         * Muzzle --------│---------- Visual End
         *
         */
        float visualTravelDistance =
            totalDistance +
            effectivePassThroughDistance;

        if (debugTracer)
        {
            Debug.Log(
                $"[BulletTracer] 視覺飛行設定。" +
                $"\nHitscan Distance：{totalDistance:F2}" +
                $"\n有效 Tracer Length：{validTracerLength:F2}" +
                $"\n有效穿透距離：{effectivePassThroughDistance:F2}" +
                $"\nVisual Travel Distance：{visualTravelDistance:F2}",
                this
            );
        }

        // -------------------------------------------------------------
        // 紀錄是否已經把 Renderer 隱藏
        // -------------------------------------------------------------

        bool rendererHiddenAfterHit =
            false;

        float tracerAge = 0f;
        bool revealStartResolved = effectiveDisplayDelay <= 0f;
        // 尚未開放時預設裁到最終位置，避免停留／淡出回補隱藏路段。
        float revealStartDistance = revealStartResolved ? 0f : visualTravelDistance;

        // =============================================================
        // 有飛行動畫
        // =============================================================

        if (animateTravel &&
            travelSpeed > 0.01f)
        {
            float headDistance =
                0f;

            while (headDistance <
                   visualTravelDistance)
            {
                float previousAge = tracerAge;
                float previousHeadDistance = headDistance;
                tracerAge += Time.deltaTime;
                // -----------------------------------------------------
                // Head 持續以固定速度前進
                // -----------------------------------------------------

                /*
                 * 注意：
                 *
                 * 這裡完全不會因為 totalDistance
                 * 也就是 Hitscan HitPoint 而停止。
                 *
                 * Head 永遠朝 visualTravelDistance 前進。
                 */
                headDistance +=
                    travelSpeed *
                    Time.deltaTime;

                headDistance =
                    Mathf.Min(
                        headDistance,
                        visualTravelDistance
                    );

                if (!revealStartResolved && tracerAge >= effectiveDisplayDelay)
                {
                    // 在跨過開放時間的這一幀內計算位置，避免結果依 FPS 向前偏移。
                    revealStartDistance = Mathf.Min(visualTravelDistance,
                        previousHeadDistance + travelSpeed * (effectiveDisplayDelay - previousAge));
                    revealStartResolved = true;
                }

                // -----------------------------------------------------
                // Tracer Head
                // -----------------------------------------------------

                Vector3 tracerHead =
                    start +
                    direction *
                    headDistance;

                // -----------------------------------------------------
                // Tracer Tail
                // -----------------------------------------------------

                /*
                 * Head 還沒走滿 tracerLength 時：
                 *
                 * Tail 留在槍口。
                 *
                 *
                 * Head 超過 tracerLength 後：
                 *
                 * Tail 開始跟著往前。
                 */
                float tailDistance =
                    Mathf.Max(
                        0f,
                        headDistance -
                        validTracerLength
                    );

                float visibleTailDistance = Mathf.Min(headDistance,
                    Mathf.Max(tailDistance, revealStartDistance));
                Vector3 tracerTail =
                    start +
                    direction *
                    visibleTailDistance;

                // -----------------------------------------------------
                // 更新 LineRenderer
                // -----------------------------------------------------

                line.SetPosition(
                    0,
                    tracerTail
                );

                line.SetPosition(
                    1,
                    tracerHead
                );

                // -----------------------------------------------------
                // 整條 Tracer 已穿過 Hitscan 命中點
                // -----------------------------------------------------

                /*
                 * 我們不能用：
                 *
                 * headDistance >= totalDistance
                 *
                 * 因為那代表只有 Head 剛碰到 HitPoint。
                 *
                 *
                 * 要判斷：
                 *
                 * tailDistance >= totalDistance
                 *
                 * 才代表整條 Tracer 都已經越過命中位置。
                 *
                 *
                 * Head 剛碰到：
                 *
                 * Tail ================= Head
                 *                       │
                 *                     Hit
                 *
                 *
                 * 整條穿過：
                 *
                 * Hit
                 *  │
                 *  │        Tail ================= Head
                 */
                if (hideAfterPassingHitPoint &&
                    rendererHiddenAfterHit == false &&
                    tailDistance >= totalDistance)
                {
                    /*
                     * 只關閉顯示。
                     *
                     * 不 Destroy。
                     * 不 yield break。
                     * 不修改 headDistance。
                     *
                     * 所以內部的飛行動畫仍會繼續跑完。
                     */
                    line.enabled =
                        false;

                    rendererHiddenAfterHit =
                        true;
                }

                line.enabled = !rendererHiddenAfterHit && revealStartResolved && headDistance - visibleTailDistance > 0.0001f;
                yield return null;
            }
        }

        // =============================================================
        // 沒有飛行動畫
        // =============================================================

        else
        {
            /*
             * 關閉 animateTravel 時，
             * 直接把 Tracer 放到完整 Visual End。
             */
            float finalHeadDistance =
                visualTravelDistance;

            float finalTailDistance =
                Mathf.Max(
                    0f,
                    finalHeadDistance -
                    validTracerLength
                );

            Vector3 tracerHead =
                start +
                direction *
                finalHeadDistance;

            Vector3 tracerTail =
                start +
                direction *
                finalTailDistance;

            line.SetPosition(
                0,
                tracerTail
            );

            line.SetPosition(
                1,
                tracerHead
            );

            /*
             * 因為沒有飛行動畫，
             * 此時 Tracer 已經直接位於 HitPoint 後方。
             */
            if (hideAfterPassingHitPoint &&
                finalTailDistance >= totalDistance)
            {
                line.enabled =
                    false;

                rendererHiddenAfterHit =
                    true;
            }
        }

        // =============================================================
        // 最終位置
        // =============================================================

        /*
         * 不管 FPS 或最後一幀的 deltaTime 如何，
         * 最後都再把位置精準設定一次。
         *
         * 注意：
         *
         * finalHead 不是 Hitscan 的 end。
         *
         * finalHead 位於：
         *
         * end + 穿透距離
         */
        Vector3 finalHead =
            start +
            direction *
            visualTravelDistance;

        float finalTailDistanceAfterTravel =
            Mathf.Max(
                0f,
                visualTravelDistance -
                validTracerLength
            );

        finalTailDistanceAfterTravel = Mathf.Min(visualTravelDistance,
            Mathf.Max(finalTailDistanceAfterTravel, revealStartDistance));
        bool hasVisibleSegment = revealStartResolved && visualTravelDistance - finalTailDistanceAfterTravel > 0.0001f;

        Vector3 finalTail =
            start +
            direction *
            finalTailDistanceAfterTravel;

        line.SetPosition(
            0,
            finalTail
        );

        line.SetPosition(
            1,
            finalHead
        );

        // =============================================================
        // 如果已經因為穿過 HitPoint 而隱藏
        // =============================================================

        /*
         * 到這裡代表內部飛行動畫已經完整跑完。
         *
         * Renderer 先前雖然已經隱藏，
         * 但 Coroutine 並沒有提前結束。
         *
         * 現在才真正 Destroy。
         */
        if (rendererHiddenAfterHit)
        {
            Destroy(
                tracerObject
            );

            yield break;
        }

        // =============================================================
        // 停留
        // =============================================================

        /*
         * hideAfterPassingHitPoint 關閉時，
         * Tracer 才會在 Visual End 保持顯示。
         */
        float holdElapsed = 0f;
        while (holdElapsed < visibleDuration)
        {
            line.enabled = hasVisibleSegment && tracerAge >= effectiveDisplayDelay;
            yield return null;
            holdElapsed += Time.deltaTime;
            tracerAge += Time.deltaTime;
        }

        // =============================================================
        // 淡出
        // =============================================================

        if (fadeDuration > 0f)
        {
            float elapsed =
                0f;

            // 使用這一發的空間漸層；淡出只縮放每個 Alpha 鍵，不覆寫顏色鍵。
            Gradient shotGradient = line.colorGradient;
            GradientColorKey[] colorKeys = shotGradient.colorKeys;
            GradientAlphaKey[] alphaKeys = shotGradient.alphaKeys;
            GradientAlphaKey[] fadedAlphaKeys = new GradientAlphaKey[alphaKeys.Length];
            Gradient fadedGradient = new Gradient { mode = shotGradient.mode };

            while (elapsed <
                   fadeDuration)
            {
                elapsed +=
                    Time.deltaTime;
                tracerAge += Time.deltaTime;
                line.enabled = hasVisibleSegment && tracerAge >= effectiveDisplayDelay;

                float progress =
                    Mathf.Clamp01(
                        elapsed /
                        fadeDuration
                    );

                for (int i = 0; i < alphaKeys.Length; i++)
                {
                    fadedAlphaKeys[i] = new GradientAlphaKey(
                        alphaKeys[i].alpha * (1f - progress),
                        alphaKeys[i].time
                    );
                }

                fadedGradient.SetKeys(colorKeys, fadedAlphaKeys);
                line.colorGradient = fadedGradient;

                yield return null;
            }
        }

        // =============================================================
        // Destroy
        // =============================================================

        Destroy(
            tracerObject
        );
    }

    #endregion

    // =====================================================================
    #region LineRenderer 設定

    /// <summary>依正式職業 Owner 讀取動能，避免把其他玩家或未 Spawn 狀態當成來源。</summary>
    private Color ResolveTracerColor()
    {
        if (!useMaximumEnergyColor)
            return tracerColor;

        if (professionRuntime == null)
            professionRuntime = GetComponentInParent<PlayerProfessionRuntime>();

        if (professionRuntime == null || professionRuntime.Object == null ||
            !professionRuntime.Object.IsValid || professionRuntime.Runner == null)
            return tracerColor;

        Player owner = professionRuntime.OwnerPlayer;
        if (owner == null || owner.Object == null || !owner.Object.IsValid)
            return tracerColor;

        PlayerGrappleMomentumEnergy energy = owner.GetComponent<PlayerGrappleMomentumEnergy>();
        if (energy == null || energy.Object == null || !energy.Object.IsValid || energy.Runner == null)
            return tracerColor;

        return SelectTracerColor(energy.NormalizedEnergy);
    }

    private Color SelectTracerColor(float normalizedEnergy)
    {
        return useMaximumEnergyColor && normalizedEnergy >= 1f
            ? maximumEnergyTracerColor
            : tracerColor;
    }

    /// <summary>
    /// 初始化 LineRenderer 外觀。
    /// </summary>
    private void SetupLineRenderer(
        LineRenderer line
    )
    {
        line.useWorldSpace =
            true;

        line.positionCount =
            2;

        line.startWidth =
            startWidth;

        line.endWidth =
            endWidth;

        // -------------------------------------------------------------
        // 圓形端點
        // -------------------------------------------------------------

        /*
         * 讓 Tracer 變成：
         *
         * (================)
         *
         * 而不是：
         *
         * [================]
         */
        line.numCapVertices =
            12;

        // LineRenderer 的漸層沿線段長度取樣；Prefab 舊色保留為整體乘色。
        Gradient source = tracerGradient ?? new Gradient();
        Color shotColor = ResolveTracerColor();
        GradientColorKey[] sourceColors = source.colorKeys;
        GradientAlphaKey[] sourceAlphas = source.alphaKeys;
        GradientColorKey[] colors = new GradientColorKey[sourceColors.Length];
        GradientAlphaKey[] alphas = new GradientAlphaKey[sourceAlphas.Length];

        for (int i = 0; i < colors.Length; i++)
        {
            Color color = sourceColors[i].color;
            colors[i] = new GradientColorKey(
                new Color(color.r * shotColor.r, color.g * shotColor.g,
                    color.b * shotColor.b, 1f),
                sourceColors[i].time
            );
        }

        for (int i = 0; i < alphas.Length; i++)
        {
            alphas[i] = new GradientAlphaKey(
                Mathf.Clamp01(sourceAlphas[i].alpha * shotColor.a),
                sourceAlphas[i].time
            );
        }

        Gradient appearance = new Gradient { mode = source.mode };
        appearance.SetKeys(colors, alphas);
        line.colorGradient = appearance;

        line.shadowCastingMode =
            UnityEngine.Rendering
                .ShadowCastingMode
                .Off;

        line.receiveShadows =
            false;

        line.textureMode =
            LineTextureMode.Stretch;

        /*
         * LineRenderer 永遠朝 Camera，
         * 適合這種曳光彈 Billboard 效果。
         */
        line.alignment =
            LineAlignment.View;

        Material materialToUse =
            tracerMaterial != null
                ? tracerMaterial
                : runtimeMaterial;

        if (materialToUse != null)
        {
            line.material =
                materialToUse;
        }
    }

    #endregion
}
