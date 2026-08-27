using UnityEngine;


/// <summary>
/// 第一人稱武器 ViewModel 的重要引用集合。
///
/// 這支腳本掛在「職業 ViewModel Prefab 的根物件」上。
///
/// 主要目的：
/// 不讓其他系統使用：
///
/// transform.Find("MuzzlePoint")
/// GameObject.Find(...)
///
/// 這種依賴物件名稱的搜尋方式。
///
/// ViewModel Prefab 只需要在 Inspector
/// 把真正引用拖進來即可。
/// </summary>
[DisallowMultipleComponent]
public class WeaponViewModelReferences :
    MonoBehaviour
{
    // =====================================================================
    #region Weapon References


    [Header("武器主要引用")]


    [SerializeField]
    [Tooltip("第一人稱武器真正的槍口位置。彈道 LineRenderer、槍口火光、煙霧等視覺效果都可以從這個位置產生。")]
    private Transform muzzlePoint;


    #endregion


    // =====================================================================
    #region ADS Animation References


    [Header("ADS 動畫引用")]


    [SerializeField]
    [Tooltip("這個 ViewModel 的 ADS 動畫控制器。Attack 與 Support ViewModel 都必須指定各自 Prefab Root 上的 FirstPersonViewModelAimAnimator。ProfessionViewModelManager 會把目前 Profession Runtime 的 PlayerAimController 綁進這裡。Tank 沒有 ADS 時可以留空。")]
    private FirstPersonViewModelAimAnimator
        aimAnimator;


    #endregion


    // =====================================================================
    #region Base Action Animation References


    [Header("基礎動作動畫引用")]


    [SerializeField]
    [Tooltip("這個 ViewModel 的 Appear、Idle、Shoot、Reload、Melee 動畫控制器。Attack 與 Support ViewModel 都必須指定各自 Prefab Root 上的 FirstPersonViewModelActionAnimator。ProfessionViewModelManager 會把目前 Runtime Weapon 與 PlayerQuickActionController 綁進這裡。Tank 尚未使用這套槍械動畫時可以留空。")]
    private FirstPersonViewModelActionAnimator
        actionAnimator;


    #endregion


    // =====================================================================
    #region Public Data


    /// <summary>
    /// 第一人稱武器槍口。
    /// </summary>
    public Transform MuzzlePoint =>
        muzzlePoint;


    /// <summary>
    /// 這個 ViewModel 的本地 ADS 動畫控制器。
    /// </summary>
    public FirstPersonViewModelAimAnimator
        AimAnimator =>
            aimAnimator;


    /// <summary>
    /// 這個 ViewModel 的本地基礎動作動畫控制器。
    /// </summary>
    public FirstPersonViewModelActionAnimator
        ActionAnimator =>
            actionAnimator;


    #endregion


    // =====================================================================
    #region Inspector Validation


#if UNITY_EDITOR

    private void OnValidate()
    {
        if (muzzlePoint == null)
        {
            Debug.LogWarning(
                $"[{nameof(WeaponViewModelReferences)}] " +
                $"Prefab「{gameObject.name}」尚未指定 MuzzlePoint。",
                this
            );
        }


        /*
         * WeaponViewModelReferences 目前主要由
         * Attack / Support 使用。
         *
         * 如果未來 Tank 也使用這顆 References，
         * Tank 沒有 ADS 時可以忽略這個警告，
         * 或再把 Profession 類型加入 References 做精確驗證。
         */
        if (aimAnimator == null)
        {
            Debug.LogWarning(
                $"[{nameof(WeaponViewModelReferences)}] " +
                $"Prefab「{gameObject.name}」尚未指定 " +
                $"{nameof(FirstPersonViewModelAimAnimator)}。" +
                $"\nAttack / Support ViewModel 需要這個引用才能播放 ADS 動畫。",
                this
            );
        }


        if (actionAnimator == null)
        {
            Debug.LogWarning(
                $"[{nameof(WeaponViewModelReferences)}] " +
                $"Prefab「{gameObject.name}」尚未指定 " +
                $"{nameof(FirstPersonViewModelActionAnimator)}。" +
                $"\nAttack / Support ViewModel 需要這個引用才能播放 Appear、Shoot、Reload 與 Melee 動畫。",
                this
            );
        }
    }

#endif


    #endregion
}
