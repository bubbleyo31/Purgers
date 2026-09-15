using System;
using Fusion;
using UnityEngine;


/// <summary>
/// 一個可裝備玩家能力的靜態定義。
///
/// 能力程式負責 Gameplay；本資產只負責 Loadout、職業限制、
/// Runtime Prefab、互斥規則與穩定排序資料。
/// </summary>
[CreateAssetMenu(
    fileName = "PlayerAbilityDefinition",
    menuName = "Game/Player Ability/Ability Definition"
)]
public class PlayerAbilityDefinition :
    ScriptableObject
{
    [Header("能力識別")]

    [SerializeField]
    [Tooltip("能力的穩定 ID。存檔、網路 Loadout 與除錯都應使用它，不要使用會變動的顯示名稱。建議格式為 grapple.pull 或 grapple.air_dash。")]
    private string abilityId =
        string.Empty;

    [SerializeField]
    [Tooltip("提供 UI 與除錯顯示的名稱，不參與能力身分判定。")]
    private string displayName =
        string.Empty;

    [SerializeField]
    [Tooltip("能力所屬管線。必須與 Runtime Prefab 上能力程式實作的 Ability Category 一致。")]
    private PlayerAbilityCategory category =
        PlayerAbilityCategory.None;


    [Header("Runtime Prefab")]

    [SerializeField]
    [Tooltip("這個能力的獨立 Network Runtime Prefab。Root 必須有 NetworkObject、PlayerAbilityRuntime，以及一個分類相符的能力模組。")]
    private NetworkObject runtimePrefab;

    [SerializeField]
    [Tooltip("此能力裝備且目前職業可用時，是否要求 PlayerGrapple 合併額外 Gameplay Target LayerMask。需要鈎到其他玩家等非一般世界層的能力才開啟。")]
    private bool includeAdditionalGrappleTargetMask;


    [Header("職業限制")]

    [SerializeField]
    [Tooltip("能力自己的職業限制。能力可以保持已裝備；切到不允許的職業時只會暫停使用，不會自動卸下或重置冷卻。")]
    private PlayerAbilityProfessionRule professionRule;


    [Header("裝備與互斥")]

    [SerializeField]
    [Tooltip("是否允許同一份 Ability Definition 在同一 Loadout 重複出現。大多數主動能力應關閉。")]
    private bool allowDuplicateEquip;

    [SerializeField]
    [Tooltip("互斥群組 ID。只要兩個能力具有任一相同且非空的群組 ID，就不能同時裝備。例如 GrappleMovementAuthority 或 AimHoldExclusive。")]
    private string[] exclusiveGroupIds =
        Array.Empty<string>();

    [SerializeField]
    [Tooltip("額外指定不能與此能力同時裝備的 Ability Definition。驗證會做雙向判斷，因此只要任一方列出對方就會阻止組合。")]
    private PlayerAbilityDefinition[] incompatibleAbilities =
        Array.Empty<PlayerAbilityDefinition>();

    [SerializeField]
    [Tooltip("多個合法能力由同一事件觸發時的執行優先權。數字越小越先執行；相同時再依槽位順序與 Ability ID 排序。")]
    private int executionPriority;


    public string AbilityId =>
        string.IsNullOrWhiteSpace(abilityId)
            ? name
            : abilityId.Trim();


    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName)
            ? AbilityId
            : displayName.Trim();


    public PlayerAbilityCategory Category =>
        category;


    public NetworkObject RuntimePrefab =>
        runtimePrefab;


    public bool IncludeAdditionalGrappleTargetMask =>
        includeAdditionalGrappleTargetMask;


    public PlayerAbilityProfessionRule ProfessionRule =>
        professionRule;


    public bool AllowDuplicateEquip =>
        allowDuplicateEquip;


    public int ExecutionPriority =>
        executionPriority;


    public bool IsProfessionAllowed(
        PlayerProfessionType profession
    )
    {
        return professionRule.IsAllowed(
            profession
        );
    }


    /// <summary>
    /// 確認兩個能力是否禁止同時裝備。
    /// 指定能力與互斥群組都會雙向檢查。
    /// </summary>
    public bool ConflictsWith(
        PlayerAbilityDefinition other
    )
    {
        if (other == null ||
            other == this)
        {
            return false;
        }

        if (ContainsIncompatibleAbility(
                other
            ) ||
            other.ContainsIncompatibleAbility(
                this
            ))
        {
            return true;
        }

        return SharesExclusiveGroup(
            other
        );
    }


    private bool ContainsIncompatibleAbility(
        PlayerAbilityDefinition other
    )
    {
        if (incompatibleAbilities == null)
        {
            return false;
        }

        for (int i = 0;
            i < incompatibleAbilities.Length;
            i++)
        {
            if (incompatibleAbilities[i] ==
                other)
            {
                return true;
            }
        }

        return false;
    }


    private bool SharesExclusiveGroup(
        PlayerAbilityDefinition other
    )
    {
        if (exclusiveGroupIds == null ||
            other.exclusiveGroupIds == null)
        {
            return false;
        }

        for (int i = 0;
            i < exclusiveGroupIds.Length;
            i++)
        {
            string left =
                NormalizeGroupId(
                    exclusiveGroupIds[i]
                );

            if (string.IsNullOrEmpty(left))
            {
                continue;
            }

            for (int j = 0;
                j < other.exclusiveGroupIds.Length;
                j++)
            {
                string right =
                    NormalizeGroupId(
                        other.exclusiveGroupIds[j]
                    );

                if (string.Equals(
                        left,
                        right,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return true;
                }
            }
        }

        return false;
    }


    private static string NormalizeGroupId(
        string value
    )
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
