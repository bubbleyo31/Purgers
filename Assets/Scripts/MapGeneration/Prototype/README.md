# Assets/Scripts/MapGeneration/Prototype 程式導覽

Phase 4-F：`MapRunSelectionPrototype` 不自行判斷 Boss，而是依 `StageRuntimePlan.MapRoute` 將普通場景首 Chunk 換成專用 Boss Chunk，再沿用既有單 Chunk 封口、NavMesh、Cartography 與 NetworkMapState 發布。Client 若收到不同首 Chunk，驗證 Catalog 後才建立權威專用 Chunk。`MapChunk_Boss` 已完成四向 Connector、玩家／Boss 錨點、碰撞、預烘焙 NavMesh、146×146 Cartography 與 Catalog 接線；Boss Chunk 不包含 NetworkObject。Runtime Host／Client 與晚加入仍待驗證。

Phase 4-B：`MapRunSelectionPrototype` 在 Host 依 `StageRules` 要求 Level 1／2／3 的 1／2／3 Chunk，交給 `ConnectorAlignmentPrototype` 建立 Phase 4-A plan 並一次提交完整鏈。它沿用 `MapConnectorBlockerPrototype` 封閉所有非接合 Connector，沿用 `MapRuntimeNavigationPrototype` 逐 Chunk 重掛預烘焙 NavMeshData、建立 N−1 雙向 Link 與每 Chunk 巡邏區；完整根仍掛在 Fusion `[Game]` 下。`NetworkMapState` 讀完整 Chunk／開口清單供 Client 重建幾何與封口，Client 不註冊 AI NavMesh。

Phase 4-C：`ConnectorAlignmentPrototype.FinalChunk` 的 plan 索引由 `NetworkMapState` 發布；`MapRuntimeNavigationPrototype.TryResolveCompleteGroundPath` 使用相同 Agent Type 驗證撤離候選的地面完整路徑。撤離選點仍由 `StageFlowController` 的 State Authority 負責。

現行序列化入口保留；Marker／F9／N 仍僅供開發。Level 4 Boss 程式 route 已建立，但專用 Prefab、獨立 Client／晚加入 Runtime 驗證尚未完成。

Phase 4-E：`StageRules.ResolveRuntimePlan` 先以 `IsBossStage` 分流。預設 CycleLength=4 的 Level 1／2／3／5 分別提交 0／1／2／0 個新增 Chunk；Boss plan 不把三個新增 Chunk 傳入普通拓撲。可調規則若產生超過兩個普通新增 Chunk，會拒絕 Ready。

入口抽選、區塊連接、阻擋屋與預先烘焙導航網格。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [ConnectorAlignmentPrototype.cs](./ConnectorAlignmentPrototype.cs) | ConnectorAlignmentPrototype | Host 多 Chunk plan、生成、全鏈 Connector 對齊、封口與導航提交。 |
| [MapChunk.cs](./MapChunk.cs) | MapChunk | `MapChunk` 的主要實作入口。 |
| [MapConnector.cs](./MapConnector.cs) | ConnectorSide, ConnectorPrototypeRole, MapConnector | 地圖連接器、方向與阻擋屋錨點。 |
| [MapConnectorBlockerPrototype.cs](./MapConnectorBlockerPrototype.cs) | MapConnectorBlockerPrototype | 未使用出口的阻擋屋與導航阻擋。 |
| [MapRunSelectionPrototype.cs](./MapRunSelectionPrototype.cs) | PlacementMode, MapRunSelectionPrototype | 依 Host 存檔等級決定入口與額外區塊數。 |
| [MapRuntimeNavigationPrototype.cs](./MapRuntimeNavigationPrototype.cs) | MapRuntimeNavigationPrototype | 載入每區塊預先烘焙導航網格、N−1 跨區連結與逐區巡邏區。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

