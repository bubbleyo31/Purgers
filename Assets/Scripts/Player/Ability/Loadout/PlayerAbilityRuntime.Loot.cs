using System.Collections.Generic;
using Fusion;
using UnityEngine;

public partial class PlayerAbilityRuntime
{
    private static readonly HashSet<PlayerAbilityRuntime> liveRuntimes = new HashSet<PlayerAbilityRuntime>();
    public static IEnumerable<PlayerAbilityRuntime> LiveRuntimes => liveRuntimes;
    [Networked] public NetworkBool IsWorldLoot { get; private set; }
    [Networked] public NetworkBool IsBonusCast { get; set; }
    [Networked] public Vector3 LootPosition { get; private set; }
    [Networked] public int SkillLevel { get; set; }
    [Networked] public int RequiredPlayerLevel { get; set; }
    [Networked] private TickTimer TransferCooldown { get; set; }
    [Networked] private float TransferCooldownDuration { get; set; }
    [Networked] private float LootFallSpeed { get; set; }
    [Networked] private NetworkBool LootGrounded { get; set; }
    [Networked] private int LootCollisionMask { get; set; }
    private readonly RaycastHit[] lootFloorHits = new RaycastHit[16];
    private NetworkObject boundOwner;
    private PlayerAbilityLootVisual lootVisual;
    public float TransferCooldownRemaining => spawned && Object != null && Object.IsValid ?
        Mathf.Max(0, TransferCooldown.RemainingTime(Runner) ?? 0) : 0;
    public float LootCooldownRemaining => Mathf.Max(TransferCooldownRemaining,
        module is IPlayerAbilityHudState hud && moduleBound ? hud.CooldownRemainingSeconds : 0);
    private void RefreshLootBinding()
    {
        if (boundOwner == OwnerPlayerObject) return;
        module?.SetProfessionAvailable(false);
        module?.BindOwnerPlayer(null);
        moduleBound = false; lastProfessionAvailability = null;
        boundOwner = OwnerPlayerObject;
        if (boundOwner != null) TryBindModule();
    }
    public void DropToWorld(Vector3 position, int collisionMask = Physics.DefaultRaycastLayers)
    {
        if (!spawned || !Object.HasStateAuthority) return;
        module?.SetProfessionAvailable(false);
        float cooldown = LootCooldownRemaining;
        if (module is IPlayerAbilityCooldownSource source && source.CooldownRemainingSeconds >= TransferCooldownRemaining)
            TransferCooldownDuration = source.CooldownDurationSeconds;
        TransferCooldown = cooldown > 0 ? TickTimer.CreateFromSeconds(Runner, cooldown) : TickTimer.None;
        ResetCameraRecoil(); RemainingMovementRecoil = Vector3.zero;
        LootPosition = position; IsWorldLoot = true; IsBonusCast = false;
        LootFallSpeed = 0f; LootGrounded = false; LootCollisionMask = collisionMask;
        Object.RemoveInputAuthority(); OwnerPlayerObject = null; LoadoutSlotIndex = -1;
        RefreshLootBinding();
    }
    public override void FixedUpdateNetwork()
    {
        if (!spawned || Object == null || !Object.HasStateAuthority || !IsWorldLoot || LootGrounded) return;
        LootFallSpeed = Mathf.Min(40f, LootFallSpeed + Mathf.Abs(Physics.gravity.y) * Runner.DeltaTime);
        float step = LootFallSpeed * Runner.DeltaTime;
        int count = Runner.GetPhysicsScene().Raycast(LootPosition, Vector3.down, lootFloorHits,
            step + .25f, LootCollisionMask, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        Vector3 ground = default;
        for (int i = 0; i < count; i++)
        {
            var hit = lootFloorHits[i];
            if (hit.collider.GetComponentInParent<Player>() != null || hit.collider.GetComponentInParent<TestDamageReceiver>() != null ||
                hit.distance >= closest) continue;
            closest = hit.distance; ground = hit.point;
        }
        if (!float.IsPositiveInfinity(closest))
        { LootPosition = ground + Vector3.up * .25f; LootGrounded = true; LootFallSpeed = 0; }
        else LootPosition += Vector3.down * step;
        var logic = GameLogic.GetPrimaryForRunner(Runner);
        if (logic != null && LootPosition.y < logic.FallDeathWorldY) Runner.Despawn(Object);
    }
    public void EquipFromWorld(NetworkObject owner, int slot)
    {
        if (!spawned || !Object.HasStateAuthority) return;
        IsWorldLoot = false; OwnerPlayerObject = owner; LoadoutSlotIndex = slot;
        Object.AssignInputAuthority(owner.InputAuthority);
        RefreshLootBinding(); RefreshProfessionAvailabilityNow();
    }
    public static void ClearWorldLoot(NetworkRunner runner)
    {
        var snapshot = new List<PlayerAbilityRuntime>(liveRuntimes);
        foreach (var runtime in snapshot)
            if (runtime != null && runtime.spawned && runtime.Runner == runner && runtime.Object != null &&
                runtime.Object.IsValid && runtime.Object.HasStateAuthority && runtime.IsWorldLoot) runner.Despawn(runtime.Object);
    }
    private void RenderLoot()
    {
        RefreshLootBinding();
        if (IsWorldLoot)
        {
            if (lootVisual == null)
            {
                var root = new GameObject("Ability Loot Presentation");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                lootVisual = root.AddComponent<PlayerAbilityLootVisual>();
                lootVisual.Bind(this);
            }
            lootVisual.transform.position = LootPosition;
        }
        else if (lootVisual != null) { Destroy(lootVisual.gameObject); lootVisual = null; }
    }
}
