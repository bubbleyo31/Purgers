# 三專注槽實作與驗證

最後核對日期：2026-10-20。
核對來源：本目錄證據、FocusAbilitySlotRulesTests、FocusAbilityIntegrationRulesTests、RewardGrowthRuntimeVerification.FocusSlots。

## 已實作

- 開局 E／Z／X 三槽、依序填空、各 Runtime 自身冷卻。原 Q 不變。
- ALT＋新按下的技能鍵丟棄；按住 ALT 右下整組含框架平滑放大，顯示「按住 ALT + 技能鍵 丟棄」。改鍵後顯示實際鍵位。
- 滿槽選取專注獎勵時開額外「替換」視窗，採原獎勵視窗尺寸與三卡版面。顯示已裝備圖示及 E／Z／X；放開 ALT 取消。
- 持有的技能排除後續新抽選；重複拾取拒絕。未開啟重複技能強化或共享冷卻。
- 狀態／存檔保留中間空槽、技能等級；掉落保留既有冷卻語意。設定版本 2，存檔版本 6。
- Host 決定換裝、掉落與獎勵扣除；Input Authority 只收集意圖。保留既有移動互斥與 ActionGate。
- 獎勵 NetworkString 改為 32 字元並由 Catalog 拒絕過長 ID。正式 Catalog 最長 25；GameLogic 織入 5513 words。保留原字典人數容量，避免擴槽造成的 Fusion 32 KiB 配置失敗。

## 資產與操作

已用 Editor API 遷移 DefaultGrappleAbilitySlots、非移動專注 Definition 共裝規則、StageHUD 與 SafeHouse 技能容器。未手寫 YAML。FocusAbilitySlotsSetup 的選單為明確手動遷移，不在載入時自動改資產。

HUD Inspector：focusManagementScale=1.18、focusManagementTransitionSeconds=0.15、focusManagementHintOffset=(0,-40)；替換標題 replacementTitleOffset=(0,-80)。離屏預覽工具不使用 Focus／Show／GetWindow。

## 驗證

- Unity 編譯無新增錯誤；四項原有編譯警告不屬本次變更。
- 聚焦 EditMode：14/14 通過，fresh RunFinished XML 見 focused.xml。
- 同 Editor 受控 Host／Client／late join：32 PASS，見 host-client.txt。測到 Z 路由、E/Z 共存、X 二次施放及獨立 CD、丟棄轉移、指定替換、stale revision 拒絕及晚加入同步。透過 Fusion 輸入回呼注入意圖，未模擬桌面鍵鼠。
- 完整 PurgersRegression：fresh RunFinished XML 為 1292 項、1286 通過、4 失敗、2 略過，見 full-regression.xml，並非全綠。失敗為 BossStageAssetTests.GameSceneContainsBossAssetWiring（引用不同）、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules（0.25 對 0.5）、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners（75 對 >80）與 ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt（NullReferenceException）。這些目標程式／資產未由本任務修改；先前 StatusEffects 驗證也記錄相同四個測試名稱，但 Reflector 當時斷言數值不同，本輪以此 XML 為準。
- 本次造成的三項舊規格斷言已同步更新：非移動專注可與舊技能共裝（仍驗證 Blink 移動互斥）、存檔版本 6、18 個綁鍵；重跑後皆通過。
- 離屏替換視窗見 replacement.png；只驗證畫面排版。

未驗：獨立程序 Host／Client、Build、實際鍵鼠自然遊玩、不同解析度的 ALT 放大視覺與存檔重連完整流程。受控測試不是這些驗收的替代品。

## 工作位置

主專案已整合本任務修改；隔離分支 codex/focus-three-slots 在 M:/CodexWorktrees/focus-three-slots/Purgers。原檔與逐檔整合雜湊保存在該工作樹 .task-baseline；未提交、未自動清理，沒有另建 Unity Library。
