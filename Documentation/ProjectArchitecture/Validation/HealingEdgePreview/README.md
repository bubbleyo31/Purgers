> **歷史預覽範圍**：本頁的「尚未實裝」描述適用於當時預覽版本。2026-10-19 使用者已另行授權正式接入；現況請讀 [112 正式模組](../../112_玩家畫面外框狀態效果.md) 與 [正式驗證](../ScreenEdgeEffects/README.md)。原預覽成果保留供對照。

# 治療螢幕外框 V1.2：單次／持續、向外收窄預覽

最後核對日期：2026-10-19（預覽工具局部核對）。
核對來源：`Assets/Scripts/Editor/HealingEdgePreviewWindow.cs`、`HealingEdgePreview.shader`。
相關既有責任：[43 LGG 調色](../../43_E技能視覺與UI配置.md)、[49 受擊來源](../../49_玩家受傷來源指示.md)、[103 護目鏡](../../103_第一人稱護目鏡效果.md)。

同日新增 [單次傷害／持續受傷預覽](../DamageEdgePreview/README.md)，以 partial 擴充同一視窗、共用 Shader；兩組樣式獨立，治療峰值逐像素對照差異 0。切換選單會切換共用視窗模式，不建立第二組正式外框系統。

## 授權與範圍

使用者要求參考 Overwatch 的螢幕周邊效果，先做治療預覽，可於 Unity 查看，但不實裝。後續要求補足上下效果、不要遮擋 HUD，V1.1 已依此修訂。外觀尚待確認；未新增正式功能索引定案。

V1.2 依使用者「再往外縮點，且需要單次與持續治療」修訂：左右與上下延伸縮小 25%，四角縱向範圍及十字符號距離同步收窄，色彩／強度／符號尺寸維持。沿用既有兩種治療節奏，現在各自輸出 GIF；舊 V1.1 圖片與證據保留在原目錄，最新交付另放 `v1.2/`。

- 僅新增 EditorWindow 與 Editor 專用 shader；不加入 Scene、Prefab、Resources、Renderer Feature 或 Build。
- 不綁定 PlayerHealth、不發送 RPC、不改生命、傷害、動能、輸入、相機或現有 LGG。
- Unity Graphics.Blit 實際執行 shader；自動輸出不使用視窗 Focus／Show、不進 Play Mode、不保存場景。
- 背景是專案既有 `Validation/SafeHouseGameplay/hud-scene.png` 歷史截圖。血量、HUD 與槍械不會隨預覽變動；不是本次實戰畫面。
- V1.1 增加上下連續柔光與少量十字。截圖已包含 HUD，使用專用保留遮罩：上方圖片面板／技能卡按位置，下方無底板圖形按截圖顏色保留，避免大片矩形缺口。這是歷史截圖專用近似合成，不能宣稱正式 HUD 排序或所有抗鋸齒邊緣已驗證。未來實裝必須把外框放在真正 HUD 下層，不得把這組截圖座標／色彩判斷搬進 Runtime。

## 使用方式

1. Unity 編譯完成後，手動開啟 `Tools > Purgers > Previews > Healing Edge (Preview Only)`。
2. 不需 Play Mode。選「單次治療」或「持續治療」，可暫停、拖時間、停止／清除。
3. 「對照：關閉效果」保留背景；深色／淺色背景方便看完整漸層；可選自訂 Texture2D。自訂背景不套用這張歷史截圖的 HUD 遮罩。
4. 16:9／21:9／16:10 會以實際預覽寬高重新計算十字比例。歷史背景在非 16:9 會拉伸，判讀外框比例請用深／淺色背景。
5. 「匯出目前畫面 PNG」寫入 `deliverables/HealingEdgePreview_20261019/v1.2/current.png` 與 `current-style.json`。視窗調整只是預覽草稿，不寫正式資產或玩家存檔。完整關閉視窗再開會回到預設；要保留數值請匯出 JSON。重開選單時只將舊預設寬高 0.14／0.10 遷移為 0.105／0.075；已手調的其他值保留。可按「還原 V1.2」取得整組新預設。

## 可調數值

| 控制項 | V1.2 初值 | 有效範圍／作用 |
|---|---:|---|
| 治療色 | RGB (0.30, 0.95, 0.64)，約 #4DF2A3 | 淺綠；Color Alpha 不使用。沿既有回血色意圖，非 OW 素材複製 |
| 外框強度 | 0.52 | 0～1；同時縮放光暈與符號，0 全關 |
| 單側延伸寬度 | 0.105 | 0.04～0.24，畫面寬度比例；四角最遠延伸 1.65 倍，仍保留中央透明區 |
| 上下延伸高度 | 0.075 | 0.03～0.20，畫面高度比例；上下亮度基礎為左右的 0.82 倍 |
| 四角加強 | 0.55 | 0～1；V1.2 預設垂直向內 18.75% 高度內漸退，隨上下高度縮小，最多維持舊 25% |
| 十字數量 | 12 | 四邊合計 0～14；前 8 個在兩側，後 6 個分布上下；0 為純柔光 |
| 淡入 | 0.10 秒 | 0.01～2 秒 |
| 單次維持 | 0.16 秒 | 0～3 秒；持續模式固定示範 2.6 秒 |
| 淡出 | 0.70 秒 | 0.01～3 秒 |

單次效果本身共 0.96 秒；循環另含開始前 0.3 秒、結束後 0.7 秒空白，GIF 以 20 FPS 取整為 2 秒。持續模式維持 2.6 秒柔光／緩動十字，結束後淡出 0.7 秒；完整 GIF 共 4.4 秒。以上是外觀試調值，未取代原正式治療 LGG 的 0.45 秒 pulse。左右十字向上移動、上下十字短距離緩動，距離按寬高同步外移、不跨進瞄準區；光暈有低幅呼吸與邊緣細光。

建議先比較原始 0.52 強度與較淡 0.30，再比較有／無十字；一次只改一個項目。

## V1.2 產出與驗證

最新交付：[v1.2](../../../../deliverables/HealingEdgePreview_20261019/v1.2)，含 `single-healing.gif`（40 幀／2 秒）與 `continuous-healing.gif`（88 幀／4.4 秒），以及原始 Unity 幀、峰值／透明層 PNG、純色／寬螢幕對照。沒有覆寫 V1.1 交付物。

- Unity C# 編譯與離屏輸出成功，shader messages 空；既有 BattleHudReferenceLayout 過時 API 警告仍存在。
- [像素核對](../../../../deliverables/HealingEdgePreview_20261019/v1.2/pixel-verification.json)：中央 alpha 與 RGB 差異仍為 0，上下有效 alpha 為 90／92；HUD 面板／兩技能卡整區差異 0，11 個前景抽樣點不變。alpha > 8 的像素從 589703 降至 446643，減少約 24.3%。
- 單次與持續動畫首尾皆清除；1.5 秒時單次已結束、持續仍有效。受傷 V1 峰值對照整張 RGB 差異 0，確認共用 Shader 修改未改受傷外觀。
- 既有相鄰呈現聚焦測試 **20/20 通過**，見 [v1.2/focused.xml](v1.2/focused.xml)。完整回歸 **1229 通過、4 失敗、2 跳過，共 1235**，2026-10-19 11:22:27（台北）RunFinished；見 [v1.2/regression.xml](v1.2/regression.xml)／[摘要](v1.2/regression-summary.txt)。失敗名稱與前一輪受傷預覽相同：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt。採實際 RunFinished XML，完成後才清除 MCP 殘留的舊進度 job。
- 保持 Editor 預覽，不實裝。歷史截圖 HUD 遮罩的限制同上；人工視窗操作、真正 HUD 分層、Host／Client、Build 與自然實戰未驗。

## V1.1 歷史產出與驗證

預覽檔案位於 [deliverables/HealingEdgePreview_20261019](../../../../deliverables/HealingEdgePreview_20261019)：Unity 渲染原始 PNG、透明效果層、88 幀動畫及 GIF。`package_preview.py` 僅將 Unity 幀編碼為 GIF，並檢查像素。

- Unity C# 編譯成功、shader messages 空；沒有本預覽造成的新錯誤。專案既有 BattleHudReferenceLayout 的 TextureImporter.spritesheet 過時警告仍存在。
- Unity 離屏輸出 1920×1080、1680×720，以及 960×540／20 FPS／4.4 秒動畫。中央抽樣區 alpha 最大 0、RGB 差異最大 0；上下非 HUD 區皆有有效 alpha；首尾回到無效果。上方面板與兩技能卡整區 RGB 差異 0，下方 11 個前景抽樣點完全相同。詳見 [像素證據](../../../../deliverables/HealingEdgePreview_20261019/pixel-verification.json)。
- 既有相鄰呈現聚焦測試 **20/20 通過**（AbilityColorGradingTests、DamageIndicatorTests、FirstPersonGogglesTests），證據 [focused.xml](focused.xml)。這不是新增預覽工具的完整互動測試。
- 完整 PurgersRegression 的首輪及 V1.1 最終重跑 RunFinished 均為 **1229 通過、4 失敗、2 跳過，共 1235**；失敗名稱與本輪前 HudKeycaps/style-regression.xml 相同，未更動或修復這些範圍：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt。最終時間為 2026-10-19 10:28:19（台北），證據見 [regression-summary.txt](regression-summary.txt) 與 [regression.xml](regression.xml)。MCP 進度曾停留早期快照，採實際 RunFinished XML 而非輪詢總數判定。
- 未執行正式整合、Build、Host／Client 或自然實戰；視窗實際人工操作與最終美術驗收待使用者確認。離屏輸出後 _Menu 維持未 dirty，未開啟 Unity 視窗或進入 Play Mode。

## 後續擴充方向（未實裝、未定案）

正式化前先決定各狀態的觸發／解除條件、重複事件刷新方式、優先級與最大視覺強度。建議每種效果保存自己的樣式與時間參數，交由單一外框呈現入口合成，不為每個狀態另建常駐全畫面疊層。

| 效果 | 可沿用的資料／待決問題 |
|---|---|
| 治療 | 已有 PlayerHealth.LocalHealingReceived，僅有效正數治療觸發；需確認外框取代或搭配現有 LGG |
| 受傷 | 已有 LocalDamageReceived 與來源快照；應保留紅弧／來源標記，外框只補瞬時受傷感 |
| 殘血 | 讀有效生命快照；門檻、解除遲滯及與受擊的混合待確認 |
| 動能滿級 | 先確認指「動能量條充滿」或其他等級狀態，才接既有動能 owner |
| 其他狀態 | 增加樣式、來源及優先規則；不新增權威玩法判定 |

正式接入應處理本機資格、死亡／觀戰／換場清理、HUD 層級與無限疊加；本預覽沒有這些 Runtime 責任。
