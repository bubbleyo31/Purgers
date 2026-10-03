# Phase 4-A：決定性多 Chunk 拓撲與生成規則核心

> **後續狀態（2026-09-21）**：Phase 4-B 已把本 Planner 接到 Host Level 1～3 Runtime，並泛化既有封口、預烘焙導航與 `NetworkMapState` 完整清單發布；本文件以下內容保留 Phase 4-A 完成當時的邊界。詳見 [149](149_Phase4-B_Host多Chunk生成與導航.md)。

> 最後核對日期：2026-09-21（局部核對）  
> 核對來源：`Assets/Scripts/MapGeneration/MapTopologyPlanner.cs`、`Assets/Scripts/Editor/Tests/MapTopologyPlannerTests.cs`  
> 相關文件：[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md)、[Prototype README](../../Assets/Scripts/MapGeneration/Prototype/README.md)、[回歸狀態](130_回歸驗證與審查狀態.md)

## 1. 本階段完成範圍

Phase 4-A 先建立不依賴 `GameObject`、Collider、Fusion 或 Scene 的純資料規劃器：

- 同一 Seed、起始定義、候選池順序與嘗試上限，產生相同 `Signature`。
- 每個 Chunk placement 有穩定的 `InstanceIndex`、來源 Chunk、定義 ID、整數平面座標與 90 度旋轉。
- 每次延伸從上一塊非入口 Connector 選出口，禁止沿接回上一塊的入口原路退出。
- 候選矩形若與任何已接受 Chunk 有正面積交集便拒絕；只共用邊界可作合法 Connector 接縫。
- 每一塊最多嘗試 `MaxAttemptsPerChunk` 次。用盡後回傳 `RetryLimitReached`，不進入無限生成。
- 只有整份拓撲成功時，才把唯一末端 placement 標記為 `IsFinalChunk` 並公開 `FinalChunkInstanceIndex`；失敗的部分結果不宣稱有最後 Chunk。

目前拓撲是符合 Level 1～3 需求的**線性鏈**，不是分支、環或多樓層圖。幾何使用整數固定單位；未來 Runtime adapter 必須以固定比例把 Prefab Connector／Bounds 量化成相同資料契約，不能讓各 Client 自行用 Physics 查詢決定結果。

## 2. 資料與責任

| 型別 | 責任 |
|---|---|
| `MapChunkTopologyDefinition` | Chunk 穩定 ID、局部矩形 Bounds 與唯一方向 Connector。 |
| `MapTopologyBuildRequest` | Seed、新增 Chunk 數、每塊嘗試上限、起始定義與有序候選池。 |
| `MapTopologyPlanner` | 使用內建穩定 PRNG 建立線性拓撲，執行回頭與重疊限制。 |
| `MapTopologyPlacement` | 單一 instance 的來源、座標、旋轉、入口／出口、世界 Bounds 與最後 Chunk 身分。 |
| `MapTopologyPlan` | 完整／失敗狀態、診斷計數、唯一最後 Chunk 與可比較 Signature。 |
| `MapTopologyRules` | 純方向旋轉、反向、Bounds 轉換與嚴格重疊判定。 |

`StageRules.GetAdditionalChunkCount` 仍是 Cycle 規則 owner；它的結果將由後續 Host adapter 傳入 `MapTopologyBuildRequest`，本階段沒有建立第二套 StageLevel 判斷。

## 3. 與既有 Prototype 的邊界

- `MapRunSelectionPrototype` 的序列化欄位、Host Run Seed 入口及現有 Level 1／雙 Chunk 路徑均保留。
- `ConnectorAlignmentPrototype` 仍只負責現行第二 Chunk 的 Instantiate、Transform 對齊、封口與導航。
- 本階段沒有把 `MapTopologyPlan` 接到上述 MonoBehaviour，也沒有修改任何 Scene、Prefab 或 ScriptableObject。
- 現有 `NetworkMapState` 仍只同步目前已生成的一／兩塊配置；三塊以上 Client 重建尚未完成。
- 最後 Chunk 已有純資料身分，但 `StageFlowController` 尚未改成只從該 Chunk 選撤離點。

因此 6 項 Phase 4 完成條件目前只完成規則核心的一部分，不能宣稱完整 Cycle 地圖已完成。

## 4. 已驗證與後續出口

EditMode 覆蓋：同 Seed 重現、不同 Seed 可產生不同結果、相鄰 Connector 座標／朝向一致、回頭限制、接縫可接觸、正面積重疊拒絕、固定重抽上限、Level 1 與多 Chunk 的唯一最後身分。

下一階段至少需要：

1. 以明確 Editor 烘焙／驗證流程建立 Prefab topology definition，不手寫 YAML。
2. Host 以 `StageRules` 與 Run Seed 建立 plan，再由單一 Runtime owner 實體化全部 placement。
3. 封閉所有未使用 Connector，並把完整 plan 發布給 Client／小地圖重建。
4. 導航與撤離只在完整 plan 成功後啟動；最後 Chunk 的撤離候選需要可達性驗證。
5. 分別驗證 Host、獨立 Client、晚加入／重連與 Level 5 回到零新增 Chunk。

## 變更紀錄

- 2026-09-21：建立 Phase 4-A 純資料拓撲核心與 EditMode 回歸；Runtime、Scene／Prefab、Client 同步及撤離接線留待後續階段。
