> **歷史預覽範圍**：本頁的「尚未實裝」描述適用於當時預覽版本。2026-10-19 使用者已另行授權正式接入；現況請讀 [112 正式模組](../../112_玩家畫面外框狀態效果.md) 與 [正式驗證](../ScreenEdgeEffects/README.md)。原預覽成果保留供對照。

# 受傷螢幕外框 V1：單次傷害與持續受傷預覽

最後核對日期：2026-10-19。外觀與節奏為試調草稿，未正式接入、未定案。
來源：`Assets/Scripts/Editor/HealingEdgePreviewWindow.cs`、`HealingEdgePreviewWindow.Damage.cs`、`HealingEdgePreview.shader`。
沿用 [治療外框預覽](../HealingEdgePreview/README.md) 的離屏繪製與歷史 HUD 保留；正式資料責任仍為 [49 玩家受傷來源指示](../../49_玩家受傷來源指示.md)。

## 範圍與外觀

使用者要求製作「持續受傷」與「單次傷害」，延續先前的 Unity 預覽、不實裝限制，以及上下需有存在感、不要遮擋 HUD 的要求。

- 單次傷害：紅色快速閃入、短停、漸退。四邊向內透明，最外圈稍深，四角較強，帶低幅不規則邊緣變化；沒有治療十字。
- 持續受傷：維持較淡紅色底層，每次模擬扣血刷新一次脈衝；取最大權重，不相加。脈衝峰值為單次的 0.84 倍，底層為 0.32 倍。模擬停止後從當前權重淡出，不突然回到最大亮度。
- 這是受傷回饋，沒有根據剩餘 HP 顯示殘血心跳，也沒有自行推測傷害方向；原有紅弧與來源標記責任維持不變。
- 只有 Editor 程式、Shader 與交付物；沒有建立 Runtime 元件、Scene／Prefab 配置、Renderer Feature 或 Resources 設定。
- 不呼叫 PlayerHealth、不訂閱 LocalDamageReceived、不發 RPC、不造成真實扣血。權威傷害路徑仍為 DamageReceiverUtility → PlayerHealth → RPC_ReceiveLocalDamage → LocalDamageReceived → LocalPlayerDamageIndicator。

背景為專案既有 `Validation/SafeHouseGameplay/hud-scene.png` 歷史截圖；血量、槍械及 HUD 都是靜態的，動畫不是本次實戰錄影。專用 HUD 遮罩僅供此截圖合成：面板與技能卡按位置保留，下方圖形按顏色保留；不保證所有抗鋸齒邊緣精準。未來正式接入必須把效果畫在真正 HUD 下方，不能將截圖座標／色彩判斷移到 Runtime。自訂背景不套用此遮罩。

## Unity 操作

1. 手動開啟 `Tools > Purgers > Previews > Damage Edge (Preview Only)`；不需 Play Mode。
2. 選「單次傷害」或「持續受傷」。可播放／暫停、拖時間、「停止／清除」，或開啟「對照：關閉效果」。
3. 調整受傷色、強度、左右寬度、上下高度、四角、淡入／維持／淡出；持續模式另有模擬扣血間隔與底層強度。
4. 深色／淺色與 16:9／21:9／16:10 可檢查輪廓。歷史截圖在非 16:9 會被拉伸，比例檢查宜選純色背景。
5. 「匯出目前畫面 PNG」保存至 `deliverables/DamageEdgePreview_20261019/current.png`，另輸出 `current-style.json`、`current-timing.json`。調整只存在預覽視窗，不寫入玩家設定或正式資產。

治療與受傷共用同一 EditorWindow／繪製實作，切換選單會切換模式；兩組樣式分開。預覽視窗由使用者手動開啟，背景自動輸出不使用 GetWindow、Focus、Show，不進播放、不存場景。

## V1 試調值

| 參數 | 初值 | 說明 |
|---|---:|---|
| 主色 | RGB (0.96, 0.16, 0.10)，約 #F5291A | 深紅外沿，中心透明；Color Alpha 不使用 |
| 強度 | 0.66 | 0～1，乘上時間權重 |
| 左右寬度 | 0.15 | 每側畫面寬度比例；四角最遠 1.65 倍 |
| 上下高度 | 0.115 | 各占畫面高度比例 |
| 四角加強 | 0.50 | 額外強度 |
| 淡入 | 0.035 秒 | 快速但非瞬間硬切 |
| 單次維持 | 0.065 秒 | 每次脈衝峰值短停 |
| 淡出 | 0.52 秒 | 單次效果合計 0.62 秒 |
| 模擬扣血間隔 | 0.55 秒 | 可調 0.35～1.2 秒，不改真實傷害 Tick |
| 持續底層權重 | 0.32 | 可調 0.10～0.65；初次受擊隨淡入建立 |
| 持續模擬時段 | 3 秒 | 開始前有 0.4 秒空白；停止後 0.52 秒淡出 |

單次 GIF 長 2.8 秒、持續 GIF 長 5 秒（皆含前後無效果空白供比較）；25 FPS。Unity 視窗循環長度按效果參數計算，結束後空白 1 秒，與 GIF 的留白長度不同。持續模式在結束時刻不再觸發新 Tick。

## 產出與核對

交付物：[DamageEdgePreview_20261019](../../../../deliverables/DamageEdgePreview_20261019)。含兩段 GIF、1920×1080 峰值與透明 PNG、深／淺色背景、1680×720 寬螢幕，以及各 70／125 張 Unity 原始幀。`package_preview.py` 只編碼 Unity 幀與檢查像素，不重畫效果。

- Unity C# 可編譯並成功執行離屏輸出；shader messages 為空。既有 `BattleHudReferenceLayout` 過時 API 警告仍在。重新匯入期間曾出現本預覽檔案 SourceAssetDB 修改時間不一致（Import Error Code 4），Unity 緊接重新匯入成功；後續 Console error 查詢為空，輸出成功。
- [像素核對](../../../../deliverables/DamageEdgePreview_20261019/pixel-verification.json)：兩模式中央 (462,280)～(1458,800) 的 alpha／RGB 差異均 0；上下 alpha 有效。上方面板、兩技能卡整區 RGB 差異 0，下方 11 個前景抽樣點不變。不能據此宣稱正式 HUD 排序已完成。
- 兩段首尾皆回到無效果；1.2 秒時單次已清除、持續仍有效。原治療峰值對照圖逐像素差異 0，確認共用 Shader 擴充未改原治療輸出。
- 既有相鄰測試 AbilityColorGradingTests、DamageIndicatorTests、FirstPersonGogglesTests：**20/20 通過**，見 [focused.xml](focused.xml)。不是預覽視窗的完整互動自動化測試。
- 完整 PurgersRegression **1229 通過、4 失敗、2 跳過，共 1235**；2026-10-19 10:42:40（台北）RunFinished，見 [regression.xml](regression.xml) 與 [摘要](regression-summary.txt)。4 個失敗名稱與上一輪治療預覽完全相同：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt。沒有改動或修復這些範圍。MCP 曾停在 34 項舊快照，以實際 RunFinished XML 判定，完成後才清除殘留 job。
- Unity 內以 1 ms 步距抽樣 0～5 秒、間隔 0.35／0.5／0.55／0.75／1／1.2 秒：持續權重均在 0～0.84，停止後單調淡出。這是局部數值核對，非新增完整行為測試；摘要見 [editor-verification.json](editor-verification.json)。
- 未執行 Build、正式整合、Host／Client 或自然實戰。視窗人工操作與最終美術驗收待確認；離屏輸出後 _Menu 維持未 dirty、EditMode。

## 後續接入邊界（尚未實作）

如外觀通過，再沿既有本機受傷事件加呈現入口，確認持續傷害的事件來源、停止條件、護盾全吸收是否顯示、傷害量強度映射、與治療／殘血的優先權及總強度上限。持續底色不可直接等同「仍在持續扣血」的權威判定。需處理死亡、觀戰、換場清理與真正 HUD 排序。

建議外觀確認後將「受傷外框／單次傷害／持續受傷／Damage Edge Preview」加入關鍵詞索引，路由至本預覽與正式 49；本輪未自行寫入設計定案。
