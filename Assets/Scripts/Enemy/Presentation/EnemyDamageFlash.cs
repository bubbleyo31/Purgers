using Fusion;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵人受到有效傷害後的本機材質閃白 Presentation。
///
/// ====================================================================
///
/// 不切換 Enemy Brain State。
/// 不封鎖移動。
/// 不封鎖攻擊。
/// 不修改正式傷害。
///
/// 每台電腦在 Render 階段觀察同步的 CurrentHealth，
/// 只有生命值下降時才播放閃白。
///
/// ====================================================================
///
/// 使用 MaterialPropertyBlock：
///
/// - 不會修改 Project 內的 Material Asset。
/// - 不會替每隻敵人複製一份 Material。
/// - 可以支援同 Renderer 的多個 Material Slot。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(TestDamageReceiver))]
public sealed class EnemyDamageFlash :
    NetworkBehaviour
{
    // =====================================================================
    #region References

    [Header("核心引用")]

    [SerializeField]
    [Tooltip(
        "提供 Networked CurrentHealth 的既有生命系統。\n" +
        "若留空會自動取得同物件的 TestDamageReceiver。")]
    private TestDamageReceiver health;

    [SerializeField]
    [Tooltip(
        "受到傷害時需要閃白的 Renderer。\n\n" +
        "可以放入 MeshRenderer 或 SkinnedMeshRenderer。\n" +
        "陣列為空時會自動取得敵人所有子物件 Renderer，包含目前未啟用的物件。\n\n" +
        "建議正式 Prefab 手動指定，避免把陰影、警示線、武器特效或其他不該閃白的 Renderer 一起加入。")]
    private Renderer[] targetRenderers;

    #endregion

    // =====================================================================
    #region Flash Settings

    [Header("閃白設定")]

    [SerializeField]
    [ColorUsage(true, true)]
    [Tooltip(
        "受傷瞬間混合到的閃光顏色。\n\n" +
        "預設為白色。若材質與後處理支援 HDR，也可以將強度提高。")]
    private Color flashColor =
        Color.white;

    [SerializeField]
    [Min(0.01f)]
    [Tooltip(
        "一次受傷閃白持續幾秒。\n\n" +
        "建議先使用 0.08～0.12 秒。\n" +
        "時間太長會讓自動武器命中時看起來像材質永久變白。")]
    private float flashDuration =
        0.1f;

    [SerializeField]
    [Tooltip(
        "閃白強度隨時間的曲線。\n\n" +
        "X = 0～1 的播放進度。\n" +
        "Y = 原始顏色到 Flash Color 的混合比例。\n\n" +
        "預設從 1 快速回到 0。")]
    private AnimationCurve flashStrengthCurve =
        AnimationCurve.EaseInOut(
            0f,
            1f,
            1f,
            0f
        );

    [SerializeField]
    [Min(0f)]
    [Tooltip(
        "判斷生命值是否真的下降的最小差值。\n\n" +
        "用來避免浮點同步誤差造成誤觸發。\n" +
        "建議維持 0.001。")]
    private float healthDecreaseEpsilon =
        0.001f;

    #endregion

    // =====================================================================
    #region Shader Properties

    [Header("Shader 顏色欄位")]

    [SerializeField]
    [Tooltip(
        "優先嘗試修改的 Shader 顏色 Property。\n\n" +
        "URP Lit 通常使用 _BaseColor。")]
    private string primaryColorProperty =
        "_BaseColor";

    [SerializeField]
    [Tooltip(
        "材質沒有 Primary Property 時使用的備援 Property。\n\n" +
        "Built-in Standard 與部分自訂 Shader 通常使用 _Color。")]
    private string fallbackColorProperty =
        "_Color";

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯")]

    [SerializeField]
    [Tooltip(
        "開啟後輸出生命值下降與可用材質 Slot 數量。\n" +
        "大量敵人測試時建議關閉。")]
    private bool debugDamageFlash;

    #endregion

    // =====================================================================
    #region Runtime

    /// <summary>
    /// 紀錄單一 Renderer 中，特定 Material 槽位的初始狀態與對應的顏色屬性 ID。
    /// 詳細註解：這能讓我們在閃白結束時，精準地把顏色恢復回原本的狀態。
    /// </summary>
    private sealed class MaterialSlotState
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public int ColorPropertyId;
        public Color OriginalColor;
    }

    /// <summary>
    /// 緩存所有支援閃白的材質槽位狀態，預設容量為 8 避免初期擴容產生 Garbage。
    /// </summary>
    private readonly List<MaterialSlotState>
        materialSlots =
            new List<MaterialSlotState>(8);

    /// <summary>
    /// 【修正重點】
    /// 緩存的材質屬性區塊，用來高效更新 Renderer 的屬性而不產生額外 Material 實例。
    /// 詳細註解：移除原本宣告時的 new 操作，改為在 Awake 生命週期中實例化，
    /// 確保遵守 Unity 必須在主執行緒及正確生命週期建立原生 API 的規範。
    /// </summary>
    private MaterialPropertyBlock propertyBlock;

    /// <summary>
    /// 標記 Fusion 的 Spawned 是否已經執行完成。
    /// </summary>
    private bool fusionSpawned;
    
    /// <summary>
    /// 標記是否已經觀測過初始的生命值（用來建立 Snapshot）。
    /// </summary>
    private bool hasObservedHealth;
    
    /// <summary>
    /// 紀錄上一次觀測到的生命值，用來比對是否有扣血。
    /// </summary>
    private float lastObservedHealth;
    
    /// <summary>
    /// 當前閃白效果已經播放的時間（秒）。
    /// </summary>
    private float flashElapsed;
    
    /// <summary>
    /// 標記當前是否正在播放閃白效果。
    /// </summary>
    private bool flashPlaying;

    #endregion

    // =====================================================================
    #region Unity Lifecycle

    private void Awake()
    {
        // 詳細註解：在這裡實例化 MaterialPropertyBlock，徹底解決 CreateImpl is not allowed 的 UnityException。
        propertyBlock = 
            new MaterialPropertyBlock();

        // 綁定必要的元件與渲染器
        ResolveReferences();
        ResolveRenderers();
        CaptureMaterialSlots();
    }

    private void OnDisable()
    {
        // 詳細註解：當物件被停用時，確保顏色恢復原狀，並且停止閃白狀態，避免再次啟用時顯示異常。
        RestoreOriginalColors();

        flashPlaying =
            false;
    }

    private void OnDestroy()
    {
        // 詳細註解：當物件被銷毀前，恢復原本顏色。
        RestoreOriginalColors();
    }

    private void OnValidate()
    {
        // 詳細註解：確保在 Editor 調整數值時，時間與容差不會變成負數或 0，造成邏輯錯誤或無限除以 0。
        flashDuration =
            Mathf.Max(
                0.01f,
                flashDuration
            );

        healthDecreaseEpsilon =
            Mathf.Max(
                0f,
                healthDecreaseEpsilon
            );

        ResolveReferences();
    }

    #endregion

    // =====================================================================
    #region Fusion Lifecycle

    public override void Spawned()
    {
        fusionSpawned =
            true;

        ResolveReferences();
        ResolveRenderers();
        CaptureMaterialSlots();

        /*
         * 不在這顆 NetworkBehaviour.Spawned() 直接讀取
         * TestDamageReceiver.CurrentHealth。
         *
         * 同一個 NetworkObject 上不同 NetworkBehaviour 的 Spawned 呼叫順序
         * 不應被視為保證；如果 Health 的 Spawned 尚未完成，
         * 提前讀取 [Networked] Property 可能拋出 InvalidOperationException。
         *
         * 等第一次 Render 時，整個 NetworkObject 的 Spawned 流程已完成，
         * 再建立初始 Health Snapshot。
         */
        hasObservedHealth =
            false;

        lastObservedHealth =
            0f;

        flashPlaying =
            false;

        RestoreOriginalColors();
    }

    public override void Render()
    {
        // 詳細註解：確保 Fusion 已經 Spawned 且有抓到生命值系統才執行視覺更新。
        if (fusionSpawned == false ||
            health == null)
        {
            return;
        }

        // 觀測生命值變化以決定是否觸發受傷閃白
        ObserveHealth();
        // 負責推進閃白的計時器並更新材質表現
        UpdateFlash();
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        // 詳細註解：當物件被 Fusion 回收 (Despawn) 時，重置所有的狀態與顏色，讓 Object Pooling 重用時保持乾淨。
        RestoreOriginalColors();

        flashPlaying =
            false;

        fusionSpawned =
            false;

        hasObservedHealth =
            false;
    }

    #endregion

    // =====================================================================
    #region Health Observation

    /// <summary>
    /// 觀察並比對本幀與上一幀的生命值，如果確認有受傷則觸發閃白。
    /// </summary>
    private void ObserveHealth()
    {
        float currentHealth =
            health.CurrentHealth;

        // 詳細註解：如果是第一次觀測，只記錄數值不觸發閃白。
        if (hasObservedHealth == false)
        {
            hasObservedHealth =
                true;

            lastObservedHealth =
                currentHealth;

            return;
        }

        // 詳細註解：考慮浮點數誤差，當前生命值必須小於上次觀測值減去 epsilon 才算有效扣血。
        if (currentHealth <
            lastObservedHealth -
            healthDecreaseEpsilon)
        {
            StartFlash();

            if (debugDamageFlash)
            {
                Debug.Log(
                    $"[{nameof(EnemyDamageFlash)}] Damage Flash。" +
                    $"\nEnemy：{name}" +
                    $"\nHealth：{lastObservedHealth:F2} → {currentHealth:F2}",
                    this
                );
            }
        }

        // 更新觀測紀錄
        lastObservedHealth =
            currentHealth;
    }

    #endregion

    // =====================================================================
    #region Flash Playback

    /// <summary>
    /// 重置計時器並開啟閃白狀態標記，同時馬上套用第 0 秒的閃白強度。
    /// </summary>
    private void StartFlash()
    {
        flashElapsed =
            0f;

        flashPlaying =
            true;

        ApplyFlashStrength(
            EvaluateFlashStrength(0f)
        );
    }

    /// <summary>
    /// 在 Render 中每幀呼叫，推進閃白時間並套用 Curve 強度，結束時恢復顏色。
    /// </summary>
    private void UpdateFlash()
    {
        if (flashPlaying == false)
        {
            return;
        }

        // 累加速度（不受 Time.timeScale 影響的話可以使用 Time.unscaledDeltaTime，這裡維持使用 deltaTime）
        flashElapsed +=
            Time.deltaTime;

        // 計算播放進度比例 0~1
        float progress =
            Mathf.Clamp01(
                flashElapsed /
                Mathf.Max(
                    0.01f,
                    flashDuration
                )
            );

        // 套用動畫曲線計算出的當下強度
        ApplyFlashStrength(
            EvaluateFlashStrength(
                progress
            )
        );

        // 詳細註解：播放結束時，確實恢復所有材質顏色，並關閉播放狀態
        if (progress >= 1f)
        {
            RestoreOriginalColors();

            flashPlaying =
                false;
        }
    }

    /// <summary>
    /// 根據當前進度與 Animation Curve 取得閃白的混合強度。
    /// </summary>
    private float EvaluateFlashStrength(
        float progress
    )
    {
        // 詳細註解：防呆機制，若沒有設定曲線則使用線性遞減。
        if (flashStrengthCurve == null ||
            flashStrengthCurve.length == 0)
        {
            return 1f - progress;
        }

        // 確保回傳值落在 0 到 1 之間
        return Mathf.Clamp01(
            flashStrengthCurve.Evaluate(
                progress
            )
        );
    }

    /// <summary>
    /// 將計算出的閃白強度 (0~1) 套用到所有快取的 MaterialPropertyBlock 上。
    /// </summary>
    private void ApplyFlashStrength(
        float strength
    )
    {
        for (int index = 0;
             index < materialSlots.Count;
             index++)
        {
            MaterialSlotState slot =
                materialSlots[index];

            if (slot == null ||
                slot.Renderer == null)
            {
                continue;
            }

            // 詳細註解：操作前先 Clear 以免殘留舊資料，接著取得當前屬性
            propertyBlock.Clear();

            slot.Renderer.GetPropertyBlock(
                propertyBlock,
                slot.MaterialIndex
            );

            // 詳細註解：使用 Color.Lerp 將原本顏色混合到受傷閃白顏色
            propertyBlock.SetColor(
                slot.ColorPropertyId,
                Color.Lerp(
                    slot.OriginalColor,
                    flashColor,
                    strength
                )
            );

            // 套用回去指定的 Renderer Slot
            slot.Renderer.SetPropertyBlock(
                propertyBlock,
                slot.MaterialIndex
            );
        }
    }

    /// <summary>
    /// 清除所有 MaterialPropertyBlock 造成的顏色偏移，恢復預設原色。
    /// </summary>
    private void RestoreOriginalColors()
    {
        for (int index = 0;
             index < materialSlots.Count;
             index++)
        {
            MaterialSlotState slot =
                materialSlots[index];

            if (slot == null ||
                slot.Renderer == null)
            {
                continue;
            }

            propertyBlock.Clear();

            slot.Renderer.GetPropertyBlock(
                propertyBlock,
                slot.MaterialIndex
            );

            // 直接塞回當初紀錄的原始顏色
            propertyBlock.SetColor(
                slot.ColorPropertyId,
                slot.OriginalColor
            );

            slot.Renderer.SetPropertyBlock(
                propertyBlock,
                slot.MaterialIndex
            );
        }
    }

    #endregion

    // =====================================================================
    #region Material Cache

    /// <summary>
    /// 嘗試自動取得同物件上的生命值系統參考。
    /// </summary>
    private void ResolveReferences()
    {
        if (health == null)
        {
            health =
                GetComponent<TestDamageReceiver>();
        }
    }

    /// <summary>
    /// 若沒有手動設定目標 Renderer，則自動尋找底下所有層級的 Renderer。
    /// </summary>
    private void ResolveRenderers()
    {
        if (targetRenderers != null &&
            targetRenderers.Length > 0)
        {
            return;
        }

        targetRenderers =
            GetComponentsInChildren<Renderer>(
                true
            );
    }

    /// <summary>
    /// 分析所有目標 Renderer，把每個支援顏色 Property 的 Material 槽位快取起來，
    /// 避免在每次受傷 (Render 迴圈中) 都去做耗時的 Shader ID 查詢與 Array 分配。
    /// </summary>
    private void CaptureMaterialSlots()
    {
        materialSlots.Clear();

        if (targetRenderers == null)
        {
            return;
        }

        // 事先轉換 Shader Property Name 到 ID，提升效能
        int primaryPropertyId =
            string.IsNullOrWhiteSpace(
                primaryColorProperty
            )
                ? -1
                : Shader.PropertyToID(
                    primaryColorProperty
                );

        int fallbackPropertyId =
            string.IsNullOrWhiteSpace(
                fallbackColorProperty
            )
                ? -1
                : Shader.PropertyToID(
                    fallbackColorProperty
                );

        for (int rendererIndex = 0;
             rendererIndex < targetRenderers.Length;
             rendererIndex++)
        {
            Renderer targetRenderer =
                targetRenderers[rendererIndex];

            if (targetRenderer == null)
            {
                continue;
            }

            // 取得共用材質陣列 (sharedMaterials 不會產生 Instance)
            Material[] materials =
                targetRenderer.sharedMaterials;

            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material =
                    materials[materialIndex];

                if (material == null)
                {
                    continue;
                }

                // 檢查這個材質是否有我們想修改的顏色欄位
                int propertyId =
                    ResolveColorProperty(
                        material,
                        primaryPropertyId,
                        fallbackPropertyId
                    );

                if (propertyId < 0)
                {
                    continue;
                }

                propertyBlock.Clear();

                targetRenderer.GetPropertyBlock(
                    propertyBlock,
                    materialIndex
                );

                // 詳細註解：優先從現有的 PropertyBlock 拿顏色，拿不到再退而求其次從 Material 本身拿
                Color originalColor =
                    propertyBlock.HasColor(
                        propertyId
                    )
                        ? propertyBlock.GetColor(
                            propertyId
                        )
                        : material.GetColor(
                            propertyId
                        );

                // 把這個合法的槽位狀態儲存起來
                materialSlots.Add(
                    new MaterialSlotState
                    {
                        Renderer =
                            targetRenderer,

                        MaterialIndex =
                            materialIndex,

                        ColorPropertyId =
                            propertyId,

                        OriginalColor =
                            originalColor
                    }
                );
            }
        }

        if (debugDamageFlash)
        {
            Debug.Log(
                $"[{nameof(EnemyDamageFlash)}] 材質快取完成。" +
                $"\nEnemy：{name}" +
                $"\nRenderer Count：{targetRenderers.Length}" +
                $"\nSupported Material Slots：{materialSlots.Count}",
                this
            );
        }
    }

    /// <summary>
    /// 依序測試並回傳材質實際擁有的顏色 Property ID。
    /// 若兩者皆無則回傳 -1 代表此材質不支援閃白。
    /// </summary>
    private int ResolveColorProperty(
        Material material,
        int primaryPropertyId,
        int fallbackPropertyId
    )
    {
        if (primaryPropertyId >= 0 &&
            material.HasProperty(
                primaryPropertyId
            ))
        {
            return primaryPropertyId;
        }

        if (fallbackPropertyId >= 0 &&
            material.HasProperty(
                fallbackPropertyId
            ))
        {
            return fallbackPropertyId;
        }

        return -1;
    }

    #endregion
}