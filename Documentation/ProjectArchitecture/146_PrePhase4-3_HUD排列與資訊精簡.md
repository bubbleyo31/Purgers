# Phase 4 前置-3：HUD 排列與資訊精簡

> 後續狀態：Phase 4 前置-4 已在 Game 的 MinimapContent 接入小地圖，Runtime 產生內容，編輯期仍是空 RectTransform。SafeHouse 保留佔位。下文描述 Phase 4 前置-3 完成時的歷史基線；現在的地圖行為與配置見 [147](147_PrePhase4-4_小地圖與戰爭迷霧.md)。

> 最後核對：2026-09-19（局部）  
> 核對來源：StageHudController、SafeHouseHudController、LocalPlayerSpeedSlider、ScreenFadeLayer，以及 Game／SafeHouse／_Menu 場景。  
> 相關文件：[UI](100_UI觀戰與聊天.md)、[Ready Check](145_PrePhase4-2_安全屋鍵盤準備與HUD.md)、[轉場](144_PrePhase4-1_共用控制與轉場.md)

## 左上固定排列

Game 的 `StageHUD` 與 SafeHouse 的 `SafeHouseHUD` 都使用 Screen Space Overlay、Default Sorting Layer、Sorting Order **100**；CanvasScaler 參考 1920×1080，寬高折衷 0.5。

`TopLeftColumn` 使用左上錨點與 pivot，位置 `(20, -20)`。三列固定位置，不會因 Ready UI 隱藏而使前兩列移動。

| 順序 | 子物件 | 位置／尺寸（參考解析度） | 責任 |
|---|---|---|---|
| 1 | `MinimapSlot` | `(0, 0)`／224×224 | 小地圖保留區 |
| 2 | `StagePanel` 或 `StageLevelPanel` | `(0, -236)`／500×100 | 等級、數字時間與任務文字 |
| 3 | `ReadyCheckSlot` | `(0, -348)`／500×196 | 安全屋 Ready Check；Game 保留空容器並停用 |

`MinimapSlot/Placeholder` 是暫時底板與標籤；`MinimapSlot/MinimapContent` 是空的 RectTransform。未建立相機、RenderTexture、定位、地圖資料或網路同步。未來將小地圖視覺接入 `MinimapContent`，再停用 `Placeholder`；沿用父 Canvas 排序，不另加高排序 Canvas。保留區不攔截滑鼠。

## 顯示與資料來源

- `StageHudController` 沿用 `StageFlowController.IsNetworkReady` 門檻。時間只有 `MM:SS`，向上取整並封底零；載入中及不限時不顯示時間，不用 `00:00` 假裝有倒數。
- 當前任務整合前往撤離區、等待隊友、全員撤離秒數與成功／失敗等狀態。舊 `ExtractionStatus`、`ExtractionProgress` 物件保留但停用，Controller 引用已清空；序列化欄位保留相容性。
- 安全屋第二列顯示等級與任務，沒有關卡倒數。Ready Panel 僅在 `ReadyCheck`／`Countdown` 顯示；Waiting、LoadingStage 與未 Spawn 時隱藏。TAB／ESC、RPC、7 秒投票、3 秒出發規則不變，HUD 不取得輸入鎖。
- `_Menu/GameplayHUD Canvas/PlayerHUDCanvas/Speed_UI` 沿用既有 Slider 與舊類名，位於下方中央。量條改為 `NormalizedEnergy` 的 0～1 增傷率，不再代表世界速度；文字格式為 `傷害增加N%`。
- 滿能量的 1.5 秒最高強化期間保持滿條；期間再次符合滿能量條件會刷新保持時間但不疊加。滿強化時文字由 24 放大到 36，量條由 380×14 放大到 440×22，藍色改為橘色，文字改為金色。
- `Speed_UI/ExtractionCountdownPanel` 是量條上方的綠色矩形提示。`StageFlowController.IsExtractionAvailable` 集中提供目前可撤離狀態；本機進入選定撤離區後，尚未全員到齊顯示等待文字，`ExtractionHoldTimer` 執行時顯示 0.1 秒精度倒數。左上任務文字與撤離倒數保留。
- `PlayerGrappleMomentumEnergyDebugHUD` 保留詳細診斷功能，但腳本預設、KCC_Player Prefab 與 Game 內既有覆寫均關閉。可手動開啟診斷，正式 UI 不依賴 Development Build。

## 黑幕

沿用 `ScreenFadeLayer` 的最高既有 Sorting Layer／32767。玩家 Canvas order 0，左上兩個 Canvas order 100，都低於黑幕；不新增轉場或輸入鎖系統。手動開啟的 IMGUI 診斷面板仍在黑幕顯示或 AllInput 鎖定時停止繪製。

## 驗證

`HudLayoutTests` 涵蓋數字倒數格式、Ready 顯示 Phase、兩場景三列順序與間距、小地圖空接入點、HUD 排序與速度根節點 Canvas 錨點。先確認最高強化規則與新版場景契約會 RED，再完成最小實作；最終 **PurgersRegression 75／75 通過**（job `e93bc61248e2483f8330aa2e4c2a54b8`）。

- 最終 Windows Development Build `build-652b92ecb2`：0 errors、0 warnings；輸出為 `Builds/Phase43Detail/Purgers.exe`。
- Editor Host＋獨立 Windows Client：Ready、7 秒逾時、整票取消、單人取消、倒數中取消、重新準備及切入 Game 通過。兩端讀回本機 Input Authority、Canvas 直屬底部中央增傷 UI、`傷害增加0%` 及純數字關卡時間。
- Host 首筆時間採樣早於 HUD Update，讀到空字串；完整畫面後重取為 `09:12` 並通過。驗證器已補上 0.5 秒表現刷新等待，Gameplay Timer 不變。
- Host 1920×1080、1366×768 截圖確認左上排列與速度 UI 不碰到血量 UI。Client 以 1280×720 完成資料／錨點查核；Hidden 視窗截圖為黑色，不作 Client 視覺通過證據。
- Host 執行期強制進入最高強化後，讀回 `IsMaximumBoostActive=True`、剩餘 1.50 秒、量條 1.00、`傷害增加100%`、字級 36、量條 440×22、橘色填滿與金色文字。截圖另確認量條上方綠色 `撤離倒數 2.4` 面板及左上資訊可同時保留；強制狀態只供表現驗證，不寫回場景。
- 完整黑幕截圖確認 HUD 被覆蓋。Game／SafeHouse／_Menu 的 Missing Script 與破損引用均為 0；最後 Editor Console Error／Warning 為 0。
- Client 切場有 8 筆 Fusion `spawned and despawned in the same tick` 警告，來自本次未改動的 Player／職業／能力生命週期。警告未解決，不宣稱 Runtime 零警告。成功連線測試的 Player log 沒有 Exception／Error。
- 首次受限程序因 PlayerPrefs／網路限制失敗；另一次 Client 早於 Host 開房出現 GameNotFound。允許網路並於開房後重啟成功，原始紀錄保留失敗。測試過程誤用依賴 MenuConnection 的 Gameplay Menu Show 曾造成診斷例外，已改為僅隱藏測試主選單，最終 Editor Console 無此例外。
- 未額外覆驗 late join／reconnect；TAB／ESC 驗證使用相同請求入口，實體鍵仍需手動複驗。

證據：[Host](Validation/Phase4-3/Host.txt)、[Client](Validation/Phase4-3/Client.txt)、[Ready 截圖](Validation/Phase4-3/Host-ReadyHUD.png)、[關卡截圖](Validation/Phase4-3/Host-StageHUD.png)、[1366×768](Validation/Phase4-3/Host-1366x768.png)、[黑幕](Validation/Phase4-3/Host-BlackOverlay.png)、[驗證摘要](Validation/Phase4-3/verification.txt)、[交接摘要](Validation/Phase4-3/HANDOFF.md)。

手動複驗：從 _Menu 開始 Host／Client，確認左上小地圖佔位在最上方；安全屋投票顯示第三列、取消後隱藏；切入 Game 後只顯示數字時間與任務；動能變化使下方增傷量條與傷害文字刷新，達峰後維持 1.5 秒滿條與最高強化樣式；進入可撤離區後確認綠色等待／倒數提示；轉場黑幕不能透出任何上述 UI。直接單開 Game 不會產生原本屬於 _Menu 的持續玩家 HUD。



