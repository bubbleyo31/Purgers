using Fusion;
using UnityEngine;


/// <summary>
/// 一個已裝備能力的獨立 Network Runtime。
///
/// Runtime 的生命週期只跟 Player Loadout 有關，不跟目前職業 Runtime
/// 綁在一起。因此 F1 / F2 / F3 切換武器職業時，能力狀態與冷卻不會
/// 因為 Profession Runtime 被 Despawn 而重置。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PlayerAbilityRuntime :
    NetworkBehaviour
{
    [Header("能力定義")]

    [SerializeField]
    [Tooltip("這顆 Runtime Prefab 對應的 Ability Definition。必須與 Loadout 裡引用的同一份資產一致。")]
    private PlayerAbilityDefinition definition;


    [Networked]
    public NetworkObject OwnerPlayerObject
    {
        get;
        private set;
    }


    [Networked]
    public int LoadoutSlotIndex
    {
        get;
        private set;
    }


    private MonoBehaviour moduleBehaviour;
    private IPlayerAbilityRuntimeModule module;
    private bool moduleBound;
    private bool? lastProfessionAvailability;


    public PlayerAbilityDefinition Definition =>
        definition;


    public Player OwnerPlayer =>
        OwnerPlayerObject != null
            ? OwnerPlayerObject.GetComponent<Player>()
            : null;


    public void InitializeBeforeSpawn(
        NetworkObject ownerPlayerObject,
        int loadoutSlotIndex
    )
    {
        OwnerPlayerObject =
            ownerPlayerObject;

        LoadoutSlotIndex =
            loadoutSlotIndex;
    }


    public override void Spawned()
    {
        ResolveModule();
        TryBindModule();
        RefreshProfessionAvailabilityNow();
    }


    public override void Despawned(
        NetworkRunner runner,
        bool hasState
    )
    {
        if (module != null)
        {
            module.SetProfessionAvailable(
                false
            );

            module.BindOwnerPlayer(
                null
            );
        }

        module =
            null;

        moduleBehaviour =
            null;

        moduleBound =
            false;

        lastProfessionAvailability =
            null;
    }


    /// <summary>
    /// 每 Tick 由 PlayerAbilityRuntimeManager 依穩定順序呼叫。
    /// </summary>
    public void Simulate(
        NetInput input,
        NetworkButtons previousButtons
    )
    {
        if (moduleBound == false &&
            TryBindModule() == false)
        {
            return;
        }

        if (RefreshProfessionAvailabilityNow() ==
            false)
        {
            return;
        }

        if (moduleBehaviour == null ||
            moduleBehaviour.isActiveAndEnabled == false)
        {
            return;
        }

        module.SimulateAbility(
            input,
            previousButtons
        );
    }


    /// <summary>
    /// 將能力 Runtime 裡的移動限制提供給 Player Core。
    /// </summary>
    public float GetMovementInputMultiplier(
        NetInput input
    )
    {
        if (moduleBound == false &&
            TryBindModule() == false)
        {
            return 1f;
        }

        if (RefreshProfessionAvailabilityNow() ==
            false)
        {
            return 1f;
        }

        if (moduleBehaviour is
            IPlayerMovementInputModifier modifier)
        {
            return Mathf.Clamp01(
                modifier.GetMovementInputMultiplier(
                    input
                )
            );
        }

        return 1f;
    }


    public bool IsAvailableForCurrentProfession =>
        IsCurrentProfessionAllowed();


    public bool TryGetModule<TModule>(
        out TModule foundModule
    )
        where TModule : class
    {
        if (moduleBound == false)
        {
            TryBindModule();
        }

        foundModule =
            module as TModule;

        return foundModule != null;
    }


    public bool ValidateConfiguration(
        PlayerAbilityDefinition expectedDefinition,
        out string failureReason
    )
    {
        failureReason =
            string.Empty;

        if (definition == null)
        {
            failureReason =
                "Runtime Prefab 尚未指定 Ability Definition。";

            return false;
        }

        if (expectedDefinition != null &&
            definition != expectedDefinition)
        {
            failureReason =
                $"Runtime Prefab 指向 {definition.AbilityId}，" +
                $"但 Loadout 要求 {expectedDefinition.AbilityId}。";

            return false;
        }

        MonoBehaviour[] behaviours =
            GetComponents<MonoBehaviour>();

        IPlayerAbilityRuntimeModule foundModule =
            null;

        int foundCount =
            0;

        for (int i = 0;
            i < behaviours.Length;
            i++)
        {
            if (behaviours[i] is
                IPlayerAbilityRuntimeModule candidate)
            {
                foundModule =
                    candidate;

                foundCount++;
            }
        }

        if (foundCount != 1 ||
            foundModule == null)
        {
            failureReason =
                $"Runtime Prefab 必須剛好有一個 " +
                $"{nameof(IPlayerAbilityRuntimeModule)}，" +
                $"目前找到 {foundCount} 個。";

            return false;
        }

        if (foundModule.AbilityCategory !=
            definition.Category)
        {
            failureReason =
                $"Ability Definition Category 是 {definition.Category}，" +
                $"但 Runtime Module 回報 {foundModule.AbilityCategory}。";

            return false;
        }

        return true;
    }


    private void ResolveModule()
    {
        module =
            null;

        moduleBehaviour =
            null;

        moduleBound =
            false;

        MonoBehaviour[] behaviours =
            GetComponents<MonoBehaviour>();

        for (int i = 0;
            i < behaviours.Length;
            i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour is
                IPlayerAbilityRuntimeModule foundModule)
            {
                if (module != null)
                {
                    Debug.LogError(
                        $"[{nameof(PlayerAbilityRuntime)}] " +
                        $"一顆 Runtime 只能有一個能力模組。" +
                        $"\nRuntime：{name}",
                        this
                    );

                    module =
                        null;

                    moduleBehaviour =
                        null;

                    return;
                }

                module =
                    foundModule;

                moduleBehaviour =
                    behaviour;
            }
        }

        if (module == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerAbilityRuntime)}] " +
                $"找不到 {nameof(IPlayerAbilityRuntimeModule)}。" +
                $"\nRuntime：{name}",
                this
            );
        }
    }


    private bool TryBindModule()
    {
        if (module == null)
        {
            ResolveModule();
        }

        Player ownerPlayer =
            OwnerPlayer;

        if (module == null ||
            ownerPlayer == null)
        {
            return false;
        }

        module.BindOwnerPlayer(
            ownerPlayer
        );

        moduleBound =
            true;

        return true;
    }


    public bool RefreshProfessionAvailabilityNow()
    {
        if (module == null)
        {
            lastProfessionAvailability =
                null;

            return false;
        }

        bool isAvailable =
            IsCurrentProfessionAllowed();

        if (lastProfessionAvailability.HasValue ==
                false ||
            lastProfessionAvailability.Value !=
                isAvailable)
        {
            module?.SetProfessionAvailable(
                isAvailable
            );

            lastProfessionAvailability =
                isAvailable;
        }

        return isAvailable;
    }


    private bool IsCurrentProfessionAllowed()
    {
        Player ownerPlayer =
            OwnerPlayer;

        if (definition == null ||
            ownerPlayer == null ||
            ownerPlayer.Profession == null)
        {
            return false;
        }

        return definition.IsProfessionAllowed(
            ownerPlayer.Profession.CurrentProfession
        );
    }
}
