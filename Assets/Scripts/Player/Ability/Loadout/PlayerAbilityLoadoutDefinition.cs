using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// 一組預先配置的玩家能力 Loadout。
///
/// 它不限制一定要有幾個專注或命中槽，而是依 Slot Layout 驗證目前
/// 裝備清單是否超過各分類容量、重複限制或能力互斥規則。
/// </summary>
[CreateAssetMenu(
    fileName = "PlayerAbilityLoadout",
    menuName = "Game/Player Ability/Loadout"
)]
public class PlayerAbilityLoadoutDefinition :
    ScriptableObject
{
    [SerializeField]
    [Tooltip("本 Loadout 使用的槽位配置。可以設定專注與命中能力各自開放 0～N 格。")]
    private PlayerAbilitySlotLayoutDefinition slotLayout;

    [SerializeField]
    [Tooltip("目前裝備的能力清單。陣列順序同時作為相同 Execution Priority 時的穩定槽位順序。")]
    private PlayerAbilityDefinition[] equippedAbilities =
        Array.Empty<PlayerAbilityDefinition>();


    public PlayerAbilitySlotLayoutDefinition SlotLayout =>
        slotLayout;


    public IReadOnlyList<PlayerAbilityDefinition>
        EquippedAbilities =>
            equippedAbilities ??
            Array.Empty<PlayerAbilityDefinition>();


    /// <summary>
    /// 驗證容量、重複裝備、Prefab 與互斥規則。
    /// </summary>
    public bool TryValidate(
        int technicalMaximumSlots,
        out string failureReason
    )
    {
        failureReason =
            string.Empty;

        if (slotLayout == null)
        {
            failureReason =
                "Slot Layout 尚未指定。";

            return false;
        }

        IReadOnlyList<PlayerAbilityDefinition> abilities =
            EquippedAbilities;

        if (abilities.Count >
            Mathf.Max(
                0,
                technicalMaximumSlots
            ))
        {
            failureReason =
                $"裝備數 {abilities.Count} 超過技術上限 " +
                $"{technicalMaximumSlots}。";

            return false;
        }

        Dictionary<PlayerAbilityCategory, int>
            usedByCategory =
                new Dictionary<PlayerAbilityCategory, int>();

        Dictionary<PlayerAbilityDefinition, int>
            duplicateCounts =
                new Dictionary<PlayerAbilityDefinition, int>();

        Dictionary<string, PlayerAbilityDefinition>
            definitionsById =
                new Dictionary<string, PlayerAbilityDefinition>(
                    StringComparer.OrdinalIgnoreCase
                );

        for (int i = 0;
            i < abilities.Count;
            i++)
        {
            PlayerAbilityDefinition ability =
                abilities[i];

            if (ability == null)
            {
                failureReason =
                    $"槽位 {i} 沒有 Ability Definition。";

                return false;
            }

            if (ability.Category ==
                PlayerAbilityCategory.None)
            {
                failureReason =
                    $"能力 {ability.name} 尚未設定 Category。";

                return false;
            }

            if (ability.RuntimePrefab == null)
            {
                failureReason =
                    $"能力 {ability.AbilityId} 尚未設定 Runtime Prefab。";

                return false;
            }

            if (definitionsById.TryGetValue(
                    ability.AbilityId,
                    out PlayerAbilityDefinition sameIdDefinition
                ) &&
                sameIdDefinition != ability)
            {
                failureReason =
                    $"能力 ID {ability.AbilityId} 被不同 Definition 重複使用。";

                return false;
            }

            definitionsById[ability.AbilityId] =
                ability;

            usedByCategory.TryGetValue(
                ability.Category,
                out int usedCount
            );

            usedCount++;

            int categoryCapacity =
                slotLayout.GetCapacity(
                    ability.Category
                );

            if (usedCount > categoryCapacity)
            {
                failureReason =
                    $"{ability.Category} 已使用 {usedCount} 格，" +
                    $"超過配置容量 {categoryCapacity}。";

                return false;
            }

            usedByCategory[ability.Category] =
                usedCount;

            duplicateCounts.TryGetValue(
                ability,
                out int duplicateCount
            );

            duplicateCount++;

            if (duplicateCount > 1 &&
                ability.AllowDuplicateEquip == false)
            {
                failureReason =
                    $"能力 {ability.AbilityId} 不允許重複裝備。";

                return false;
            }

            duplicateCounts[ability] =
                duplicateCount;

            for (int j = 0;
                j < i;
                j++)
            {
                PlayerAbilityDefinition previous =
                    abilities[j];

                if (previous != null &&
                    ability.ConflictsWith(
                        previous
                    ))
                {
                    failureReason =
                        $"能力 {ability.AbilityId} 與 " +
                        $"{previous.AbilityId} 不能共用。";

                    return false;
                }
            }
        }

        return true;
    }
}
