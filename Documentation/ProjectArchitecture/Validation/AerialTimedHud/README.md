# 空中緩速中央倒數量條驗證

日期：2026-10-11。使用者要求空中緩速比照子彈時間顯示倒數秒數與量條，並同意停止當時的 Play Mode 執行驗證。

## 實作範圍

- `SupportAerialAbility` 接入 `IPlayerAbilityHudState`；Host／State Authority 啟動時保存 Networked `ActiveDurationSeconds`，與既有 `ActiveTimer` 提供 Active 快照。Spawned／結束時清除總長。
- 共用 `LocalPlayerBattleAbilityHUD` 及正式 StageHUD 的中央量條、名稱、「效果持續」、一位小數秒數與配色。E 槽位也沿用既有作用中秒數；結束後回到冷卻顯示。
- HUD IsUsable 保留既有職業可用狀態；不加入新的操作或地面限制。無本地獨立倒數計時器。
- 再按 E、落地、期限到等既有結束規則與能力數值不變；沒有主動寫入 Scene／Prefab／Definition。本輪開始時空中緩速資產已是持續 5 秒、冷卻 5 秒、速度倍率 0.1，保留人工設定。
- StageHUD SHA-256 前後一致。空中能力 Prefab 在驗證期間出現唯一差異 `NetworkObject.Flags: 1 → 262145`（新增 DestroyWhenStateAuthorityLeaves）；將該欄位代回舊值後 SHA 與本輪基線完全相同，證明數值／接線未變。Fusion Inspector 在未允許 State Authority Override 時會強制該旗標（Fusion.Unity.Editor.cs 的 DrawToggleFlag）；實際觸發者未追蹤，本輪不擅自回退這項 Editor 期間的變化。見 [資產檢查](asset-checks.json)。

## 驗證證據

- 先紅：`ActiveAbilityHudTests.AerialProvidesSafeTimedHudAndSynchronizedDuration` 確認缺少 HUD 契約而失敗（實際執行 1 項），見 [red-test.json](red-test.json)。
- 聚焦 **14／14 通過**，見 [focused-tests.txt](focused-tests.txt)。含 HUD 11 項與受控 Single Runner 的 3 項 UnityTest；測試確實進入 Play Mode，使用正式玩家／能力／StageHUD Prefab。
- 新增 Single Runner 測試模擬首次在半程讀取、啟動後更改本地 duration，確認總長快照不變、量條仍為半滿；驗證正式 HUD 名稱／狀態／秒數及填色比例，取消／落地／到期後隱藏且開始冷卻。另保留原按下切換與子彈時間傷害結算測試。
- 完整 `PurgersRegression` **676 項結果：674 通過、1 失敗、1 Explicit 略過**（實際執行 675 項），見 [regression-tests.txt](regression-tests.txt)。失敗仍為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`，不屬於本次 HUD 變更。不能宣稱全套零失敗。
- Unity `scriptCompilationFailed=false`、`isCompiling=false`；結束時 `isPlaying=false`、Runner 0、dirtyScenes 空；最終 Console Error／Warning 0。見 [Editor 狀態](editor-final.json)。
- MCP job 在 UnityTest Domain reload 後保留舊 running 狀態；測試完成依 TestRunnerApi 的 RunFinished 完整結果樹判讀，確認退出 Play Mode 後才清除 orphan job。

## 驗證邊界與人工確認

本輪未執行獨立 Host／Client、晚加入／重連或 Build，未做最終遊戲畫面人工驗收。同步欄位與中途讀取快照有測試，不能代替多人傳輸證據。上一輪受控連線的 Fusion 例外仍未解決，見 [前輪紀錄](../AbilityToggle/README.md)。

在正式遊戲裝備空中緩速，進入可用鈎索空中狀態後按 E：中央沿用子彈時間量條，顯示技能名稱、效果持續及秒數；放開 E 繼續倒數。再按 E、落地或時間到後中央量條消失，E 槽位顯示冷卻。無須新增或重新接線 UI。多人驗收時再核對 Client 同步縮減與晚加入比例。

配置與正式責任見 [43 E 技能視覺與 UI 配置](../../43_E技能視覺與UI配置.md)。
