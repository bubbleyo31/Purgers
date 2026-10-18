# 鈎索飛行雙擊側移驗證

日期：2026-10-13。來源：目前工作區，包含先前已存在的捕食怪／移動修改；未宣稱整個工作區由本次建立。

## 證據

- 紅燈：GrappleSideDashRulesTests 先建立，實際執行 14 項，均因 GrappleSideDashRules 尚未存在而失敗。MCP job：aff011a1b2c84685a5c555cf2f1b2aba。
- 聚焦 EditMode：72／72 通過，含新側移 14、既有能量、落地及 ActiveControlRules。MCP job：ea042a1888ed427a90f8e4c61b9310be；[完整聚焦紀錄](focused-tests.txt)。
- 完整 PurgersRegression：740 通過、1 失敗、1 略過。失敗為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring；非本次 Dash 回歸。MCP job：3e2cdecb74dc432986aaa0bf7c5566c3；Domain Reload 使 MCP 進度停在先前狀態，正式結果取既有重新註冊的 TestRunner 回呼，見 [完整紀錄](regression-tests.txt)。
- 隔離 Single Runner：15 個 Check 通過；額外側移 x=-20.001 m，前進 z=11.192 m、下落 y=-4.717 m；動態側向速度未殘留。[單人紀錄](single.txt)。
- 同 Editor 受控 Host／Client：最後版本 28 個 Check 通過，並完成非同步等待的 Client 輸入由 Host 接受、施放序號／能量收斂、晚加入及重連。包含超時／交替輸入、零能量、共用冷卻、斷繩、合成路徑碰牆、移動鎖中斷、與閃現雙向互斥。[多人紀錄](host-client.txt)。
- Editor 已確認原 KCC_Player 的新增序列化欄位可讀：距離 20、速度 150、窗口 0.25、冷卻 1、skin 0.02。沒有新增需掛載元件，沒有自動保存 Scene／Prefab。
- 編譯時未出現本次新增程式的錯誤；曾出現第三方 DemoController.camera、既有 TankPreparationRuntimeVerification 比較及 BattleHudReferenceLayout 過期 API 警告，與本次修改無關。

## 驗證方式與限制

`GrappleSideDashRuntimeVerification.Begin(false/true)` 僅限 Editor Play Mode 且沒有現存運行 Runner。工具建立隔離 Single 或 Host／Client Runner；以正式 NetInput → Player → PlayerGrapple → KCC 路徑施放，但飛行初始狀態、測試能量、部分連繩狀態由測試反射設定，並非自然出鈎／實體按鍵證據。測試版互斥案例只改 runtime instance 的速度，不保存 Prefab。結束會關閉 Runner 並清理臨時物件。

普通飛行／碰牆案例使用正式 20 m／150 m/s。距離測量的前進／下落包含雙擊等待時間，不能將 z/y 當成只有 0.133 秒 Dash 的位移。

實體鍵鼠雙擊、第一下 A/D 原有慣性轉向手感、高延遲／丟包／極低 FPS、斜牆／複雜轉角、獨立程序 Host／Client、Build 與最終手感仍需人工驗收。尚未加入 Dash HUD 或 VFX。不可將同 Editor 受控多人稱為獨立多人驗收。

手動配置及驗收見 [32 鈎索飛行雙擊側移](../../32_鈎索飛行雙擊側移.md)。新規則索引條目尚待使用者確認收錄；本次沒有自行改寫定案索引。

## 最後 Editor 狀態

2026-10-13：Play Mode 已停止、編譯與 Domain Reload 均完成、Console 讀取 0 errors／0 warnings、_Menu dirty=False；KCC_Player 全階層 Missing Script 數為 0。以上是本輪最後查核的 Editor 狀態，不表示此前未曾出現既有警告。失效 MCP TestRunner 工作已清除。
