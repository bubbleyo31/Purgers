# Phase 4 前置-4：程序地圖小地圖與戰爭迷霧

> **後續狀態（2026-09-22）**：Phase 4-D 已將本文件的一／兩 Chunk 配置契約擴充為完整線性多 Chunk 拓撲，並完成獨立晚加入 Client 驗證；本文件以下相容性段落保留前置-4 完成時的歷史邊界。詳見 [151](151_Phase4-D_NetworkMapState完整拓撲同步.md)。

> 2026-09-21 交接：使用者確認功能可用且不再卡頓，暫停擴充。下一輪先讀 [交接摘要](147_PrePhase4-4_交接摘要.md)；本次僅整理文件，沒有重跑下列 2026-09-19 驗證。

> 最後核對：2026-09-19（局部）
> 核對來源：MapGeneration/Minimap、LocalMinimapController、地圖原型與 StageFlowController。
> 相關文件：[HUD](146_PrePhase4-3_HUD排列與資訊精簡.md)、[UI](100_UI觀戰與聊天.md)、[場景生成](110_場景工具與選單.md)。

## 責任與資料流

既有 MapRunSelectionPrototype／ConnectorAlignmentPrototype 完成地圖後，由 StageFlow 同物件上的 NetworkMapState 發布配置。它不重抽 Seed，也不取代原生成或導航 owner。

`MapChunk.Cartography` 指向每塊的 MapCartographyData：局部格網、地形種類、地面高度與內容版本。Editor 工具在隔離預覽場景中，利用 Collider 與既有預烘焙 NavMesh 產生資料；不在 Runtime 重建 NavMesh，不加入 NavMeshAgent。

Host 發布 Chunk Catalog 索引、內容版本、世界位置／旋轉、開口遮罩。Client 驗證後重建相同區塊與封口，停用自己區塊上的 NavMeshSurface，不重複註冊全域 NavMesh；AI 導航仍由 Host 負責。StageFlow 的本機載入回報與 Host 開始計時都等待 MapState.IsReady。地圖資料錯配時停在準備流程並明確報錯，不顯示錯誤底圖。

## 探索與網路

- Host 每 0.1 秒依存活玩家水平位置、半徑、封口與地形遮蔽更新個人探索位元。不要求接地；飛行可探索下方表面。Vertical Tolerance 只限制高於玩家的表面，下方沒有高度上限，仍須通過實際視線。
- ExplorationStore 以 PlayerRef、Chunk 清單索引、格網 word 區分個人紀錄；共享聯集是獨立讀取結果，不回寫個人紀錄。
- Host 的 Share Team Exploration 可在 Play Mode 即時切換，Networked SharedExploration 同步到所有 Client。Client 無權改模式。
- 個人歷史以可靠 RPC 的非零 word 差量同步；晚加入者請求快照。快照和差量用位元 OR 合併，舊快照不會清除較新的探索。
- 同關重生沿用 PlayerRef 紀錄；離開關卡清空。斷線重連若得到新的 PlayerRef，視為新個人探索；沒有跨 Session／磁碟探索存檔。
- 本版所有 peers 收到個人探索資料，UI 決定呈現自身或聯集；這是顯示規則，不是防修改 Client 的保密機制。
- 當隊員離線，本关團隊探索記憶保留，直到關卡卸載。

## 視圖與資訊隱藏

- 接在 Game/StageHUD/TopLeftColumn/MinimapSlot/MinimapContent。Runtime 才建立裁切容器與區塊 RawImage，維持 Phase 4 前置-3 的編輯期空容器契約。
- 北方朝上，玩家固定在中央。M 切換 224×224 小地圖與 600×600 中央展開圖；世界視野寬度預設 140／360 公尺。
- 不提供自由平移；展開不增加探索半徑（22 公尺）或目標發現半徑（30 公尺）。不改相機 FOV，不取得輸入鎖，不移動游標。AllInput、死亡與未載入時收合並隱藏內容。
- 顯示範圍內所有已生成地形預設可見：不可通行／無資料區近黑色，牆體／障礙亮色填滿、地面依 3 公尺高度帶呈中間灰階。地面大落差使用強對比，牆體高度只微調亮度；不再逐格畫每個高度邊緣。已探索區加上微量青綠 tint，不壓過高度明暗。迷霧只隱藏內容資訊，不把道路塗黑。黑色背景表示沒有底圖資料。
- 本機玩家為黃色、選定且可用的撤離點為綠色、存活敵人為紅色。目標需要已探索、個人水平距離、上方高度容許與地形視線；飛行可看見符合條件的下方目標。共享與展開不增加發現距離。
- 敵人讀取同 Runner 的 EnemyActor：先確認 IsFusionSpawned、有效 Object，才讀 IsAlive 與已同步 Transform。每 0.5 秒刷新候選快取，每 0.1 秒更新可見性，已顯示目標位置每幀更新；死亡、失去視線、離開範圍或 Despawn 後不留下記憶點。沒有新增網路欄位或敵人 RPC。
- 道具、獎勵、任務道具與特殊裝置尚未接入標記，維持隱藏；沒有世界俯視相機，因此模型與 Beacon 不會漏進底圖。
- 撤離選擇仍由 StageFlow 決定，本輪只將候選收集限制於 Flow 所在 Scene；不將 Connector 出口當作撤離點。

## Inspector 與製作流程

1. 在沒有載入 NavMesh 的 _Menu 場景，執行 Tools/Purgers/Map/Bake Prototype Cartography。
2. 工具建立 MapChunk_Prototype_Cartography.asset 並把引用寫入原 MapChunk Prefab。預设格距 1.5 公尺，地形修改後需重新烘焙。
3. Tools/Purgers/Map/Wire Phase 4 前置-4 Game HUD 接上既有 StageFlow、選擇器、Catalog 與 MinimapContent。此工具是明確執行入口，不會自動修改場景。
4. Host 在 StageFlow/NetworkMapState 切換 Share Team Exploration，Client 的同名 Inspector 欄位不會覆寫 Host。
5. 在 LocalMinimapController 調整一般／展開世界寬度與 Marker Discovery Radius。舊 Texture Resolution 保留序列化但隱藏、不再參與繪製；不需要降低解析度換效能。底圖精度由 Cartography.CellSize 決定，展開直接顯示原生格網。

## 相容性邊界

- 支援現有單 Chunk 與動態雙 Chunk，Client 消費的配置格式容量為 16；目前生成 owner 仍只會產生一或兩塊，沒有假稱完成第三塊以上生成。
- 底圖是目前地圖的單層投影，不保證重疊樓層。未來多層必須加入樓層資料，不可只加大高度容許值。
- 可通行底圖以現有地面 AI NavMesh 為起點；它不代表所有鈎索／跳躍可到達的表面。
- Runtime 動態門與破壞地形尚未接入；本輪封口會覆蓋底圖並阻擋探索。
- 同一關配置在準備完成後發布一次；關卡中追加／卸載區塊需要後續版本化配置更新，不由 UI 掃描場景猜測。
- 既有正式生成入口不再受 DevelopmentToolsPolicy 阻擋；離線 Marker／F9／N 仍維持開發模式限制。
- SafeHouse 保留原小地圖佔位，沒有 MapChunk 來源，不假造底圖。

## 驗證紀錄

以下為初版驗證；飛行探索、道路預設可見與動態敵人標記的修訂驗證另外列於文末，不能把舊截圖當作新版畫面。

- TDD：7 個聚焦案例先失敗，再通過；額外加入晚加入快照與新差量合併、旋轉／平移座標測試。
- 初次測試 API 篩選回傳 total=0，未列為通過；改用測試群組後實際執行。
- 最終 PurgersRegression：84／84 通過，job `a377bdc4cd4849af89992694ed74897f`。
- 最終 Windows Development Build：`build-b6ced3044f`，0 errors／0 warnings，輸出 `Builds/Phase44/Purgers.exe`。
- Editor Host＋獨立 Windows Client：雙 Chunk／六個封口、單 Chunk／四個封口兩輪皆通過；兩端位置、旋轉、內容版本相同。首輪另確認過第二塊 180° 旋轉。
- Client 在 Host 已進入 Active 後加入，成功取得配置及探索快照；沒有把這當作斷線重連保留個人探索的證據。
- Host／Client 分開移到不同區域後，Client 可看到自身探索；Host 切共享後可讀取指定的 Host 專屬格子；切回獨立後該格仍未被 Client 探索。Client 改模式請求被拒。
- 本機視圖一般寬度 140、展開寬度 360，Host 截圖已檢視；遠方撤離標記為 0，Host 靠近後為 1，遠方 Client 始終為 0。
- 遮蔽補驗使用執行期臨時 Cube 與隔離測試玩家紀錄：cell 20956 在阻擋時不揭露，關閉 Collider 後才揭露；測試物件未保存。自然走路／鈎索路徑及實體 M 鍵仍建議手動試玩，不以強制位置取代手感驗收。
- Game：0 Missing Scripts、0 Broken References；最後 Editor Console 無 Error／Warning。兩輪成功 Client log 的 Exception／Error／FAIL 搜尋無命中。
- 首次受限 Client 啟動因 PlayerPrefs／網路限制失敗，允許測試程序所需權限後成功。首輪共享驗證誤取兩人都走過的出生格，產生兩筆 FAIL；改為在切換前保存 Host 專屬格子後重跑通過，原失敗保留在 `Builds/Phase44/Evidence`。
- Hidden Client 截圖是黑畫面，不列為 Client 視覺驗證通過；Client 以資料、UI 像素計數與配置讀回驗證。
- 測試已停止，Editor 返回 _Menu；正式存檔沒有修改，測試存檔位於 Build 下的隔離資料夾。

證據：[雙塊 Host](Validation/Phase4-4/TwoChunk-Host.log)、[雙塊 Client](Validation/Phase4-4/TwoChunk-Client.log)、[單塊 Host](Validation/Phase4-4/SingleChunk-Host.log)、[單塊 Client](Validation/Phase4-4/SingleChunk-Client.log)、[小地圖](Validation/Phase4-4/Host-Compact.png)、[展開圖](Validation/Phase4-4/Host-Expanded.png)、[撤離附近](Validation/Phase4-4/Host-NearExtraction.png)。

## 道路可見／飛行探索修訂驗證（2026-09-19）

- 4 個新增案例先失敗：高空／地面／過高樓層，以及未探索道路可見但標記不揭露。修正後聚焦 13／13、完整 PurgersRegression 88／88 通過（`89782e08117a4f08af80bc12c58168e3`）。
- Windows Development Build `build-61bc4154e1`：0 errors／0 warnings。
- 真實雙 Chunk Host：底圖 65,536 像素、展開時探索 473 像素，未探索部分仍有可見道路／高度輪廓。已檢視 [新版一般底圖](Validation/Phase4-4/Revision-Terrain.png) 與 [新版展開底圖](Validation/Phase4-4/Revision-Expanded.png)。這是實際 UI 使用的 Texture，不是概念圖。
- 以隔離測試玩家紀錄呼叫同一 Host RevealAround，輸入實際位置上方 80 公尺，產生 45 個非零探索 word；驗證離地不再被 6 公尺門檻排除。未以此取代自然鈎索操作的手感驗收。
- 透過 Runner.Spawn 生成既有 Enemy_Melee_A，暫停該測試敵人的移動／思考元件。附近紅點 1；位移後紅點仍為 1，圖中紅點中心由約 (133,127) 移至 (133,122)；Despawn 後為 0。測試物件未保存至場景／Prefab。
- [移動後圖](Validation/Phase4-4/Revision-EnemyMoved.png)、[Runtime 讀回](Validation/Phase4-4/Revision-RuntimeChecks.txt)。敵人候選快取最多延遲 0.5 秒納入新生成目標；既有目標的位置與隱藏條件以 10 Hz 更新。尚未完成獨立 Client 的敵人移動／死亡視覺驗收。
- 新版 Editor Host＋Windows Client 重跑：雙 Chunk／六封口，第二塊旋轉 270°；晚加入快照、個人→共享→個人、Client 無權改模式、遠方撤離隱藏與 Host 近距離撤離標記皆通過。證據：[Host](Validation/Phase4-4/Revision-Host.log)、[Client](Validation/Phase4-4/Revision-Client.log)。Console 無 Error／Warning，Client log 無 Exception／Error／FAIL。測試程序已關閉，Editor 回到 _Menu，正式存檔未變更。

## 區塊快取與高度配色（2026-09-19 效能修訂）

- LocalMinimapController 不再以視窗解析度逐像素掃 Chunk、查探索字典與遍歷封口。每塊建立原生格網的 Terrain／Exploration 貼圖，使用 RectMask2D 裁切；移動和展開只改 UI 位置、比例，不重建貼圖。
- MinimapTileCache 在整個視圖共用每幀 8192 格的建立預算。完成後地形不再更新；每塊保留獨立探索紋理，ExplorationStore.Changed 只通知實際增加的 word。收到通知不消耗 Host RPC 的 dirty 集合；UI 每 0.1 秒只對 dirty 貼圖 Apply。切換共享模式才清除呈現遮罩並重播歷史，不改個人紀錄。
- MinimapBlockerIndex 以 16 公尺 XZ 桶篩選候選；最後仍執行原本 Bounds.Contains(floor + up)。六個封口屋共 192 個 Collider Bounds，不再每格遍歷全部 192 個。這同時供 Host 探索與底圖快取使用；不變更導航、物理或權威結果。
- 灰階由 MinimapRules.HeightColor 決定：無資料／封口近黑，牆體障礙約 208–224，地面約 88–176。3 公尺高度帶減少裝飾雜訊；底色相對每塊最低有效地形高度。屋頂若已烘焙為可通行 NavMesh，仍視為高處地面，不靠物件名稱猜成牆。多層室內仍需正式樓層資料。
- 標記使用獨立 Image，位置每幀跟隨，發現條件每 0.1 秒更新。死亡／Despawn 即時清除已存在圖示。新增圖示才調整 UI 層序，避免每幀無條件重建 Canvas。
- 舊 Texture Resolution 欄位為序列化相容性保留但隱藏、不再控制效能與清晰度。600×600 展開不再放大一張 64×64 或 256×256 的視窗圖；原始 Cartography 的 1.5 公尺格距仍是幾何精度上限，沒有假稱向量圖或完整多樓層。
- 診斷：Profiler 標記 Purgers.Minimap.View／BuildCache；LastViewMilliseconds、TerrainCellsBuiltLastFrame、IsTerrainReady 可讀回。舊 TerrainPixelCount／ExploredPixelCount 現在是快取格子數，不是視窗像素數。

效能量測在同一 Editor 雙 Chunk Host，以 Stopwatch 直接呼叫函式，排除網路啟動等待；不能換算為保證的整體 FPS，也未包含全部 GPU／Canvas 成本。原版 Draw：64×64 平均 27.62 ms（20 次）、256×256 平均 391.61 ms（3 次）。快取完成後 UpdateView 約 0.008 ms（100 次），強制每次走標記刷新仍約 0.008 ms（50 次），反覆縮放約 0.12 ms（50 次）、地形重建 0 格。這些是微量測，不是完整場景 Profiler capture。

首輪快取建立仍量到 57.63 ms／8192 格，因而追加封口空間索引，而非忽略首次載入尖峰。空間索引測試先確認失敗，再實作通過；另有 palette、探索通知與網路 dirty 獨立、快取預算、無變更不重傳測試。完整最新結果與索引後耗時如下方紀錄。
最終驗證：

- 索引後：單 Chunk 快取初始化約 4.94 ms，每批 8192 格最高 1.45 ms、共 21 批；已探索位置的 RevealAround 平均 0.75 ms（先前約 2.06 ms）。探索 dirty 紋理上傳另測約 0.076 ms。首次快取只在載入後建立，不是每次 M 展開都建立。
- 新版同場景開／關小地圖：各排除前 20 個取樣，採樣 80 個 Editor 更新看到的遊戲幀，平均幀時間約 11.98／12.00 ms。非完整逐幀錄製，受 Editor／背景程序影響；不宣稱所有場景都達到固定 FPS。
- 最終 PurgersRegression 92／92，job c6c9753b77f146beac970115fceacf23；聚焦 17／17。新增空間索引案例有實際 RED→GREEN；前三個新增案例在 MCP 斷線期間撰寫，未取得實作前的 RED 執行紀錄。
- 最終 Windows Development Build build-94fae53e83：0 errors／0 warnings。前一 Build 的唯一警告來自保留舊序列化欄位沒有程式讀取，已針對該 legacy 欄位註明並局部抑制 CS0414；沒有掩蓋其他警告。
- 新版雙 Chunk Host＋Windows Client：晚加入快照、兩塊幾何／六封口、共享切換、Client 不能改模式、遠方撤離不顯示／近距離撤離為 1，均通過。
- 實際 UI 快取切換：個人 1572 格→共享 2237 格→個人 1572 格，個人歷史不被共享污染。
- Host 使用既有敵人 Prefab 驗證 UI 紅點：附近 1，移動 2 公尺後 anchor x 由 1.6 變 4.8，Despawn 後為 0。獨立 Client 的敵人移動／死亡視覺驗收仍未完成；不把原測試的黑畫面截圖當視覺通過。
- Console 無 Error／Warning；ClientPlayer log 無 Exception／Error／FAIL。測試 Client 已關閉、Editor 返回 _Menu；沒有修改正式存檔或場景／Prefab 接線。

證據：[一般畫面](Validation/Phase4-4/Cached-Compact.png)、[展開畫面](Validation/Phase4-4/Cached-Expanded.png)、[原版64耗時](Validation/Phase4-4/Baseline-64-ms.txt)、[原版256耗時](Validation/Phase4-4/Baseline-256-ms.txt)、[快取耗時](Validation/Phase4-4/Cached-ms.txt)、[索引後耗時](Validation/Phase4-4/Indexed-ms.txt)、[幀取樣](Validation/Phase4-4/FrameSample.txt)、[共享快取](Validation/Phase4-4/CachedSharing.txt)、[動態標記](Validation/Phase4-4/CachedMarkers.txt)、[Host](Validation/Phase4-4/Cached-Host.log)、[Client](Validation/Phase4-4/Cached-Client.log)。
