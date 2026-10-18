# 粗襯線／各階適配正式套用（2026-10-17）

使用者已選定該版本，正式 Runtime 與資產已套用。

- `GrowthRewardRankView` 依當前階級量測可見字形，等比適配 94% 原字框；固定 `GrowthRankCenter`，保留圖示完整 UV 與已確認倍率。I 保留較大尺寸，III 依寬度縮小。
- 自製字體 `Assets/_Project_Assets/UI/GrowthIcons/Fonts/PurgersRankSlab.ttf` 保留核准預覽的 I／V 向量輪廓，另補 TMP 所需底線並移除字型內部 Preview 命名；正式來源為 984 bytes，建字程式見 BuildFont.py。I 有方正上下厚橫槓；V 為已核准的同風格延伸。
- `PurgersRankSlab SDF.asset` 為靜態 512×512 Alpha8 atlas，約 256 KiB 像素資料；材質和 atlas 內嵌於同一 asset。包含空白、I、V 及 TMP 初始化所需底線。Runtime 不建字、不掃圖、不產生貼圖或材質。
- TMP 保留 Bold；專用字型已是粗筆畫，額外 boldStyle／boldSpacing 均為 0，與核准預覽一致。
- 經 Unity Editor API 更新 StageHUD／SafeHouse 共六個 GrowthRank 字體與材質引用；與本輪備份 diff 僅六組 m_fontAsset／m_sharedMaterial，沒有 RectTransform 或其他欄位變動。_Menu SHA-256 不變。
- `GrowthIconSetup.Configure` 使用新字型路徑，避免完整套版覆回 NotoSansTC。重現窄範圍遷移見 [ApplyFont.cs.txt](ApplyFont.cs.txt)。

## 驗證

- [BeforeTests.txt](BeforeTests.txt)：先修改既有測試，舊正式字體失敗，確認新規則確實被測試。
- [FocusedTests.txt](FocusedTests.txt)：13／13 通過；全部 42 階級核對專用字型、當階字框利用率、固定中心、字元與完整圖示。
- [Applied_1080p_1to1.png](Applied_1080p_1to1.png)：直接使用正式資產與啟用的正式 Runtime 元件渲染，未覆寫字型、字級或座標。與核准 Adaptive 預覽 PNG **SHA-256 完全相同**：15146B5168B809E2A1F9E7B0BA7330EAA9B7B44723842A583D94D1ECCDADDB51。
- [FormalRenderChecks.txt](FormalRenderChecks.txt)：51 次文字案例、63 次 mesh 檢查通過。42 階級均已目視核對。
- [FileVerification.json](FileVerification.json)：本輪開始前的檔案快照與比對；保留先前工作區改動。

完整回歸結果與接線回讀追加於下方。未宣稱自然操作、獨立 Host／Client、Build 或效能 profiling 已驗收。

### HUD 字體規則同步

第一次完整回歸為 980 通過、3 失敗、1 略過；其中新出現的 BattleHudReadabilityTests 舊規則要求「所有 StageHUD 文字都是 NotoSansTC」，與本次核准規格衝突，且直接讀取靜態字體不存在的 Runtime sourceFontFile。已把規則明確改為：既有三個 rankLabel 引用必須使用專用靜態粗襯線，其他文字仍必須使用 NotoSansTC-Bold；沒有跳過其他文字檢查。見 [首次回歸](FullRegression_BeforeFontRuleUpdate.txt)。

修正後 [FocusedTests_WithHudFontRules.txt](FocusedTests_WithHudFontRules.txt) **17／17 通過**。六個正式文字引用及靜態 atlas 已重新讀取：[Bindings.txt](Bindings.txt)。最終完整回歸另存 FullRegression_Final.txt，不將第一次失敗紀錄覆寫成成功。

### 最終完整回歸

[FullRegression_Final.txt](FullRegression_Final.txt) 的 RunFinished：**981 通過、2 失敗、1 Explicit 略過，合計 984**。字型規則測試已通過；失敗僅為開工前已記錄的 BossStageAssetTests.GameSceneContainsBossAssetWiring 與 ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt，沒有新增圖示失敗。略過為需獨立 Photon 驗證的 AbilityToggleInputTests。不是全回歸全綠，也不是自然遊戲／Build 驗收。

九張原始 PNG 雜湊全部與原始修正前一致；正式 StageHUD／SafeHouse 只有六組字體與材質引用變動。最終編譯完成。正式渲染與核准預覽位元組一致見 [PreviewMatch.json](PreviewMatch.json)。


### 底線字元相容性收尾

完整回歸後發現 TMP 對缺少底線字元發出警告；已補上底線、重建同一份靜態 atlas，I／V 輪廓與材質不變。重渲染的 42 階級 PNG 仍與核准預覽 SHA-256 完全相同，51 文字／63 mesh 檢查通過；最終字型聚焦測試另存 FocusedTests_FinalFont.txt。完整回歸紀錄來自補底線前；此相容性收尾未重跑全套。

最終補底線後聚焦測試 **17／17 通過**（FocusedTests_FinalFont.txt）；字型經 Domain Reload 後仍為靜態、包含底線。Console 無 error；保留歷史底線警告、MCP 清理舊測試工作／WebSocket 重連及 Runner Undo 耗時警告，未清空使用者 Console。
