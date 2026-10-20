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
public partial class PlayerAbilityRuntime :
    NetworkBehaviour
{
    [Header("能力定義")]

    [SerializeField]
    [Tooltip("這顆 Runtime Prefab 對應的 Ability Definition。必須與 Loadout 裡引用的同一份資產一致。")]
    private PlayerAbilityDefinition definition;

    [Header("E 專注能力後座力")]
    [SerializeField, Tooltip("每顆技能 Runtime 各自配置鏡頭與移動後座力；預設停用且未設定時機。僅 GrappleFocus 模組成功施放事件可觸發，不套用到鉤索命中能力。")]
    private PlayerAbilityRecoilSettings recoil = new PlayerAbilityRecoilSettings();

    // 待施加與回正狀態由 State Authority 保存；Proxy 不重播觸發，只讀原 KCC 同步結果。
    [Networked] private float PendingCameraPitch { get; set; }
    [Networked] private float PendingCameraYaw { get; set; }
    [Networked] private float CameraApplicationRemaining { get; set; }
    [Networked] private float AccumulatedCameraPitch { get; set; }
    [Networked] private TickTimer CameraRecoveryTimer { get; set; }
    [Networked] private int CameraRecoilSequence { get; set; }
    [Networked] private int LastCameraApplicationTick { get; set; }
    [Networked] private int LastCameraRecoveryTick { get; set; }

    [Networked] private Vector3 RemainingMovementRecoil { get; set; }
    [Networked] private float MovementRecoilSeconds { get; set; }
    [Networked] private int LastMovementRecoilTick { get; set; }
    [Networked] private TickTimer MovementRecoilLifetime { get; set; }

    /// <summary>既有 KCC PrepareData 每固定 Tick 消耗一次；碰撞丟失的位移不存回、不補發。</summary>
    public Vector3 ConsumeMovementRecoil(float deltaTime)
    {
        if (!spawned || Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            LastMovementRecoilTick == Runner.Tick.Raw) return Vector3.zero;
        LastMovementRecoilTick = Runner.Tick.Raw;
        var owner = OwnerPlayer;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority ||
            owner.Health == null || !owner.Health.IsAlive || MovementRecoilLifetime.ExpiredOrNotRunning(Runner))
        {
            RemainingMovementRecoil = Vector3.zero; MovementRecoilSeconds = 0f;
            return Vector3.zero;
        }
        var distance = RemainingMovementRecoil;
        float remaining = MovementRecoilSeconds;
        Vector3 delta = AbilityMovementRules.ConsumeLinearDecay(ref distance, ref remaining, deltaTime);
        RemainingMovementRecoil = distance; MovementRecoilSeconds = remaining;
        return delta;
    }

    /// <summary>由成功受理／施放入口呼叫；只由權威修改 Owner KCC，不以 RPC 或 Render 補播。</summary>
    public void ApplyAbilityRecoil(PlayerAbilityRecoilEvent trigger, Vector2? releaseAim = null)
    {
        if (!spawned || Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            definition == null || definition.Category != PlayerAbilityCategory.GrappleFocus || recoil == null) return;
        Player owner = OwnerPlayer;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority ||
            owner.Health == null || !owner.Health.IsAlive || owner.Movement == null || owner.Movement.KCC == null) return;
        var data = owner.Movement.KCC.Data;
        Vector2 aim = releaseAim ?? new Vector2(data.LookPitch, data.LookYaw);
        Vector3 velocity = recoil.ResolveMovement(trigger, aim.x, aim.y);
        Vector2 angles = recoil.ResolveCamera(trigger);
        if (velocity != Vector3.zero)
        {
            float duration = recoil.MovementDecayDuration;
            // 保留尚未施加的距離，再合併本次三角速度曲線面積；重觸發只重設衰減時間。
            RemainingMovementRecoil += velocity * (duration * 0.5f);
            MovementRecoilSeconds = duration;
            // 保留派發到下一個 KCC Tick 的餘裕，避免 seconds→ticks 截斷吃掉最後一段。
            MovementRecoilLifetime = TickTimer.CreateFromTicks(Runner, Mathf.CeilToInt(duration / Runner.DeltaTime) + 2);
        }
        if (angles == Vector2.zero) return;
        CameraRecoilSequence++;
        PendingCameraPitch += angles.x;
        PendingCameraYaw += Mathf.Abs(angles.y) * (CameraRecoilSequence % 2 == 0 ? 1f : -1f);
        CameraApplicationRemaining = recoil.CameraApplicationDuration;
        CameraRecoveryTimer = TickTimer.CreateFromSeconds(Runner, recoil.CameraRecoveryDelay);
        // 0 秒保留原立即施加契約；全勾且無前搖可同 Tick 觸發兩次。
        if (CameraApplicationRemaining <= 0f) ApplyPendingCameraRecoil(true);
    }


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


    [Networked] public int FocusSlotIndex { get; set; }

    private MonoBehaviour moduleBehaviour;
    private IPlayerAbilityRuntimeModule module;
    private bool moduleBound;
    private bool spawned;
    private bool? lastProfessionAvailability;


    /// <summary>先處理真正手動壓槍與既有回正，再讓成功施放把新角度排入。</summary>
    private void ProcessCameraRecoilRecovery()
    {
        if (!spawned || Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        var owner = OwnerPlayer;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority || owner.Health == null || !owner.Health.IsAlive)
        {
            ResetCameraRecoil();
            RemainingMovementRecoil = Vector3.zero; MovementRecoilSeconds = 0f;
            MovementRecoilLifetime = TickTimer.None;
            return;
        }
        if (owner.Movement == null || recoil == null || LastCameraRecoveryTick == Runner.Tick.Raw) return;
        LastCameraRecoveryTick = Runner.Tick.Raw;
        float accumulated = AccumulatedCameraPitch;
        float manual = owner.Movement.ConsumeManualRecoilPitch(accumulated);
        float step = WeaponRecoilRecovery.Step(ref accumulated, manual,
            PendingCameraPitch != 0f || PendingCameraYaw != 0f,
            CameraRecoveryTimer.ExpiredOrNotRunning(Runner), recoil.CameraRecoverySpeed, Runner.DeltaTime);
        if (step > 0f)
        {
            float applied = owner.Movement.AddLookRotationImpulse(step, 0f);
            accumulated -= step - applied;
        }
        AccumulatedCameraPitch = accumulated;
    }

    private void ApplyPendingCameraRecoil(bool immediate = false)
    {
        if (!spawned || Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        var owner = OwnerPlayer;
        if (owner == null || owner.Object == null || !owner.Object.IsValid || !owner.Object.HasStateAuthority || owner.Health == null || !owner.Health.IsAlive || owner.Movement == null) return;
        if (!immediate && LastCameraApplicationTick == Runner.Tick.Raw) return;
        LastCameraApplicationTick = Runner.Tick.Raw;
        float pitch = PendingCameraPitch, yaw = PendingCameraYaw, remaining = CameraApplicationRemaining;
        Vector2 step = WeaponRecoilApplication.Step(ref pitch, ref yaw, ref remaining, immediate ? 0f : Runner.DeltaTime);
        PendingCameraPitch = pitch; PendingCameraYaw = yaw; CameraApplicationRemaining = remaining;
        if (step != Vector2.zero)
            AccumulatedCameraPitch += owner.Movement.AddLookRotationImpulse(step.x, step.y);
    }

    private void ResetCameraRecoil()
    {
        PendingCameraPitch = 0f; PendingCameraYaw = 0f; CameraApplicationRemaining = 0f;
        AccumulatedCameraPitch = 0f; CameraRecoveryTimer = TickTimer.None;
        CameraRecoilSequence = 0; LastCameraApplicationTick = int.MinValue; LastCameraRecoveryTick = int.MinValue;
    }

    public PlayerAbilityDefinition Definition =>
        definition;


    public Player OwnerPlayer =>
        spawned && Object != null && Object.IsValid && OwnerPlayerObject != null
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
        spawned = true;
        liveRuntimes.Add(this);
        if (Object.HasStateAuthority)
        {
            ResetCameraRecoil();
            RemainingMovementRecoil = Vector3.zero; MovementRecoilSeconds = 0f;
            LastMovementRecoilTick = int.MinValue; MovementRecoilLifetime = TickTimer.None;
        }
        ResolveModule();
        TryBindModule();
        boundOwner = OwnerPlayerObject;
        RefreshProfessionAvailabilityNow();
    }


    /// <summary>各端皆重試尚未解析的 Owner；遠端 Proxy 不依賴輸入或本機 HUD 才恢復綁定。</summary>
    public override void Render()
    {
        if (spawned && Object != null && Object.IsValid) RenderLoot();
        if (spawned && Object != null && Object.IsValid && !moduleBound && TryBindModule())
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

        liveRuntimes.Remove(this);
        if (lootVisual != null) Destroy(lootVisual.gameObject);
        spawned = false;
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
    public void Simulate(NetInput input, NetworkButtons previousButtons)
    {
        if (spawned && (IsWorldLoot || TransferCooldownRemaining > 0)) return;
        RefreshLootBinding();
        ProcessCameraRecoilRecovery();
        SimulateModule(input, previousButtons);
        // 即使資格不符或按鍵被阻擋，已成功觸發的後座力仍完成施加／回正。
        ApplyPendingCameraRecoil();
    }

    private void SimulateModule(
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
            if (!isAvailable && spawned && Object != null && Object.IsValid && Object.HasStateAuthority &&
                OwnerPlayer != null && OwnerPlayer.GetComponent<PlayerActionGate>()?.IsCaptured == true)
            {
                RemainingMovementRecoil = Vector3.zero;
                MovementRecoilSeconds = 0f;
                MovementRecoilLifetime = TickTimer.None;
            }
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
        if (!spawned || IsWorldLoot) return false;
        Player ownerPlayer =
            OwnerPlayer;

        if (definition == null ||
            ownerPlayer == null ||
            ownerPlayer.Profession == null)
        {
            return false;
        }

        if (ownerPlayer.Health != null && ownerPlayer.Health.Object != null &&
            ownerPlayer.Health.Object.IsValid && !ownerPlayer.Health.IsAlive) return false;
        var captureGate = ownerPlayer.GetComponent<PlayerActionGate>();
        if (captureGate != null && captureGate.IsCaptured) return false;

        return definition.IsProfessionAllowed(
            ownerPlayer.Profession.CurrentProfession
        );
    }
}
