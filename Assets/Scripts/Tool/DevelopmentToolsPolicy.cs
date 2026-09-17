using Fusion;
using UnityEngine;

/// <summary>
/// 開發入口的共用邊界。客戶端輸入與 State Authority 請求處理都必須檢查。
/// 不作為正式職業切換、地圖生成或敵人生成規則。
/// </summary>
public static class DevelopmentToolsPolicy
{
    public static bool IsEnabled
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }
    }

    // 本地地圖原型只可在獨立 Play Mode 執行，避免單端建立碰撞幾何。
    public static bool CanRunLocalMapPrototype
    {
        get
        {
            if (!IsEnabled || !Application.isPlaying)
                return false;

            foreach (NetworkRunner runner in NetworkRunner.Instances)
            {
                // Runner 啟動連線前也禁止本地生成，避免 Start 執行順序造成單端幾何。
                if (runner != null)
                    return false;
            }

            return true;
        }
    }
}
