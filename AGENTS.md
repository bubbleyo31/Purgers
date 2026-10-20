# Purgers Agent Rules

These rules apply to Hermes, Codex, Claude Code, and other coding agents working in this repository.

## Read before editing

1. Read `Documentation/ProjectArchitecture/00_CHATGPT快速讀取.md`.
2. For feature additions or changes, first search `Documentation/ProjectArchitecture/關鍵詞規則索引.md` by keyword, alias, and class name; then read the matched rules and the task-specific architecture document.
3. Inspect the real implementation under `Assets/Scripts`; source code and serialized assets are the final truth.
4. Check `git status --short`. The working tree may contain unfinished user work. Never reset, revert, overwrite, or reformat unrelated changes.

## 關鍵詞規則索引

- 新對話涉及新增、修改或打磨功能時，先查 [關鍵詞規則索引](Documentation/ProjectArchitecture/關鍵詞規則索引.md)，再讀命中的正式規則與設定位置並核對實作；先說明沿用哪些既有規則，避免重新建立已規劃的系統。
- 同詞命中多個系統時，依任務上下文區分；仍不明確時先詢問使用者。新對話收到添加功能的指令，查索引及相關文件仍找不到時，必須先詢問「這是全新功能，還是先前已規劃／實作過的功能？」不能把「添加」或搜尋未命中當成全新設計的授權。
- 若使用者回答不是新功能，先深入搜尋專案文件、歷史規劃與設定／驗證紀錄，再追查 `Assets/Scripts` 及必要資產，找回既有規則與責任入口後接續工作；仍找不到時列明搜尋範圍並請使用者提供舊名稱或線索，不自行重建。
- 若確認為新功能，再依既有架構規劃與實作。答覆前可繼續唯讀查找及不依賴此判定的工作，但不得先建立這項功能的新系統；未回覆不視為「是」。同一對話已明確確認新舊性質時不重複詢問。詳細分支見索引的「找不到功能時的必要確認」。
- 功能或規則打磨完成後，主動提出是否新增或更新索引，附上建議關鍵詞、定案摘要與規則／設定連結，讓使用者確認具體條目。使用者已明確要求收錄時直接完成，不重複詢問；未答覆不得視為同意。
- 新增條目或改寫定案規則須遵循上述確認；已收錄條目的路徑、事實漂移及驗證狀態依文件維護規則同步修正，不必因索引確認流程停止已授權的實作或文件維護。
- 索引只保存精簡規則與路由；完整數值及操作步驟保留在正式模組文件。分開標記設計定案、實作／資產與驗證狀態；暫定方案不可冒充已確認規則。

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

## 3D art topology and resource budget

- Default to quad-based modeling. Use triangles only where they improve a necessary termination, hidden cap, or topology transition; do not increase geometry merely to force every face into a quad.
- Control runtime cost from the start: silhouette and deformation justify geometry; small grooves, grain, knurling, and surface wear should preferentially use textures or normal maps. Avoid unnecessary subdivision, tiny separate meshes, material slots, and oversized textures.
- Before modeling, record the existing asset cost and set a task-specific budget. Do not invent a universal production budget without a target-platform measurement.
- Deliver polygon distribution (quads/triangles/ngons), triangulated triangle count, vertex count, mesh/material counts, texture resolutions, and comparison with the baseline. Quads are an authoring preference, not a claim of lower GPU cost.
- Preserve approved silhouettes, hand meshes, rigs, weights, and animations. Optimize inherited content only within the approved scope; report inherited costs separately.
- Verify exported FBX topology and animation round-trip. DCC inspection does not prove Unity import, draw calls, runtime performance, or final visual acceptance.
- Detailed default rules: `Documentation/ProjectArchitecture/150_開發工具與AI_Agent工作流.md` section 6.

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

## 背景託管不搶焦點

- Unity MCP／Agent 背景託管時，預設使用者正在其他視窗工作。不得主動將 Unity 或測試 Player 帶到前景、切換應用程式、聚焦 EditorWindow，或以桌面點擊／模擬鍵鼠操作干擾使用者；不得以搶焦點作為 MCP 逾時、背景節流或測試卡頓的恢復手段。
- 優先使用 MCP 資料查詢、Console、測試結果及不需要聚焦的相機離屏渲染。呼叫截圖或開啟 Editor 視窗前，確認實作沒有 `Focus()`、`GetWindow` 預設聚焦、`Show()` 或前景啟用等副作用；不可只依工具名稱判斷安全。現有 MCP 的 Scene View 截圖會呼叫 `sceneView.Focus()`，背景託管期間不可使用這條截圖路徑。
- Game View 進入播放時使用 `PlayUnfocused`，不啟用 Play Focused／Play Maximized；保留 `Run In Background`。後者僅確保背景執行，不是防止搶焦點的保證。這些是本機 Editor／版面設定；新建 Game View、切換 Layout 或升級 Unity 後須重新核對，不可為核對而聚焦視窗。
- 必須取得前景焦點的視覺／輸入驗證，先記錄具體待驗項目，繼續不依賴焦點的工作，待使用者明確允許該前景操作後才執行。未執行的驗證必須標示待驗，不可宣稱通過。
- 不為避免搶焦點而擅自修改 Windows 全域焦點／登錄設定、安裝攔截程式、強制最小化視窗，或直接修改 `Library/PackageCache` 套件程式。若安全的背景路徑不足，先回報具體觸發操作與限制。

## Verification targets

- No new Unity compile errors or warnings attributable to the change.
- `PurgersRegression` runs with a non-zero test count and zero failures.
- Scene/prefab edits have no missing scripts or broken serialized references.
- Runtime or networking changes include the relevant Host/Client manual verification; EditMode tests alone are not enough.


## 儲存位置與工作樹規範（2026-10-19）

- 使用者已要求：工作樹及其他大型工作資料，能放 M 槽就優先放 M 槽，避免占滿 C 槽。
- 工作樹實體根目錄採用 M:\CodexWorktrees；既有 C:\Users\hsuai\.codex\worktrees 路徑以目錄連結相容。建立前核對該連結仍指向 M 槽；不要刪除連結後在 C 槽重建實體工作樹。
- Unity 隔離測試快取使用 M:\CodexUnityCaches\<task>；其他任務暫存、下載、建置、匯出、效能擷取與大型中間產物優先使用 M:\CodexData 下的具名任務目錄，或已在 M 槽的專案目錄。每個工作樹保有獨立 Library，不共用可寫入快取。
- 優先使用工具正式支援的輸出／快取路徑設定；程序暫存需要改路徑時只對該程序設定 TEMP/TMP。不要為此改 Windows 全域 TEMP/TMP、pagefile、整個 AppData、使用者目錄或所有 CODEX_HOME。
- 先核對 M 槽可用、空間足夠及路徑權限；不能放 M 槽時先說明原因與預估 C 槽成本，不默默產生大型 C 槽副本。受執行環境固定路徑限制時如實記錄。
- 排隊、唯讀查核與單純討論不要預先建立完整工作樹。必要的並行實作仍須隔離；正式開始才建立，優先復用合適的既有工作樹。
- 搬移前停止相關寫入，保留未提交、未追蹤及必要忽略檔案；核對檔案內容、Git 狀態、目錄連結與工具路徑，確認後才釋放舊副本。不可只因閒置就刪除。
- 工作完成後回報新增工作樹／快取位置與後續保留需求；依使用者授權封存或清理，不自動刪除未整合成果。
