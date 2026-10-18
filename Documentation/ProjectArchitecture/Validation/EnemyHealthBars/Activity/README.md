# 敵人血條生命變化期限驗證 — 2026-10-14

已實作初始滿血隱藏、生命數值變動後顯示、本機連續 10 秒無變化隱藏。維持既有固定百分比分格、遮擋、畫面邊界與 Boss 排除；不變更 Scene／Prefab 接線或權威傷害邏輯。

## EditMode

- RED：[red.json](red.json)，15 項因未提供變化期限行為而預期失敗；最初未匯入測試的 total=0 工作未列為有效證據。
- GREEN：[focused.json](focused.json)，57/57。新增 15 項涵蓋初始滿血、精確 10 秒界線、相同快照、不同比例／同百分比生命變化、小額傷害、續期、回血至滿血、初次殘血、無效／死亡快照、重設與個別計時。
- 完整 [PurgersRegression](regression-tests.txt)：878 passed、1 failed、1 skipped，實際執行 879 項。失敗仍為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring；跳過為 Explicit 的 AbilityToggleInputTests 網路測試。未宣稱全綠。
- MCP 全套工作在 Domain Reload 後停留舊進度，使用既有跨 Reload TestRunner 收集器；確認實際 TestRunner 已停止後清掉過期工作。

## 受控 Host／Client Play Mode

入口：隔離、沒有 Runner 的 Play Mode 執行 `EnemyHealthBarRuntimeVerification.BeginActivity(true)`。

[host-client.txt](host-client.txt)：19 項 PASS，包括兩端啟動與首 Tick、初始滿血隱藏、Host 權威傷害顯示、真實等待超過 10 秒後隱藏、Host 已逾時時 Client 晚加入取得殘血顯示窗、Client 無生命權威、不同端期限隔離、HUD 停用／恢復不重設期限、再次扣血重現、第二次扣血延長、回血到滿血顯示與再逾時、死亡立即隱藏。

時間檢查使用真實非縮放等待（10.5 秒及 5.5 秒交錯傷害）；精確 10.000 秒界線另由 EditMode 純規則測試確認。測試對象為隔離盒形 TestDamageReceiver／Target 與正式 HUD Prefab，同 Editor 多 Peer，不代表獨立程序多人或自然關卡操作。本輪未新增重連實測；新 Spawn 的本機初始策略由純規則與晚加入覆蓋。

## 編譯與資產

無新編譯錯誤。編譯階段曾見既有 DemoController.camera、TankPreparationRuntimeVerification 參考比較、BattleHudReferenceLayout 過時 API 及 Player Required Components 警告；本輪沒有修改這些來源。最終受控 Play Mode Console 查詢為 0 error／warning，但不抹除前述編譯警告。

本輪僅新增本機時間狀態、Target 接入、測試與文件；既有 _Menu／9 個一般敵人接線沿用，Boss 保持排除。已依使用者「收錄」授權新增關鍵詞索引 ENEMY-HEALTH-BAR。