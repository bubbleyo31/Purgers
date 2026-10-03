# Menu 子視窗 V2 驗證（2026-10-03）

> 局部核對：Menu UI 呈現、存檔摘要、既有視窗事件。
> 來源：_Menu Scene、MenuWindowStyleBuilder、MenuSaveSummaryView、MenuSaveFlowController、Unity Test Runner 與受控 Play Mode。
> 配置說明：[166](../../166_Menu筆觸視覺與配置.md)。

## 已落地

- _Menu 已保存設定、Continue、多人選單、名稱、刪除、載入與通用 Popup 的 V2 版型；主選單保留與網頁共用的 1560 × 360 Brush.png。
- 新 SaveSlotRowBrushV2 與 WindowPanel.png 已匯入；原 SaveSlotRow.prefab SHA256 仍為 E29F3806AF5071FFFCA85B9288F6950262B06DB6E4527FCDBE13CBC490A1344D。
- MenuSaveFlowController 本輪僅新增可空 selectionSummary 引用，以及列檔數／所選摘要推送。沒有改動 repository、載入、刪除或連線決策。
- 新名稱確認複用原 Blocker 的完成事件。其餘原按鈕事件、Fusion UI 控制器與控制項保留；存檔列引用有意改為獨立 V2 Prefab。
- 最終 Editor 檢查：非 Play Mode、未編譯中、scriptCompilationFailed=false、_Menu dirty=false。
- Menu 階層及 V2 列 Prefab：Missing Script=0、失效 ObjectReference=0；selectionSummary 已接，saveSlotRowPrefab 指向 V2。
- 最終 Console 查詢 0 Error／Warning。Editor.log 曾有既有 BattleHudReferenceLayout 的 spritesheet obsolete 警告，不屬本輪 Menu 變更。

## 原有未儲存內容

開始時 _Menu 有 dirty 修改。先保留原磁碟版本、以 SaveScene(saveAsCopy=true) 保存編輯中 Scene 快照，再保存原 Scene。原有未儲存內容已一起保存，不能宣稱仍維持未儲存狀態。

本機暫存備份位於 Temp/MenuWindowsV2：Menu.disk-before.unity、Menu.unsaved-before.unity、SaveSlotRow.before.prefab、MenuSaveFlowController.before.cs、inventory.json、wiring-before.jsonl。Temp 不是長期版本庫備份。

## 測試

- 缺少摘要元件的初始紅燈：job b88805aba8a24ea7ab53824e9e3c121a，3 項因缺少型別失敗。
- 最終聚焦 job **1d72f6aaa17444e2be6d44b24bddf34e**：MenuSaveSummaryViewTests、MenuBrushButtonTests、MenuBrushAssetsTests，**9/9 通過**。
- 完整 PurgersRegression job **b2c5d8b69f584ac7856943e13f16bede**：progress.completed=**313**、探索 total=328、6 項失敗、result=null。不把 328 當執行數，也不宣稱全套通過。
- 同名既有失敗與 [V1](../MenuBrushV1/README.md) 記錄一致：
  1. BossStageAssetTests.GameSceneContainsBossAssetWiring
  2. DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel
  3. FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies
  4. PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward
  5. PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards
  6. PlayerExperienceRulesTests.RequirementDoublesFromTen

測試後僅調整 Editor 版面／文字／提示樣式；Runtime 摘要與 Flow 程式未再修改。最後編譯與 Play Mode 驗證在視覺調整後執行。

## 受控 Play Mode

由 MCP 呼叫原 Button 事件及既有控制器檢查，不等同完整真人滑鼠／鍵盤操作測試。

- Continue 讀到 25 份現有存檔；選取前不能開始，選取後啟用且摘要為實際 Stage／名稱／等級／日期。選取後清除會清掉舊摘要。
- 列高 146、Content 高度 3890、Viewport 高度 390，捲動清單正常。調整文字框高度後存檔名稱和 Stage 都能渲染。
- 點選刪除要求會出現原確認；按取消後仍有 25 列，未點正式確認刪除。
- Settings 的真實選項、展開／關閉下拉與返回正常；3 個原啟用的提示按鈕保留，PhotonRegion 的提示可開啟、確認關閉。沒有以範本值更改真實設定。
- Party 的代碼欄限制為正式 8 碼；送出 BAD 由原 SDK 顯示 Invalid Session Code，不啟動連線。
- Name 的輸入與新確認會返回，保留原玩家名稱。
- Loading 經既有控制器呈現，截圖文字「正在檢查介面顯示」僅為當次顯示檢查，非實際連線狀態；沒有假百分比。回主選單後 Loading 已隱藏。
- 穩定新一輪 Play 中 Controller 引用存在，Show／Hide 後舊頁隱藏，返回主頁正常。早期在編譯／工具檢查穿插的執行曾遇非序列化 Controller 未初始化，完整退出後重新進入再驗證，沒有修改 Fusion 控制器作為繞過。

## 實際截圖

全為 Unity 1920 × 1080 Play Mode 畫面。

- [設定最終版（含原提示按鈕）](settings-final.png)
- [繼續遊戲／真實摘要](continue.png)
- [多人選單最終版](party-final.png)
- [玩家名稱](name.png)
- [刪除確認（取消，未刪檔）](delete.png)
- [載入顯示檢查](loading.png)
- [設定區域提示 Popup](popup-help.png)

settings.png／party.png 是同輪較早截圖；以 -final 版本為準。

## 邊界

未實際建立新存檔、刪除存檔、Continue 載入、開房、有效加入、連線取消、失敗復原或獨立 Host／Client。畫面檢查與聚焦測試不能當作多人端到端證明。直式手機與其他長寬比未重新驗收。