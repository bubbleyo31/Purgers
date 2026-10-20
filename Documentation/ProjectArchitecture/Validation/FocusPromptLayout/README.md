# ALT 提示排版套用

最後核對日期：2026-10-21（局部核對）。
核對來源：HudKeyPromptRow、LocalPlayerBattleAbilityHUD.FocusSlots、LocalPlayerRewardHUD、HudKeyPromptStyleSetup、StageHUD.prefab、SafeHouse.unity。
正式設定：[111 共用按鍵提示](../../111_HUD按鍵提示共用美術與位置.md)。

## 使用者確認與套用

- 使用者確認第二版預覽並授權關閉 Play Mode。FocusDropHint 保留播放時調整的 offset (-50,103)、320×26、文字色 #78EE23 與字級 14。
- 「按住」＋原共用 ALT 鍵帽＋「+ 技能鍵 丟棄」；鍵帽連外框等比縮至中文字形可見高度，現在約 13.244 Canvas 單位。仍讀 Rewards 綁定；整列由原顯示權重淡入淡出。
- AltPrompt 保留各資產外層位置與尺寸，拆成 Description1「按住」＋原鍵帽＋Description2「選擇升級獎勵」。按住狀態色、待選資格與候選準備中文字沿原 owner。
- 透過 Editor 寫入 StageHUD.prefab 與 SafeHouse.unity；_Menu 沒有這兩個 HUD owner，檔案未改。未改輸入、技能丟棄判定、權威或原放大／淡出時間。
- Editor 重存另補齊既有 LocalPlayerRewardHUD 的 replacementTitleOffset／focusSelectionPanelSize 預設序列化值；與原程式預設相同。

## 證據與限制

證據保留於 `deliverables/FocusPromptLayout_20261021/`（M 槽，約 1.55 MB）。本輪未建立工作樹或額外 Unity Library；原始備份與預覽腳本暫存於 `Temp/FocusPromptPreview/`，未自動清理。

- 新增排版測試先執行：3 項因缺少新排版入口而預期失敗，見 `focus-prompts-red.xml`。
- 新排版 4 項＋既有共用鍵帽 7 項：11/11，見 `focus-prompts-focused.xml`。涵蓋前後說明、位置、中文字形高度、ALT／長鍵名改鍵、原圖片與已序列化子物件重用。
- 實作後 Unity 離屏渲染：`applied-focus.png`、`applied-reward.png`，已檢視與核准小鍵帽版一致。
- 兩個正式資產皆無 Missing Script，HUD 參考／圖片／文字／位置／顏色核對通過。以隔離 Preview Scene 呼叫實際 HUD owner，確認提示建立、整列透明度及 Reset 隱藏；見 `verification.txt`。此為受控 Editor 呼叫，不冒充自然 Play Mode。
- 套用前同步 PurgersRegression：1310 項，1307 通過、3 失敗；套用後：1314 項，1311 通過、3 失敗。兩份 XML 的失敗名稱與訊息相同：BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners。
- 同步模式未涵蓋 29 項協程測試。完整非同步執行曾停在 AbilitySlotCenteringTests，後續確認框架已停止但沒有 RunFinished/XML；第二次非同步嘗試也未產生完成結果。因此完整回歸未通過驗收，不將同步子集宣稱為全綠。
- 最後 Console 查詢 0 error／warning。未執行 Build、獨立 Host／Client、晚加入或自然遊玩視覺驗收。Play Mode 保持關閉，未使用前景聚焦操作。
