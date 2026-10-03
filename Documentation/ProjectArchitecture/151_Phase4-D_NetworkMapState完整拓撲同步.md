# Phase 4-D：NetworkMapState 完整多 Chunk 拓撲同步

> 最後核對日期：2026-09-22（程式、Unity Test Runner、Windows Development Player）  
> 核對來源：`NetworkMapState`、`MapLayoutRules`、`StageFlowController`、`Phase4DNetworkVerification`、`NetworkMapTopologyRulesTests`  
> 前置文件：[149 Phase 4-B](149_Phase4-B_Host多Chunk生成與導航.md)、[150 Phase 4-C](150_Phase4-C_最後Chunk撤離.md)

## 1. 完成範圍

- `MapChunkPlacement` 除 Catalog／版本／Pose／開口遮罩外，現在也發布 `ConnectedFromIndex`、`EntrySide`、`ExitSide`。Client 不再只拿一串 Transform 猜測線性拓撲。
- `MapLayoutRules` 在 Host 寫入 NetworkArray 前、Client Instantiate 前，統一驗證 Chunk 數、父連線、入口／出口、唯一末端與 `OpenSides`。任何部分拓撲都不會進入 Ready。
- `FinalChunkIndex` 與 `SelectedExtractionIndex` 同由 `NetworkMapState` 發布。`StageFlowController` 仍是撤離候選、NavMesh 可達性與 Seed 抽選 owner，但不再保存第二份 Networked 撤離結果。
- 晚加入 Client 收到 `LayoutRevision` 與完整配置後，驗證整份 Catalog／Cartography／拓撲，再一次重建所有 Chunk、Collider 與封口；它不執行 Planner、Seed 抽選或 AI NavMesh 建置。
- 容量仍為 16 Chunk，且本階段驗證的是 Phase 4-A～C 的線性鏈；沒有假稱支援分支、環或多樓層。

## 2. 權威資料流

```text
Host MapTopologyPlanner
→ ConnectorAlignmentPrototype 生成／對齊完整鏈
→ Host 封口 + 預烘焙 NavMesh + N-1 Link + 每 Chunk Patrol Area
→ NetworkMapState 驗證並發布完整 MapChunkPlacement[] + FinalChunkIndex
→ StageFlowController 只在 Host 驗證撤離 NavMesh 並以 Run Seed 選點
→ NetworkMapState.SelectedExtractionIndex 發布結果
→ Client 驗證完整封包後重建 Chunk／Collider／Cartography／封口
```

Client 的 `MapRuntimeNavigationPrototype.IsReady` 必須保持 false、`GeneratedGroundAreas` 必須為 0，且 Replica 下所有 `NavMeshSurface` 會 `RemoveData()` 並停用。這是權威隔離，不只是效能選項。

## 3. 失敗策略

- `ConnectedFromIndex` 不連續、入口／出口超界、非末端缺出口、末端仍有出口、入口原路退出、`OpenSides` 不吻合或 `FinalChunkIndex` 不是唯一尾端：Host 拒絕發布，Client 拒絕 Instantiate。
- Catalog 索引、Cartography 版本或起始 Chunk 不一致：Client 不進入 Ready。
- State Authority 尚未完成地圖或撤離索引超出候選範圍：`TryPublishExtractionSelection` 失敗，Stage 保持 `Initializing`。
- Client 不因本地可達性、Physics 結果或畫面位置改寫最後 Chunk／撤離選擇。

## 4. 驗證結果

- 紅燈：先加入 `NetworkMapTopologyRulesTests`，Unity 得到 8 個預期編譯錯誤（缺 `MapLayoutRules` 與三個新欄位）。
- 聚焦 EditMode：`NetworkMapTopologyRulesTests` **9／9** 通過，job `6355a3d2669b44458e7967258287faca`。
- 完整 `PurgersRegression`：**118／118** 通過，job `87ea5cb8edf94a0c80cfc5e9396a0e2a`。
- Windows Development Build：`build-996a7ed53f`，**0 errors／0 warnings**，輸出 `Builds/Phase4D/Purgers.exe`。
- 獨立 Player：兩個不同 Windows 程序，Host 先啟動 Level 3，Client 延遲 20 秒加入。兩端比對相同的 3 Chunk 拓撲快照、`FinalChunkIndex = 2`、撤離索引／`PointId`、Pose、Catalog／Cartography 版本與開口遮罩。
- Host 驗證三個 Runtime Ground Patrol Area、導航 Ready 與每 Chunk NavMeshSurface；Client 驗證每 Chunk 有有效 Collider／Cartography、8 個封口，且導航未 Ready、0 Patrol Area、所有 NavMeshSurface 停用。
- 最終有圖形裝置的 Host／Client Player log 搜尋 `FAIL`、Exception、Null／Missing Reference、Assertion 與 Error 均無命中。

證據位於 [Validation/Phase4-D/Graphical](Validation/Phase4-D/Graphical)：`Host.log`、`Client.log`、`Host.snapshot` 與兩端完整 Player log。

## 5. 邊界

- 這次證明晚加入時的完整拓撲、封口、Cartography、Collider、最後 Chunk、撤離選擇與導航權威分離；未把測試瞬移或資料探針當成自然跑圖手感證據。
- 尚未驗證斷線後以新 PlayerRef 重連時的個人探索保存；既有設計仍把它視為新個人探索。
- Level 4 Boss 地圖、分支／環、多樓層與執行中增量增刪 Chunk 不在此契約內。

## 變更紀錄

- 2026-09-22：完成 Phase 4-D 拓撲契約、撤離結果集中發布、回歸測試與獨立 Host／晚加入 Client 驗證。
