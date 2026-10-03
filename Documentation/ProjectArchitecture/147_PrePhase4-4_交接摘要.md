# Phase 4 前置-4 小地圖與戰爭迷霧：交接摘要

> 交接日期：2026-09-21。使用者確認「功能還不錯，也不卡了」，本功能暫告一段落，後續依新需求調整。
> 本次只整理文件與核對原始碼，未修改功能、未重跑 Unity 測試。下列執行驗證來自 2026-09-19 最後完成的版本。
> 專案：M:/UnityProject/Purgers。完整契約與歷次證據：[147 功能文件](147_PrePhase4-4_小地圖與戰爭迷霧.md)。

## 1. 已完成行為與不可誤解的需求

- 顯示範圍內的已生成地形／道路預設可見；迷霧限制的是敵人、撤離等內容資訊，不把未探索道路整片塗黑。
- 玩家飛行也能探索下方：依水平半徑與實際地形視線判定，不要求接地。玩家上方表面仍有高度容許限制。
- Host 可切換個人／全隊共享探索；共享是聯集讀取，切回個人不會保留隊友探索。
- 北方朝上、玩家置中；M 展開只拉遠附近視野，不自由平移，也不增加探索／目標發現距離。
- 玩家黃標、敵人紅標、選定且可用的撤離點綠標；目標仍需通過探索、個人接近與遮蔽條件。
- 底圖採近黑背景／封口、亮色牆體障礙、依高度分段的中間灰階地面。已探索區只有淡青綠色調，不蓋掉高度對比。
- 效能版本使用每 Chunk 貼圖快取、變更才上傳探索、封口空間索引、獨立 UI 標記。不要改回每次逐像素掃描整張視窗。

## 2. 本功能新增／修改檔案

以下路徑相對專案根目錄。此清單依本功能實作範圍整理，不能把目前整份 git diff 都歸入 Phase 4 前置-4；工作樹另有其他階段的未提交修改。

### 新增程式

| 檔案 | 責任 |
| --- | --- |
| `Assets/Scripts/MapGeneration/Minimap/MapCartographyData.cs` | 每 Chunk 局部格網：種類、高度、格距、範圍與內容版本。 |
| `Assets/Scripts/MapGeneration/Minimap/NetworkMapState.cs` | Host 配置發布／探索，Client 配置套用，差量／快照與共享模式。 |
| `Assets/Scripts/MapGeneration/Minimap/ExplorationStore.cs` | 個人位元歷史、共享聯集、單調 OR 合併、呈現 Changed 通知。 |
| `Assets/Scripts/MapGeneration/Minimap/MinimapRules.cs` | 高度色階、座標投影、探索高度與標記規則。現行配色入口是 HeightColor；TerrainColor 是早期保留函式，不是目前 UI 的配色來源。 |
| `Assets/Scripts/MapGeneration/Minimap/MinimapTileCache.cs` | 原生格網地形／探索貼圖、分批建立、dirty 上傳與釋放。 |
| `Assets/Scripts/MapGeneration/Minimap/MinimapBlockerIndex.cs` | 16 公尺 XZ 空間桶；最後保留精確 Bounds.Contains。 |
| `Assets/Scripts/UI/Player/LocalMinimapController.cs` | 裁切、縮放、探索呈現、玩家／敵人／撤離標記與資源生命週期。 |
| `Assets/Scripts/Editor/MapCartographySetup.cs` | 明確執行的底圖烘焙／Game 接線工具；不在 Player 內執行。 |
| `Assets/Scripts/Editor/Tests/MinimapRulesTests.cs` | 規則、分享、快照、座標、快取、dirty 通知與封口索引測試。 |
| `Assets/Scripts/Tool/Testing/Phase44NetworkVerification.cs` | 明確 opt-in 的 Host／Client 測試，使用 Build 下的隔離存檔。 |

新增檔案與目錄有 Unity 產生的 `.meta`；一併保留。

### 修改既有程式與資產

- `Assets/Scripts/MapGeneration/Prototype/MapChunk.cs`：新增 Cartography 引用。
- 同目錄 `MapRunSelectionPrototype.cs`：公開起始 Chunk／Alignment，正式出生位置流程不再受開發模式門檻阻擋。
- 同目錄 `ConnectorAlignmentPrototype.cs`：公開實際生成 Prefab、區塊與開口資料供配置發布使用。
- 同目錄 `MapConnectorBlockerPrototype.cs`：提供封口清單與依權威配置重建封口的入口。
- 同目錄 `MapRuntimeNavigationPrototype.cs`：正式單／雙塊導航準備入口不再受開發模式門檻阻擋；仍用既有預烘焙 NavMesh。
- `Assets/Scripts/GameFlow/Stage/StageFlowController.cs`：等待本端 MapState 就緒；撤離候選限制同 Scene。
- `Assets/Prefabs/Map/MapChunk_Prototype_Cartography.asset`：新增底圖資料。
- `Assets/Prefabs/Map/MapChunk_Prototype.prefab`：接上 Cartography 引用。
- `Assets/Scenes/Game.unity`：StageFlow 加入 NetworkMapState；既有 StageHUD 加入 LocalMinimapController 與引用。場景內其他既有變動不全屬本功能。
- `MapConnector.cs` 是既有連接器來源；目前工作樹雖有修改，不應據此歸為本功能新增修改。

同步文件包括架構 `00`、`01`、`02`、`90`、`100`、`110`、`130`、`140`、`146`、`147`、文件索引，以及 Minimap／MapGeneration／Prototype／Stage／UI Player／Tool Testing 的相關 README。證據集中在 `Documentation/ProjectArchitecture/Validation/Phase4-4/`。

## 3. 資料與網路責任

| 責任方 | 擁有的資料／工作 |
| --- | --- |
| Editor 烘焙 | 在隔離預覽場景讀 Collider 與既有預烘焙 NavMesh，生成每 Chunk Cartography。不在 Runtime BuildNavMesh，不新增 NavMeshAgent。 |
| 原生成系統 | MapRunSelectionPrototype／ConnectorAlignmentPrototype 決定實際生成結果；小地圖不另外抽 Seed 或生成另一份配置。 |
| Host／State Authority | 發布 Catalog 索引、內容版本、世界位置／旋轉、開口；每 0.1 秒由存活玩家位置更新探索；決定共享模式。 |
| Client | 驗證 Catalog／版本，套用相同 Chunk 與封口，停用自己複本的 NavMeshSurface 註冊；不接管 AI 導航。 |
| 探索同步 | 依 PlayerRef＋Chunk 索引＋word 保存。可靠 RPC 傳非零 word 差量，晚加入傳快照；OR 合併避免舊快照覆蓋新差量。 |
| 本機 UI | 讀自身或共享聯集；地形快取不因移動／展開重建。Changed 通知不消耗網路 dirty 集合。 |
| 玩家／敵人／撤離來源 | 玩家來自 Runner 的本機 PlayerObject／Transform，生命由 PlayerHealth；敵人讀同 Runner 的 EnemyActor.IsFusionSpawned、有效 Object、IsAlive 與同步 Transform；撤離讀 StageFlow 的選定 StageExtractionPoint。 |

網路欄位僅在有效 Spawned 生命週期使用。敵人標記沒有新增另一套位置 RPC 或敵人權威。每 0.5 秒更新敵人候選，0.1 秒檢查可見性，已顯示位置每幀跟隨；死亡／Despawn 清除圖示，不保留敵人最後位置。

## 4. Inspector／場景設定

| 位置 | 設定／契約 |
| --- | --- |
| Game 的 StageFlow NetworkObject | NetworkMapState 綁既有 selection；catalog 目前含 MapChunk_Prototype，各端順序／版本需一致。 |
| NetworkMapState | Share Team Exploration 只有 Host 生效，可在 Play Mode 切換；Exploration Radius 預設 22 m；Vertical Tolerance 預設 6 m，僅限制玩家上方表面。 |
| MapChunk Prefab | Cartography 必須有效；目前要求世界 Scale 為 1。地形更動需重新烘焙。 |
| Game 的 StageHUD | LocalMinimapController 綁 map、compactContent、placeholder。內容位於 `TopLeftColumn/MinimapSlot/MinimapContent`。 |
| LocalMinimapController | 一般／展開世界寬度預設 140／360 m；M 切換；Marker Discovery Radius 預設 30 m。 |
| 貼圖 | 舊 textureResolution 保留序列化但隱藏且停用；不用再調它換 FPS。精度由 Cartography.CellSize 決定，目前 1.5 m。 |

上述數值是程式預設，Inspector 序列化值仍可能由使用者調整。UI 容器與貼圖於 Runtime 建立，不需手動放置紅／綠標記。SafeHouse 目前保留原佔位。

必要時使用 `Tools/Purgers/Map/Bake Prototype Cartography`：先到沒有已載入 NavMesh 的 _Menu，並處於 Edit Mode。`Tools/Purgers/Map/Wire Phase 4 前置-4 Game HUD` 是明確的接線／遷移工具，現有 Game 已接線，普通試玩不需反覆執行。

## 5. 驗證結果與證據

最近一次自動／雙程序驗證為 **2026-09-19**，本次交接未重跑：

- 聚焦測試 **17／17**；完整 PurgersRegression **92／92**。最終 job：`c6c9753b77f146beac970115fceacf23`。
- Windows Development Build：`build-94fae53e83`，**0 errors／0 warnings**。
- 初版曾通過單 Chunk／四封口與雙 Chunk／六封口；最後效能版重跑的是雙 Chunk Host＋Windows Client，不把早期單塊結果寫成最後版本重跑。
- 最後雙程序驗證：晚加入快照、配置一致、Host 共享切換、Client 無權改模式、遠方撤離隱藏／Host 靠近顯示，均通過。
- UI 個人→共享→個人格數：**1572→2237→1572**。
- Host 紅點：附近 1、位置移動後 anchor 更新、Despawn 後 0。高空探索另曾用實際位置上方 80 m 呼叫相同 Host 探索入口驗證。
- 最後 Console 無 Error／Warning；Client log 無 Exception／Error／FAIL。測試使用隔離存檔並已結束，未改正式存檔。
- 2026-09-21 使用者回饋功能可用且不再卡頓；這是使用者試玩回饋，與自動測試結果分開記錄。

效能量測（同一 Editor 雙 Chunk 場景，非所有情境 FPS 保證）：

| 項目 | 結果 |
| --- | --- |
| 舊逐像素 Draw | 64² 約 27.62 ms；256² 約 391.61 ms。 |
| 快取後 UI UpdateView | 約 0.008 ms；切換縮放約 0.12 ms，不重建地形。 |
| 封口索引後每批 8192 格 | 最高約 1.45 ms；先前約 57.63 ms。單 Chunk 初始化另約 4.94 ms。 |
| 已探索位置的 Host 探索 | 約 0.75 ms。 |
| 小地圖開／關短時間幀取樣 | 約 11.98／12.00 ms；不含完整場景長時間 Profiler 驗收。 |

證據：[展開圖](Validation/Phase4-4/Cached-Expanded.png)、[一般圖](Validation/Phase4-4/Cached-Compact.png)、[Host](Validation/Phase4-4/Cached-Host.log)、[Client](Validation/Phase4-4/Cached-Client.log)、[索引量測](Validation/Phase4-4/Indexed-ms.txt)、[幀取樣](Validation/Phase4-4/FrameSample.txt)。

## 6. 已知限制與待驗項目

- 單層投影，不支援重疊樓層。NavMesh 可走屋頂仍算高處地面，沒有靠名稱猜建築／屋頂語意；可通行資料也不代表所有鈎索／跳躍可達面。
- 高度色階以每塊最低有效地形為基準且有亮度上限；極高區域可能共用最高色階，不是每個高度都有唯一顏色。不同類型 Chunk 的跨塊高度配色一致性尚未擴充。
- 生成 owner 仍只產生一或兩塊。配置容量 16 不代表已完成三塊以上生成；同關中途追加／卸載 Chunk 尚無版本化更新流程。
- 動態門、破壞地形與移動封口尚未接入快取失效。不要只修改 Renderer 而忘記探索／封口資料更新。
- 獎勵、道具、任務物件與特殊裝置標記尚未接入；最終 Chunk 撤離策略也尚未完成。
- 探索不跨 Session 存檔；重連拿到新 PlayerRef 視為新個人歷史。各端目前收到個人探索資料，隱藏規則是 UI 層，不是防修改 Client 的保密機制。
- 獨立 Client 的敵人移動／死亡視覺驗收仍待完成；不能把 Hidden Client 黑畫面截圖當成視覺通過。自然飛行／鈎索手感及大量敵人情境尚無完整自動驗收。
- TerrainPixelCount／ExploredPixelCount 是保留的診斷名稱，現在統計快取格子，不是視窗像素。使用 Profiler 的 `Purgers.Minimap.View`／`BuildCache`、LastViewMilliseconds、TerrainCellsBuiltLastFrame 識別效能問題。

## 7. 下一個功能的閱讀順序

1. [00 快速讀取](00_CHATGPT快速讀取.md) → [147 完整契約](147_PrePhase4-4_小地圖與戰爭迷霧.md) → [120 文件維護規則](120_文件維護規則.md)。
2. [Minimap README](../../Assets/Scripts/MapGeneration/Minimap/README.md)：先讀 NetworkMapState、ExplorationStore，再讀 MinimapTileCache／MinimapBlockerIndex。
3. [UI Player README](../../Assets/Scripts/UI/Player/README.md)：讀 LocalMinimapController 與 MinimapRules；配色修改以 HeightColor 為入口。
4. 若新增敵人／道具／任務標記：再讀 [Enemy Core README](../../Assets/Scripts/Enemy/Core/README.md) 與 EnemyActor，以及 [Stage README](../../Assets/Scripts/GameFlow/Stage/README.md)、StageFlowController、StageExtractionPoint。沿用現有權威狀態，不新增平行同步系統。
5. 若擴充生成或底圖：[Prototype README](../../Assets/Scripts/MapGeneration/Prototype/README.md) → MapChunk／MapConnector／MapRunSelectionPrototype／ConnectorAlignmentPrototype／MapRuntimeNavigationPrototype → MapCartographySetup。保留預烘焙導航與 Host 導航責任。
6. 修改後參考 [Tests README](../../Assets/Scripts/Editor/Tests/README.md)、MinimapRulesTests，以及 [Testing README](../../Assets/Scripts/Tool/Testing/README.md)、Phase44NetworkVerification。新增目標標記應先補探索、距離、遮蔽、死亡／Despawn、Runner 隔離與 Host／Client 驗證。

目前沒有授權繼續下一階段；本文件是交接，不是自動開始新功能的指令。下一輪先確認 git status，保留所有既有未提交工作。
