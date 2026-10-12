# 傷害跳字驗證紀錄

日期：2026-10-11。範圍僅傷害回饋擴充、本機跳字及獨立資產；[正式規則與手動接線](../../61_命中傷害跳字.md)。

## 自動驗證

| 證據 | 結果 |
|---|---|
| 749b93c0529a4862b1f6643c1a1cb64b | 初始新測試未匯入，total=0，不算通過 |
| f0003b7af9e942a18099c0fa0a45f1eb | 13 項 Red，全部因 DamageNumberRules 尚不存在失敗 |
| 8a0ba3dfa2ca407a913a9ae987f6f1e7、7abf23e69a654ce5a937445672a0305a | 聚焦 23/23 passed |
| c8b15201ceea400a83caa1679d9749dd | 全套實際 671 項，670 passed／1 failed／0 skipped；見 [完整 result tree](regression.txt) |
| [Single Runner](single.txt) | 7 PASS；實際權威扣血、位置／頭部資訊、遮牆、延後結算禁止提前回饋、Despawn 快照 |
| [受控 Host／Client](host-client.txt) | 15 PASS；攻擊者隔離、Client 禁止偽造回饋、DoT 合併、HP 扣除、晚加入／重連不重播、Despawn 保留快照；斷線清除亦為 harness 等待條件 |

全回歸中的既有 GrappleAimInputTests 會 EnterPlayMode／ExitPlayMode 與 Domain Reload，因此 MCP job 在 261 筆進度後失去回呼。Editor 已完成測試，Temp/GrappleAimFix/latest-tests.txt 的既有 GrappleAimTestResults 保存完整 RunFinished result tree；此處複製保留。探索 686 不當作執行數。唯一失敗是已記錄的 BossStageAssetTests.GameSceneContainsBossAssetWiring，本任務未更動該測試或 Boss 資產。

Runtime harness 入口：空閒 Play Mode 且沒有正在運行的 Runner，透過 Unity MCP execute_code 呼叫 DamageNumberRuntimeVerification.Begin(false)（Single）或 Begin(true)（受控 Host／Client）；以 Status、Stage 查結果，Temp/DamageNumbers 保存輸出。建立私有臨時 Photon Session；不保存正式 Scene。使用既有 TestEnemy 與 KCC_Player 的真正生命／Relay，不改傷害倍率。這是受控送件，不是物理射擊操作驗收。

## 資產與畫面

- DamageNumbers.prefab：0 Missing Scripts，Settings／Visual Root 正確；沒有新增 NetworkObject。
- DamageNumberV1.asset：Head Color=#FFD34DFF，Head Scale=1.35，Font=MuseoModerno-Bold SDF。
- 該既有字型為 Dynamic，0～9 已存在；小數點與小於符號可能在使用時由原字型補入／TMP fallback 呈現，需在正式接線時查看小數傷害。
- [實際 TMP 渲染](actual-ui.png)：960×540 preview Camera + 真正 Prefab／設定，在 0.15 秒時截取；24 為一般白色，86 為金色 1.35 倍。這是隔離 UI 驗證，不是正式場景遊玩截圖。
- Runtime 無額外傷害碰撞查詢。上限控制物件數，但尚未量測壓力場景幀率／GC／網路流量，不能宣稱性能驗收。

## 編譯與 Console

最後核對 scriptCompilationFailed=false、isCompiling=false，Console Error=0。曾出現的既有編譯警告：第三方 DemoController.cs CS0108、TankPreparationRuntimeVerification.cs CS0252、BattleHudReferenceLayout.cs CS0618，以及 MCP WebSocket 日誌。沒有觀察到本功能的新編譯錯誤／警告。

## Scene 儲存副作用

開工時 `_Menu` isDirty=true。MCP run_tests 的 TestRunnerService.cs 第 238 行會呼叫 SaveDirtyScenesIfNeeded，第 479～500 行對已命名的 dirty Scene 呼叫 EditorSceneManager.SaveScene，因此第一輪測試於本機時間 10:48:34 將原 `_Menu` 狀態寫入磁碟，後續 isDirty=false。

本功能沒有在 `_Menu` 放置跳字物件，但**不能宣稱 Scene 沒有被寫入**。工具副作用事前未被攔住，已告知使用者；沒有儲存前序列化內容快照可做精確欄位差異核對，亦未回退使用者修改。後續遇 dirty Scene，需先隔離測試場景並保留原狀態，或取得針對儲存的授權，再啟動此 MCP 測試入口。

## 尚待使用者驗收

1. 按 61 文件手動接入 `_Menu/GameplayHUD Canvas/PlayerHUDCanvas`。
2. 打近處矮敵、巨型敵人的腿部、遠處／高空敵人；確認命中點起跳、準心不被蓋住及實際字級。
3. 打身體與 Head，對比預設白／金色、1／1.35 倍；更改 Head Color／Head Scale 即時比較。
4. 分別切換 Individual Hits／Merge Continuous Damage，觀察每筆或固定窗累加；一般連射不視為 DamageOverTime。
5. 測試實際可穿透或持續生效的權威傷害在隔牆時仍顯示；UI 不授予穿牆傷害能力。
6. 不同解析度、快速轉頭、相機 FOV、敵人死亡及轉場；獨立 Host／Client、晚加入／重連再做自然操作測試。

正式場景接線、自然射擊、獨立多人、Player Build 與最終美術接受度尚未完成。
