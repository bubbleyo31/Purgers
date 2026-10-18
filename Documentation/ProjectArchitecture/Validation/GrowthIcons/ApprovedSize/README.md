# 核准預覽套用正式 UI（2026-10-17）

使用者已確認 deliverables/GrowthRewardIconPreview_20261017 的版本，包含換彈速度及彈匣容量的彈匣內左右等距。本次正式套用沿用既有 GrowthRewardRankView／AbilityIconFrameFit／PlayerRewardDefinition；不改玩法與網路權威。

## 已套用

- 九種屬性以 Definition 的 GrowthIconScale 保存核准倍率；完整 UV、等比縮放，正式底板仍為 71.28525 × 82.95008、Inset 2。
- 階級採實際 glyph ink 置中，同一屬性所有階級維持同字級；既有 Bold 字型取消額外合成粗體。版面未變時不重建文字。
- 換彈中心 x=618.5/1254，彈匣容量 x=654.5/1254；保留原 Y 與字框。兩者 I～IV 對齊核准的內壁中心。
- 九張 RankBackgrounds 匯入設定開啟 Mipmap、Trilinear、mip bias -0.5；仍為 256 上限及無壓縮。來源 PNG 像素不變。
- 以 GrowthIconSetup.ApplyApprovedReadability 窄範圍套用九份 Definition／九份 importer；既有完整 Apply 也銜接此步，避免重設回舊值。
- StageHUD 與 SafeHouse 沿用既有接線。SafeHouse 三卡均有 Image、rankView、啟用的 frame fit 與一致底板；檢查後未保存場景。

設定與各屬性正式尺寸見 [168](../../../168_角色成長被動與技能掉落.md)。

## 驗證證據

- [BeforeTests.txt](BeforeTests.txt)：新增三測試先在舊實作失敗，分別命中圖示尺寸、階級字級與取樣設定。
- [FocusedTests.txt](FocusedTests.txt)：GrowthIconIntegrationTests + AbilityIconFrameFitTests，13／13 通過。
- [FullRegression.txt](FullRegression.txt)：本次 RunFinished 實際 981 通過、2 失敗、1 Explicit 略過，合計 984。MCP 初始探索數量 999 不是完成數；以此持久結果為準。
- 兩項範圍外失敗：BossStageAssetTests.GameSceneContainsBossAssetWiring（場景引用）；ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt（Field helper NullReference）。前次 GrowthIcons 回歸已記錄相同兩項失敗；本輪未改這些測試或玩法。
- Explicit 略過：AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState，需 Photon 連線的獨立受控驗證。
- 編譯完成；最後 Console 無 error，唯一 warning 為測試 Runner 的 Undo 耗時 3 秒。
- [FormalRenderChecks.txt](FormalRenderChecks.txt)：正式元件直接呈現全部 42 階級，加上三卡重複案例，共 51 次字形檢查、63 次 mesh 檢查。四頂點、完整 UV、等比幾何與字元／中心均通過。
- [Applied_1080p_1to1.png](Applied_1080p_1to1.png)：正式元件全部階級原生尺寸渲染；[RenderApplied.cs.txt](RenderApplied.cs.txt) 可重現。沒有預覽程式覆寫正式字級、位置或 mesh。
- [PreviewPixelDifference.txt](PreviewPixelDifference.txt)：與核准預覽比較 627200 像素，最大通道差 5／255，平均 0.0245／255；幾何、字級與文字中心一致。不是逐位元相同。
- [SafeHouseBindings.txt](SafeHouseBindings.txt)：正式三卡接線及框尺寸回讀。
- [FileVerification.json](FileVerification.json)：56 份開工快照比對；33 未變，23 變更（5 程式、9 Definition、9 importer）。九原始 PNG、StageHUD、SafeHouse、_Menu 全部未變。Definition 差異僅新增倍率與兩項水平中心；importer 差異僅上述三個取樣欄位。

## 驗收邊界

完成正式 UI 元件的隔離 Unity 渲染、資產接線、編譯、聚焦測試及完整回歸記錄。完整回歸並非全綠。未宣稱自然遊戲流程、獨立 Host／Client、Build 或效能 profiling 已完成。720p 的鈎索小圓內數字仍受已核准原圖留白限制。

## 2026-10-17 後續：階級數字設為 Bold

依使用者指示，GrowthRewardRankView 改用 TMP FontStyles.Bold，取代上述初版 Normal 樣式；圖示倍率、完整 UV、原圖與階級中心設定不變。沿用既有字形量測，加粗的字距使部分字級自動縮合至原字框內，同一屬性仍使用一致字級。

已重新編譯，Console 無 error；正式元件重新渲染全部 42 階級，51 次文字案例通過 Bold／中心／字元核對，63 次圖示 mesh 核對通過。已視看 [Bold 正式尺寸圖](Bold/Applied_1080p_1to1.png)，[檢查紀錄](Bold/FormalRenderChecks.txt)。本輪為單一文字樣式調整，未重跑完整回歸；上方 13／13 與完整回歸數字屬前一版本，不充作 Bold 版測試結果。


## 後續核准粗襯線／各階適配

使用者已選定 SlabRank 的 Adaptive 版本，正式字體與各階適配已取代本目錄的 NotoSansTC／Bold 版本。最新規則與證據見 [SlabAdaptive](../SlabAdaptive/README.md)。本目錄保留歷史驗證。
