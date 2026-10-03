# 2026-10-02 鈎索使用能量驗證

## 範圍

玩家等級制鈎索使用能量、發射及拉動消耗、升級兩種補充方式、HUD 與重新生成／正式切場補滿。左下動能增傷未改。設定與人工驗收見 [30](../../30_鈎索系統.md)。

## EditMode 與編譯

- 修改前完整回歸：job `106a0d84c4224f70af335438796578ec`，`progress.completed=267`，6 項失敗。
- TDD 預期失敗：job `39ad84b3418d4296bb781a89b6c49061`，6 項確實執行並因新規則尚未提供而失敗。先前 `c5c6b06cef6940df813e79f03c7a1c00` 為 0 tests，明確不算通過；完整 AssetDatabase refresh 後才真正匯入新測試。
- 修改後聚焦：job `7534c27885ba414482f33eb41936b8a4`，17／17 通過。
- 修改後完整回歸：job `814cbd00d1c74edb8feafad09d3cf71e`，`progress.completed=284`、同樣 6 項失敗；失敗回應的 result 為 null，因此採 progress 報告實際執行數，不把 tree total=299 當成已執行數。
- 中途 `d5239221c9244516a20a96e4162c0e41` 初始化逾時、0 項，不列為測試證據。
- Windows Development Build `build-ae363f77d3`：成功，0 error／0 warning。後續僅修正驗證探針的安全屋 HUD 查詢；最終建置與網路結果見下。

六項既有失敗：

1. `BossStageAssetTests.GameSceneContainsBossAssetWiring`
2. `DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel`
3. `FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies`
4. `PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward`
5. `PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards`
6. `PlayerExperienceRulesTests.RequirementDoublesFromTen`

## 網路探針與限制

`GrappleEnergyNetworkVerification` 僅在 Editor／Development Build 且命令列明確包含 `--grapple-energy-test` 時啟用。使用專案 Logs 下獨立測試存檔；不寫現有使用者存檔、Scene 或 Prefab。

首次沙箱程序因 PlayerPrefs 存取失敗、StartGame=False 而中止；取得沙箱外測試執行授權後 Host／Client 均成功連線。第二輪停在探針誤要求 SafeHouse 必須掛載戰鬥圓環；安全屋只驗證資源，Game 才核對中央圓環。此為驗證工具修正，不是更動遊戲 HUD 配置。

探針直接呼叫受控消耗／升級、死亡與切場流程；即使通過也不等同自然出鈎的直拉／側甩、遮擋／落地釋放、Support Tether、Client 預測回滾或畫面視覺完整驗收。這些仍依 30 的人工步驟驗收。

第三輪在 step 0～5 通過 Host／Client 雙端確認，Host step 6 重生補滿也已成立；Client 讀取正被 Host 覆寫的階段檔案時發生 sharing violation，該輪未完成其後流程。探針改為每階段獨立檔案、寫入結束才發布 ready 標記；不得將受中斷那輪記為完整通過。

## 最終建置與資產檢查

- 最終 Windows Development Build：`build-2f7adadac2` 成功，0 error／0 warning；輸出為 `Builds/GrappleEnergyVerification/Purgers.exe`。第二次探針修正建置 `build-43e652c3a4` 亦為 0 error／0 warning。
- Unity MCP 檢查：`isCompiling=false`、`scriptCompilationFailed=false`，當前 Game Scene 非 dirty，最終 Console 0 error／0 warning。
- `KCC_Player.prefab` 與 `StageHUD.prefab` 的階層 Missing Script 均為 0；玩家 Prefab 讀回容量 50／每級 25、發射費 1、拉動每秒 1、Level Up Mode=RefillToMaximum。未改 Scene／Prefab／職業資產接線。
- 自動探針採 `-batchmode -nographics`，因此 Player log 有無 GPU 的 Shader 警告；同時觀察到既有 Attack Runtime 找不到 `AttackFocusAbility` 的配置訊息，本輪未改該 Driver 或職業 Runtime Prefab。不得把 Editor／Build 的零錯誤等同於兩個 Player log 完全無訊息。

## 最終雙程序結果：run4 通過

2026-10-02，獨立 Host／延後加入 Client，兩端皆產生 complete=PASS，10 個步驟全部通過。[Host 精簡紀錄](Host.log)／[Client 精簡紀錄](Client.log)。原始 Player log 與測試存檔位於專案 `Logs/GrappleEnergyVerification/run4`。

| 步驟 | Host 能量 | Client 能量 | 驗證 |
|---|---|---|---|
| 0 | 49／50 | 50／50 | 發射扣 1，晚加入看見已消耗值 |
| 1 | 46.5／50 | 50／50 | 受控拉動消耗 2.5 秒 |
| 2 | 75／75 | 50／50 | Host 升 2 級，補滿模式 |
| 3 | 75／75 | 35／75 | Client 原有 10／50，升 2 級補差額 |
| 4 | 75／75 | 35／75 | 等待不自然回充，擊殺回充入口不補充 |
| 5 | 0／75 | 35／75 | 剩 0.5 不足發射費不扣款，耗盡後拒絕下次發射 |
| 6 | 75／75 | 35／75 | 正式傷害致死與 Player 重生，按保留等級補滿 |
| 7 | 75／75 | 75／75 | 扣點後 SafeHouse → Game 補滿，Client Game 圓環 fillAmount=1 |
| 8 | 75／75 | 75／75 | 扣點後 Game → SafeHouse 補滿 |
| 9 | 75／75 | 50／50 | 訪客斷線重連，依既有進度規則回到 1 級滿 50；Host 不變 |

每步也確認 Client 自己具 Input Authority 而無 State Authority；Client 的遠端 Proxy 不能消耗或補滿 Host 能量。這是受控 API／網路及 UI 數值證據，未宣稱自然出鈎物理、低網速預測回滾、繩索耗盡不中斷或實際畫面已完成手動驗收。文件相對連結與 task-scoped diff whitespace 檢查通過。
