using UnityEngine;
using System;

/// <summary>
/// 場景攝影機跟隨器。
///
/// 場景中只能存在一個 CameraFollow。
/// 本地玩家生成後會把自己的 camTarget 指定給它。
/// </summary>
public class CameraFollow : MonoBehaviour
{

    /// <summary>
    /// CameraRig 在本幀完成位置與旋轉更新後觸發。
    ///
    /// 第一人稱中依賴 CameraRig 最新 Transform 的視覺系統，
    /// 例如勾索 LineRenderer，可以訂閱這個事件。
    /// </summary>
    public event Action AfterCameraUpdated;

    public static CameraFollow Singleton
    {
        get => singleton;

        private set
        {
            if (value == null)
            {
                singleton = null;
            }
            else if (singleton == null)
            {
                singleton = value;
            }
            else if (singleton != value)
            {
                Destroy(value);

                Debug.LogError(
                    $"場景中只能存在一個 {nameof(CameraFollow)}。"
                );
            }
        }
    }

    private static CameraFollow singleton;

    private Transform target;

    private void Awake()
    {
        Singleton = this;
    }

    private void OnDestroy()
    {
        if (Singleton == this)
        {
            Singleton = null;
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        /*
        * 先完成 CameraRig 的位置與旋轉。
        */
        transform.SetPositionAndRotation(
            target.position,
            target.rotation
        );

        /*
        * CameraRig 已經是這一幀最新的位置。
        *
        * 所有需要依賴 CameraRig 最新 Transform 的視覺，
        * 都從這裡之後再更新。
        */
        AfterCameraUpdated?.Invoke();
    }

    /// <summary>
    /// 設定攝影機跟隨目標。
    /// </summary>
    public void SetTarget(
        Transform newTarget
    )
    {
        target = newTarget;
    }

    /// <summary>
    /// 只有目前跟隨指定目標時才清除。
    /// 避免其他玩家銷毀時誤清除本地攝影機。
    /// </summary>
    public void ClearTarget(
        Transform oldTarget
    )
    {
        if (target == oldTarget)
        {
            target = null;
        }
    }
}