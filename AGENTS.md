# Purgers Agent Rules

These rules apply to Hermes, Codex, Claude Code, and other coding agents working in this repository.

## Read before editing

1. Read `Documentation/ProjectArchitecture/00_CHATGPT快速讀取.md`.
2. Read the task-specific architecture document linked from that file.
3. Inspect the real implementation under `Assets/Scripts`; source code and serialized assets are the final truth.
4. Check `git status --short`. The working tree may contain unfinished user work. Never reset, revert, overwrite, or reformat unrelated changes.

## Project baseline

- Unity `2022.3.62f1` LTS, URP 14.
- Photon Fusion multiplayer with KCC.
- Input System, AI Navigation, Unity Test Framework, ParrelSync, and MCP for Unity.
- Host/State Authority owns gameplay results. Input Authority gathers intent. Local presentation must not decide networked results.
- `[Networked]` data is accessed only after a valid `NetworkBehaviour.Spawned()` lifecycle.

## Architecture invariants

- Preserve the arbitration layers: `PlayerActionGate`, `PlayerProfessionRuntimeManager`, `PlayerAbilityRuntimeManager`, `PlayerIncomingDamageModifierBridge`, `EnemyActionGate`, `EnemyMovementOwnership`, and `EnemyCombatDecisionController`.
- Do not create parallel damage, health, movement, input, save, scene-transition, or camera-FOV systems before proving the existing owner cannot support the requirement.
- `TestDamageReceiver` is currently production enemy health behavior despite its name; do not delete or rename it casually.
- Keep local presentation separate from authoritative world state.
- Do not move or rename serialized Unity types or fields without planning prefab/scene migration.

## Editing Unity assets

- Prefer MCP for Unity or a focused Unity Editor script for scenes, prefabs, ScriptableObjects, and serialized references.
- Do not hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` YAML unless no Editor path exists and the exact serialization impact is understood.
- Do not run two write-capable agents against the same checkout. Use a Git worktree for parallel implementation.
- Package dependencies must be pinned to a released version, tag, or exact commit; do not leave production dependencies on a moving branch.

## Development workflow

1. Define one vertical behavior slice.
2. For behavior changes, add a focused failing EditMode/PlayMode test and confirm the expected failure.
3. Implement the smallest change that passes it.
4. Wait for Unity compilation and inspect the Console.
5. Run the focused test, then the full `PurgersRegression` EditMode suite.
6. A reported success with `total = 0` is a failure to execute tests, not a pass.
7. For multiplayer behavior, separately verify State Authority, Input Authority, late join/reconnect, and local presentation as applicable.
8. Update architecture documents according to `120_文件維護規則.md` when responsibilities, public interfaces, network authority, serialized setup, or data flow change.
9. Review only the files changed for the task and report pre-existing failures separately.

## Tooling policy

- Use the existing MCP for Unity integration for Editor-aware automation.
- Memory Profiler, Profile Analyzer, and Code Coverage are approved editor-only diagnostics.
- Do not introduce DOTween, UniTask, Cinemachine, Addressables, a dependency-injection framework, or a paid asset merely for convenience. Add one only when a concrete feature has an owner, migration plan, and measurable benefit.
- Existing custom FOV, camera shake, and screen transition systems have gameplay-specific ownership and must not be silently replaced by a generic tween/camera package.

## Verification targets

- No new Unity compile errors or warnings attributable to the change.
- `PurgersRegression` runs with a non-zero test count and zero failures.
- Scene/prefab edits have no missing scripts or broken serialized references.
- Runtime or networking changes include the relevant Host/Client manual verification; EditMode tests alone are not enough.
