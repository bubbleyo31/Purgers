# TAB 隊伍資訊窗驗證

最後核對日期：2026-10-18（本功能局部）。規則與 Inspector：[106](../../106_TAB隊伍資訊窗.md)。

## 交付狀態

已實作 Runtime、Ready 共鍵與第五操作模式、Combat／Look 本機鎖及 Host 過濾、正式設定引用、動態成長排列與 tooltip。原圖示／字體／武器 PNG 未重製；未改 Scene／Prefab 接線。等待前順位「討論怪物暈眩狀態呈現」完成並明確釋放 Editor 後，才開始 Editor 工作。

## 已完成的驗證

- 測試先失敗：`4823900d361e4fe384a1629f555ccdfe`，Fire 未被封鎖，確認紅燈後加入 Combat 過濾。
- 最終聚焦 **21／21 通過**，0 失敗、0 略過，job `6d2a9bc7fe5d4d1588ba08295f796e59`；[原始結果](focused.json)。包含 hold／toggle／投票交接、防穿透、ESC、1／2／5／8／15／64 項等距排列、冷卻比例、舊設定保存、四列與空槽、無底板數字、64-byte 八槽死亡快照、Catalog token 唯一性及銷毀武器引用。
- 同 Editor、實際 Photon Host／Client、晚加入及觀察端重連 **51 項檢查通過**；[逐項結果](host-client.txt)。實際載入正式玩家與能力 Prefab，使用原權威裝備／成長入口。驗證名稱、HP、換武器、裝備技能、成長、冷卻秒數／分母、Host 拒絕被封鎖的視角與攻擊、移動持續、冷卻不中斷，以及死亡後保留技能與武器顯示。冷卻測試只暫時設定 Runtime 60 秒，不保存資產。
- 正式 uGUI 元件以 PreviewScene + Camera.Render 離屏檢查；[預覽](ui-preview.png)。示例資料為三人加一空位、被動 1／2／8 個及冷卻 8／3／12 秒，並非即時實戰截圖。確認武器完整輪廓、四列等高、中文標題位置及整格冷卻數字。
- Unity 完成編譯，未見本功能新增 C# error。既有編譯警告：DemoController CS0108、TankPreparationRuntimeVerification CS0252、LocalHealthSegmentView.cornerRadius CS0414、BattleHudReferenceLayout CS0618；保留原狀。MCP 重連警告、既有測試最小 Player 配置警告與回歸失敗另列，不宣稱 Console 全零。

- 額外以正式設定頁元件確認五種操作模式，隊伍資訊「切換／按住」按鈕都更新 Draft；以 EventSystem 原生 PointerEnter／Exit 確認有內容顯示描述、離開隱藏、空格不出提示。未派送 OS 輸入。
- 最後限制 HP／護盾數字寬 116（字級 12～18）與武器名稱寬 90（11～15），避免欄位重疊；其後重跑上述 21 項聚焦與完整回歸。

## 完整回歸

`PurgersRegression` job `c75f03f4212d451db5003ea0e145f189` 經過 Domain Reload；MCP 進度可能停留舊值，採用同次真正 RunFinished 回呼保存的 [XML](regression.xml) 與 [摘要](regression-summary.txt)。

結果：**1111 通過、4 失敗、1 略過，共 1116 項**。失敗項目：

- `BossStageAssetTests.GameSceneContainsBossAssetWiring`
- `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`
- `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`
- `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`

前順位驗證已記錄 Boss 接線、Flytrap 參數、Reflector Prefab 與 reload camera 四項同名失敗；本次沒有修理這些範圍外資產／測試。不可將本功能聚焦通過稱為完整回歸全綠。Explicit 的多人輸入測試略過另保留在 XML 中。

## 尚待自然驗收

1. 正常主選單加入遊戲，按住 TAB／改成切換模式；移動、滑鼠懸停、放鍵後重新攻擊，以及 ESC 只關窗。
2. 安全屋實際觸發準備投票，確認原本按住 TAB 不會自動準備、出發倒數共鍵優先、投票結束不自動開窗。
3. 1～4 人、不同解析度、長名稱、八技能、多行被動捲動；以真實鍵鼠確認間距、字體與工具提示舒適度。
4. 獨立執行檔雙端、真實擁有者斷線恢復／重生、網路延遲與 Build。本輪重連證據為觀察端重新加入，不能等同所有保存／還原流程驗收。

本輪未操作桌面鍵鼠或呼叫會 Focus 的 Scene View 截圖；正式元件離屏及同 Editor 多 Runner 不取代以上項目。

收尾：已退出 Play Mode，Runner 數量 0、編譯閒置，_Menu 場景無 dirty；因 Domain Reload 留下的 MCP stale job 已清除。保留本次檔案與差異於 Temp/TeamInfo，隔離來源快照 worktree 已封存。
