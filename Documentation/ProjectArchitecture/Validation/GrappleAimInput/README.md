# 右鍵不取消鈎索：驗證紀錄

最後核對日期：2026-10-11（局部核對）。
核對來源：PlayerGrapple、Player、GrappleAimInputTests；Unity MCP／Unity Test Runner。
相關文件：[鈎索正式規則](../../30_鈎索系統.md)、[玩家核心](../../20_玩家核心移動與狀態.md)。

## 修正範圍

移除 Attached 按下 Aim 觸發收繩的舊入口；不再消耗右鍵或因此封鎖職業 Aim／E。移除僅供 AimRelease 使用的視角方向動能分支，保留列舉數值、原 Simulate 介面及 Networked 配置。Q toggle／hold 與自動釋放沿用既有流程；無 Scene／Prefab 儲存、無人工數值修改。

## 自動測試

- 修改前：新增 `GrappleAimInputTests.RightMousePreservesGrappleAndManualQStillReleases`，實際失敗在 `False / Attached / Aim 0`：預期 Attached，實際 Retracting，見 [修改前](before-tests.txt)。
- 修改後：相同聚焦 UnityTest **1/1 通過**，見 [聚焦結果](focused-tests.txt)。涵蓋 toggle／hold 兩模式 × PreFire／Shooting／Attached 三階段 × Aim 按下／維持／放開；同 Tick Q 取消與右鍵、原 Q 動能及舊消耗鎖清除。
- 測試由 EditMode Test Runner 啟動，暫時進入 Play Mode 建立有效 Single Runner、Spawn 正式玩家，才注入 Networked 階段並呼叫正式 Simulate。這是受控 Runtime 測試，不是鍵鼠自然操作或獨立多人程序。
- 測試掛點、遮擋遮罩、Q 模式及動能倍率只修改暫時物件；正式玩家 Prefab 的 `releaseSpeedMultiplier = 0` 保留。動能測試另將測試物件倍率設為 1 並注入水平速度，避免把人工關閉動能誤判為程式失敗。
- 完整 `PurgersRegression`：**648 項，647 通過、1 失敗、0 跳過**，見 [完整結果](regression-tests.txt)。包含新輸入測試 1、GrappleEnergyRules 22、GrappleLandingRules 17、GrappleRopeVisual 14，鈎索相關合計 54 項均通過。
- 唯一失敗：`BossStageAssetTests.GameSceneContainsBossAssetWiring`，Boss MapChunk 物件身分引用不符；與 [較早鈎索驗證](../GrappleLanding/README.md) 記載相同。未改動 Boss 程式或資產。
- EnterPlayMode／ExitPlayMode 的 Domain Reload 會使 MCP job 停留 running；以測試內重新註冊的 NUnit 回呼保存實際結果，再於確定 Editor 已返回且測試結束後清除工具的殘留 job。最初探索回傳 total=0 的執行不計入通過證據。

## 受控 Host／Client

同一 Editor 的雙 Runner **10/10 通過**，見 [結果](host-client.txt)。MCP 在 Play Mode 執行 [探針](network-probe.cs.txt)，沿用既有 GrappleVisualRuntimeVerification 的 Runner 啟動及隔離物理場景；Host 與 Client 的 NetworkEvents.OnInput 送出正式 NetInput.Aim，並確認各玩家 PreviousButtons 收到，接著核對原 Player.FixedUpdateNetwork 的結果。

涵蓋 Host 本地按下／放開 Aim、Client Input Authority 的按下／維持／放開、Host State Authority 與 Client 皆保持 Attached、未建立輸入消耗鎖、本地 LineRenderer 持續顯示、晚加入與重連接收 Attached 快照。

探針直接準備 Attached 階段，測試物件暫停拉力加速並關閉落地釋放／遮擋以隔離右鍵條件；這不代表真實命中／落地／遮擋或延遲回滾全部驗收。只修改暫時物件，結束後關閉兩個 Runner，退出 Play Mode 回到未修改的 _Menu。

## 編譯、Console 與還原狀態

最終 Unity `isCompiling=false`、`scriptCompilationFailed=false`；Console 當時讀取 error／warning 為 0 項。中途完整編譯曾顯示既有 DemoController.camera 隱藏成員、TankPreparationRuntimeVerification 參考比較與 BattleHudReferenceLayout 過期 API 警告，均不屬本次修改。結束時無運行中的 Runner、已離開 Play Mode，_Menu 場景 `isDirty=false`。任務變更的 `git diff --check` 與文件連結檢查通過。

## 多人與手動驗收

1. Host、Client 使用同一版程式重建 Session。各自分別測 Q 切換及按住模式。
2. 出鈎前搖、繩頭飛行、直拉與左右側甩期間，按下／按住／放開右鍵：不得因 Aim 收繩；繩索持續顯示。武器功能仍遵守各武器與 Action Gate 的條件。
3. 右鍵與第二次 Q 同 Tick（toggle）、右鍵與放開 Q 同 Tick（hold）：依 Q 規則取消。保留玩家人工動能設定。
4. 實際落地、遮擋、距離／角度、目標失效及 Support Tether：確認既有自動釋放／特殊互動沒有改變。
5. Client 延遲／回滾、晚加入觀看附著玩家及重連：Host 結果與繩索呈現一致，不出現舊 Aim 消耗鎖。

Windows Build、獨立程序 Host／Client、自然鍵鼠與完整物理情境仍需人工驗收；自動測試不能替代這些證據。