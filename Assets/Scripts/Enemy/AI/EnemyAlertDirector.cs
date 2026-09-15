using System.Collections.Generic;
using Fusion;
using UnityEngine;


/// <summary>
/// 場景中的敵人警戒協調器。
///
/// 每個戰鬥場景只放一個，不必掛 NetworkObject。
/// 真正呼叫它的只有具備 State Authority 的 EnemyAwarenessBrain。
///
/// 核心目的：
/// 同一警戒群組在冷卻期間只能有一隻敵人播放「發現／呼喚」演出，
/// 其他同伴直接接收情報，不跟著一起僵直、叫聲也不會疊成噪音。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAlertDirector : MonoBehaviour
{
    private readonly List<EnemyAwarenessBrain>
        registeredEnemies =
            new List<EnemyAwarenessBrain>(64);

    private readonly Dictionary<string, GroupCooldown>
        cooldownByRunnerAndGroup =
            new Dictionary<string, GroupCooldown>(32);

    private struct GroupCooldown
    {
        public NetworkRunner Runner;
        public TickTimer Timer;
    }

    /// <summary>
    /// Enemy Spawned 後註冊。
    /// 重複註冊會被忽略。
    /// </summary>
    public void Register(
        EnemyAwarenessBrain awareness
    )
    {
        if (awareness == null ||
            registeredEnemies.Contains(awareness))
        {
            return;
        }

        registeredEnemies.Add(awareness);
    }

    public void Unregister(
        EnemyAwarenessBrain awareness
    )
    {
        if (awareness == null)
        {
            return;
        }

        registeredEnemies.Remove(awareness);
    }

    /// <summary>
    /// 嘗試成為這次群組警戒的唯一呼喚者。
    ///
    /// 回傳 true：來源敵人可以播放發現／呼喚動畫與聲音。
    /// 回傳 false：群組仍在冷卻，來源敵人直接進入追擊準備，不播放呼喚演出。
    /// </summary>
    public bool TryBroadcastAlert(
        EnemyAwarenessBrain source,
        NetworkObject target,
        Vector3 lastKnownPosition,
        string alertGroupId,
        float alertRadius,
        float groupAnnouncementCooldown
    )
    {
        if (source == null || !source.IsEligibleForSharedAlert ||
            source.Runner == null ||
            target == null ||
            target.IsValid == false)
        {
            return false;
        }

        CleanupMissingEnemies();

        string normalizedGroupId =
            NormalizeGroupId(alertGroupId);

        string cooldownKey =
            BuildCooldownKey(
                source.Runner,
                normalizedGroupId
            );

        bool announcementCoolingDown = cooldownByRunnerAndGroup.TryGetValue(
                cooldownKey,
                out GroupCooldown cooldown
            ) &&
            cooldown.Runner == source.Runner &&
            cooldown.Timer.ExpiredOrNotRunning(
                source.Runner
            ) == false;

        if (!announcementCoolingDown)
        {
        cooldownByRunnerAndGroup[cooldownKey] =
            new GroupCooldown
            {
                Runner = source.Runner,
                Timer = TickTimer.CreateFromSeconds(
                    source.Runner,
                    Mathf.Max(0.01f, groupAnnouncementCooldown)
                )
            };
        }

        float squaredRadius =
            Mathf.Max(0f, alertRadius) *
            Mathf.Max(0f, alertRadius);

        for (int index = registeredEnemies.Count - 1;
             index >= 0;
             index--)
        {
            EnemyAwarenessBrain receiver =
                registeredEnemies[index];

            if (receiver == null ||
                receiver == source ||
                receiver.Runner != source.Runner ||
                receiver.IsEligibleForSharedAlert == false ||
                NormalizeGroupId(receiver.AlertGroupId) !=
                    normalizedGroupId)
            {
                continue;
            }

            float squaredDistance =
                (receiver.transform.position -
                 source.transform.position).sqrMagnitude;

            if (squaredDistance > squaredRadius)
            {
                continue;
            }

            receiver.ReceiveSharedAlert(
                target,
                lastKnownPosition,
                source
            );
        }

        return !announcementCoolingDown;
    }

    private void CleanupMissingEnemies()
    {
        for (int index = registeredEnemies.Count - 1;
             index >= 0;
             index--)
        {
            if (registeredEnemies[index] == null)
            {
                registeredEnemies.RemoveAt(index);
            }
        }
    }

    private static string NormalizeGroupId(
        string groupId
    )
    {
        return string.IsNullOrWhiteSpace(groupId)
            ? "default"
            : groupId.Trim();
    }

    private static string BuildCooldownKey(
        NetworkRunner runner,
        string normalizedGroupId
    )
    {
        return runner.GetInstanceID() +
               ":" +
               normalizedGroupId;
    }
}
