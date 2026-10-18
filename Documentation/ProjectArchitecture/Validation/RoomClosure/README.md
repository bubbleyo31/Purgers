# 玩家離開與房間關閉驗證

最後核對日期：2026-10-17（局部）。
來源：NetworkChatSystem、MenuConnection、MenuConnection.SessionEnd、MenuSessionEndState、MenuConnectionBehaviour.RoomClosed。

## 已確認需求與責任

- Client 離開：Host 使用已驗證名稱廣播「名字 離開了遊戲」，剩餘玩家在原聊天窗收到一次通知。
- Host 關房：原本已連線的 Client 結束 Session 後顯示置中「房間已關閉」，唯一按鈕「回到主畫面」。
- 自行離開、取消／失敗的加入、Host 本身退出不顯示 Client 關房通知；兩種斷線回呼去重。
- 連線終止提示涵蓋已建立 Session 的非主動中斷，不區分 Host 主動關房、異常退出或網路問題。
- 沿用原本聊天、連線管理與 V2 Popup；未修改 Photon 套件、Scene、Prefab、存檔規則或玩家權威。

## 工作隔離

依使用者要求，其他 agent 使用 Editor 時，先在 Temp/RoomClosure 只準備檔案與離線型別檢查，沒有操作 Editor 或寫入 Assets。確認「調整升級所需經驗」完成後才匯入測試與正式程式。

## EditMode 與編譯

- 修改前：3 個聚焦測試實際失敗，缺少名稱保存及離開訊息接收入口；見 red.xml、red.txt。
- 修改後：10/10 聚焦通過；見 focused.xml、focused.txt。
- 前段曾因新測試未匯入得到 total=0；已明確排除，不當作通過。
- Unity 與 Fusion Weaver 已完成編譯；編譯後 Console 查詢沒有 error。離線型別檢查不替代 Unity 編譯。
- 完整 PurgersRegression：本次 RunFinished total=1026，1023 通過、2 失敗、1 略過；見 regression.xml／regression.txt。失敗為 BossStageAssetTests.GameSceneContainsBossAssetWiring 與 ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt，與當日修改前 ExperienceTable 紀錄相同。此輪完整回歸在最後的 Popup「只隱藏啟用視窗」防護之前；該呈現修正另由後續 Runtime 驗證。

## Runtime 與視覺

RoomClosureRuntimeVerification.Begin() 已完成 16/16 受控檢查，見 [runtime.txt](runtime.txt)。隔離三 Runner、不使用正式存檔，確認 Host 與留下的 Client 收到同一則離開通知、主動離開不跳提示、Host 關房後 Client 清理連線、文字、唯一按鈕、游標與返回主選單；共用 Popup 的文字及位置成功恢復。已查看 [1920×1080 實際畫面](popup.png)，面板／訊息／按鈕置中，未裁切。最後一次呈現防護修正包含於本次測試。受控測試會為同程序的多個 chat Spawned 暫時分配既有 local singleton，驗證可靠廣播；不代表 LocalChatPanel 在同程序多個玩家的綁定，亦不替代獨立程序測試。

## 正式多人驗收

1. 使用同一版本的 Host＋兩名獨立 Client，先確認三人加入名稱。
2. 一名 Client 按離開：Host 與留下的 Client 各出現一次正確名稱的離開訊息，房間繼續運作。
3. 該 Client 重連後再離開：可再次通知；死亡／重生、Game↔SafeHouse 切換不可出現假的離開訊息。
4. Host 主動關房：留下的 Client 停止遊戲，視窗置中、滑鼠可用，僅「回到主畫面」能操作。
5. 點按返回：主選單可操作並可重新加入／建立測試房；再次開一般 Popup，文字／按鈕排版恢復。
6. 取消加入、錯誤房號、自行離開與關閉應用程式不顯示關房視窗。另驗證關房恰逢場景載入時黑幕／輸入鎖會正常釋放。
7. 獨立程序自然斷線／重連、跨場景關房與最終美術須另行驗收；聚焦測試不能證明這些情境。

索引：本次尚未獲收錄授權；建議關鍵詞「玩家離開、離開聊天、房間已關閉、Host 關房、Client 斷線」，正式規則保存於 100／110，確認後再加入索引。


收尾：受控測試後 Console error=0；已退出 Play Mode。_Menu、Game、SafeHouse 的 SHA256 與本輪開始前相同，未保存 Scene／Prefab。所有新增腳本的 meta 由 Unity 自動匯入產生。
