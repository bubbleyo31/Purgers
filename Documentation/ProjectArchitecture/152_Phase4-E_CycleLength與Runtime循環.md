# Phase 4-E：可調 CycleLength 與 Level 1／2／3／5 Runtime 循環

> 最後核對日期：2026-09-22（程式、Unity Test Runner、Windows Development Player）  
> 核對來源：`StageRules`、`MapRunSelectionPrototype`、`StageFlowController`、`SafeHouseFlowController`、兩側 HUD、存檔建立流程與 `Phase4ENetworkVerification`  
> 前置文件：[151 Phase 4-D](151_Phase4-D_NetworkMapState完整拓撲同步.md)、[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md)

## 1. 完成範圍

- `_Menu` 的 `MenuConnectionBehaviour.New Save Cycle Length` 成為新存檔規則入口。Quick Play／Fresh Host 建檔時一次寫入 `CycleLengthSnapshot`；Continue 保留原存檔快照，不被目前 Inspector 值改寫。
- `StageRules.ResolveRuntimePlan` 統一正規化 StageLevel／CycleLength、CycleStage、一般新增 Chunk 數與 Runtime route。
- `StageRules.IsBossStage` 仍是 Boss 唯一判定入口。Boss plan 的 `AdditionalChunkCount` 強制為 0，禁止 Level 4（預設 CycleLength=4）落入普通三個新增 Chunk 流程。
- 目前普通 Runtime 明確支援最多兩個新增 Chunk：預設 CycleLength=4 時，Level 1／2／3／5 分別為 1／2／3／1 個總 Chunk。可調規則若導出三個以上的普通新增 Chunk，回傳 `UnsupportedOrdinary` 並停止地圖 Ready，不會截斷、回退或假裝成功。
- `StageFlowController` 與 `SafeHouseFlowController` 由 State Authority 發布同一輪 `StageLevel`、`CycleLength`、`CycleStage` 與 Boss 身分。Client／HUD 只讀同步值。
- Game 與 SafeHouse 的左上標題共用 `StageHudText`，一般關顯示「關卡等級／循環」，Boss 入口顯示「Boss 關」。
- `StageHUD` 已從 `MapChunk_Prototype` 抽成獨立 UI Prefab，Game Scene 只持有一份；動態生成多少 Chunk 都不會複製 Canvas。Game／SafeHouse HUD 以 Runner＋StageLevel 互斥接管，離場時清空並銷毀舊關卡 UI。

## 2. 權威與持久化

```text
MenuConnectionBehaviour.newSaveCycleLength
→ JsonGameSaveRepository.CreateNew(..., cycleLength)
→ GameSaveData.CycleLengthSnapshot（新存檔固定）
→ Host MapRunSelectionPrototype / StageFlow / SafeHouseFlow
→ StageRules.ResolveRuntimePlan
→ Networked StageLevel + CycleLength + CycleStage + IsBossStage
→ Client HUD / Runtime 驗證只讀
```

- Host 存檔是 CycleLength 的持久化來源；Client 的 `GameSaveRuntimeContext` 仍為 ReadOnly 且沒有 `ActiveSave`。
- `StageFlowController.fallbackCycleLength` 只處理沒有 Host 存檔 Context 的診斷回退；欄位以 `FormerlySerializedAs` 保留原序列化資料。
- 成功關卡只遞增 `RunProgression.StageLevel`，不改寫 `CycleLengthSnapshot`；失敗重設循環進度同樣保留快照。

## 3. Boss 與未支援規則的失敗策略

- 預設 CycleLength=4 的 Level 4 回傳 `StageRuntimeKind.Boss`，一般 Chunk 數為 0；目前 Boss 地圖、Boss 勝利條件、計時、撤離與獎勵仍未實作，因此 Stage 不會進入 Active。
- CycleLength 可以調整，但不是任意內容擴充器。例：CycleLength=5 的 Level 4 是普通 CycleStage 4，現有線性 Runtime 需要三個新增 Chunk，故回傳 `UnsupportedOrdinary` 並 fail-closed；不可默默少生成一塊。
- 後續 Boss 實作必須接 `IsBossStage` route，而不是放寬普通拓撲上限來繞過特殊關。

## 4. Inspector

CycleLength 的 Inspector 調整方式如下；後續 HUD 生命週期修正另修改了 Game Scene、MapChunk 與 StageHUD Prefab：

1. 開啟 `_Menu` Scene。
2. 找到 `MenuConnectionBehaviour`。
3. 修改 `New Save Cycle Length`；預設與目前完整 Runtime 驗證值為 4。
4. 只有之後建立的新存檔會採用新值；既有 Continue 存檔不變。
5. 若值會產生普通 CycleStage 4 以上，先完成對應 Runtime 能力；目前會刻意拒絕進場。

## 5. 驗證結果

- 紅燈：先加入 `StageRuntimeKind`／`StageRuntimePlan`、可調建檔與 HUD 測試，Unity 得到預期缺型別編譯錯誤。
- 聚焦 EditMode：`StageRulesTests`、`GameSaveRepositoryTests`、`HudLayoutTests` 共 **41／41** 通過，job `6fbf478c0fde49bcb628ef196f15a81c`。
- 最終完整 `PurgersRegression`：**129／129** 通過，job `001ad8ecc65d4368a909d423825e9628`。
- 最終 Windows Development Build：`build-6a35c67727`，**0 errors／0 warnings**，輸出 `Builds/Phase4E/Purgers.exe`。
- 獨立 Host／Client，CycleLength=4：
  - Level 1：兩端 1 Chunk、Cycle 1/4、HUD 與撤離結果一致。
  - Host 成功提交後：兩端返回 SafeHouse Level 2；Host 記憶與磁碟存檔皆為 Level 2，CycleLengthSnapshot 仍為 4；Client 保持 ReadOnly／無 ActiveSave。
  - SafeHouse 重新進場 Level 2：兩端 2 Chunk、Cycle 2/4、HUD 與拓撲一致。
  - Level 3：兩端 3 Chunk、Cycle 3/4、HUD 與拓撲一致。
  - Level 5：兩端回到 1 Chunk、Cycle 1/4、HUD 與拓撲一致。
  - 每組 Client 均沒有 AI NavMesh／巡邏區權威。

Runtime 證據與 Snapshot 位於 [Validation/Phase4-E/Runtime](Validation/Phase4-E/Runtime)。驗證探針以反射呼叫既有 private 成功提交與切場方法，只用來縮短自動化時間；它沒有取代自然走入撤離區的操作手感驗證。

## 6. 已知限制

- Level 4 Boss 只有清楚且不可誤入普通地圖的入口，Boss 內容仍未完成。
- 尚未證明 CycleLength 不是 4 時的完整內容矩陣；本階段只證明設定能原子寫入新存檔、權威傳遞與不支援配置 fail-closed。
- 本輪沒有修改 Scene／Prefab；既有 `_Menu` 元件會使用新欄位預設值 4，製作者若要調整需依 Inspector 步驟操作。

## 變更紀錄

- 2026-09-22：修正 StageHUD 被包進 MapChunk 而隨多 Chunk 重複生成；改為 Game Scene 單一 root，並加入 Runner／StageLevel 接管與離場清理。
- 2026-09-22：完成可調新存檔 CycleLength、Boss／普通 Runtime route、Level 1／2／3／5、成功提交、SafeHouse 往返、HUD 與獨立 Host／Client 驗證。
