# Phase 4-C：最後 Chunk 撤離與地面 NavMesh 可到達性

> **後續狀態（2026-09-22）**：Phase 4-D 已把撤離結果集中到 `NetworkMapState.SelectedExtractionIndex`，並以獨立 Host／延遲晚加入 Client 驗證最後 Chunk 與選擇一致；本文件保留 Phase 4-C 決策邊界。詳見 [151](151_Phase4-D_NetworkMapState完整拓撲同步.md)。

> 最後核對日期：2026-09-22（程式與 MSBuild 局部核對）  
> 核對來源：`StageFlowController`、`StageExtractionPoint`、`StageRules`、`NetworkMapState`、`ConnectorAlignmentPrototype`、`MapRuntimeNavigationPrototype`、`StageRulesTests`  
> 前置文件：[148 Phase 4-A](148_Phase4-A_決定性多Chunk拓撲核心.md)、[149 Phase 4-B](149_Phase4-B_Host多Chunk生成與導航.md)

## 1. 完成範圍

- Host 將完整拓撲的 `FinalChunkInstanceIndex` 寫入 `NetworkMapState.FinalChunkIndex`，Client 只接受同步值。
- `StageFlowController` 只把 `FinalChunkIndex` 所指 Chunk 底下的 `StageExtractionPoint` 納入候選。
- 現有 Game Scene 的四個撤離點仍是起始 Chunk 實例子物件；若最後 Chunk 不是起始 Chunk，各 peer 會以相對起始 Chunk 的局部位置／旋轉把這些模板投影到 Host 發布的最後 Chunk。沒有手改 Scene 或 Prefab YAML。
- State Authority 從本輪入口位置取樣 NavMesh，逐一要求到候選點的 `NavMeshPathStatus.PathComplete`。
- 只有通過地面路徑的候選能進入本輪 Seed 抽選。提交的是「完整、穩定排序候選清單」中的索引，Client 不重做可到達性篩選。
- 小地圖繼續只讀 `StageFlowController.TryGetSelectedExtraction`；不自行判定最後 Chunk、NavMesh 或 Seed。

## 2. 權威與資料流

| 階段 | Owner | 契約 |
|---|---|---|
| 最後 Chunk 身分 | Host `MapTopologyPlan` → `NetworkMapState` | 只在完整 plan 後發布有效索引。 |
| 撤離模板綁定 | 各 peer 的 `StageFlowController` | 依 Host 發布索引做 Transform 投影，不產生 gameplay 結果。 |
| 地面可到達性 | State Authority `StageFlowController` → `MapRuntimeNavigationPrototype` | 使用導航 owner 的 Agent Type、NavMesh 取樣與完整路徑。 |
| Seed 選點 | State Authority `StageRules` | 使用本輪 `ActiveRunSeed`，只從可達候選索引抽選。 |
| 結果同步 | `SelectedExtractionIndex` | Client／HUD／小地圖只讀。 |

Client 不呼叫撤離路徑查詢，不以 Physics、Chunk 順序或畫面位置猜測結果。鈎索與跳躍不屬於本階段可到達性模型。

## 3. 失敗策略

- 拓撲不完整、最後 Chunk 索引越界或 Client 收到無效索引：`NetworkMapState` 不進入 Ready。
- 最後 Chunk 沒有啟用候選、無法取得 Host 入口／導航 owner，或所有候選都沒有完整地面路徑：Stage 保持 `Initializing`，不啟動 Timer，也不回退到其他 Chunk。
- 候選依 `PointId` 與 hierarchy path 穩定排序；Seed 選點回傳完整清單索引，避免 Host 可達清單與 Client 呈現清單索引錯位。

## 4. 資產與 Inspector

- 本次沒有新增 Scene／Prefab 必填引用，也沒有手改 YAML。
- `StageFlowController.Extraction Nav Mesh Sample Distance` 預設 8 公尺；只影響入口與候選投影到 NavMesh 的容許距離，不放寬路徑完整性。
- 現有四個撤離點必須繼續位於起始 `MapChunk` 階層下，且 `PointId` 不可重複。
- 若未來把撤離點正式烘進每個 Chunk Prefab，最後 Chunk 原生候選優先；起始 Chunk 場景模板不會再被搬移。

## 5. 驗證狀態與手動清單

已完成：

- 先建立缺少新規則 API 的紅燈編譯，再補最小實作。
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore`：成功，0 errors；1 個既有 `JsonGameSaveRepository` CS0649 warning。
- `StageRulesTests` 新增最後 Chunk 候選資格、同 Seed 重現、只回傳 eligible index 與空集合失敗案例；目前僅確認測試程式可編譯。
- `dotnet test` 沒有回報任何測試數；依專案規則不算測試執行，不能替代 Unity Test Runner。
- `dotnet test` 沒有回報任何測試數；依專案規則不算測試執行，不能替代 Unity Test Runner。

尚未宣稱完成：

1. 等 Unity 完成 Fusion codegen／編譯，Console 確認沒有本次新增錯誤或警告。
2. 執行聚焦 `StageRulesTests`，確認實際測試數非零且全通過。
3. 執行完整 `PurgersRegression` EditMode suite，確認實際測試數非零且零失敗。
4. Host Level 1／2／3：確認 log 的 `FinalChunkIndex` 分別對應唯一末端 Chunk，撤離視覺位於該 Chunk，且 `ReachableCandidates` 大於零。
5. 暫時破壞最後 Chunk 候選的 NavMesh 路徑，確認 Stage 留在 `Initializing`，不回退到前一 Chunk。
6. 獨立 Windows Client 與晚加入／重連：確認相同撤離點與小地圖綠點，Client Console 不出現撤離 NavMesh／Seed 抽選行為。
7. 自然走入撤離點並完成 3 秒全員撤離，確認既有成功提交與回安全屋流程不變。

## 變更紀錄

- 2026-09-22：完成 Phase 4-C 程式接線與編譯層驗證；Unity Test Runner 與 Host／Client Runtime 證據待補。
