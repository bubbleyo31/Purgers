# 區域路由與連線恢復驗證

日期：2026-10-17（Asia/Taipei）。Unity 2022.3.62f1，既有 Purgers Editor，Photon Fusion Multiple Peer。使用者已核准修正及收錄 MENU-CONNECTION-RECOVERY。正式規則：[10_網路連線與輸入](../../10_網路連線與輸入.md#2026-10-17-區域路由與可恢復連線局部核對)。

## 結果

| 層級 | 結果與證據 |
|---|---|
| 先失敗 | 最初三個針對性測試實際執行並如預期失敗，見 [initial-red-result.json](initial-red-result.json)。首次 total=0 不列入證據。 |
| 編譯 | 完成，scriptCompilationFailed=false；本次檔案未出現新編譯警告。見 [editor-final-result.json](editor-final-result.json)。 |
| 針對性 EditMode | **33/33 通過**，含新增 14 項、既有 MenuSessionEnd／MenuSaveFlow／ConnectionLoadingSorting，見 [focused-result.json](focused-result.json)。 |
| 完整 PurgersRegression | **1040 項：1035 通過、4 失敗、1 略過**。新增 14 項全通過。以新鮮 RunFinished XML 為準：[regression.xml](regression.xml)、[摘要](regression-summary.json)。MCP job 曾停留舊進度，不將其當作測試完成。 |
| 真實 UI 取消／逾時 | 注入不回傳的 Lobby Task；實際 Loading 取消按鈕的 UI raycast 命中且 onClick 可完成取消，回主選單、解除輸入鎖。1 秒測試期限到期後回主選單並提示。見 [playmode.txt](playmode.txt)。 |
| 真實 Photon StartGame 取消 | 既有 MenuConnectionBehaviour 的正式 Create/event 路徑，指定 asia 啟動約 100ms 後觸發實際取消按鈕；回主選單、新存檔回滾；同一 MenuConnection 隨即重新連線成功。見 [real-cancel.txt](real-cancel.txt)。 |
| 同 Editor 真實 Photon Host／Client | Best Host 實際 asia，代碼解碼同區；Client 原偏好 eu 仍正確加入；離開後同一 Client 重新加入成功；Host 關閉仍觸發既有 RoomClosed。結束零 Runner、AllInput=false。見 [playmode.txt](playmode.txt)。 |
| 下拉選單／資產 | 真正 Settings TMP_Dropdown 包含 asia；保留前五個索引，經 Editor 追加區域。見 [settings-result.json](settings-result.json)。 |
| 測試後 Editor | 已退出 Play Mode，_Menu dirty=false、零 Runner；_Menu.unity 與 Menu.prefab 測試前後 SHA256 相同，見 [scene-hashes.json](scene-hashes.json)。未儲存場景。 |

完整回歸的四項失敗均位於本次未修改的測試／資產範圍，未以修改期望值掩蓋；沒有本輪修正前完整基準可證明全部原已失敗，因此此處只記為非本次修改範圍，**完整回歸尚未全綠**：

- BossStageAssetTests.GameSceneContainsBossAssetWiring：MapChunk_Boss 參考不相同。
- EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules：預期 0.5、實際 0.25。
- EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners：預期大於 10、實際 10。
- ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt：測試反射 Field helper 發生 NullReference。

既有編譯警告含 DemoController.camera 隱藏成員、TankPreparationRuntimeVerification 參考比較、BattleHudReferenceLayout spritesheet obsolete。多人 Play Mode 的 Editor.log 另出現 AttackProfessionRuntimeDriver 找不到 AttackFocusAbility，以及 Teammate 缺少 CanvasRenderer 的 MissingComponentException；這些所在檔案未修改，未將本輪結果描述為零執行期錯誤。

## 實作與驗證邊界

沿用 MenuConnection、既有 LocalSceneTransition、存檔及 RoomClosed 責任；沒有另建玩法或轉場系統。正常啟動預設總期限 30 秒，可在 MenuConnectionBehaviour Inspector 調整；取消／逾時清理最多另等 5 秒。保留原區域索引並追加 asia 等區，Best 取得實際區域後編碼；舊 Best 代碼無法還原實際區域，要求同版重新開房。

實測 Best 選區約 4.25 秒，Host 啟動合計約 22.21 秒；Client 初次加入約 16.65 秒、重新加入約 16.49 秒。這是同 Editor 單次觀察，含場景載入，不代表效能改善基準；本次修正錯區及無限等待，沒有證明場景載入或所有網路延遲已改善。

取消／遲到 Task／重新嘗試隔離有針對性測試；5 秒 Shutdown fallback 有程式保護，但未注入實際 Photon Shutdown 永久卡住或原生場景載入永久卡住。期限依賴 Unity 主執行緒可更新，不能中斷同步載入凍結。

此輪是同 Editor Multiple Peer 與真實 Photon Cloud，不是兩部電腦／兩個獨立 Windows 程序。未做新 Build、外網雙端、封包遺失或斷網恢復驗收，也未驗證全部新增區域的可達性。Host／Client 連線與重入已覆蓋，但沒有進行完整權威玩法回歸。現有 M:/Test/PurgersAlpha1017 執行檔不會自動更新；交付新 Build 後兩端需使用同版。

## 重跑入口

1. Test Runner 執行 MenuConnectionRecoveryTests、MenuSessionEndTests、MenuSaveFlowTests、ConnectionLoadingSortingTests。
2. 執行完整 PurgersRegression；如需 XML，先在 Editor 呼叫 MenuConnectionRecoveryVerification.CaptureNextRun()，RunFinished 寫入 Temp/connection-regression.xml。
3. _Menu Play Mode 呼叫 MenuConnectionRecoveryVerification.Begin()；此工具使用 Temp 的隔離存檔，進行真實 Photon 連線並在 finally 清理。結果寫入 Temp/connection-playmode.txt。
4. 獨立 Build 手動確認 asia 下拉選項、Best 房碼跨偏好加入、連線途中取消、逾時關閉提示後重試、Host 關閉提示；檢查無殘留 Runner、黑幕或輸入鎖。
