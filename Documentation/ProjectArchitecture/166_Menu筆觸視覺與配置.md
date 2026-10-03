# Menu 筆觸主選單與子視窗 V2 配置

> 最後局部核對：2026-10-03  
> 核對來源：`Assets/Scenes/_Menu.unity`、`Assets/Scripts/MenuImplementation/MenuBrushButtonVisual.cs`、`Assets/Scripts/Editor/MenuBrushStyleBuilder.cs`  
> 相關文件：[110 選單](110_場景工具與選單.md)、[141 Continue](141_Phase1B_Menu存檔手動配置與驗證.md)

## 範圍與所有權

這版移植已確認網頁主畫面的 MenuV2 配圖、右側六個入口與筆觸互動。四個主按鈕為「快速開始／繼續遊戲／多人選單／離開遊戲」；右上保留玩家名稱與設定。快速開始為芥末黃，其餘深橄欖色，移入／鍵盤選取時由左向右展開黃色筆觸。

原始 MenuV2.jpg、Logo、Menu.prefab 與 Photon 套件碼未更換。修改落在 _Menu 的既有 Menu Prefab Instance。Settings／Party／Continue／NameInput 等子頁已套用下方 V2 版型並保留正式功能，未導入網頁示範資料、假連線、網頁音效、額外 Escape handler 或離開確認流程。

- Button／EventSystem 保有點擊與導航責任；沒有新的 Input 採集層。
- MenuConnection／MenuSaveFlowController 保有開房、存檔、刪除、連線與失敗處理責任。
- MenuBrushButtonVisual 不持有網路物件、不讀 Networked、不呼叫或替換 On Click。
- 原按鈕事件與 MenuSaveFlowController 引用套用前後逐項比對相同。
- 主選單 Animator 仍由 FusionMenuUIScreen Show／Hide 驅動。專用資產只動畫根 CanvasGroup alpha，避免原控制器覆寫新位置。

## 階層與資產

主畫面根：`GameplayHUD Canvas/Menu/FusionMenuViewMainMenu`。

- `MainButtons/QuickPlay、ContinueGame、PartyMenu` 與 `RightButtons/QuitButton` 保留原 Button。
- 每個 `Background` 換成筆觸 Sprite，新增 `BrushHighlight`、`BrushArrow` 和 CanvasGroup。
- 原 Button Image 為透明但保留 Raycast Target；所有筆觸、文字、箭頭只做裝飾，不擋射線。
- 四個舊按鈕 Animator 停用，Button Transition = None，避免兩個視覺寫入者。
- MainButtons／RightButtons 的 GridLayoutGroup 停用；三個按鈕群組完整 Stretch 且 Anchored Position = 0。
- Background 新增 `MenuRightShade` 與 `MenuVersionLabel`。原版本 Plugin 保留並改指向新文字；原 VersionLabel 的文字元件停用。
- `Assets/_Project_Assets/UI/MenuBrushV1/` 保存 Brush.png、RightShade.png、UtilityPlate.png、含內嵌 Show／Hide 的 MenuBrushFadeV1.controller。
- 現有 FusionMenuImageFitter 繼續負責 MenuV2 等比 Cover，不新增背景適配元件。

## 排版入口

參考 Canvas = 1920 × 1080，沿用原 CanvasScaler（Match Width = 0）。以 Anchor Y 比例支援一般橫向畫面高度差異；這版 Unity 不包含網頁的直式手機版排版。

| 物件 | Anchor Y min / max | 寬度 | 右側距離 |
| --- | --- | --- | --- |
| QuickPlay | 0.48 / 0.59 | 500 | 52 |
| ContinueGame | 0.35 / 0.46 | 500 | 52 |
| PartyMenu | 0.22 / 0.33 | 500 | 52 |
| QuitButton | 0.085 / 0.165 | 500 | 52 |
| ProductLogo | 0.665 / 0.84 | 500 | 56 |

以上 Anchor X = 1、Pivot = (1, 0.5)、Size Delta Y = 0。原按鈕本身保持 Scale = 1。玩家名稱／設定使用右上錨點，距頂部 60，尺寸分別 322 × 72、78 × 72。版本文字位於左下 (40, 28)。

## Inspector 調整

選取四個主按鈕上的 `Menu Brush Button Visual`：

| 欄位 | 此版值／範圍 | 效果與依賴 |
| --- | --- | --- |
| Reveal Duration | 0.18 秒；0.01～1 | 黃色筆觸展開／收回速度，使用 unscaledDeltaTime |
| Hover Offset | 8；0～24 | 文字向右位移，單位為 Canvas 參考座標 |
| Pressed Scale | 0.975；0.9～1 | 只縮放 Background；1 關閉按壓縮放 |
| Press Duration | 0.07 秒；0.01～0.3 | 視覺縮放過渡時間，也用於 Submit 短脈衝 |
| Disabled Alpha | 0.35；0～1 | Button 或祖先 CanvasGroup 不可互動時的亮度 |
| Label Rest Position | (52, 0) | 若手動改文字位置，必須同步改此基準，避免 Runtime 拉回 |
| Normal / Highlighted Text | 米白／深色；主要按鈕兩者皆深色 | 配合黃色筆觸確保文字對比 |

Highlight 必須使用有 Sprite 的 Image，Type=Filled、Method=Horizontal、Origin=Left、Raycast Target=false。Visual Root、Visual Group、Label、Arrow 已接線。不可將 Visual Root 改為 Button 自身，否則點擊區也會縮放。

工具選單 `Tools/Purgers/Menu/Apply approved brush style V1` 是一次性的配置工具，僅接受已保存的 _Menu，拒絕 Play Mode、未保存 Scene 與已套用元件的主畫面。既有實例應直接用 Inspector 調整。EnsureAssets 只處理本版獨立資產，不更改來源圖。

## 驗證與操作

驗證記錄與實際 Play Mode 圖放在 [Validation/MenuBrushV1](Validation/MenuBrushV1/README.md)。

人工驗收順序：

1. 開啟 _Menu，確認六個入口、右側間距與原背景。
2. 移入四個主按鈕，確認黃色展開、文字右移；滑鼠按住及放開查看縮放。
3. 透過既有 EventSystem 導航確認選取高亮；禁止右鍵觸發按壓表現。
4. 開啟 Continue 再返回，確認正式存檔清單；這一步不要刪除不必要的存檔。
5. 開啟 Settings、Party、玩家名稱，再回主畫面，確認淡出後舊畫面不殘留或攔截點擊。
6. Quick Play 實際建立新檔、Host／Client 連線及回選單需另做端到端驗收；本次視覺驗證不代表已驗證多人遊戲。

## 變更紀錄

- 2026-10-03：新增主畫面筆觸視覺、Scene Instance 配置、淡入淡出資產與局部互動測試。
## 2026-10-03 筆刷來源校正與子視窗範本（歷史階段，已由下方實作接續）

- Brush.png 改為由已確認網頁 BrushSource.svg.txt 匯出的 1560 × 360 RGBA 圖；保留原 GUID。網頁 assets/brush-shared.png 與 Unity Brush.png 位元組相同。
- 匯入使用 Full Rect、無壓縮、無 Mipmap。Simple 底圖與 Filled 高亮不再依賴 Tight Sprite 自動裁切；完整高亮的幾何及 UV 一致。
- 停止缺圖時自動生成不同外形的程序筆刷。MenuBrushStyleBuilder.EnsureAssets 在共用 PNG 缺少時明確報錯；正常存在時保留現有資產。
- 本輪只更新筆刷貼圖、匯入設定與上述缺圖保護；六個按鈕 RectTransform 和 Persistent On Click 比對相同。原 Scene 已有 dirty 修改，未代存、未進入 Play Mode、未啟動會自動存 Scene 的 Test Runner。
- Settings、Continue、Party、玩家名稱、刪除確認及載入轉場的第二版美術仍為本機網頁範本，尚未套用 Unity 子視窗。主題採深橄欖面板、米白文字、芥末黃筆觸標題及操作。
- 範本只用示範資料；解析度、區域、人數、代碼長度等正式值應由既有 Fusion Config 與裝置讀取，不可照抄範本常數。
- 詳見 [V2 驗證紀錄](Validation/MenuBrushV2/README.md)；先前 V1 截圖不再代表最新筆刷。
## 2026-10-03 子視窗 V2 已實作（局部核對）

核對來源：`MenuWindowStyleBuilder.cs`、`MenuSaveSummaryView.cs`、`MenuSaveFlowController.cs`、`Assets/Scenes/_Menu.unity` 與 `SaveSlotRowBrushV2.prefab`。已實際保存 Scene 與 Prefab，並在 Play Mode 檢查顯示及原有 UI 流程；上方「子視窗尚為範本」是先前階段紀錄。

| 視窗 | 美術與正式資料 |
| --- | --- |
| 設定 | 1280 × 780 深橄欖面板，畫面／連線兩欄；原 Dropdown、Toggle、玩家數、提示按鈕仍由 Fusion 初始化。版本欄依原 Config 是否有選項顯示。 |
| 繼續遊戲 | 同尺寸面板，左側可捲動正式存檔，右側選取摘要；未選取時顯示提示且不能開始。 |
| 多人選單 | 同尺寸面板，建立／加入並排；沿用原開房與 Join 按鈕，實際代碼長度 8，沒有移植網頁的示範常數。 |
| 玩家名稱 | 840 × 500；沿用原輸入、Enter 與外部點擊完成，新增確認按鈕接同一個既有完成事件。 |
| 刪除確認 | 830 × 510；仍由原 Controller 決定刪除與取消，沒有自動刪除或另建存檔服務。 |
| 載入 | 840 × 480；原旋轉圖示改為金色，文字及取消由原流程驅動，不顯示假進度。 |
| 通用提示 | 900 × 550；原 IFusionMenuPopup、提示內容及確認 callback 保留，SDK 動態訊息未硬改中文。 |

所有尺寸為 1920 × 1080 Canvas 參考座標，沿用現有 CanvasScaler。Settings／Party／Loading／Popup 的根 Animator 使用專案 `MenuBrushFadeV1.controller`，Show／Hide 只寫根 CanvasGroup alpha，避免動畫把新版位置覆寫。仍由 Fusion Screen 控制生命週期；沒有新增輸入或網路所有權。

### 資產與接線

- `Assets/_Project_Assets/UI/MenuBrushV1/WindowPanel.png`：128 × 128 切角透明底圖，20 px Slice 邊界，僅供子視窗面板。
- 子視窗主操作重用共用 `Brush.png` 與 `MenuBrushButtonVisual`；命中 Image 透明，筆刷裝飾不攔截射線。主畫面保持先前網頁共用的大筆刷。
- 新 `Assets/Prefabs/UI/Menu/SaveSlotRowBrushV2.prefab` 由原存檔列複製，列高 146，保留選取／刪除控制項。原 `SaveSlotRow.prefab` 未覆寫。
- `MenuSaveFlowController.saveSlotRowPrefab` 指向 V2，新增可選的 `selectionSummary` 指向 Continue 的摘要元件。其他既有控制器引用與按鈕事件保留。
- `MenuSaveSummaryView.Show(summary)` 只呈現目前選擇；`Show(null)` 清除舊文字並切回提示；`SetCount(count)` 顯示有效存檔數。它不讀寫 repository、不發網路命令、不自行選檔。
- Controller 在重新列檔後推送真實數量，在選取／清除選取後推送摘要。最後遊玩時間轉為本機時區，無效時間顯示「時間資料無效」。

### Inspector 與後續調整

`Menu Save Summary View` 的 Empty State、Details Root、Stage／Name／Level／Date／Count Label 都是可空的物件引用，本 Scene 已全部接妥。空引用只省略相應顯示，不影響存檔流程；修改版面時維持文字與根物件引用。摘要 Stage 文字使用 80 字級、125 高度，名稱列使用 48 高度，避免 TMP 文字框過小而隱藏內容。

一次性工具：`Tools/Purgers/Menu/Apply approved window style V2`。只接受已保存且尚未套用的 _Menu、非 Play Mode；已完成 Scene 請直接調整 Inspector，不重跑覆蓋人工版面。工具保留 Prefab Instance，不改 Photon 套件或原 Menu Prefab。

### 驗收順序與界線

1. Play _Menu，開設定，展開下拉；點連線區域提示並確認返回，確認控制項數值來自實際裝置／Config。
2. 開 Continue，檢查正式清單、捲動、選取摘要、未選取不能開始；點刪除後取消，確認資料仍在。
3. 開多人選單，輸入不足 8 碼會走原無效代碼提示；返回主選單後無殘留遮擋。
4. 玩家名稱用原值按確認，確認返回；載入與通用 Popup 由正式流程驅動。
5. 實際建立遊戲、Continue 載入、有效 Join、連線取消／失敗復原與獨立 Host／Client 仍須端到端驗收。本次不刪除或建立使用者存檔。

聚焦 9/9 通過；完整回歸仍有既有 6 項失敗，並非全套通過。實際截圖、測試數與保護原未儲存 Scene 的方式見 [MenuWindowsV2 驗證](Validation/MenuWindowsV2/README.md)。

- 2026-10-03：子視窗 V2 完成 Scene 套用、獨立 V2 存檔列、只讀摘要與 UI 流程驗證。