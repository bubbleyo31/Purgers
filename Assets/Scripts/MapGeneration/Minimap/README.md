# 小地圖與探索資料

效能：MinimapTileCache 使用原生 Cartography 格網建立一次地形貼圖；探索透過 ExplorationStore.Changed 更新格子，只在 dirty 時上傳。MinimapBlockerIndex 以水平空間桶篩選封口 Bounds，精確判定不變。LocalMinimapController 以裁切區塊貼圖平移／縮放，標記獨立更新；舊 Texture Resolution 已停用。

`MapCartographyData` 是每個 Chunk 的預製單層格網；`ExplorationStore` 保留個人歷史並另外讀取共享聯集；`MinimapRules` 放無場景依賴的顯示規則。

`NetworkMapState` 掛在 Game 的 StageFlow NetworkObject。Host 讀既有生成結果、發布含父連線／入口／出口／開口遮罩的完整線性拓撲、最後 Chunk、撤離選擇並採樣存活玩家探索；Client 先以 `MapLayoutRules` 驗證完整拓撲與內容版本，再建立相同區塊、Collider、Cartography 與封口，並以可靠差量／晚加入快照取得探索。Client 不註冊自己的 NavMesh，不接管 AI。共享模式由 Host 的 Inspector 或 SetSharedExploration 切換。

Phase 4-D 由同一 `NetworkMapState` 發布 `FinalChunkIndex` 與 `SelectedExtractionIndex`。`StageFlowController` 仍只在 State Authority 執行撤離 NavMesh 與 Seed 抽選；Client 只綁定同步結果。

本機視圖在 UI/Player/LocalMinimapController，不把邏輯塞入 StageHudController。M 展開只增加顯示範圍，不增加探索半徑或標記發現距離。更多契約、限制與操作見 Documentation/ProjectArchitecture/147_PrePhase4-4_小地圖與戰爭迷霧.md。

道路底圖預設可見，探索決定色調與資訊顯示；飛行時依水平半徑與實際地形視線探索下方。UI 每 0.1 秒讀取同 Runner 的存活 EnemyActor 同步位置畫紅點，撤離為綠點；未探索或未接近目標不顯示。
