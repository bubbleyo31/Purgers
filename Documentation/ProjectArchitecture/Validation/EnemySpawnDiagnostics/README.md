# EnemySpawnZone Inspector 診斷驗證

日期：2026-10-20。主專案 `M:\UnityProject\Purgers`；隔離基準 HEAD `123817feb9923729748e90cdcabeb7f29fb3b561`。

## 實作與入口

選取 Zone，Inspector 頂端顯示配置、Play 中 Director 目前狀態、最近 8 筆結果，以及編輯模式的飛行空間預檢。設定見 [164](../../164_Phase10_怪物生成區與Encounter守則.md#2026-10-20-enemyspawnzone-inspector-診斷)。

不修改 Scene／Prefab 的生成配置，不新增 Networked／RPC。歷史與狀態限 Editor；診斷不推進計畫、不擲骰。既有流程增加事件記錄，玩家距離與遮擋維持原短路順序，分開呈現拒絕次數。

## 編譯與測試

| 證據 | 實際結果 |
|---|---|
| `before.xml` | 新增 6 項測試因缺少診斷入口／歷史而預期失敗。最初測試檔 Assert 命名衝突已修正；編譯失敗不算此證據。 |
| `focused.xml` | **44／44 通過**，含 Diagnostics、Composition、Occlusion、EncounterRules、FlyingPatrolPlanner。 |
| `regression.xml` | 隔離完整回歸：1225 通過、10 失敗、2 略過，共 1237。 |
| `baseline-regression.xml` | 隔離原始程式基準：1219 通過、**相同 10 項失敗**、2 略過，共 1231；差異正好是新增 6 項診斷測試。 |
| `main-regression.txt` | 主專案完整回歸：**1291 通過、5 失敗、2 略過，完成 1298**，MCP 曾探索 1313。採本輪 RunFinished 於 2026-10-20 14:45:38 UTC 寫出的紀錄。Domain Reload 使 MCP 進度停在舊值；已核對 TestRunnerApi 無活動工作，未把舊進度當通過。 |

主專案 `scriptCompilationFailed=false`，本次無新增編譯錯誤／警告；原有 DemoController、TankPreparationRuntimeVerification、LocalHealthSegmentView、BattleHudReferenceLayout 警告仍存在。

**完整回歸未全綠。** 主專案 5 項失敗也均出現在隔離修改前基準：

1. `AbilityToggleInputTests.BulletTimeSecondPressUsesNormalFinishAndResolvesDamageExactlyOnce`：FirstPersonHitCameraShake.Singleton 為 null 的未預期 log。
2. `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Boss Chunk 引用預期不符。
3. `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：預期 0.5，資產為 0.25。
4. `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：預期生命大於 80，資產為 75。
5. `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：_Menu 鏡頭效果引用缺失。

隔離額外失敗涉及 HUD 配置、三項 TMP 字型載入與 batchmode 視窗投影尺寸；未據此修改正式美術或場景。

## 編輯模式查核

Game 場景共找到 9 個 Zone，8 個在 Chunk 內；**Scene Root 的 EnemySpawnZone 不在 MapChunk 下**，診斷正確顯示「正式 Encounter 不會登記」，未自動搬動。

`EnemySpawnZone (7)` 的飛行預檢：Enemy_Ranged_A／B 各取得 16 個連通點，前後 Random.state 與 Scene dirty 狀態相同，見 `edit-mode-check.txt`。此結果只對當時載入的幾何成立，不能推論玩家條件或預算通過。

## Play 受控查核與限制

沿現有 `EnemySpawnCompositionVerification.Begin(true, 3)` 建立臨時 Host／Chunk；監測期間關閉該臨時 Director 的 Debug Encounter。此 verifier 直接驅動內部生成切片，非自然 Gate 穿越。

- `runtime-inspector-events.txt`：捕獲 6 筆事件，Ground 256 次失敗（NavMesh 221、貼地 35）、FreeFlying 成功 2 隻、Stationary 成功 3 隻。證明不依賴 Console 開關。
- `controlled-host.txt`：15 項 Check 通過後，因 Ground 未達預期 2 隻而失敗。本次不調整地圖／生成參數來掩蓋失敗。
- **未走到 Client／晚加入／重連，不能算多人通過。** `runtime-status.txt` 的 Stage 是最後一次等待標籤，真正失敗以 `controlled-host.txt` 為準。
- 結束後 Runner=0、臨時 Zone=0，已退出 Play、回到 Game。Game View 原本即為 PlayUnfocused；未呼叫 Focus／Show／Scene View 截圖，也未保存測試配置。
- Inspector 自然操作與實際排版、自然 Encounter、獨立程序多人、Build 尚未驗收。

## 保留位置

實作已接回主專案。隔離工作樹 `M:\CodexWorktrees\spawn-zone-inspector\Purgers`、獨立 Library `M:\CodexUnityCaches\spawn-zone-inspector`、原始 logs／備份 `M:\CodexData\spawn-zone-inspector` 均保留，未自動刪除；Codex 工作樹登記使用既有 C 槽 Junction 相容路徑。
