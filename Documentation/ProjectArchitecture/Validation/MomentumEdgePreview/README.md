> **後續命名與觸發更新**：目前稱為「速度動能增益」。正式遊戲僅滿格時淡入；Editor「蓄能→滿格」也等待模擬滿格才出現。本頁及舊輸出檔保留原預覽歷史；最新驗證見 [FullOnly](../ScreenEdgeEffects/FullOnly/README.md)。

> **歷史預覽範圍**：本頁的「尚未實裝」描述適用於當時預覽版本。2026-10-19 使用者已另行授權正式接入；現況請讀 [112 正式模組](../../112_玩家畫面外框狀態效果.md) 與 [正式驗證](../ScreenEdgeEffects/README.md)。原預覽成果保留供對照。

# 橘金動能增益外框 V1（Editor 預覽，未實裝）

核對日期：2026-10-19。使用者要求速度增益量條有橘金色、具力量與速度感的效果，延續本對話外框效果的獨立預覽範圍。
程式：`Assets/Scripts/Editor/HealingEdgePreviewWindow.Speed.cs`，共用既有 `HealingEdgePreviewWindow.cs` 與 `HealingEdgePreview.shader`。

## 沿用責任

已核對 [31 動能與傷害倍率](../../31_鈎索動能與傷害倍率.md)、[146 HUD](../../146_PrePhase4-3_HUD排列與資訊精簡.md)、`PlayerGrappleMomentumEnergy` 與 `LocalPlayerSpeedSlider`。雖保留 Speed 舊類名，現有量條是 `NormalizedEnergy` 動能增傷率，不是世界速度；現有滿格量條橘色、文字金色，因此本預覽沿橘金意圖延伸。

本次沒有改動上述玩法／HUD、不增加速度或傷害倍率、不讀寫 Networked 資料、不接新事件、不放 Scene／Prefab、不改 Renderer Feature、相機 FOV 或震動。程式在 Editor 目錄，沒有加入正式 Runtime 外框系統。

## 視覺與節奏

- 橘色四邊光暈＋亮金色透視流線。流線由畫面周邊向外掠過，尾端漸細／漸透明，形成向前加速感；32 條不同相位的射線，沒有治療十字。
- 模擬比例增加時，流線變長、整體變亮；滿格仍持續移動，邊緣有低幅明暗變化。中心瞄準區完全透明。
- 預設示範：0.4 秒後從約 14% 開始，2.2 秒內逐漸達滿；4.4 秒開始退去、0.65 秒淡出；整段含留白為 5.8 秒。這些是預覽編排，不是正式充能、最高強化 Timer 或衰退規則。
- 另提供「滿格維持」：以淡入直接展示滿格外觀，採相同停止／淡出時刻。未額外加入滿格瞬間爆閃。

背景是歷史 `Validation/SafeHouseGameplay/hud-scene.png` 靜態截圖，原量條與數字不隨模擬比例改變。沿前兩版的截圖專用 HUD 保留遮罩；已核對面板、技能卡與前景抽樣點，不能視為完整抗鋸齒邊緣或正式 HUD 排序驗證。自訂背景不套用遮罩；正式接入需要真正的 HUD 分層，不能使用截圖座標／顏色分類。

## 開啟與調整

1. 手動開啟 `Tools > Purgers > Previews > Momentum Edge (Preview Only)`，不需 Play Mode。
2. 「蓄能→滿格」觀看漸強；「滿格維持」直接看最高強度。可播放／暫停、拖時間、停止清除、切換效果對照。
3. 可調主色、強度、左右／上下範圍、四角加強、淡入淡出與流線速度；預覽下方顯示模擬比例。
4. 深／淺色、16:9／21:9／16:10 可檢查輪廓；非 16:9 歷史截圖會拉伸，比例檢查用純色背景。
5. 匯出目前 PNG、`current-style.json`、`current-motion.json` 到 `deliverables/MomentumEdgePreview_20261019/`。參數只存在視窗，不保存正式資產。

| 預設參數 | 數值 |
|---|---:|
| 橘色主色 | RGB (1, 0.53, 0.075)，約 #FF8713 |
| 強度 | 0.64 |
| 左右延伸 | 每側畫面寬度 10.5% |
| 上下延伸 | 每側畫面高度 7.5% |
| 四角加強 | 0.60 |
| 淡入／淡出 | 0.16／0.65 秒 |
| 流線速度 | 1.6，可調 0.5～3 |
| 流線數 | 32，Shader 固定；無新增貼圖 |

流線延伸範圍較底層柔光長，限於左右 22%／上下 17% 內漸退；長度跟模擬比例增加。所有模式共用 EditorWindow 與離屏輸出；各選單會互斥切換模式，樣式獨立保存。

## 交付與驗證

[動畫與原始 Unity 幀](../../../../deliverables/MomentumEdgePreview_20261019)：`momentum-preview.gif` 為 960×540、25 FPS、145 幀／5.8 秒；另有 1920×1080 峰值、透明層、深淺背景與 1680×720 寬螢幕圖。Python 只編碼原始 Unity 幀與核對像素。

- C# 編譯與 Unity 離屏輸出成功；最終 Shader messages 為空。初次新增 Shader 的保留字 `line` 編譯錯誤已改為 `lineMask` 並重新匯入驗證。既有 `BattleHudReferenceLayout` 過時 API 警告仍在。
- [像素核對](../../../../deliverables/MomentumEdgePreview_20261019/pixel-verification.json)：整段中央區域 RGB 都不變，峰值中心 alpha 0，上下有效 alpha 各 80；面板／兩技能卡整區 RGB 差異 0，下方 11 前景抽樣點不變；首尾清除，滿格仍有動畫。
- 治療 V1.2 與受傷 V1 對照峰值圖整張 RGB 差異均 0。
- 既有相鄰聚焦測試 AbilityColorGradingTests、DamageIndicatorTests、FirstPersonGogglesTests、HudLayoutTests：**46/46 通過**，見 [focused.xml](focused.xml)。不是新外框視窗的完整互動測試。
- 完整 PurgersRegression **1229 通過、4 失敗、2 跳過，共 1235**，2026-10-19 14:47:21（台北）RunFinished，見 [regression.xml](regression.xml)／[摘要](regression-summary.txt)。失敗名稱與前輪治療 V1.2 相同：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt。未修改或修復這些範圍；採實際 RunFinished XML，完成後才清除 MCP 殘留 job。
- 沒有進入 Play Mode、修改或儲存 Scene；人工視窗操作、自然遊戲、Host／Client、Build 與正式 GPU 效能待驗。離屏預覽不能當作 Runtime 效能證據。

## 正式接入前待定

外觀確認後再決定效果從多少動能開始、是否僅滿格顯示、`NormalizedEnergy` 與 `IsMaximumBoostActive` 的使用時機、與治療／受傷／殘血的優先級及總強度上限。呈現只讀既有結果，不重新計算充能、衰退或最高強化保持時間。

建議收錄索引關鍵詞「動能增益外框／速度增益外框／橘金／Momentum Edge」，路由本預覽與 31；本輪為外觀草稿，未擅自寫入定案索引。
