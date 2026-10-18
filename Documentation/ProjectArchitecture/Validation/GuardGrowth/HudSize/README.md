# 格檔 HUD 小幅放大驗證

日期：2026-10-18。範圍僅本機呈現、共用設定與既有排版測試；三項屬性圖示 V2 仍是外部預覽，沒有匯入 Unity 或寫入 Reward Definition。

## 實際調整

| 項目 | 原值 | 本次 |
|---|---:|---:|
| 格檔狀態／數值字級 | 12 | 14 |
| 標籤預留高度 | 20 | 21 |
| 格檔整列寬 | 280 | 308 |
| 扣除圖示預留後的量條寬 | 251 | 279 |
| 量條高度 | 7 | 8 |

`StatusEffectHud.asset` 已透過 Unity Editor 保存 `guardFontSize=14`、`guardWidthMultiplier=1.1`、`guardBarHeight=8`；原異常圖示、量條與技能施法條尺寸不變。格檔與異常條中心仍對齊，寬度受父層限制。字級範圍 8～24；文字框高度至少 20，並依 1.5 倍字級向上取整。

## 聚焦與視覺證據

- 編譯／Fusion weaving 完成，沒有新增可歸因本次的編譯錯誤或警告。
- 聚焦 `GuardGrowthIntegrationTests` **3／3** 通過：[RunFinished XML](focused.xml)、[摘要](focused-summary.txt)。MCP 初始化逾時封包不是結果；採用本次 09:22:54 UTC 新產生、確實包含三項案例的正式回呼報表。
- [文字量測](measurements.json)：格檔、等待恢復、破防、恢復中共八個文字欄位全部容納於自己的 RectTransform。字高 20.28，文字框高 21；最長標籤 95.02 寬，可用 172.98。
- 四列異常及技能施法條同時顯示，格檔頂端由一般 80 調至 57，保留施法名稱上方 8 單位間距；三列以下維持 80。既有隱藏／收合與破防空條測試仍通過。
- 正式元件／正式施法條 Prefab 的 1920×1080 離屏渲染：[格檔](hud-guard.png)、[破防](hud-broken.png)。採用獨立 Preview Scene 與 Camera.Render，沒有 Focus／Show／桌面輸入。

## 完整回歸

本次 09:30:20 UTC 的正式 RunFinished 報表：**1222 通過、4 失敗、2 跳過，共 1228 項**。[XML](regression.xml)、[摘要](regression-summary.txt)。四項失敗與先前完整回歸一致，沒有新增失敗：

- BossStageAssetTests.GameSceneContainsBossAssetWiring
- EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules
- EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners
- ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt

未修改上述系統或放寬斷言，完整回歸不是全綠。測試後重新讀取正式設定，14／1.1／8 均已保存；_Menu 乾淨、非播放、Runner=0、Console Error=0，Editor 已釋出。

## 驗證邊界

接管前確認前一順位明確釋出 Editor；PlayUnfocused（GameView behavior=2）、Run In Background=true。未改動遊戲規則、輸入、傷害、網路資料或存檔；本次不把先前受控 Host／Client 驗證算作重新執行。自然實戰、其他解析度與 Build 視覺驗收仍待使用者確認。
