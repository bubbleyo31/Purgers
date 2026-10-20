# HUD 按鍵提示驗證

最後核對日期：2026-10-19（局部美術／位置／改鍵提示驗證）。
正式規則：[111 共用美術與位置](../../111_HUD按鍵提示共用美術與位置.md)、[104 ESC 玩家設定](../../104_ESC選單與玩家設定.md)、[167 HUD 美術](../../167_已確認HUD美術與整格血量.md)。

## 2026-10-19 共用美術與全提示盤點

### 問題與正式套用

Energy Q 舊用 Key.png、米粉底及不同文字縮放，技能鍵帽則用 SkillSquare；只修寬度與間距仍會看出差異。本輪將鍵帽美術集中到 `Assets/Resources/Presentation/HudKeyPromptStyle.asset`，統一背景、字型／材質、幾何置中、狀態色、內距與尺寸。Energy、鈎索特殊能力、專注能力使用相同 19 單位上下間隔；窄列／世界提示保留原位置並等比適配。

已接入換彈、救援／自救、準備與取消投票、觀戰、獎勵、開始裝置、動態技能拾取及 ESC 按鍵頁預覽。技能拾取提示使用目前 Interact 綁定；不更動拾取判定、戰鬥輸入、保存格式或網路權威。NotoSansTC-Bold HUD SDF 動態圖集補入 X／J 字形，未替換原背景 PNG。

正式 StageHUD、PlayerRescueHUD、SpectatorHUD、_Menu、SafeHouse 已由 Unity Editor 套用及存檔。[PromptInventory.json](PromptInventory.json) 保存 22 個序列化提示的完整路徑、Sprite、字型、材質、色彩、錨點、位置、內距、尺寸與父層座標鏈；動態拾取／設定頁的建立位置記於 111 文件。未重跑整個 HUD 版面工具，原有無關變更保留。

### 驗證證據

- 紅燈：`EnergyAndAbilityUseTheSameKeycapAppearance` 先在舊版執行，實際以 SkillSquare／Key 背景不一致失敗（1 案例）。
- [聚焦結果](style-focused-summary.txt)／[XML](style-focused.xml)：**28 通過、0 失敗、0 略過**。包含 HudKeyPrompt、PlayerSettings、PlayerSettingsIntegration、SafeHouseHudPresentation、Phase6RewardHudPrefab、SpectatorPresentation。檢查改鍵背景保留、短／長鍵名、字級／內距、未啟用 TMP、零縮放 Prefab 的相對間距、修改共用樣式後已建立鍵帽更新，以及原設定整合。工作 ID `b724c4d376e24fd1a5151a5a1ba5b7f4`。
- [正式資產核對](style-asset-audit.json)：22 個提示 × 8 種鍵名 = **176 組通過**；共用 Sprite／字型／材質／對齊引用一致，寬度在 32–104，背景保留；檢查的來源資產 **Missing Scripts = 0**。在暫時載入的 Prefab／Preview Scene 檢查，未保存測試鍵名。
- [動態技能拾取核對](style-loot-prompt-audit.json)：E、RightControl、滑鼠側鍵、未配置 **4 組通過**，跟隨 Interact 綁定、長字不溢位、相同美術且取消高亮後隱藏。使用已啟用的暫時物件；第一次以尚未 Awake 的 3D TMP 建立測試物件時在既有 Bind 字型初始化失敗，修正測試物件初始化後通過。僅隔離呈現，未 Spawn 網路物件，未保存玩家偏好。
- [完整回歸結果](style-regression-summary.txt)／[XML](style-regression.xml)：新鮮 RunFinished 為 **1,229 通過、4 失敗、2 略過，共 1,235 案例**，結果 Failed(Child)，不是全綠。工作 ID `aa0fde782b78444090da89902ad05474`。MCP 曾保留舊進度，因此以本輪 RunFinished XML 核對；不是使用舊工作進度或 total=0 推定通過。
- 本次程式已完成 Unity 編譯；完整回歸後 Console 核對無 error／warning。後續隔離呈現核對未改程式。收尾 [Editor 狀態](style-editor-final.json)：_Menu 場景已儲存且不為 dirty，非 Play Mode、非編譯／匯入中，沒有執行中的 Runner；保留 PlayUnfocused 與 Run In Background。Editor 狀態資源另確認 idle、無測試執行且 is_focused=false。
- [Energy／能力鍵帽原生離屏圖](HudKeycapsUnifiedPreview.png)、[16 種提示／狀態圖](HudKeycapsCatalog.png) 已目視檢查。使用正式 Sprite、TMP 字型與共用 helper，沒有網頁模擬、Scene View 截圖、桌面輸入或焦點操作。

四項完整回歸失敗與先前 [GuardGrowth/HudSize 紀錄](../GuardGrowth/HudSize/README.md) 相同，未擴大本次範圍修復：

| 測試 | 原因 |
|---|---|
| BossStageAssetTests.GameSceneContainsBossAssetWiring | Boss／MapChunk 資產引用不符 |
| EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules | 預期 0.5，正式資產為 0.25 |
| EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners | 預期 >80，正式資產為 75 |
| ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt | 既有測試的反射取欄位 NullReference |

自然操作改鍵、實際遊玩中的各 HUD 視覺比例、自然救援／拾取流程、獨立 Host／Client 及 Build **尚未驗收**；聚焦／資產／離屏檢查不代表這些情境通過。

### 重現與日後更換美術

調整共用 HudKeyPromptStyle.asset；如需同步非播放時的 Scene／Prefab 外觀與清單，儲存自己的場景後執行 `Purgers → UI → Apply Shared Keycap Style`。目標場景未儲存時工具會拒絕開始，且不重設共用美術資產。完整參數與新增提示接入規則見 [111](../../111_HUD按鍵提示共用美術與位置.md)。

`HudKeyPromptPreview.Render(path)` 輸出 Energy／能力對齊圖；`RenderCatalog(path)` 輸出短鍵、長鍵、滑鼠、按下／未配置圖。兩者使用 Preview Scene 離屏渲染，不開視窗。

## 2026-10-18 歷史紀錄

以下是前次「保留各自原圖」階段的紀錄；其中 Energy 使用 Key.png 的美術結論已由 2026-10-19 共用樣式取代。歷史測試數量不作為本輪結果。

### 已確認的資產問題與修正

舊版 `BattleHudReferenceLayout.ApplyStage` 將 `grappleKeyBackground` 設為 `null`，且將能量鍵 Image 改成透明底色。已修正作者入口，避免重套時再清空引用。`SafeHouse.unity` 的獨立 HUD 也有同樣的透明背景及空序列化引用。已在 `StageHUD.prefab`、`SafeHouse.unity` 恢復原 `Key.png`、米粉底色與背景引用；HUD 初始化時若引用缺漏，仍會由既有 Label 父物件找回 Image。保留兩個資產其餘現有變更。

`HudKeyPromptLayout` 由現有 `LocalPlayerBattleAbilityHUD` 呼叫，不讀取或裁定按鍵。只在按鍵改變時依共用 `PlayerSettingsData.KeyLabel` 更新文字與鍵帽寬度；使用既有 Image／Sprite，兩側各留 8 單位，背景高 32、最大寬 104，置於上方 UI 底緣下 19 單位。最大寬度內字級由 21 自動降至 10，不換行、不裁字。Q／E 按下與不可用色仍由原 HUD 決定。

### 驗證

- `HudKeyPromptTests` EditMode：3／3 通過。對正式 `StageHUD.prefab` 驗證能量鍵與能力鍵的原圖、Q、Shift、Ctrl、較長鍵名、滑鼠主鍵／側鍵、未配置文字、內距、尺寸、文字不溢位、上方間隔及改回 Q 的尺寸復原。
- `SafeHouse.unity`：由 Unity Editor 開啟檢查並修復能量鍵背景引用與底色；存檔後序列化引用已接回既有 GrappleKey Image，場景其他 HUD 變更均保留。
- [HUD 鍵帽離屏預覽](HudKeycapsPreview.png)：正式 Sprite、TMP 字體與排版 helper 組合呈現 Q、L CTRL、PAGE DOWN、滑鼠側鍵；不依賴 Editor 焦點或遊戲 Play Mode。這是版面檢視，不代表自然遊玩驗收。
- PurgersRegression EditMode：已嘗試啟動，但 Unity 進入 Play Mode 切換且 MCP 工作階段中斷，未取得新鮮 `RunFinished` 結果；不宣稱完整回歸通過。啟動前最近完整回歸的 4 項既有失敗／2 項略過紀錄不等同本輪結果。
- Unity Console 沒有本次 HUD 程式錯誤。可見的 `BattleHudReferenceLayout.cs` `TextureImporter.spritesheet` 過時 API 警告為既有項目，與本次未改的作者工具有關。
- 自然遊玩、按鍵實際重綁後的玩家視覺接受與 Build 尚未驗證。

### 重現離屏預覽

`HudKeyPromptPreview.Render(path)` 在 Unity Preview Scene 使用正式 Prefab Sprite、字體及共用 layout helper 輸出 PNG，不開視窗、不切換焦點。
