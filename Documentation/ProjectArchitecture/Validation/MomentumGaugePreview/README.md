# 動能量條流光：正式整合與獨立預覽

最後核對日期：2026-10-19（本次量條範圍）
核對來源：LocalPlayerSpeedSlider、MomentumGaugeFlow.shader、MomentumGaugeFlowCore.cginc、Unity 離屏／聚焦／受控多人結果。
相關文件：[100 UI](../../100_UI觀戰與聊天.md)、[31 動能](../../31_鈎索動能與傷害倍率.md)。

## 已套用行為

使用者先確認獨立 Unity 預覽，後續明確要求「未滿也流動、滿值變色加速」並授權直接實作。

- 保留原 Momentum.png 斜角輪廓、量條位置與尺寸。未滿維持原藍色，已填區域持續慢速流動；滿值基色 #25FFD9、流速預設兩倍。
- 尺寸固定使用 normalGaugeSize，字級固定使用 normalLabelFontSize；移除整體縮放與顏色呼吸。保留原文字狀態色。
- 動畫相位連續累積，進出最高強化只換色／流速，不重置或跳位；空量條由原 Image／Slider 填充幾何裁切。
- 沿用原 NormalizedEnergy、DamageMultiplier、IsMaximumBoostActive 與本機／觀戰 PlayerObject 路由。無 RPC、無動能或傷害寫入；充能、衰退、保持 Timer 不變。
- 本次未改寫周邊薄霧／外框、原 PNG 或其他 HUD；保留外框既有責任。收尾雜湊檢查發現 ScreenEdgeEffects.asset 在 15:56:22 另有變更（包含既有外框色／強度），本次操作未寫入該檔，因此保留現況、不回復舊雜湊。不得宣稱工作期間所有外框設定都未變動；見 integration-integrity.json。

## Inspector

_Menu / GameplayHUD Canvas / PlayerHUDCanvas / ReferenceFrame / PlayerHUDController 上的 LocalPlayerSpeedSlider：

| 欄位 | 本次值／責任 |
|---|---|
| Maximum Boost Gauge Color | #25FFD9，RGB 37,255,217 |
| Gauge Flow Speed | 0.5，一般流速 |
| Maximum Boost Flow Speed Multiplier | 2，滿值倍速 |
| Gauge Flow Strength | 0.7，局部紋理明暗強度；0 關閉流紋 |
| Normal Gauge Size | 保留既有約 561.84×85.21 |
| Normal Label Font Size | 保留 24 |

maximumBoostGaugeSize／maximumBoostLabelFontSize 原序列化欄位保留並隱藏，避免破壞相容性，現在不參與放大。場景本次差異僅滿值色與三個新流光參數，未改其他接線。

正式 shader 在 Resources/Presentation，確保 Build 可由既有 HUD 載入。每個 HUD 建立自己的 HideAndDontSave 材質；支援 Sprite atlas UV、stencil 與 RectMask2D 裁切，更新 stencil 衍生材質的相位。停用、失去玩家或銷毀時還原原材質並釋放，重新綁定會重建。這不是新的資料 owner。

## 預覽

Unity 選單：Tools > Purgers > Previews > Momentum Gauge (Preview Only)，不需 Play Mode。

一般 65% 與目前狀態均會流動；可切滿值維持、一般 65%、充滿→退出、暫停，並調整一般流速、滿值倍率與紋理強度。模擬循環為 2 秒充滿／4 秒保持／2 秒退出，僅示範，不是正式計時。預覽和正式 UI 共用流紋核心。

預覽展示 562×50，對應現有容器與 Fill Y Scale 0.58852；獨立預覽不重現整套 HUD 座標及旋轉。正式 HUD 圖另由現有 UI 複本和實際材質離屏渲染。

## 驗證

- 先確認聚焦測試失敗：舊滿值樣式會將 562×85 改為 700×100（測試設定），job a467b8a3835840c2b2f40faa5adffe48。
- 修正後聚焦 4/4 通過，job 93c30647c02b4e899897989aa3ec4281：固定尺寸／縮放／滿值色、連續相位／兩倍速／退出還原、停用清理／重建、不同 HUD 材質隔離。
- 純色中心採樣 RGB 37,255,217；一般跨時間動畫像素 43,954、滿值 67,258；原輪廓外變化 0。96 幀／24 FPS／4 秒循環。
- 同 Editor 受控 Host／Client 通過：Host／Input Authority、未滿動畫、同步滿值色、不同玩家不串狀態、固定尺寸、延後綁定還原滿值、退出還原、停用重啟、Despawn／換玩家、斷線清理。記錄見 [host-client.txt](host-client.txt)。測試只向臨時 Host 實例注入動能，不改正式充能規則。
- Unity 編譯完成；既有外部 DemoController、TankPreparationRuntimeVerification、LocalHealthSegmentView、BattleHudReferenceLayout 警告及 TMP 缺字／MCP 訊息分開保留，未宣稱專案零警告。
- 完整 PurgersRegression 原生 RunFinished（2026-10-19 16:06:03 台北時間）：實際執行 1255，1249 通過、4 失敗、2 跳過；探索總數 1270 含非本分類測試。見 [regression.xml](regression.xml)／[summary](regression-summary.txt)。四個失敗與 ScreenEdgeEffects 基線同名：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt。不是全綠，不修範圍外資產。MCP 曾回報 total=0 或保留過期執行狀態，不把這些當作通過；確認原生完成後清除失效 job。
- 未執行獨立 Build、獨立程序 Host／Client、自然遊玩或使用者最終美術驗收。

所有操作沿背景安全路徑；PlayUnfocused 已確認，沒有自動開預覽視窗、Scene View 截圖或桌面輸入。

## 證據

- [Unity 動態對照](../../../../deliverables/MomentumGaugePreview_20261019/preview.gif)
- [正式 HUD 未滿](../BattleHUD/momentum-gauge-partial.png)／[正式 HUD 滿值](../BattleHUD/momentum-gauge-full.png)
- [離屏像素檢查](../../../../deliverables/MomentumGaugePreview_20261019/verification.txt)
- [整合範圍檢查](integration-integrity.json)

protected-files.json 是前一階段「僅預覽」的歷史紀錄，不能用它宣稱本次正式程式／場景仍未修改。尚未新增正式索引條目，待使用者確認收錄。
