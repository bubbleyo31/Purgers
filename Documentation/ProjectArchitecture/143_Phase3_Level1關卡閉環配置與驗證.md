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
