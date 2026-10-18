# ESC／玩家設定正式整合驗證

日期：2026-10-18。設計與入口：[104 完整規則](../../104_ESC選單與玩家設定.md)。

本次使用者明確授權將已確認的 Unity 版本實裝。主選單設定與連線後 ESC 共用正式 uGUI、16 個共用綁定、四操作模式、音量、顯示與連線偏好。原獨立預覽保留，不列入 Build。

## 已完成的驗證

- 集中 **14／14 通過**：設定資料／模型、保存與空白綁定、草稿失敗回復、ESC 層級、15 秒顯示回滾、按鍵放開門檻、原生 Slider／InputField／Button、正式主選單事件、連線包裝層、Mixer 參數、有效 Spawned 鈎索模式與滑鏟速度，另含原獎勵選擇輸入相容測試。
- 最後清理重連後殘留提示文字，重跑資料與整合測試 **10／10 通過**（`Temp/GrappleFeedback/settings-notice-focused.xml`）。交付時 Editor 停止播放、無測試／Runner 運行，`_Menu` 場景乾淨、設定入口只有一個、Missing Script 為 0；Console 僅有 MCP WebSocket 連線警告，無本次程式編譯錯誤。
- 先重現跑步第二次按下取消、選單滑鏟速度被清零、正式 Fusion Connection 包裝層取值為 null 三項失敗，再修正並驗證。早期一次測試探索為 0，已排除，不列為通過。
- 背景 Play Mode：正式主選單按鈕、JSON 保存與正式偏好套用；音效 50% 讀回 -6.0206 dB，放棄回原 0 dB。測試後還原原偏好檔，沒有修改使用者關卡存檔。見 [UI 記錄](playmode-ui.txt)。
- 真實 Photon、同 Editor 隔離 Host／Client：Client 個別按住／切換跑步、再按取消／停止解除、正式 Player 開選單取消鈎索並保留蹲姿、重力與世界 tick 持續、Client 同步與重連後不復活舊狀態；正式 Client 選單顯示「離開遊戲」，走 MenuConnection 離開後 Host 繼續、沒有世界存檔且控制鎖釋放。見 [多人記錄](host-client.txt)。
- 正式 SafeHouse、GameLogic、設定選單與 MenuConnection，使用 Temp 下獨立存檔庫：房間資訊、Host 離房文案、正式投票取消優先且保留原選單層級、寫檔故障注入後留房與重試、成功保存後離房並釋放鎖。見 [保存離房記錄](host-leave.txt)。此項使用 Single Runner 的 Host 權威，與真實 Photon 多人測試分列。
- 1600×900 離屏渲染已目視檢查主設定頁的欄位、滑桿、數值與頁尾對齊：[正式控制頁](production-controls.png)。未使用 Scene View 截圖、Focus 或桌面模擬鍵鼠。

## 完整回歸

最終 `PurgersRegression`：**1061 項；1056 通過、4 失敗、1 略過**。新設定測試全通過，但全套並非全綠。四項失敗與前次獨立預覽的既有失敗同名：

1. `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Boss 引用不一致。
2. `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：期望 0.5，資產為 0.25。
3. `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：期望 >10，資產為 10。
4. `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：既有 NullReferenceException。

摘要與完整測試名見 [test-results.json](test-results.json)。原始新鮮 RunFinished 證據位於 `Temp/GrappleFeedback/settings-focused-delivery.xml`、`settings-regression-delivery.xml`；MCP 在 Domain Reload 後的殘留 running 不作為結果，確認 Unity IsRunActive=false 後才清除殘留工作狀態。

## 正式配置與範圍

- `_Menu/GameplayHUD Canvas/Player Settings`；SettingsButton 的單一 persistent event → `OpenFromMain`。
- Canvas：最高 Sorting Layer／32000；Loading 32766、Fade 32767 在上方。黑色 60% 遮罩與字型沿已確認預覽；後續依使用者修訂，選取改為金色純色底，不使用筆刷。
- 正式字型：`Assets/_Project_Assets/UI/PlayerSettings/PlayerSettingsFont.asset`。沒有改用預覽模擬資料。
- `MainAudioMixer` 保存三個音量參數與 Music 分組，原 SFX 子分組不變。沒有新增背景音樂曲目。
- `_Menu` 經 Editor 保存時，自動補序列化既有 GameplayAudioService 的 `maximumWorldLoopVoices: 32` 預設欄位；本次沒有改該設定值或音效池邏輯。
- 未改 Game／SafeHouse 場景或玩家 Prefab；該處 HUD 提示由既有元件讀新綁定。沒有改網路傷害、存檔格式、地圖生成或關卡計時規則。
- 改變 NetInput layout，Host／Client 需使用同一版。當前工作區原有大量未提交變更，僅審查本次範圍；未 reset／回退其他工作，未 commit／push。

## 待自然操作與獨立程式驗收

- 未做前景鍵鼠自然操作／使用者最終視覺驗收：ESC、滾輪、上下鍵、滑鼠選取、Y、長按再關選單與完整遊玩手感應在 Game View 實際操作確認。
- 未執行 Standalone Build，也未驗獨立兩台／兩個程序。Editor 的受控 Host／Client 不等同跨程序驗收。
- Editor 版不更改桌面解析度；實際全螢幕／解析度 15 秒還原仍需 Standalone 測試。模型、取消與逾時計時已驗。
- 未做全解析度／超寬螢幕自然操作、裝置拔插、磁碟／OS 失敗全矩陣。

## 建議操作

1. 從 `_Menu` 播放，按右上角設定；確認三個靈敏度與五分頁對齊。
2. 把 E 改為另一鍵，再故意衝突；檢查整列紅底、未配置警告與 Y／按鈕保存。
3. 進房後 ESC；確認底部房間號與複製、正常畫面資訊預設隱藏，開啟 FPS／延遲才在右上角出現。
4. 安全屋投票中按 ESC，先取消投票；再按才開小選單。主客端分別確認離房文字。
5. 跑步／瞄準／鈎索／蹲姿與滑鏟中開選單，確認世界繼續、無輸入補發；保存離房後從既有存檔繼續。

## 2026-10-18 後續視覺修訂

- 正式設定／ESC 選取改用純色金底，保留深色字與左側選取提示；其他主選單美術不變。
- 右上角 FPS／延遲採深橄欖底、米白數值、金色側線；保留原本兩個開關與真實資料來源，單項顯示縮成一列，全部 Graphic 不攔截滑鼠。
- 根因修正：Player Settings 原為普通 Transform，巢狀 Canvas 未填滿畫面。Build 現在確保 RectTransform 並拉滿父容器；資訊卡錨定全畫面，中央設定頁仍置中。
- `_Menu` 的 SessionCodeOverlay 整體停用，包含標題、齒輪、X 與舊房號；保持原序列化引用避免破壞 Fusion 畫面流程。場景相對本輪基準只有這項 active override，Installer 同步。
- 集中測試 10／10 通過。初次驗證捕捉到 Unity 假 null 的 RectTransform 判斷問題，修正後重跑通過；完整 PurgersRegression 為 1061 項，1056 通過／4 項上述既有失敗／1 略過。證據：`settings-flat-focused-fixed.xml`、`settings-flat-regression.xml`（Temp/GrappleFeedback）。
- 原生 uGUI 離屏檢查：[1600×900](flat-controls-1600.png)、[2560×900](flat-controls-2560.png)。資訊卡右上角分別為 (1572,876)、(2532,876)，距畫面右緣 28／上緣 24 參考單位；無筆刷 Image。截圖用 120 FPS／42 ms 範例數值展示排版，未覆寫正式偏好、實際計數或連線。
- 本輪未進行自然前景鍵鼠操作／Standalone Build，沒有新增多人玩法變更。
## 2026-10-18 主選單區域下拉修復

使用者回報主選單「連線 → 區域」點了看不到／無法選取。根因為 TMP_Dropdown.SetupTemplate 固定 popup sortingOrder=30000，低於設定 Canvas 的 32000；原先測試未展開原生清單，漏掉這條路徑。

- 新增 PlayerSettingsDropdown，只覆寫原生 CreateDropdownList，套用 owner Sorting Layer／order + 2。TMP 原生 blocker 沿 popup - 1，因此設定／blocker／清單分別 32000／32001／32002，低於 Loading／Fade。不改套件程式、伺服器清單、區域索引、鎖定或保存規則。
- 聚焦失敗先重現 30000 < 32000；修正後設定測試 **11／11 通過**。新增測試包含原生 Show、清單／blocker 層級與區域草稿通知；EditMode 顯式初始化 TMP.Start 並在關閉 PreviewScene 時交由場景清理物件，避免原生延遲 Destroy 不適用 EditMode。
- 使用者允許停止試玩後，在 PlayUnfocused 進行正式主選單按鈕 → 連線分頁 → 展開實際區域選單。等待版面更新後確認原生 EventSystem 第一個 Raycast 命中選項，再派送 pointerClick 選 us，區域草稿正確更新、清單收起。放棄草稿，未保存／更動使用者偏好。見 [背景 Play Mode 記錄](dropdown-playmode.txt)。未模擬桌面鍵鼠或搶焦點。
- 完整 PurgersRegression **1062 項：1057 通過／4 項上述既有失敗／1 略過**。fresh RunFinished：`Temp/GrappleFeedback/settings-dropdown-focused-verified.xml` 與 `settings-dropdown-regression.xml`。MCP reload 後殘留 running 已在 Unity 測試停止且 XML 完成後清除。
- 本次僅修改共用設定下拉呈現，未重新進行 Host／Client 或 Standalone Build；自然滑鼠操作仍由使用者確認。
## 2026-10-18 伺服器區域清單捲動速度

依 Editor 順位，先等「尋找逃離塔科夫觀察者視角圖片」完成並 idle，再接手本次修正。

- 僅在 ConnectionSettings 的區域下拉模板設定 scrollSensitivity=80，對應 40 高的選項每標準滾輪單位約兩列；其餘下拉保留原值，未改視角靈敏度、保存、層級與原生 Elastic 邊界處理。
- PlayUnfocused 下展開正式區域清單，派送原生 ScrollRect.OnScroll：往下位移 80、反向回原位；上下邊界 normalizedPosition 為 0／1；其他下拉敏感度仍為 1。接著由 EventSystem Raycast 與 pointerClick 選取，草稿更新正常。見 [滾輪紀錄](region-wheel.txt)。未保存測試草稿、未修改使用者偏好。
- 集中既有設定測試 **11／11 通過**，原始證據 `Temp/GrappleFeedback/settings-region-wheel-focused.xml`。本次未新增僅檢查常數值的測試；硬體滾輪自然手感仍待使用者確認。
- 完整 PurgersRegression **1078 項：1073 通過／4 項既有失敗／1 略過**；與前順位最新基準相同。證據 `Temp/GrappleFeedback/settings-region-wheel-regression.xml`。結束時 Editor 無測試、未播放、未編譯，主選單場景無未保存變更；沒有新編譯錯誤。
