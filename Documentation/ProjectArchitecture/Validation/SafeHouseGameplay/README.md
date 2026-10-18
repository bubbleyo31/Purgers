# 安全屋 Gameplay 與 HUD 驗證

日期：2026-10-13。規則／場景配置：[145 安全屋](../../145_PrePhase4-2_安全屋鍵盤準備與HUD.md)。

## 範圍與環境

Unity 2022.3.62f1；既有 `_Menu` 場景在執行前為乾淨狀態。初次程式實作未保存／配置正式 Scene 或 Prefab；同日後續使用者要求「自動配置」後已完成 SafeHouse Scene 接線，詳見下節。未讀寫正式存檔，其他功能原有未提交修改保留。

- `SafeHouseGameplayRulesTests`：13／13 通過，job `4c98b90e57694714a40b0a6af90b6c8d`。包含 Q／E 與一般操作的遮罩邊界、非安全屋狀態、玩家歸屬在來源物件消失後仍有效、未 Spawn Gate，以及從實際 StageHUD 複製兩組子樹後的安全屋 owner／完整 ALT 與 XP 引用。
- 先寫測試再改程式：job `54effe7a2c2a4f3ba4712dba2bb7f654` 實際執行 7 項全部失敗（6 項規則尚未實作，1 項 Gate 尚未 Spawn 的 Networked 存取）；第一次匯入未完成的 total=0 結果不採用。
- 隔離 Single Runner：29 PASS，見 [single.txt](single.txt)。Q 不耗能、E 不起冷卻、Loadout 換装仍可用、所有 DamageType 的自傷與直接 ReceiveDamage 都拒絕、目標 Modifier 未被呼叫（證明在護盾／格擋消耗前拒絕）、環境來源照舊、離開恢復 E／傷害且不清除其他 Gate 來源。
- 同 Editor 受控 Host／Client：37 PASS，見 [host-client.txt](host-client.txt)。另涵蓋晚加入 Input Authority、Host→Client／Client→Host 致死友傷、Client Q／E 封包、離線 Runner 隔離與重連。使用 Asia 臨時私有測試 Session，兩端均在 finally Shutdown。
- Runtime fixture 最初兩輪在 Loadout 尚未初始化時過早換装而失敗；已改等待正式起始 Loadout Tick，再套用測試 Loadout。這是測試時序修正，沒有改正式初始化或覆寫玩家資產。

## 尚未驗證

- 正式 SafeHouse 已接入 BattleAbilityHUD／Phase6RewardHud；Single Runner 場景畫面證據見下節。
- 已確認正式場景 XP 更新、ALT 提示、血量與彈藥顯示；尚未完成自然 ALT 點選、護盾變動、實際射擊換彈的完整畫面驗收。
- 受控多人不是獨立 Windows 程序的自然鍵鼠測試；未宣稱完成正式 SafeHouse→Game→SafeHouse 切場、所有 E 技能逐一施放、真實射擊／近戰／爆炸物理路徑或 Player Build。
- Runtime fixture 以防禦 Modifier 探針確認未進入防禦消耗；未另宣稱安全屋中自然護盾特效驗收。

## 重跑入口

EditMode：`SafeHouseGameplayRulesTests` 與完整 `PurgersRegression` 分開執行並確認測試數非零。Runtime：在乾淨 `_Menu`、無既有 Runner 的 Play Mode，明確呼叫 `SafeHouseGameplayRuntimeVerification.Begin(false)` 或 `Begin(true)`；等待 Status 結束且 Runner 清除，讀取 `Temp/SafeHouseGameplay`。臨時物件只存在測試程序，結束返回 EditMode。

## 最終編譯與完整回歸

- Unity `scriptCompilationFailed=false`，本輪最終 Console error 查詢為 0（測試失敗以正式結果紀錄為準）；本次變更未產生新的編譯错误。先前 Console 留有第三方 DemoController CS0108、既有 TankPreparationRuntimeVerification CS0252 及 MCP WebSocket 警告，不宣稱整個專案零警告。
- 完整 `PurgersRegression`：**755 項，753 通過、1 失敗、1 略過**。唯一失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`，期待 MapChunk_Boss 的既有場景引用與目前資產不符。見 [regression.txt](regression.txt)。本次 SafeHouseGameplayRulesTests 13 項在完整回歸中亦通過。
- 前兩次 MCP job `b20b0a6e453c441ebfe9e523b674e6f9`／`065b07c6f4704d22bf443ee84b7b5bd9` 的狀態因 Domain Reload 沒有完整回傳，不採其 running／completed=0 為成功證據。
- 最後 job `c81f53aef10e4639a21213cf6ef6bb49` 先使用既有 `EnemySuicideTestResults.Arm` 指向本工作專屬結果檔，再啟動完整回歸；正式 Unity RunFinished callback 跨 Domain Reload 寫出以上完整樹。MCP job 仍停在 running，但反射檢查 TestRunnerApi.IsRunActive=false，結果檔已落盤並解除一次性 reporter；僅清除殘留 job，不修改測試結果。
- 最終無執行中的 Runner、返回 EditMode，`_Menu` 場景未標記 dirty；未提交 Git。

## 2026-10-13 後續：依授權自動配置正式安全屋 HUD

- 編輯入口：Unity Editor MCP。修改並保存 `Assets/Scenes/SafeHouse.unity`，新增 `SafeHouseHUD/ReferenceFrame/{BattleAbilityHUD,Phase6RewardHud}`，沿用 StageHUD Prefab 的完整序列化子樹與 BattleHudReferenceFrame 縮放；移除副本的 TopLeftColumn，保留原安全屋 TopLeftColumn。沒有新增 StageHudController、第二個 Canvas 或第二個 Minimap owner；原 Prefab 未修改。
- 配置前快照：`Temp/SafeHouseHudSetup/SafeHouse.before.unity`。場景原有 157 個 YAML 物件區塊全部保留；只有 SafeHouseHUD RectTransform 的 children 增加 1 個引用，其餘原有區塊完全相同。新增 71 個 GameObject／共 286 個序列化物件區塊。保存後重開確認 Battle HUD 1 組、Reward HUD 1 組、CanPresentRewardSelection=true；新增子樹 Missing Script=0、損壞引用=0。
- Play Mode：從 `_Menu` 啟動 Single Runner，載入正式 SafeHouse Scene；使用 `Temp/SafeHouseHudSetup/Saves` 臨時存檔。安全屋 Runner 綁定成立，技能與經驗 CanvasGroup alpha 均為 1，Gate 精確為 Grapple、ActiveAbility，Q 禁用標記可見。血量 100/100、彈藥 30/30、等級 1 與現有版型可見：[場景截圖](hud-scene.png)。
- 在 Fusion Tick 透過既有 GameLogic 給隔離玩家 125 XP；HUD 更新為等級 5、待選獎勵 4 次、ALT 提示，權威狀態 XP=50；Q／E 仍禁用：[XP 截圖](hud-xp.png)、[讀回紀錄](hud-runtime.json)。這是受控 XP 更新與 UI 顯示檢查，不等同自然 ALT 按鍵／領取操作。
- 接線後聚焦測試：13/13，job `fa0dc0c6d10b4845b6e2a2f2b9d4d117`。完整 PurgersRegression：775 項，773 通過、1 既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗、1 Explicit 略過；[正式 RunFinished 結果](hud-regression.txt)。MCP job `b7241670cbc64745b4e4b5a5888d7286` 遇 Domain Reload 停留 running，依持久 reporter 結果及 TestRunnerApi.IsRunActive=false 確認結束後清理殘留 job。
- 最後已 Shutdown 隔離 Runner 並退出 Play Mode；Runner=0，僅 `_Menu` 開啟且非 dirty，scriptCompilationFailed=false，Console error／warning 查詢 0。未提交 Git；獨立程序多人、自然鍵鼠及往返切場未在此次資產配置重跑。
