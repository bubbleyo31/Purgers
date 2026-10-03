# Phase 4-F：Boss 關第一個可玩垂直切片

> **2026-09-24 後續狀態**：使用者回報 Phase 4 整體手動測試通過，包含本切片的驗收狀態更新為「通過（使用者回報）」。本次未取得 Boss 自然擊殺、晚加入、全滅或獨立 Host／Client 的逐項日誌，因此下方 2026-09-22 的「待驗證」是當時證據快照，不能改讀成這些個別案例已有可重現紀錄。詳見 [130](130_回歸驗證與審查狀態.md)。

> 最後核對日期：2026-09-22（程式、資產接線與 EditMode；Play Mode、Host／Client 待驗證）  
> 核對來源：`StageRules`、`MapRunSelectionPrototype`、`NetworkMapState`、`BossStageObjectiveController`、`StageFlowController`、`StageHudController`  
> 前置文件：[152 Phase 4-E](152_Phase4-E_CycleLength與Runtime循環.md)、[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md)

## 1. 本切片已決定規格

- 使用既有 `Game` Scene，不新增 Boss Scene。
- Boss 地圖使用專用 `MapChunk` Prefab，不走普通多 Chunk 生成。
- 擊敗 Boss 立即成功，不再要求撤離。
- Boss 時限可設定秒數；`0` 代表 Unlimited，第一版預設 `0`。
- 全部連線玩家死亡仍由 State Authority 判定失敗。

## 2. 集中式規格與資料流

```text
Host Save StageLevel + CycleLengthSnapshot
→ StageRules.ResolveRuntimePlan
→ StageRuntimePlan
   ├─ MapRoute = DedicatedBossChunk
   ├─ ObjectiveKind = DefeatBoss
   ├─ RequiresExtraction = false
   └─ StageRules.ResolveTimeLimitSeconds(...)
→ MapRunSelectionPrototype 建立專用 Boss Chunk
→ NetworkMapState 發布／Client 重建權威首 Chunk
→ BossStageObjectiveController 由 State Authority Spawn Boss
→ TestDamageReceiver.Died
→ BossDefeated
→ StageFlowController 原子成功提交、StageLevel + 1、返回 SafeHouse
```

Boss 身分只由 `StageRules` 解析。地圖只讀 `StageMapRoute`，目標控制器只讀 `StageObjectiveKind`，HUD 只讀同步的 `ObjectiveKind`／時間狀態；三者都不自行用 StageLevel 或 CycleLength 判斷 Boss。

## 3. 程式完成範圍

- `StageRuntimePlan` 新增 `StageMapRoute`、`StageObjectiveKind` 與 `RequiresExtraction`。
- `StageRules.ResolveTimeLimitSeconds` 集中普通關／Boss 關的限時或 Unlimited 選擇。
- `MapRunSelectionPrototype` 可由 `bossChunkPrefab` 建立專用首 Chunk，停用場景普通首 Chunk後再沿用既有封口、NavMesh、Cartography 與拓撲發布。
- `NetworkMapState` 若 Host 發布的首 Chunk 與場景模板不同，Client 會先驗證 Catalog，再建立同一專用首 Chunk；不自行判斷 Boss。
- `BossSpawnPoint` 是 Boss Chunk 內唯一權威生成錨點。
- `BossStageObjectiveController` 只由 State Authority Spawn Boss，訂閱既有正式生命元件 `TestDamageReceiver.Died`，並同步 Boss Object ID、Objective Ready 與 Boss Defeated。
- `StageFlowController` 對 DefeatBoss 路徑不建立撤離點；Boss 死亡立即走既有唯一成功提交入口，全滅／逾時仍走既有失敗入口。
- `StageHudController` 顯示「擊敗 Boss／Boss 已擊敗」與「不限時」，Boss 路徑隱藏撤離狀態。

## 4. 已完成的 Inspector／Prefab 設定

1. `MapChunk_Boss.prefab` Root 已掛 `MapChunk`／`NavMeshSurface`，且不包含 `NetworkObject`。
2. 已建立四向唯一 `MapConnector`、各自有效的 `PlayerSpawnPoint`，以及剛好一個 `BossSpawnPoint`。
3. 既有模型補上 735 個靜態 MeshCollider；NavMesh 已獨立保存為 `NavMesh-MapChunk_Boss.asset`。
4. Boss Cartography 已保存為 `MapChunk_Boss_Cartography.asset`，尺寸 146×146 且資料有效。
5. `Game` Scene 的 `MapRunSelectionPrototype.Boss Chunk Prefab` 已指定，`NetworkMapState.Catalog` 依序包含普通 Chunk 與 Boss Chunk。
6. `StageFlowController` 同一 Scene NetworkObject 已加入 `BossStageObjectiveController`，NetworkBehaviour 清單共三個並已回填引用。
7. 第一切片依使用者決定暫用 `Enemy_Boss.prefab`；Root 具備 `NetworkObject`、`EnemyActor`、`TestDamageReceiver`，且已位於 Fusion Prefab Table。
8. `Boss Time Limit Seconds` 保持 `0`，即 Unlimited。
9. `MapCartographyData.cells`／`elevations` 保持序列化但從預設 Inspector 隱藏；避免點選 171,810 Cells 的原型 Cartography 時建立龐大屬性樹。

上述資產可由 `Tools/Purgers/Map/Configure, Bake and Wire Phase 4-F Boss` 明確重建；工具只允許在 Edit Mode 且目前沒有已載入 NavMesh 時執行。

## 5. 驗證結果與限制

- 紅燈：新增 Boss Map／Objective／Timing 規格測試後，先確認缺少契約的預期 C# 編譯錯誤。
- 聚焦 `StageRulesTests`：**19／19** 通過，job `1adf75a6a73441b680bd5930ba26cbb5`。
- 完整 `PurgersRegression` EditMode：**131／131** 通過，job `4651f55b009b47c589eacaafb7249c35`。
- 資產契約紅燈：新增 `BossStageAssetTests` 後，Boss Chunk 與 Inspector payload 三項依預期失敗；完成 Prefab 與 Scene 接線契約後 `PurgersRegression` **136／136** 通過，job `a5baadacfc18408a8a64a645315b46b8`。
- `Game` Scene 驗證：0 missing scripts、0 broken prefabs、0 issues。
- 乾淨 Unity 編譯後 Console：0 error／0 warning。
- 資產接線後 Windows Development Build：job `build-332036d2a8`，0 error／0 warning，輸出 `Builds/Phase4FAssets/Purgers.exe`。
- 尚未完成 Play Mode、Host／Client 一致、晚加入與自然擊殺回安全屋，因此仍不可把資產接線寫成完整可玩驗證。

## 6. Runtime 驗證出口

下一步至少驗證：Host 與獨立 Client 都只看到同一 Boss Chunk；Client 不載入 AI NavMesh；Boss 只生成一次；晚加入 Client 收到同一 Boss 與目標狀態；Boss 死亡後不出現撤離要求而直接提交 Level 5；Unlimited 不倒數；全員死亡仍重設循環並返回 SafeHouse。

## 變更紀錄

- 2026-09-22：完成 Phase 4-F 程式切片與 EditMode 回歸；保留資產接線及 Runtime 證據為下一步。
- 2026-09-22：完成 Boss Chunk、NavMesh、Cartography、暫用 Enemy_Boss 與 Game Scene 接線；修正大型 Cartography Inspector 卡頓，136／136 EditMode 通過，Runtime 證據仍待補。
