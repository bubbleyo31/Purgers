using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Purgers.GameFlow.Control;

public partial class PlayerAbilityRuntimeManager
{
    [Header("技能掉落與拾取")]
    [SerializeField, Min(.1f), Tooltip("按 E 拾取技能最大距離（公尺）。需面向技能、無牆遮擋；隊友救援優先。")]
    private float lootPickupDistance = 2.5f;
    [SerializeField, Tooltip("掉落貼地與拾取遮擋 Layer；忽略 Trigger，需包含地板牆壁。")]
    private LayerMask lootWorldMask = Physics.DefaultRaycastLayers & ~((1 << 5) | (1 << 7) | (1 << 8));
    [Networked] private NetworkBool LootEConsumed { get; set; }
    private readonly RaycastHit[] lootSightHits = new RaycastHit[32];
    public PlayerAbilityRuntime GetCategoryRuntime(PlayerAbilityCategory category)
    {
        for (int i = 0; i < MaximumAbilityRuntimeSlots; i++)
        { var runtime = GetEquippedRuntimeAtSlot(i); if (runtime != null && runtime.Definition.Category == category) return runtime; }
        return null;
    }
    private bool TryEquipSingleStateAuthority(PlayerAbilityDefinition selected, PlayerAbilityRuntime incoming, out string error)
    {
        error = string.Empty;
        if (Object == null || !Object.HasStateAuthority || !StartingLoadoutApplied || LoadoutRevision <= 0 || selected == null || startingLoadout == null)
        { error = "能力管理器尚未就緒。"; return false; }
        var definitions = new List<PlayerAbilityDefinition>();
        var previous = GetCategoryRuntime(selected.Category);
        if (previous != null && previous.Definition == selected && incoming == null) return true;
        int slot = previous != null ? previous.LoadoutSlotIndex : -1;
        for (int i = 0; i < MaximumAbilityRuntimeSlots; i++)
        {
            var runtime = GetEquippedRuntimeAtSlot(i);
            if (runtime == null) { if (slot < 0) slot = i; continue; }
            if (runtime != previous) definitions.Add(runtime.Definition);
        }
        definitions.Add(selected);
        if (slot < 0 || !PlayerAbilityLoadoutDefinition.TryValidateEntries(startingLoadout.SlotLayout, definitions, MaximumAbilityRuntimeSlots, out error)) return false;
        var prefab = selected.RuntimePrefab != null ? selected.RuntimePrefab.GetComponent<PlayerAbilityRuntime>() : null;
        if (prefab == null || !prefab.ValidateConfiguration(selected, out error)) return false;
        if (incoming == null)
        {
            var created = SpawnAbilityRuntime(new SpawnPlanEntry { SlotIndex = slot, Definition = selected });
            if (created == null) { error = "技能生成失敗，原裝備保留。"; return false; }
            incoming = created.GetComponent<PlayerAbilityRuntime>(); incoming.SkillLevel = 1; incoming.RequiredPlayerLevel = 1;
        }
        if (previous != null) previous.DropToWorld(ResolveDropPosition(), lootWorldMask.value);
        ActiveAbilityRuntimeObjects.Set(slot, incoming.Object);
        incoming.EquipFromWorld(Object, slot);
        LoadoutRevision++;
        return true;
    }
    private Vector3 ResolveDropPosition()
    {
        Vector3 position = transform.position + Vector3.up * .5f;
        if (Runner.GetPhysicsScene().Raycast(position, Vector3.down, out var hit, 4f, lootWorldMask, QueryTriggerInteraction.Ignore) &&
            hit.collider.GetComponentInParent<Player>() == null) position = hit.point + Vector3.up * .25f;
        return position;
    }
    private int lootPresentationFrame = -1;
    private PlayerAbilityRuntime lootPresentationCandidate;
    public PlayerAbilityRuntime FindLootCandidateForPresentation()
    {
        if (lootPresentationFrame != Time.frameCount)
        {
            lootPresentationFrame = Time.frameCount;
            lootPresentationCandidate = FindLootCandidate();
        }
        return lootPresentationCandidate;
    }
    public PlayerAbilityRuntime FindLootCandidate()
    {
        var owner = GetComponent<Player>();
        if (Object == null || !Object.IsValid || owner.Health == null || !owner.Health.IsAlive ||
            owner.GetComponent<PlayerActionGate>()?.IsBlocked(PlayerActionBlockMask.ActiveAbility) == true) return null;
        Vector3 origin = transform.position + Vector3.up * 1.4f;
        Vector3 direction = owner.Movement != null ? owner.Movement.GetAimDirection() : transform.forward;
        PlayerAbilityRuntime best = null; float bestDot = .65f;
        foreach (var runtime in PlayerAbilityRuntime.LiveRuntimes)
        {
            if (runtime == null || runtime.Object == null || !runtime.Object.IsValid || runtime.Runner != Runner || !runtime.IsWorldLoot) continue;
            Vector3 delta = runtime.LootPosition - origin;
            if (delta.magnitude > lootPickupDistance) continue;
            float dot = Vector3.Dot(direction, delta.normalized);
            if (dot < bestDot) continue;
            int count = Runner.GetPhysicsScene().Raycast(origin, delta.normalized, lootSightHits, delta.magnitude, lootWorldMask, QueryTriggerInteraction.Ignore);
            bool blocked = count == lootSightHits.Length;
            for (int i = 0; i < count && !blocked; i++)
                if (lootSightHits[i].collider.GetComponentInParent<Player>() == null) blocked = true;
            if (blocked) continue;
            best = runtime; bestDot = dot;
        }
        return best;
    }
    public void FilterLootInput(ref NetInput input)
    {
        if (Object == null || !Object.IsValid) return;
        bool held = input.Buttons.IsSet(InputButton.Ability1);
        if (!held) { LootEConsumed = false; return; }
        if (PlayerControlLocks.BlocksGameplay(input.BlockedControls)) return;
        var owner = GetComponent<Player>();
        var loot = owner.Health != null && owner.Health.FindRescueCandidate() == null ? FindLootCandidate() : null;
        if (!LootEConsumed && loot != null)
        {
            LootEConsumed = true;
            if (Object.HasStateAuthority)
            {
                var logic = GameLogic.GetPrimaryForRunner(Runner);
                if (logic != null && logic.TryGetPlayerExperience(Object.InputAuthority, out var xp) &&
                    xp.Level >= loot.RequiredPlayerLevel && PlayerAbilityQualification.IsAllowed(owner, loot.Definition) &&
                    TryEquipSingleStateAuthority(loot.Definition, loot, out _)) logic.RecordEquippedAbility(owner, loot);
            }
        }
        if (LootEConsumed) input.Buttons.Set(InputButton.Ability1, false);
    }
}
