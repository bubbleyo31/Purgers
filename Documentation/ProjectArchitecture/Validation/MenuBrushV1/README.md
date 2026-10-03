# MenuBrush V1 驗證（2026-10-03）

## 實際成果

- 已保存 `Assets/Scenes/_Menu.unity` 的 Menu Prefab Instance 覆寫；保留 6 個可見按鈕。
- [最終 Play Mode 主畫面](main-final.png)；[Continue 移入高亮](hover-continue.png)。
- 本輪前後所有既有 Button persistent event 與 MenuSaveFlowController object reference 逐項相同。
- `Menu.prefab` 與本輪開始前備份逐位元一致；Photon 原碼／控制器無本輪改動。
- Scene YAML 文件塊比對：所有原有塊皆保留；原有塊只改 Menu 的 PrefabInstance override，新增本版元件／視覺塊。未改其餘玩家 HUD 等場景物件。

## 測試

- 初始按鈕測試 4 項因尚無 MenuBrushButtonVisual 失敗，job `cf76a4552f6840b8a1791581e60b4e68`。
- 新增動畫保存測試先取得 Show state 缺失失敗，job `9b879a5995a841509ba9905d12c08d57`。
- 最終聚焦 **5/5 通過**，job `e182f4096ca943bfb8b371b3fa4b1265`。
- 最終完整 PurgersRegression，job `747f4720d45b49a18e769903237c10de`：progress.completed = **309**、探索 total = 324、6 項失敗、result = null。因此不將 324 宣稱為執行數，也不宣稱全套通過。
- 失敗與 130／165 的既有記錄同名：BossStageAssetTests.GameSceneContainsBossAssetWiring；DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel；FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies；PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward、OneLargeGrantCanQueueSeveralRewards、RequirementDoublesFromTen。

## Play Mode 證據

透過 Unity MCP 與既有 Button／EventSystem 做受控 UI 驗證：

- 主畫面 CanvasGroup alpha = 1，控制器 HasState(Show/Hide) 均為 true。
- 指標移入後黃色筆觸展開；受控按下時 Background Scale = 0.975，Button Scale = 1，點擊範圍保持固定。
- Continue 原事件可開啟正式 Overlay，原 Back 可返回；沒有選檔載入或刪除存檔。
- Settings／Party 原事件可開啟；淡出後主畫面 activeInHierarchy = false。原返回事件讓主畫面重新啟用、alpha = 1。
- 玩家名稱原事件可開啟輸入，Blocker 可關閉；保留原名稱。
- 最終已退出 Play Mode，_Menu 無未保存 Scene 修改；編譯狀態為非編譯中／未失敗，最後 Console 0 Error／Warning。
- Missing Script = 0；主畫面序列化 ObjectReference 壞引用 = 0。

未執行 QuickPlay 新建存檔、Application.Quit、實際 Host／Client 連線、晚加入／重連或手機直式 Unity 畫面。受控 UI 注入不是人工滑鼠逐項驗收或多人端到端證據。這版保留原子頁版型，並非移植網頁的所有示範視窗功能。