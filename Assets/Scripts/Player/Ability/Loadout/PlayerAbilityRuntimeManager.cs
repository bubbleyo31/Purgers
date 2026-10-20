using System.Collections.Generic;
using Fusion;
using UnityEngine;


/// <summary>
/// 玩家能力 Loadout 的網路 Runtime 管理器。
///
/// 實際槽位數由 Slot Layout 決定；八格只是 Fusion NetworkDictionary
/// 必須在編譯期知道的技術上限，不代表 Gameplay 固定有八個槽位。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerProfession))]
public partial class PlayerAbilityRuntimeManager :
    NetworkBehaviour
{
    public const int MaximumAbilityRuntimeSlots =
        8;


    [Header("起始能力配置")]

    [SerializeField]
    [Tooltip("玩家 Spawn 後使用此 Loadout 的 Slot Layout；新輪不自動裝備其技能清單，由開局獎勵或存檔填入空槽。")]
    private PlayerAbilityLoadoutDefinition startingLoadout;


    [Header("除錯設定")]

    [SerializeField]
    private bool debugLoadout =
        true;


    [Networked]
    private NetworkBool StartingLoadoutApplied
    {
        get;
        set;
    }


    [Networked]
    public int LoadoutRevision
    {
        get;
        private set;
    }


    [Networked, Capacity(MaximumAbilityRuntimeSlots)]
    private NetworkDictionary<int, NetworkObject>
        ActiveAbilityRuntimeObjects => default;


    private readonly List<PlayerAbilityRuntime>
        orderedRuntimeCache =
            new List<PlayerAbilityRuntime>(
                MaximumAbilityRuntimeSlots
            );


    private readonly List<SpawnPlanEntry>
        spawnPlan =
            new List<SpawnPlanEntry>(
                MaximumAbilityRuntimeSlots
            );


    private readonly List<NetworkObject>
        spawnedRuntimeBuffer =
            new List<NetworkObject>(
                MaximumAbilityRuntimeSlots
            );


    private readonly List<NetworkObject>
        activeRuntimeDespawnBuffer =
            new List<NetworkObject>(
                MaximumAbilityRuntimeSlots
            );


    private struct SpawnPlanEntry
    {
        public int SlotIndex;
        public int FocusSlot;
        public PlayerAbilityDefinition Definition;
    }


    public override void FixedUpdateNetwork()
    {
        if (Object == null ||
            Object.HasStateAuthority == false ||
            StartingLoadoutApplied)
        {
            return;
        }

        StartingLoadoutApplied =
            true;

        if (startingLoadout == null)
        {
            if (debugLoadout)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerAbilityRuntimeManager)}] " +
                    $"尚未指定 Starting Loadout，玩家不會生成可選能力。",
                    this
                );
            }

            return;
        }

        if (TryApplyDefinitionsStateAuthority(
                startingLoadout.SlotLayout, System.Array.Empty<PlayerAbilityDefinition>(),
                out string failureReason
            ) == false)
        {
            Debug.LogError(
                $"[{nameof(PlayerAbilityRuntimeManager)}] " +
                $"起始 Loadout 套用失敗。" +
                $"\n原因：{failureReason}",
                this
            );
        }
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        if (hasState)
        {
            DespawnBonusRuntimes(runner);
            DespawnAllActiveRuntimes(
                runner
            );
        }

        orderedRuntimeCache.Clear();
        spawnPlan.Clear();
        spawnedRuntimeBuffer.Clear();
        activeRuntimeDespawnBuffer.Clear();
    }


    /// <summary>
    /// State Authority 套用一份已知 Loadout。
    /// 未來 UI / 存檔應先用穩定 Ability ID 解析成 Definition，再呼叫此入口。
    /// </summary>
    public bool TryApplyLoadoutStateAuthority(PlayerAbilityLoadoutDefinition loadout, out string failureReason)
    {
        if (loadout == null)
        {
            failureReason = "Loadout 是 Null。";
            return false;
        }
        return TryApplyDefinitionsStateAuthority(loadout.SlotLayout, loadout.EquippedAbilities, out failureReason);
    }

    public bool TryApplyDefinitionsStateAuthority(PlayerAbilitySlotLayoutDefinition slotLayout, IReadOnlyList<PlayerAbilityDefinition> abilities, out string failureReason)
    {
        failureReason = string.Empty;

        if (Object == null ||
            Object.HasStateAuthority == false)
        {
            failureReason =
                "只有 Player State Authority 可以套用能力 Loadout。";

            return false;
        }
        if (!PlayerAbilityLoadoutDefinition.TryValidateEntries(slotLayout, abilities, MaximumAbilityRuntimeSlots, out failureReason))
            return false;

        BuildAndSortSpawnPlan(abilities);

        for (int i = 0;
            i < spawnPlan.Count;
            i++)
        {
            PlayerAbilityDefinition definition =
                spawnPlan[i].Definition;

            PlayerAbilityRuntime prefabRuntime =
                definition.RuntimePrefab
                    .GetComponent<PlayerAbilityRuntime>();

            string runtimeFailure =
                prefabRuntime == null
                    ? $"找不到 {nameof(PlayerAbilityRuntime)}。"
                    : string.Empty;

            if (prefabRuntime == null ||
                prefabRuntime.ValidateConfiguration(
                    definition,
                    out runtimeFailure
                ) == false)
            {
                failureReason =
                    $"能力 {definition.AbilityId} 的 Runtime Prefab 無效：" +
                    $"{runtimeFailure}";

                return false;
            }
        }

        spawnedRuntimeBuffer.Clear();

        for (int i = 0;
            i < spawnPlan.Count;
            i++)
        {
            SpawnPlanEntry entry =
                spawnPlan[i];

            NetworkObject spawned =
                SpawnAbilityRuntime(
                    entry
                );

            if (spawned == null)
            {
                RollbackSpawnedRuntimes();

                failureReason =
                    $"能力 {entry.Definition.AbilityId} Runtime Spawn 失敗。";

                return false;
            }

            spawnedRuntimeBuffer.Add(
                spawned
            );
        }

        DespawnAllActiveRuntimes(
            Runner
        );

        for (int i = 0;
            i < spawnPlan.Count;
            i++)
        {
            ActiveAbilityRuntimeObjects.Add(
                spawnPlan[i].SlotIndex,
                spawnedRuntimeBuffer[i]
            );
        }

        LoadoutRevision++;

        if (debugLoadout)
        {
            Debug.Log(
                $"[Player Ability Loadout] 已套用。" +
                $"\nPlayer：{Object.InputAuthority}" +
                $"\n來源：權威能力清單" +
                $"\n能力數：{spawnPlan.Count}",
                this
            );
        }

        return true;
    }


public bool TryEquipRewardAbilityStateAuthority(PlayerAbilityDefinition selected, out string failureReason)
    {
        return TryEquipSingleStateAuthority(selected, null, out failureReason);
    }

public string[] GetEquippedAbilityIds()
    {
        var ids = new List<string>();
        if (Object == null || !Object.IsValid || LoadoutRevision <= 0)
            return ids.ToArray();

        for (int slot = 0; slot < MaximumAbilityRuntimeSlots; slot++)
        {
            if (!ActiveAbilityRuntimeObjects.TryGet(slot, out NetworkObject runtimeObject) ||
                runtimeObject == null || !runtimeObject.IsValid)
                continue;
            PlayerAbilityRuntime runtime = runtimeObject.GetComponent<PlayerAbilityRuntime>();
            if (runtime != null && runtime.Definition != null &&
                !string.IsNullOrWhiteSpace(runtime.Definition.AbilityId))
                ids.Add(runtime.Definition.AbilityId);
        }
        return ids.ToArray();
    }


    /// <summary>供本地 HUD 讀取指定槽位；不改動 Loadout 或網路狀態。</summary>
    public PlayerAbilityRuntime GetEquippedRuntimeAtSlot(int slot)
    {
        if (slot < 0 || slot >= MaximumAbilityRuntimeSlots ||
            Object == null || !Object.IsValid || LoadoutRevision <= 0 ||
            !ActiveAbilityRuntimeObjects.TryGet(slot, out NetworkObject runtimeObject) ||
            runtimeObject == null || !runtimeObject.IsValid)
            return null;

        return runtimeObject.GetComponent<PlayerAbilityRuntime>();
    }
    public void SimulateActiveAbilities(NetInput input, NetworkButtons previousButtons)
    {
        SimulateActiveAbilities(input, previousButtons, input);
    }

    public void SimulateActiveAbilities(
        NetInput input,
        NetworkButtons previousButtons,
        NetInput legacyInput
    )
    {
        var actionGate = GetComponent<PlayerActionGate>();
        if (actionGate != null && actionGate.IsBlocked(PlayerActionBlockMask.ActiveAbility))
        {
            ClearFocusButtons(ref input.Buttons);
            ClearFocusButtons(ref legacyInput.Buttons);
        }
        RefreshOrderedRuntimeCache();

        for (int i = 0;
            i < orderedRuntimeCache.Count;
            i++)
        {
            PlayerAbilityRuntime runtime =
                orderedRuntimeCache[i];

            if (runtime == null)
            {
                continue;
            }

            if (runtime.IsBonusCast) continue;
            // 新 E 不沿用舊空中技能的鈎索／職業過濾；全域控制鎖已由 Player 與技能共用入口處理。
            var routed = runtime.TryGetModule<PlayerActiveAbilityBase>(out _) ? input : legacyInput;
            var previous = previousButtons;
            if (runtime.Definition.Category == PlayerAbilityCategory.GrappleFocus)
            {
                var button = FocusButton(runtime.FocusSlotIndex);
                bool held = FocusAbilitySlotRules.IsSlot(runtime.FocusSlotIndex) && routed.Buttons.IsSet(button);
                bool wasHeld = FocusAbilitySlotRules.IsSlot(runtime.FocusSlotIndex) && previous.IsSet(button);
                ClearFocusButtons(ref routed.Buttons);
                ClearFocusButtons(ref previous);
                routed.Buttons.Set(InputButton.Ability1, held);
                previous.Set(InputButton.Ability1, wasHeld);
            }
            runtime.Simulate(routed, previous);
        }
        TickBonusAbility(input);
    }


    /// <summary>由既有 PlayerMovement 在 KCC 固定 PrepareData 合併已裝備能力的額外位移。</summary>
    public Vector3 ConsumeMovementRecoil(float deltaTime)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return Vector3.zero;
        RefreshOrderedRuntimeCache();
        Vector3 delta = Vector3.zero;
        for (int i = 0; i < orderedRuntimeCache.Count; ++i)
            if (orderedRuntimeCache[i] != null) delta += orderedRuntimeCache[i].ConsumeMovementRecoil(deltaTime);
        return delta;
    }

    public float GetMovementInputMultiplier(
        NetInput input
    )
    {
        RefreshOrderedRuntimeCache();

        float multiplier =
            1f;

        for (int i = 0;
            i < orderedRuntimeCache.Count;
            i++)
        {
            PlayerAbilityRuntime runtime =
                orderedRuntimeCache[i];

            if (runtime == null)
            {
                continue;
            }

            multiplier *=
                runtime.GetMovementInputMultiplier(
                    input
                );
        }

        return Mathf.Clamp01(
            multiplier
        );
    }


    /// <summary>
    /// 目前可用的已裝備能力是否需要鈎索加入額外 Gameplay Target Layer。
    /// 由 Definition 描述，不把 Support／其他玩家 Layer 再綁死在職業。
    /// </summary>
    public bool RequiresAdditionalGrappleTargetMask()
    {
        RefreshOrderedRuntimeCache();

        for (int i = 0;
            i < orderedRuntimeCache.Count;
            i++)
        {
            PlayerAbilityRuntime runtime =
                orderedRuntimeCache[i];

            if (runtime == null ||
                runtime.Definition == null ||
                runtime.RefreshProfessionAvailabilityNow() == false)
            {
                continue;
            }

            if (runtime.Definition
                .IncludeAdditionalGrappleTargetMask)
            {
                return true;
            }
        }

        return false;
    }


    /// <summary>
    /// 查詢目前真正已裝備、且目前職業可用的某種能力。
    /// 查不到是合法情況，代表玩家沒有選該能力。
    /// </summary>
    public bool TryGetActiveModule<TModule>(
        out TModule module
    )
        where TModule : class
    {
        RefreshOrderedRuntimeCache();
        foreach (var extra in orderedRuntimeCache)
            if (extra != null && extra.IsBonusCast && extra.RefreshProfessionAvailabilityNow() &&
                extra.TryGetModule<IPlayerAbilityHudState>(out var extraHud) && extraHud.IsAbilityActive && extra.TryGetModule<TModule>(out module)) return true;
        for (int i = 0;
            i < orderedRuntimeCache.Count;
            i++)
        {
            PlayerAbilityRuntime runtime =
                orderedRuntimeCache[i];

            if (runtime == null ||
                runtime.RefreshProfessionAvailabilityNow() == false)
            {
                continue;
            }

            if (runtime.TryGetModule(
                    out module
                ))
            {
                return true;
            }
        }

        module =
            null;

        return false;
    }


    private void BuildAndSortSpawnPlan(IReadOnlyList<PlayerAbilityDefinition> abilities)
    {
        spawnPlan.Clear();
        int focusSlot = 0;

        for (int i = 0;
            i < abilities.Count;
            i++)
        {
            spawnPlan.Add(
                new SpawnPlanEntry
                {
                    SlotIndex = i,
                    FocusSlot = abilities[i].Category == PlayerAbilityCategory.GrappleFocus ? focusSlot++ : -1,
                    Definition = abilities[i]
                }
            );
        }

        spawnPlan.Sort(
            CompareSpawnPlanEntries
        );
    }


    private static int CompareSpawnPlanEntries(
        SpawnPlanEntry left,
        SpawnPlanEntry right
    )
    {
        int priorityComparison =
            left.Definition.ExecutionPriority.CompareTo(
                right.Definition.ExecutionPriority
            );

        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        int slotComparison =
            left.SlotIndex.CompareTo(
                right.SlotIndex
            );

        if (slotComparison != 0)
        {
            return slotComparison;
        }

        return string.CompareOrdinal(
            left.Definition.AbilityId,
            right.Definition.AbilityId
        );
    }


    private NetworkObject SpawnAbilityRuntime(
        SpawnPlanEntry entry
    )
    {
        return Runner.Spawn(
            entry.Definition.RuntimePrefab,
            transform.position,
            Quaternion.identity,
            Object.InputAuthority,
            (runner, spawnedObject) =>
            {
                PlayerAbilityRuntime runtime =
                    spawnedObject.GetComponent<
                        PlayerAbilityRuntime
                    >();

                if (runtime != null) runtime.FocusSlotIndex = entry.FocusSlot;
                runtime?.InitializeBeforeSpawn(
                    Object,
                    entry.SlotIndex
                );
            }
        );
    }


    private void RollbackSpawnedRuntimes()
    {
        for (int i = 0;
            i < spawnedRuntimeBuffer.Count;
            i++)
        {
            NetworkObject runtime =
                spawnedRuntimeBuffer[i];

            if (runtime != null &&
                runtime.IsValid &&
                runtime.HasStateAuthority)
            {
                Runner.Despawn(
                    runtime
                );
            }
        }

        spawnedRuntimeBuffer.Clear();
    }


    private void DespawnAllActiveRuntimes(
        NetworkRunner runner
    )
    {
        activeRuntimeDespawnBuffer.Clear();

        foreach (
            KeyValuePair<int, NetworkObject> pair
                in ActiveAbilityRuntimeObjects
        )
        {
            if (pair.Value != null)
            {
                activeRuntimeDespawnBuffer.Add(
                    pair.Value
                );
            }
        }

        for (int i = 0;
            i < MaximumAbilityRuntimeSlots;
            i++)
        {
            ActiveAbilityRuntimeObjects.Remove(
                i
            );
        }

        for (int i = 0;
            i < activeRuntimeDespawnBuffer.Count;
            i++)
        {
            NetworkObject runtime =
                activeRuntimeDespawnBuffer[i];

            if (runtime != null &&
                runtime.IsValid &&
                runtime.HasStateAuthority)
            {
                runner.Despawn(
                    runtime
                );
            }
        }

        activeRuntimeDespawnBuffer.Clear();
        orderedRuntimeCache.Clear();
    }


    private void RefreshOrderedRuntimeCache()
    {
        // NetworkDictionary 可能在能力數量相同的情況下整批換成另一組
        // Runtime。固定上限只有八格，這裡每次重建可避免 Proxy 保留已
        // Despawn 的舊快取，成本也比維護額外網路版本號更單純可靠。
        orderedRuntimeCache.Clear();

        foreach (
            KeyValuePair<int, NetworkObject> pair
                in ActiveAbilityRuntimeObjects
        )
        {
            NetworkObject runtimeObject =
                pair.Value;

            if (runtimeObject == null ||
                runtimeObject.IsValid == false)
            {
                continue;
            }

            PlayerAbilityRuntime runtime =
                runtimeObject.GetComponent<
                    PlayerAbilityRuntime
                >();

            if (runtime != null)
            {
                orderedRuntimeCache.Add(
                    runtime
                );
            }
        }

        CollectBonusRuntimes(orderedRuntimeCache);
        orderedRuntimeCache.Sort(
            CompareRuntimes
        );

    }


    private static int CompareRuntimes(
        PlayerAbilityRuntime left,
        PlayerAbilityRuntime right
    )
    {
        if (left == null)
        {
            return right == null
                ? 0
                : 1;
        }

        if (right == null)
        {
            return -1;
        }

        PlayerAbilityDefinition leftDefinition =
            left.Definition;

        PlayerAbilityDefinition rightDefinition =
            right.Definition;

        if (leftDefinition == null)
        {
            return rightDefinition == null
                ? left.LoadoutSlotIndex.CompareTo(
                    right.LoadoutSlotIndex
                )
                : 1;
        }

        if (rightDefinition == null)
        {
            return -1;
        }

        int priorityComparison =
            leftDefinition.ExecutionPriority.CompareTo(
                rightDefinition.ExecutionPriority
            );

        if (priorityComparison != 0)
        {
            return priorityComparison;
        }

        int slotComparison =
            left.LoadoutSlotIndex.CompareTo(
                right.LoadoutSlotIndex
            );

        if (slotComparison != 0)
        {
            return slotComparison;
        }

        return string.CompareOrdinal(
            leftDefinition.AbilityId,
            rightDefinition.AbilityId
        );
    }
}
