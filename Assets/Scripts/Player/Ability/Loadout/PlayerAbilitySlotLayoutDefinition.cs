using System;
using UnityEngine;


/// <summary>
/// 某一能力分類目前開放多少裝備空間。
/// </summary>
[Serializable]
public struct PlayerAbilityCategoryCapacity
{
    [SerializeField]
    [Tooltip("要設定容量的能力分類。")]
    private PlayerAbilityCategory category;

    [SerializeField]
    [Min(0)]
    [Tooltip("這個分類實際開放的槽位數。0 代表本模式不開放這類能力。")]
    private int capacity;


    public PlayerAbilityCategory Category =>
        category;


    public int Capacity =>
        Mathf.Max(
            0,
            capacity
        );
}


/// <summary>
/// 玩家能力槽位配置。
///
/// 不固定「一個專注＋一個命中」。每個遊戲模式或測試設定都可以自行
/// 決定 GrappleFocus、GrappleHit 各自開放 0～N 格。
/// </summary>
[CreateAssetMenu(
    fileName = "PlayerAbilitySlotLayout",
    menuName = "Game/Player Ability/Slot Layout"
)]
public class PlayerAbilitySlotLayoutDefinition :
    ScriptableObject
{
    [SerializeField]
    [Tooltip("各能力分類實際開放的容量。相同分類若重複出現會加總，方便之後由不同規則提供額外槽位。")]
    private PlayerAbilityCategoryCapacity[] categoryCapacities =
        Array.Empty<PlayerAbilityCategoryCapacity>();


    public int GetCapacity(
        PlayerAbilityCategory category
    )
    {
        if (category ==
                PlayerAbilityCategory.None ||
            categoryCapacities == null)
        {
            return 0;
        }

        int total =
            0;

        for (int i = 0;
            i < categoryCapacities.Length;
            i++)
        {
            PlayerAbilityCategoryCapacity entry =
                categoryCapacities[i];

            if (entry.Category != category)
            {
                continue;
            }

            total +=
                entry.Capacity;
        }

        return Mathf.Max(
            0,
            total
        );
    }


    public int GetTotalCapacity()
    {
        if (categoryCapacities == null)
        {
            return 0;
        }

        int total =
            0;

        for (int i = 0;
            i < categoryCapacities.Length;
            i++)
        {
            total +=
                categoryCapacities[i]
                    .Capacity;
        }

        return Mathf.Max(
            0,
            total
        );
    }
}
