# Phase 4-B：Host Level 1～3 多 Chunk 生成與導航

> **後續狀態（2026-09-22）**：Phase 4-D 已把父連線、入口／出口、開口遮罩、最後 Chunk 與撤離選擇納入同一可驗證網路契約，並完成獨立 Host／晚加入 Client 驗證。詳見 [151](151_Phase4-D_NetworkMapState完整拓撲同步.md)。

> **後續狀態（2026-09-22）**：Phase 4-C 已接上最後 Chunk 撤離、地面 NavMesh 可到達性與 Host Seed 選點；本文件保留 Phase 4-B 完成時的邊界。詳見 [150](150_Phase4-C_最後Chunk撤離.md)。

> 最後核對日期：2026-09-21（程式與 EditMode 局部核對）  
> 核對來源：`MapTopologyPlanner`、`MapRunSelectionPrototype`、`ConnectorAlignmentPrototype`、`MapConnectorBlockerPrototype`、`MapRuntimeNavigationPrototype`、`NetworkMapState`  
> 前置文件：[148 Phase 4-A](148_Phase4-A_決定性多Chunk拓撲核心.md)

## 1. 完成範圍

- Host 以既有 Run Seed、已固定的玩家入口／第一個出口與 `StageRules.GetAdditionalChunkCount` 建立 Phase 4-A plan。
- Level 1／2／3 分別提交 1／2／3 Chunk；不另建 StageLevel 規則。
- `ConnectorAlignmentPrototype` 把候選 Prefab 的四個 Connector 局部座標量化成整數 definition，再依 plan 逐塊 Instantiate 與 Connector 對齊。
- 完整鏈對齊成功後，`MapConnectorBlockerPrototype` 才一次封閉所有非接合 Connector。玩家出生入口不是 Chunk 接縫，維持封閉。
- `MapRuntimeNavigationPrototype` 對每個 Chunk 重掛自己的預烘焙 `NavMeshData`，對 N 個 Chunk 建立 N−1 條雙向 Link，並為每個 Chunk 建立一個可連通地面巡邏區；沒有 Runtime Bake，也沒有 `NavMeshAgent`。
- `RuntimeGeneratedMapRoot` 以起始 Chunk 原父層為父節點，因此 Fusion Multiple-Peer 下仍由 `[Game]` 生命週期清除。
- `NetworkMapState` 發布完整 Host Chunk／Transform／開口遮罩清單；Client 沿用既有 Catalog 驗證、幾何重建與封口，不執行 AI 導航。

## 2. 權威與失敗策略

| 階段 | Owner | 失敗行為 |
|---|---|---|
| Stage／Seed／入口 | `MapRunSelectionPrototype`（Host） | 不建立第二套關卡或 Seed 規則。 |
| 拓撲 | `MapTopologyPlanner`（純資料） | 達每 Chunk 重抽上限便拒絕整份 plan。 |
| Runtime 幾何／封口 | `ConnectorAlignmentPrototype`（Host） | 對齊或封口失敗不提交導航，`IsMapPreparationReady` 維持 false。 |
| NavMesh／Link／Patrol | `MapRuntimeNavigationPrototype`（Host） | 任一 Chunk 缺少唯一預烘焙 Surface、任一 Link 或巡邏區失敗，整份地圖不 Ready。 |
| Client 幾何 | `NetworkMapState` | 只接受 Host 發布資料與相同 Catalog／Cartography 版本；不自行抽 Seed。 |

`MapTopologyPlan.FinalChunk` 已保留給後續撤離接線，但本階段沒有改動 `StageFlowController`，不能宣稱撤離已限制在最後 Chunk。

## 3. Inspector 手動接線順序

本次未自動修改 Scene、Prefab 或 ScriptableObject。請在 Unity 依下列順序人工檢查；已存在的引用不必重建。

1. 開啟 `Game` Scene，找到既有 `ConnectorAlignmentPrototype`。
2. `Fixed Chunk` 保持指向 Scene 內起始 `MapChunk`；不要改指 Prefab 資產。
3. `Moving Chunk Prefab` 保留目前回退 Prefab。
4. `Host Moving Chunk Prefabs` 依 Host／Client 共用 Catalog 的穩定順序加入 Level 2～3 可用 Prefab；不要放 Scene instance、Null 或重複項目。
5. 每個候選 Prefab 必須有四個唯一方向 `MapConnector`，Connector 位於矩形邊界、Root Scale 為 1，且每個 Prefab 恰有一個已 Bake 的 `NavMeshSurface/NavMeshData`。
6. `Connector Blockers` 指向既有 `MapConnectorBlockerPrototype`；其 Blocker Prefab 不得包含 `NetworkObject`，Carving 設定沿用現值。
7. `Runtime Navigation` 指向既有 `MapRuntimeNavigationPrototype`；Agent Type 必須與所有 Chunk 預烘焙資料一致。
8. `Topology Units Per Meter` 先維持 `1000`；所有 Host 使用同一程式值。`Max Topology Attempts Per Chunk` 先維持 `32`。
9. 回到 `MapRunSelectionPrototype`，`Next Chunk Alignment` 指向上述元件，`Spawn Second Chunk On Host` 必須啟用。欄位名稱為序列化相容而保留，實際語意已是「生成完整 StageRules Chunk 鏈」。
10. `NetworkMapState.Selection` 保持指向上述 `MapRunSelectionPrototype`；`Catalog` 必須涵蓋起始 Chunk 與所有候選 Prefab的 Cartography，Host／Client 順序及 Content Version 必須完全相同。
11. 儲存 Scene／Prefab 後檢查 Console，確認沒有 Missing Script、重複方向 Connector 或缺 NavMeshData。

## 4. Play Mode 驗證

1. 以正常 Menu → SafeHouse → Game 流程啟動 Host，不使用離線 N 鍵替代。
2. Level 1：確認 `[Game]/RuntimeGeneratedMapRoot` 下只有 1 個 Chunk、0 條跨區 Link、4 個封口與 1 個 Runtime Ground Patrol Area。
3. Level 2：確認 2 個 Chunk、1 條 Link、6 個封口與 2 個 Patrol Area。
4. Level 3：確認 3 個 Chunk、2 條 Link、8 個封口與 3 個 Patrol Area；Hierarchy 不得出現額外頂層 Runtime 地圖物件。
5. 每級確認 `MapTopologyRuntime` log 的 Chunk Count、Final Chunk 與 Signature；同 Seed 應重現相同 Signature。
6. 讓地面 Enemy 跨每一條接縫巡邏／追逐，確認使用既有 Fusion Tick 移動且沒有 `NavMeshAgent`。
7. 連入獨立 Windows Client，確認 Chunk 數、Transform、封口與小地圖一致；Client 上的 Chunk `NavMeshSurface` 應停用。
8. 晚加入／重連後再確認 LayoutRevision、完整 Chunk 清單與探索快照；最後從 Game 回 SafeHouse，確認 `[Game]`、Runtime root、Chunk、封口、Link 與巡邏區都被清除。

## 5. 已知限制

- 只涵蓋 Level 1～3 線性鏈；沒有分支、環、多樓層或 Level 4 Boss 地圖。
- Prefab topology definition 目前由四個 Connector 矩形量化，不是獨立烘焙資產；非矩形 Chunk 需要下一階段明確資料契約。
- 最後 Chunk 只有 plan 身分，撤離候選與可達性尚未接線。
- 本文件的 Host／Client Play Mode 清單仍需人工執行；EditMode 通過不能替代多人證據。

## 變更紀錄

- 2026-09-21：完成 Host Level 1～3 plan、完整鏈生成／封口、N Chunk 預烘焙導航、完整配置發布與手動接線／驗證清單；未修改 Scene／Prefab。
