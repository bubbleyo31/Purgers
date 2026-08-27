using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Player Core 的「受到傷害 → Profession Runtime」橋接器。
///
/// ====================================================================
///
/// DamageReceiverUtility
/// 只能從被命中的物件父階層尋找：
///
/// IDamageRequestModifier。
///
/// 但是 TankGuardAbility 現在存在：
///
/// TankProfessionRuntime
///
/// 不在 Player 階層中。
///
/// ====================================================================
///
/// 所以正式流程是：
///
/// Player 被打
/// ↓
/// DamageReceiverUtility
/// ↓
/// PlayerIncomingDamageModifierBridge
/// ↓
/// Current Profession Runtime
/// ↓
/// IPlayerIncomingDamageModifier
/// ↓
/// TankGuardAbility。
///
/// ====================================================================
///
/// Player Core 完全不知道：
///
/// TankGuardAbility。
///
/// 它只知道共用 Interface。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
[RequireComponent(typeof(PlayerProfessionRuntimeManager))]
public class PlayerIncomingDamageModifierBridge :
    MonoBehaviour,
    IDamageRequestModifier
{
    // =====================================================================
    #region Player Core 引用

    [Header("Player Core 引用")]

    [SerializeField]
    [Tooltip("玩家目前正式職業。Bridge 會確認目前 Runtime 與正式職業一致，避免職業切換的短暫 Tick 使用到舊 Runtime。若留空會自動取得。")]
    private PlayerProfession profession;

    [SerializeField]
    [Tooltip("玩家 Profession Runtime 管理器。受到傷害時會從目前 Runtime 尋找所有 IPlayerIncomingDamageModifier。若留空會自動取得。")]
    private PlayerProfessionRuntimeManager
        runtimeManager;

    #endregion

    // =====================================================================
    #region Debug

    [Header("除錯設定")]

    [SerializeField]
    [Tooltip("開啟後，在 Runtime 改變並重新建立 Incoming Damage Modifier Cache 時顯示目前找到多少個傷害修改器。")]
    private bool debugModifierBridge =
        false;

    #endregion

    // =====================================================================
    #region Runtime Cache

    /// <summary>
    /// 上一次建立 Cache 時的 Profession Runtime。
    /// </summary>
    private NetworkObject
        cachedRuntimeObject;

    /// <summary>
    /// 是否至少建立過一次 Cache。
    /// </summary>
    private bool cacheInitialized;

    /// <summary>
    /// 目前 Runtime 裡所有
    /// IPlayerIncomingDamageModifier。
    ///
    /// 使用 MonoBehaviour 保存是為了
    /// 正確處理 Unity Destroy Null。
    /// </summary>
    private readonly List<MonoBehaviour>
        cachedModifierBehaviours =
            new List<MonoBehaviour>(4);

    #endregion

    // =====================================================================
    #region Unity

    private void Awake()
    {
        if (profession == null)
        {
            profession =
                GetComponent<PlayerProfession>();
        }

        if (runtimeManager == null)
        {
            runtimeManager =
                GetComponent<
                    PlayerProfessionRuntimeManager
                >();
        }
    }

    #endregion

    // =====================================================================
    #region IDamageRequestModifier

    /// <summary>
    /// DamageReceiverUtility
    /// 在傷害真正送進 Player Receiver 前呼叫。
    /// </summary>
    public void ModifyDamageRequest(
        ref DamageRequest request
    )
    {
        if (profession == null ||
            runtimeManager == null)
        {
            return;
        }

        // =============================================================
        // Runtime 必須跟正式職業一致
        // =============================================================

        if (runtimeManager.CurrentRuntimeProfession !=
            profession.CurrentProfession)
        {
            return;
        }

        // =============================================================
        // Cache
        // =============================================================

        RefreshModifierCache();

        // =============================================================
        // 依 Priority 執行
        // =============================================================

        for (int i = 0;
             i < cachedModifierBehaviours.Count;
             i++)
        {
            MonoBehaviour behaviour =
                cachedModifierBehaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IPlayerIncomingDamageModifier modifier)
            {
                modifier.ModifyIncomingDamage(
                    ref request
                );

                /*
                 * 保證傷害不會因多個 Modifier
                 * 被扣成負數。
                 */
                request.RequestedDamage =
                    Mathf.Max(
                        0f,
                        request.RequestedDamage
                    );
            }
        }
    }

    #endregion

    // =====================================================================
    #region Cache

    private void RefreshModifierCache()
    {
        NetworkObject currentRuntime =
            runtimeManager != null
                ? runtimeManager.CurrentRuntimeObject
                : null;

        // =============================================================
        // Runtime 沒有改變
        // =============================================================

        if (cacheInitialized &&
            cachedRuntimeObject ==
                currentRuntime)
        {
            /*
             * 如果 Runtime 還有效，
             * Cache 就可以繼續使用。
             */
            if (currentRuntime == null ||
                currentRuntime.IsValid)
            {
                return;
            }
        }

        // =============================================================
        // Reset
        // =============================================================

        cachedRuntimeObject =
            currentRuntime;

        cacheInitialized =
            true;

        cachedModifierBehaviours.Clear();

        if (currentRuntime == null ||
            currentRuntime.IsValid == false)
        {
            return;
        }

        // =============================================================
        // 找所有 Runtime Modifier
        // =============================================================

        MonoBehaviour[] behaviours =
            currentRuntime
                .GetComponentsInChildren<
                    MonoBehaviour
                >(
                    true
                );

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour == null)
            {
                continue;
            }

            if (behaviour is
                IPlayerIncomingDamageModifier)
            {
                cachedModifierBehaviours.Add(
                    behaviour
                );
            }
        }

        // =============================================================
        // Priority Sort
        // =============================================================

        cachedModifierBehaviours.Sort(
            CompareModifierPriority
        );

        // =============================================================
        // Debug
        // =============================================================

        if (debugModifierBridge)
        {
            Debug.Log(
                $"[Player Incoming Damage Modifier Bridge]" +
                $"\nProfession：{profession.CurrentProfession}" +
                $"\nRuntime：{currentRuntime.name}" +
                $"\nModifier Count：{cachedModifierBehaviours.Count}",
                this
            );
        }
    }

    private static int CompareModifierPriority(
        MonoBehaviour a,
        MonoBehaviour b
    )
    {
        int priorityA =
            a is IPlayerIncomingDamageModifier modifierA
                ? modifierA.IncomingDamageModifierPriority
                : int.MaxValue;

        int priorityB =
            b is IPlayerIncomingDamageModifier modifierB
                ? modifierB.IncomingDamageModifierPriority
                : int.MaxValue;

        return
            priorityA.CompareTo(
                priorityB
            );
    }

    #endregion
}