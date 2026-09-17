# Phase 1-B：Menu 存檔實際配置與驗證

> 最後核對：2026-09-18  
> 適用場景：`Assets/Scenes/_Menu.unity`  
> 前置程式：`GameSaveData`、`JsonGameSaveRepository`、`GameSaveRuntimeContext`、`MenuConnection`、`MenuSaveFlowController`、`MenuSaveSlotRow`  
> 狀態：2026-09-18 已由 UnityMCP 完成 Scene／Prefab 配置；本文件保留實際階層、維護方式與尚未完成的連線驗證。

## 1. 已確認的現有階層

```text
GameplayHUD Canvas
└─ Menu                              ← MenuUIController + MenuConnectionBehaviour
   ├─ FusionMenuViewMainMenu         ← FusionMenuUIMain
   │  ├─ MainButtons                 ← GridLayoutGroup，單欄直向
   │  │  ├─ QuickPlay
   │  │  ├─ PartyMenu
   │  │  ├─ ScenesSelection
   │  │  └─ SettingsButton
   │  └─ ContinueGame                ← Quick Play 右方，不受 Grid 排列
   └─ ContinueOverlay                ← 獨立 Canvas，Sorting Order 200
```

`MainButtons` 目前是 `GridLayoutGroup`，Cell Size 為 `360 x 100`、Spacing 為 `16 x 16`、Constraint Count 為 1。把 Continue 直接留在此物件下只會排到下一列，不會出現在 Quick Play 右方。

現有 Quick Play 透過 Button Persistent Call 對 `FusionMenuViewMainMenu` 執行 `SendMessage("OnPlayButtonPressed")`。不要修改這個既有事件；`MenuConnection` 已在收到空 Session／`Creating=false` 時，把它改判成 `QuickPlayNewHost`。

## 2. 已建立「繼續遊戲」按鈕

1. 在 Hierarchy 找到 `GameplayHUD Canvas/Menu/FusionMenuViewMainMenu/MainButtons/QuickPlay`。
2. Duplicate 一份，命名為 `ContinueGame`。
3. 將 `ContinueGame` 拖到 `FusionMenuViewMainMenu` 底下，成為 `MainButtons` 的同層物件，避免被單欄 Grid 重新排列。
4. 實際 RectTransform：
   - Anchor Min／Max：沿用 Quick Play 的 `0, 1`
   - Pivot：沿用 Quick Play
   - Width／Height：`360 / 100`
   - Anchored Position：Quick Play 的 Anchored Position 加上 `376 / 0`
5. X 位移 376 代表按鈕寬 360 加 16 間距；主選單改版時必須仍以 Quick Play 的實際 RectTransform 為基準。
6. 將子物件 TMP 文字改為「繼續遊戲」。
7. 在 Button 的 On Click 清除從 Quick Play 複製來的 `SendMessage / OnPlayButtonPressed`。若未清除，按一次會同時建立新檔與開啟 Continue Overlay。

## 3. 建立 Continue Overlay

建議階層：

```text
GameplayHUD Canvas
└─ Menu
   ├─ ...既有 Fusion Menu Views
   └─ ContinueOverlay                ← 放在 Menu 最後一個 Sibling
      ├─ ScreenBlocker               ← 全畫面 Image，Raycast Target 開啟
      └─ SaveSelectionPanel
         ├─ Title                    ← TMP：選擇存檔
         ├─ SaveScrollView
         │  └─ Viewport
         │     ├─ Content            ← VerticalLayoutGroup + ContentSizeFitter
         │     └─ EmptyState         ← TMP：目前沒有存檔
         ├─ Feedback                 ← TMP：損壞檔／讀取錯誤
         ├─ StartSelectedSave        ← TMP：開始遊戲
         └─ Back                     ← TMP：返回
```

1. `ContinueOverlay` 的 RectTransform 設成 Stretch／Stretch，四邊 Offset 都為 0。
2. 將它放在 `Menu` 最後一個 Sibling，確保 Hierarchy 順序最高。
3. `ContinueOverlay` 已加獨立 Canvas：
   - Override Sorting：開啟
   - Sorting Order：`200`
   - 再加 `GraphicRaycaster`
4. `ScreenBlocker` 必須覆蓋全畫面並開啟 Raycast Target，避免點擊穿透到底下 Quick Play。
5. `Content` 實際使用：
   - Vertical Layout Group
   - Spacing：`12`
   - Child Control Width／Height：開啟
   - Child Force Expand Width：開啟
   - Content Size Fitter / Vertical Fit：Preferred Size
6. `ContinueOverlay` 可在編輯時保持啟用；`MenuSaveFlowController.Awake()` 會在 Play Mode 自動隱藏。完成排版後也可以手動關閉。

## 4. 已建立 SaveSlotRow Prefab

1. 在 Canvas 內先建立一個 Button，命名 `SaveSlotRow`，建議高度 `100`。
2. 在其下建立：
   - `DisplayName` TMP
   - `Level` TMP
   - `LastPlayed` TMP
   - `SelectedIndicator` Image，可選
3. 在根物件加入 `MenuSaveSlotRow`。
4. Inspector 指派：
   - Select Button：根物件 Button
   - Display Name Label：`DisplayName`
   - Level Label：`Level`
   - Last Played Label：`LastPlayed`
   - Selected Indicator：`SelectedIndicator`，不需要選中效果時可留空
5. 將它拖成 Prefab，例如：
   `Assets/Prefabs/UI/Menu/SaveSlotRow.prefab`
6. Prefab 建立後，刪除 Canvas／Content 裡用來製作的暫時實例。正式列會由 Controller 動態 Instantiate。

## 5. 已配置 MenuSaveFlowController

1. 選取 `GameplayHUD Canvas/Menu`。
2. Add Component：`MenuSaveFlowController`。
3. 依序指派：
   - Main Menu Screen：`FusionMenuViewMainMenu` 上的 `FusionMenuUIMain`
   - Menu Connection：同一個 `Menu` 上既有的 `MenuConnectionBehaviour`
   - Continue Button：`ContinueGame`
   - Overlay Root：`ContinueOverlay`
   - Save List Root：`SaveScrollView/Viewport/Content`
   - Save Slot Row Prefab：剛建立的 `SaveSlotRow.prefab`
   - Empty State Label：`EmptyState`
   - Feedback Label：`Feedback`，可留空但建議保留
   - Start Button：`StartSelectedSave`
   - Back Button：`Back`
4. 三個按鈕的事件由 `MenuSaveFlowController.Awake()` 清除複製來源事件後重新綁定。不要再於 Inspector 疊加 Persistent Call。
5. Save Scene。

不要替 Quick Play 新增 `MenuSaveFlowController` 事件。Quick Play 保持原本 Fusion Menu 事件即可。

## 6. 單機 Play Mode 驗證

### 6.1 Quick Play 新檔

1. 清除 Console。
2. Play `_Menu`。
3. 按 Quick Play 一次。
4. 預期 Console 出現：
   - `[MenuConnection] Prepared QuickPlayNewHost with new save ...`
5. 預期以 Host 進入目前 Fusion Menu 設定的 Game Scene。
6. 停止 Play，再次進入 `_Menu`。
7. 按「繼續遊戲」，應看到剛建立的存檔：
   - 關卡 1
   - 玩家 Lv.1
   - 最後遊玩時間

實際檔案位於 Repository Log 顯示的目錄，預設為：

```text
Application.persistentDataPath/Purgers/Saves/<SaveId>.json
```

### 6.2 Continue

1. 開啟「繼續遊戲」。
2. 選擇一列；預期 Selected Indicator 顯示，Start Button 變成可按。
3. 按開始。
4. 預期 Console 出現：
   - `[MenuConnection] Prepared ContinueHost for save '<SaveId>'.`
5. 預期不新增第二份 JSON，並以 Host 開啟 Session。

### 6.3 空清單與損壞檔

1. 暫時把 `Saves` 目錄移到別處，再開 Continue；預期顯示「目前沒有存檔」。
2. 在 `Saves` 建立一個檔名為合法 32 位 GUID、內容不是 JSON 的 `.json`。
3. 再開 Continue；有效存檔仍應列出，Feedback 顯示損壞檔錯誤。
4. 測試後移除這個人工損壞檔。

## 7. Host／Client 驗證

請使用兩個獨立程序或 ParrelSync Clone；不要只依賴同一 Runner 的單機結果。

1. Host 在原專案按 Quick Play，記下 Session Code 與 Console 的 SaveId。
2. Client Clone 從 Party Menu 輸入 Session Code 並 Join。
3. Host 預期：Runner 上的 `GameSaveRuntimeContext.AccessMode = HostWritable`，且有 ActiveSave。
4. Client 預期：自己的 Runner 上 `AccessMode = ClientReadOnly`，ActiveSave 為空。
5. Client 本機不應新增 `Purgers/Saves/*.json`。
6. Client 加入不應清除 Host Runner 的 ActiveSave。此項已有 EditMode Runner 隔離測試，但仍需 Play Mode 確認實際 Runner 組裝。

## 8. 目前驗證邊界

- EditMode `PurgersRegression` 已 24/24 通過。
- `_Menu` UI 已由 UnityMCP 配置並儲存。Play Mode 已驗證：Continue 位於 Quick Play 右方、Overlay 位於最上層、空清單提示、動態存檔列、選取 Indicator、Start 由 Disabled 轉為 Interactable、Back 關閉 Overlay。
- UI 驗證使用的暫時存檔已刪除，沒有留下測試 Save。
- 實際 Quick Play／Continue Host 開房、連線失敗回滾、Unity 重啟後列出與獨立 Client 加入仍待多人 Play Mode 驗證。
- 此文件記錄 Phase 1-B 當時狀態；Phase 2 已把 Fusion Menu Config 入口改為 SafeHouse，最新流程見 `142_Phase2_安全屋灰盒配置與驗證.md`。
- Continue 尚未提供重新命名或刪除按鈕。
- Host 開房失敗會刪除該次新建、尚未啟用的存檔；Continue 開房失敗不刪除既有存檔。
