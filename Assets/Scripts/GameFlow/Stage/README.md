# Assets/Scripts/GameFlow/Stage 程式導覽

Phase 4-F：`StageRuntimePlan` 集中發布 Boss 的 DedicatedBossChunk、DefeatBoss、免撤離與時間 route。`BossStageObjectiveController` 只由 State Authority 生成 Boss並觀察既有 `TestDamageReceiver.Died`；`StageFlowController` 沿用唯一成功／失敗提交。HUD 只讀同步 Objective。Game Scene 已加入 Objective Controller，並暫以已註冊 Fusion Prefab Table 的 `Enemy_Boss` 作為 Boss；136／136 EditMode 通過，Runtime Host／Client 與自然擊殺仍待驗證。

Phase 4-E：新存檔的 `CycleLengthSnapshot` 由 Host 解析成 `StageRuntimePlan`。`IsBossStage` 先分流 Boss，Boss 不進普通多 Chunk／撤離流程；State Authority 同步 StageLevel、CycleLength、CycleStage 與 Boss 身分，Game／SafeHouse HUD 共用 `StageHudText`。

Stage HUD 是 Game Scene 的單一 `StageHUD` root，不屬於任何 `MapChunk`。`StageHudLifetimeRegistry` 以本機 Runner 與 StageLevel 維持唯一顯示權；新關卡或 SafeHouse HUD 接手時，同一 Runner 的舊 HUD 會先隱藏再銷毀，場景卸載也會清空文字與小地圖。

Phase 4-C：NetworkMapState 發布 Host 拓撲的 FinalChunkIndex。StageFlow 將現有起始 Chunk 撤離模板依局部座標投影到最後 Chunk，只在 State Authority 從本輪入口檢查完整地面 NavMesh 路徑並以 Run Seed 選點。Client／HUD／小地圖只解析同步索引，不自行做可到達性或抽選。

Phase 4 前置-4：StageFlow 同物件上的 NetworkMapState 負責地圖配置與探索，載入回報／Host 開始計時等待它 IsReady。小地圖只讀其選定撤離點，不將 Connector 出口視為撤離。

關卡狀態、撤離點、倒數、成功／失敗與返回安全屋。

Phase 4 前置-1：Host 在地圖準備完成且所有連線玩家的當次 PlayerObject 載入回報齊全後，才建立 StageTimer。`LoadedPlayerCount` 可供診斷；RPC 使用 Fusion 來源身分，不能替他人回報。詳見 [轉場工具](../Transition/README.md)。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [BossSpawnPoint.cs](./BossSpawnPoint.cs) | BossSpawnPoint | Boss Chunk 內唯一的權威生成錨點。 |
| [BossStageObjectiveController.cs](./BossStageObjectiveController.cs) | BossStageObjectiveController | State Authority 生成 Boss、觀察正式死亡事件並同步目標結果。 |
| [StageExtractionPoint.cs](./StageExtractionPoint.cs) | StageExtractionPoint | 場景撤離範圍、選中視覺與 Gizmo。 |
| [StageFlowController.cs](./StageFlowController.cs) | StageFlowController, StageTransformPathExtensions | 關卡主狀態機；Host 決定撤離點、時間與關卡結果。 |
| [StageHudController.cs](./StageHudController.cs) | StageHudController | 左上第二列等級、MM:SS 與整合撤離狀態的任務文字；現行場景停用舊撤離量條。 |
| [StageHudLifetimeRegistry.cs](./StageHudLifetimeRegistry.cs) | StageHudLifetimeRegistry | 本機 Runner／StageLevel 的唯一關卡 HUD 擁有權；Game 與 SafeHouse 共用同一顯示槽。 |
| [StageHudText.cs](./StageHudText.cs) | StageHudText | Game／SafeHouse 共用的等級、循環與 Boss 標題格式。 |
| [StageRules.cs](./StageRules.cs) | StageRuntimePlan, StageMapRoute, StageObjectiveKind, StageRules | 無場景依賴的關卡 route、循環、時間與撤離規則。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

