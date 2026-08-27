using UnityEngine;
using System.Collections;
using TMPro; // 必須引用 TextMeshPro 命名空間

[RequireComponent(typeof(TextMeshProUGUI))] // 確保物件上有 TMP 元件，防止報錯
public class TextFlasher : MonoBehaviour
{
    [Header("顏色設定")]
    [Tooltip("文字原本的顏色 (預設為 Inspector 中設定的顏色)")]
    [SerializeField] private Color originalColor = Color.white;
    
    [Tooltip("要閃爍到的目標顏色")]
    [SerializeField] private Color flashColor = Color.red;

    [Header("頻率設定")]
    [Tooltip("每秒閃爍的次數 (次/秒)")]
    [Range(0.1f, 10f)] // 限制範圍，避免數值過激
    [SerializeField] private float flashFrequency = 2.0f;

    // 用於快取元件參考
    private TextMeshProUGUI textComponent;
    // 用於控制 Coroutine 的停止
    private Coroutine flashCoroutine;

    private void Awake()
    {
        // 獲取 TMP 元件
        textComponent = GetComponent<TextMeshProUGUI>();
        
        // 如果你希望以 Inspector 目前設定的顏色作為原色，
        // 可以取消註解下面這行，這樣就不用手動在腳本元件裡設原色
        // originalColor = textComponent.color;
    }

    private void OnEnable()
    {
        // 當物件啟用時，確保文字回到原色並開始閃爍
        textComponent.color = originalColor;
        
        // 啟動 Coroutine (協程) 來處理閃爍邏輯，這比在 Update 裡寫更有效率
        flashCoroutine = StartCoroutine(DoFlash());
    }

    private void OnDisable()
    {
        // 當物件隱藏時，停止 Coroutine，避免記憶體洩漏
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        
        // 選做：隱藏時將文字還原回原色，確保下次顯示時狀態正確
        if (textComponent != null)
        {
            textComponent.color = originalColor;
        }
    }

    // 閃爍邏輯的協程
    private IEnumerator DoFlash()
    {
        // 無限迴圈，直到 OnDisable 中被停止
        while (true)
        {
            // 計算半個週期的時間 (從 A 變到 B 需要的時間)
            float halfCycleTime = 1f / (flashFrequency * 2f);

            // === 階段 1: 從原色 變到 閃爍色 ===
            yield return StartCoroutine(LerpColor(originalColor, flashColor, halfCycleTime));

            // === 階段 2: 從閃爍色 變回 原色 ===
            yield return StartCoroutine(LerpColor(flashColor, originalColor, halfCycleTime));
        }
    }

    // 平滑插值顏色的協程
    private IEnumerator LerpColor(Color startColor, Color endColor, float duration)
    {
        float timeElapsed = 0f;

        while (timeElapsed < duration)
        {
            // 使用不可中斷的時間 (unscaledDeltaTime)，
            // 這樣即使遊戲暫停 (Time.timeScale = 0)，UI 可能依舊可以閃爍(視需求而定)。
            // 如果希望遊戲暫停時閃爍也暫停，請改用 Time.deltaTime
            timeElapsed += Time.unscaledDeltaTime;
            
            // 計算進度 (0 ~ 1)
            float normalizedTime = timeElapsed / duration;

            // 平滑套用顏色
            textComponent.color = Color.Lerp(startColor, endColor, normalizedTime);
            
            // 等待下一影格
            yield return null;
        }

        // 確保最終顏色精準到達目標
        textComponent.color = endColor;
    }

    // (選做) 用於在 Inspector 外部修改頻率的方法
    public void SetFrequency(float newFrequency)
    {
        flashFrequency = Mathf.Max(0.1f, newFrequency);
    }
}