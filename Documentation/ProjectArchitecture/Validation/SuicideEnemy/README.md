# 自爆撲擊敵人驗證紀錄

日期：2026-10-11。範圍：[92 自爆撲擊敵人](../../92_自爆撲擊敵人.md)。Unity 2022.3.62f1，既有 Fusion／KCC，Editor MCP。

## 結果與界線

| 層級 | 結果 | 證據／範圍 |
|---|---|---|
| 先失敗再實作 | 8 項預期失敗 | 最初 EnemySuicideRulesTests 找不到尚未建立的規則類別；此為預期的缺少實作失敗，不冒充行為通過 |
| 最終聚焦 EditMode | 19／19 通過 | [結果](focused-editmode.txt)，job `4eb3e838181744eb9d47b70ed1fb5208`；衰減、掃掠、VFX 直徑、傷害觸發種類及 Prefab 引用 |
| 隔離 Single Runner | 29 個檢查通過 | [結果](single-runner.txt)；正式 Fusion 物件生命週期、傷害／死亡、爆炸、斷鈎、速度／能量、UI、效果到期、牆壁、高速穿越與有限撲擊 |
| 受控 Host／Client | 36 個檢查通過 | [結果](controlled-host-client.txt)；同一 Editor 的 Multiple Runner，Photon Asia 隱藏 Session；包含單機項目、晚加入、重連、Client 不可改寫 Host 效果、Client Input Authority 玩家受擊同步及鈎索封鎖 |
| 完整 PurgersRegression EditMode | 693 通過、1 失敗、1 略過 | [完整結果](regression-editmode.txt)，共 695 個結果；唯一失敗為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring；並非全綠 |
| 編譯／Console | 無本次新增編譯錯誤／警告 | 最後 `EditorUtility.scriptCompilationFailed=false`，Editor 不在編譯／Play Mode；既有 DemoController.camera CS0108、TankPreparationRuntimeVerification CS0252 不屬於本次變更 |
| 自然遊玩、Build、獨立程式多人、大量效能 | 尚未驗收 | 不把受控測試當成正式地圖視覺／效能驗收 |

完整回歸略過 `AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`，原因是該測試標示 Explicit，需要單獨 Photon 連線。既有 Boss 失敗是 Game Scene Boss MapChunk 參照不是預期資產實例；本次未更動 Boss 或 Game Scene。

MCP 回歸進度在含 EnterPlayMode 的測試 Domain Reload 後停留於 34／710（710 是 discovery 數量，不是本類別最終執行量）。以 `EnemySuicideTestResults` 的 RunFinished 遞迴結果確認實際 695 個結果，並在 Editor.log 確认 RunFinished、Editor 回到 `_Menu` 後，清除 MCP 遺留 job `1273a03717b244a89498c88314980f17`。這不是中止正在執行的測試。

## 執行期測試的限制

`EnemySuicideRuntimeVerification.Begin(false/true)` 必須由沒有現存 Runner 的 Play Mode 明確呼叫。測試臨時建立平面、Patrol Area 與玩家，完成後 Shutdown Runner／清理，沒有保存正式場景或生成表。

- 隔離敵人的一般巡邏、追逐、感知與 CombatDecision 大多停用，以直接驅動指定情境；「未發現近身」另啟用真正 CombatDecision 仲裁器。未證明正式 Chunk 的自然巡邏／感知與 NavMesh 邊界。
- 撲擊測試以受控位置變更及手動 Tick 驗證早期修正、晚期鎖定、期限結束；落點測試關閉 NavMesh 需求，只使用臨時物理地板。實際 Prefab 仍開啟 NavMesh 落點限制。
- 緩速透過既有 PlayerMovement 入口驗證 KCCData 修正，確認 50% 地面位移與不改持久／空中速度；不等於鍵盤操作的全流程手感驗收。
- 經驗驗證到正式 KillingDamage 的玩家歸屬與單次死亡為止；沒有在 Active Stage 建立 GameLogic 隊伍經驗交易。實戰 XP 數字／獎勵 UI 仍須依 92 文件步驟確認。
- 斷鈎使用 Attached 狀態與非零 DynamicVelocity，確認回到 Idle、無額外推進且保留動態速度。未驗收所有職業鈎索的自然視覺過渡。
- UI 在隔離 LocalPlayerBattleAbilityHUD 的既有 RefreshGrapple 入口檢查禁用物件顯示／到期隱藏；未改動正式 UI 排版。
- 初次重連測試使用 30 秒樣本，連線過程超過期限，等待剩餘封鎖逾時；第二次把測試樣本改為 300 秒後通過。Prefab 的正式 3／0.25 秒設定沒有改動。
- VFX 引用／比例通過資產與規則驗證，Runtime 已走 RPC 實例化；未做各客戶端畫面與同時大量粒子效能驗收。

## 場景保護與手動接線

測試前 `_Menu` 原本有未儲存修改，已用 Editor 保存副本。測試後發現唯一差異是 `Explosion FREE 4 Variant` 預覽物件消失；使用者不記得是否手動刪除，因此沒有推測加回。

兩份持久副本及說明：[Scene 備份](../../../../deliverables/Backups/SuicideEnemy_20261011/README.md)。最終 `_Menu` 與 after-tests 副本一致。自爆敵人未放進正式 Scene、SpawnZone 或 MapChunk；使用者依 92 文件手動加入生成候選。

本次新增獨立 Enemy_Suicide Prefab 與 ED_Suicide Definition；Prefab 無缺失腳本／斷裂引用，只有一個攻擊選項，沒有模型、Animator 或 NavMeshAgent。所有平衡數值、幀時序、範圍及 VFX 基準皆在 Inspector；準心遮擋只保留 Sprite 欄位。

規則索引新條目尚待使用者確認，建議關鍵詞：自爆怪、撲擊、EnemySuicideAttack、距離衰減、鈎索封鎖；路由至 92 文件。
