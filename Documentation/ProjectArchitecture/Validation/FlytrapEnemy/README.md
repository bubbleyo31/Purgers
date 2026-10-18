# 定點捕食植物驗證紀錄

日期：2026-10-12。本輪只處理捕食怪，未改 Scene／MapChunk／既有生成器候選。

## EditMode 與編譯

- TDD：規則尚未存在時實際執行 18 項，全部因規則類別未實作而預期失敗。job：`335118adc778416ab5c7e8f44bb29d38`。更早第一次 `total=0` 是未完成 import，不計入測試證據。
- 聚焦：**21／21 通過**，涵蓋靜止／站走／蹲行／射擊／鉤索的純判定、房間人數與最大生命比例、首個間隔、不能整除的尾段、間隔大於總時長、累計上限、Stationary 組裝、擊退開關、掛點、預設時間與引用。job：`a5fd094118bc442cb26d5cc7f80cb2df`，原始 [focused.json](focused.json)。
- 完整 `PurgersRegression`：持久結果收集器回報 **714 passed、1 failed、1 skipped**。唯一失敗是既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`（Boss 資產引用），與本次捕食資產無關。略過為既有 Explicit Photon Toggle 測試。原始 [regression.txt](regression.txt)。
- MCP 全套工作在 Domain Reload 後停留 completed=0／running，不能拿它宣稱通過。重跑前以既有 `EnemySuicideTestResults.Arm` 註冊跨重載收集器，取得上述完整結果；collector job：`20812e7a1a194f5bb3179bc36dcbb713`。確認 Editor 已結束後清除 orphan job。
- 最終編譯檢查 `isCompiling=false`、`scriptCompilationFailed=false`；Console error／warning 查詢 0。編譯過程的既有 DemoController.camera CS0108 及 TankPreparationRuntimeVerification CS0252 與本次分開；不宣稱專案歷史警告已全部清除。

## 隔離 Play Mode

入口：`EnemyFlytrapRuntimeVerification.Begin(false)`；在沒有其他 Runner 的 Play Mode 明確啟動，不自動執行、不保存場景。

實際使用正式玩家／敵人 Prefab、既有 Combat Decision、KCC、PlayerHealth。原始 [single.txt](single.txt)。已驗證：

- 安靜站立／蹲行安全；牆壁拒絕感應；完整身體路徑拒絕欄杆。
- 射擊通知引發抓取；獨占玩家；另一隻不能搶走；360 度後方同樣能抓。
- 真正抵達嘴邊才開始計時，拉動不扣血；0.5 秒首次扣 5% 最大生命，五秒累計 50%。
- 受困時輸入移動、跳躍、射擊、Q／E／F 不會離開嘴邊。
- 動畫吞食進度在中途維持 0～1 的部分值。
- 存活釋放後 10 秒冷卻，期間射擊通知不能再抓。
- 擊殺當下解救，不等死亡動畫；不補扣後續傷害，保留其他 System 鎖；Despawn 不遺留捕食鎖。
- 測試用 0.5 秒／0.17 秒的不能整除情境仍累計精確半血，不修改 Prefab 的正式預設時間。

修正過的失敗：同 Tick 蹲下＋移動曾讀到舊姿態，已改讀該 Tick 蹲下意圖。牆壁測試最初在架牆前就放開蹲下並帶著慣性移動，已修正測試前置順序，不以刪除檢查讓測試通過。

## 受控 Host／Client

入口：`EnemyFlytrapRuntimeVerification.Begin(true)`；同 Editor 的兩個獨立 Runner，透過 Photon 建立隱藏測試房。測試專用 Config 副本使用 Multiple Peer，不改專案全域設定。原始結果交付於 [host-client.txt](host-client.txt)。

測試涵蓋：Host State Authority 抓取、晚加入取得 Captor／受困狀態、Client 不能寫入釋放、重連重新取得受困狀態、解救同步、Client Input Authority 的玩家不能靠輸入逃脫、房內兩人時累計扣除 100% 最大生命並死亡，以及相同 10 秒冷卻。

晚加入／重連情境把單次吞食延長為 300 秒，只修改隔離測試物件，避免連線時間超過原 30 秒測試窗口；多人致死情境使用 1 秒／0.2 秒加速測試。預設單人 5 秒／0.5 秒另有上節的實測。

## 剩餘驗收邊界

- 尚未做獨立程序／獨立建置的 Host＋Client、自然 Encounter 出生、實際關卡轉場、各技能逐一交互、延遲／丟包／重模擬壓力測試與效能剖析。
- 尚未配置正式 Scene／MapChunk／生成區；需依 [93 設定步驟](../../93_定點捕食植物.md) 手動配置。
- 未建模、未製作動畫 Clip／Controller，只提供方塊與接口；第一人稱拉動觀感、釋放位置與正式美術仍需人工驗收。
- 根據已確認路徑規則，未通過完整膠囊檢查時不指定。指定後新動態障礙的安全釋放與回程阻擋備援有程式保護，未宣稱所有地圖幾何已驗證。

最終資產修正：補齊既有 GrappleInteractionTarget 描述元件，關閉 Support Pull／Tank Gather。修正後重跑 21 項聚焦、完整回歸與單人隔離 Play Mode，結果如上；單人生成／清理 Console 無 error／warning。受控多人紀錄來自此描述元件修正前、相同 Gameplay 程式的測試。
