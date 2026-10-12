# Phase 3：Level 1 關卡閉環配置與驗證

## 已完成行為

1. Host 從存檔取得 `StageLevel`，並透過 `StageFlowController` 同步關卡狀態。
2. `StageLevel = 1` 時不生成第二個 Chunk，起始 Chunk 的四個 Connector 全部生成阻擋屋。
3. 起始 Chunk 載入預烘焙 `NavMeshData`，並自動建立單一 Chunk 的 `EnemyPatrolArea`。
4. Host 依本輪 Run Seed 從所有啟用的 `StageExtractionPoint` 抽選一個撤離點。
5. 所有存活玩家都在撤離區內時開始 3 秒倒數；任何必要玩家離開就歸零。
6. 撤離成功後 Host 先將 `StageLevel + 1` 寫入存檔，再返回 `SafeHouse`。
7. 時間到或全員生命歸零時，Host 重設 `RunProgression` 後返回 `SafeHouse`；永久進度不清除。
8. `GameLogic` 只在權威切場期間暫停死亡重生排程，仍保留原本的玩家生成與死亡重生責任。
9. Runtime 生成的地圖根與第二個 Chunk 都維持在 Fusion 的 `[Game]` 場景根下；Level 2 以上返回 `SafeHouse` 時會隨 `LoadSceneMode.Single` 清除，不會遺留 Collider、NavMesh 或巡邏區。

## Game 場景灰盒

```text
Game
├─ StageFlow
│  ├─ NetworkObject
│  └─ StageFlowController
├─ StageHUD
│  └─ StagePanel
│     ├─ StageLevel
│     ├─ Timer
│     ├─ Objective
│     ├─ ExtractionStatus
│     └─ ExtractionProgress
└─ Map(Z:-263)/MapChunk_Prototype
   └─ StageExtractionPoints
      ├─ Extraction_north_inner
      ├─ Extraction_east_inner
      ├─ Extraction_south_inner
      └─ Extraction_west_inner
```

四個預設撤離點都位於起始 Chunk 內側，並已用 `NavMesh.SamplePosition` 驗證在 12 公尺搜尋距離內可投影到預烘焙 NavMesh。綠色圓盤與 Beacon 只在該點被抽中時顯示。

## 製作者如何新增或調整撤離點

1. 開啟 `Assets/Scenes/Game.unity`。
2. 展開 `Map(Z:-263)/MapChunk_Prototype/StageExtractionPoints`。
3. 移動現有 `Extraction_*`，或複製一個作為新候選點。
4. 在 `StageExtractionPoint` 設定：
   - `Point Id`：同一張地圖內必須唯一，Host／Client 會依 ID 排序。
   - `Radius`：玩家水平進入範圍，預設 8。
   - `Vertical Tolerance`：允許的上下高度差，預設 6。
   - `Selected Visual Root`：只在本輪被選中時顯示的子物件。
   - `Draw Gizmos`：Scene View 顯示範圍。
5. 新位置必須落在可行走區；播放前可用 Scene Gizmo 檢查，正式驗證仍需確認玩家能走入且 NavMesh 可到達。

Phase 4 把撤離點放入可生成的 MapChunk Prefab 時，也使用相同元件與唯一 ID；屆時 `StageFlowController` 只收集最後一個 Chunk 的候選點。目前 Phase 3 的四個候選點屬於 Game 場景中的起始 Chunk 實例。

## 可調整規則

`StageFlow/StageFlowController`：

- `Cycle Length`：預設 4；正式流程優先使用存檔的 `CycleLengthSnapshot`。
- `Default Time Limit Seconds`：預設 600，即 10 分鐘；設為 0 代表所有一般關卡不限時。
- `Unlimited Stage Levels`：可列出個別不限時的 `StageLevel`。
- `Extraction Hold Seconds`：預設 3 秒。
- `Safe House Scene Name`：預設 `SafeHouse`。

## 已完成驗證

- Unity 編譯與 Game Scene 驗證：0 Missing Script、0 Broken Prefab。
- EditMode：44/44 通過，job `9c6c6f07fd524672a0de54d13f1ee3de`。
- Host 成功路徑：`_Menu → SafeHouse → Game`；Level 1 為 1 個 Chunk、4 個阻擋屋、NavMesh／Patrol Ready；撤離 3 秒後磁碟存檔由 1 寫成 2，再回到 SafeHouse。
- Host 限時失敗：將 Runtime Timer 壓到 2 秒後，返回 SafeHouse，Runtime 與磁碟存檔均為 Level 1。
- 所有測試產生的暫存檔已刪除，原有存檔未修改。

## 尚未完成

- Phase 4 前置-1 已補上 Level 1 的雙程序載入屏障、撤離後返回與重進驗證，詳見 [144](144_PrePhase4-1_共用控制與轉場.md)；多 Chunk 拓撲同步仍未完成。
- 全滅失敗的實際傷害流程 Play Mode 驗證；判定與提交路徑已接入。
- Level 2 以上的多 Chunk 鏈、最後 Chunk 撤離點與非回頭方向限制，屬 Phase 4。
- Runtime 生成的第二 Chunk／阻擋屋目前仍不是 NetworkObject；Client 地圖拓撲同步需在 Phase 4 一起處理。

## 2026-10-08 局部核對：Play Mode 關卡限時暫停

核對來源：`Assets/Scripts/InputManager.cs`、`GameFlow/Stage/StageFlowController.cs`、`StageRules.cs`（後兩者亦位於 Assets/Scripts）。本節只補充開發功能，不重新驗收前述歷史 Phase 3／4 紀錄。

### 設定與使用

1. 在 Project 開啟 `Assets/Prefabs/Runner.prefab`，選取根物件的 `InputManager`。
2. 在「Play Mode 開發者按鍵」設定 `Development Stage Timer Pause Key`。
3. 預設 **F7**；有效值為 Unity `KeyCode`，`None` 停用。建議保留 F7，避免與 Q／E、TAB、F8 等既有操作重疊。若指定同一鍵，既有操作仍會收到該鍵。
4. 儲存 Prefab 後，新建立的 Runner 使用新設定。Play Mode 可在執行中 Runner 的 InputManager 改鍵，立即生效，但退出後不保留。
5. 以 **Unity Editor Host／Single** 進入有限時間的 Active 關卡，讓 Game 視窗取得焦點，按一下 F7 暫停，再按一下恢復。長按只切換一次；不必新增場景物件。
6. Console 會記錄暫停／恢復。HUD 沿用原計時讀值：暫停時固定剩餘秒數，恢復後從該值繼續。

本次沒有自動修改 Runner Prefab、Game Scene 或手動覆寫；舊元件未序列化的新欄位採用程式預設 F7。按鍵設定不是玩家存檔／Settings 重綁介面。

### 生效範圍與權威

- 僅 Editor Play Mode Host 操作；Client、Development Build、正式 Build 均無此按鍵入口。不新增 Client RPC。
- 輸入沿用 `InputManager.IBeforeUpdate`，受 `LocalPlayerControl.AllInputBlocked` 限制；按鍵交給同 Runner 的 `RequestDevelopmentTimerToggle()` 排入意圖。
- `StageFlowController.FixedUpdateNetwork` 再確認 State Authority、Server、Active、有限且尚未到期，才改既有 `StageTimer`。
- 新增私有 `[Networked] PausedStageTicks`：大於 0 表示已暫停；`IsStageTimerPaused` 提供生命週期防護後的公開只讀狀態。暫停剩餘秒數＝剩餘 Tick × Runner.DeltaTime，恢復使用 `TickTimer.CreateFromTicks`，避免每次切換的浮點時間累積偏移。
- Client／晚加入 HUD 仍讀 `RemainingStageSeconds` 與同步快照，不自行決定暫停；兩端須使用相同程式版本與 NetworkBehaviour layout。
- 不改 `Time.timeScale`、Fusion Tick、移動、攻擊、技能冷卻或 `ExtractionHoldTimer`。撤離／Boss 擊敗／全滅判定繼續執行。
- 初始載入、不限時與結束狀態拒收；到期 Tick 不可用暫停救回。新關卡、Spawn 與完成／失敗提交前清除暫停和待處理意圖。
- 暫停狀態不寫入存檔；新關卡正常倒數。它只暫停限時，不會防止其他條件造成成功或失敗。

### Play Mode 手動檢查

以 Editor Host + Client 進入同一有限時間關卡：Host F7 後兩端時間固定，但仍可移動、射擊、技能冷卻；再按後接續倒數。Client F7 不應改變關卡時間。暫停期間測試撤離、全滅與有限時間 Boss 擊敗仍可結束關卡，返回後新關卡不應繼承暫停。另檢查晚加入／重新連線者讀到相同剩餘時間。不限時關卡不受影響。

自動測試與實測邊界見 [本次驗證紀錄](Validation/StageTimerPause/README.md)。
