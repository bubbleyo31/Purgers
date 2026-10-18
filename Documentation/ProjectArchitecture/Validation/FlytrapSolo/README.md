# 捕食原地釋放與單人武器反擊

日期：2026-10-16。範圍：EnemyFlytrapAttack.ReleaseVictim、PlayerActionGate.Capture、Player 輸入過濾、TankMeleeCombo／PreparedDash，以及聚焦／隔離 Runtime 驗證。

## 需求及實作

- 正常完成與提前解救／中止均在解除當下的位置釋放，不回到原捕捉點；原 KCC 後續恢復重力與正常移動。
- Host 依實際 Runner.ActivePlayers 同步單人武器資格；僅一人時保留主武器 Fire／Reload，技能、Aim、移動、鉤索、Quick Action 與 ActiveAbility 持續封鎖。
- 人數變為多人時取消當前武器動作，所有武器輸入重新受限；剩一人再恢復。捕食傷害仍用抓住當時的快照。
- Tank 不允許武器預備衝刺；單人受困時可朝實際瞄準方向原地近戰，保留原距離、扇形、LOS 與 Damage Resolver。其他狀態保留原水平 Yaw。
- 無新增 Gameplay 模組、傷害入口或位移系統；無 Scene／Prefab 參數修改。

## 先失敗再修正

1. 修改前隔離 Play Mode 在 solo capture allows primary weapon fire 斷言失敗，證實原本封鎖 Fire。
2. 實際 Tank 準備／放開已可出手且不能位移，但原水平判定打不到正下方捕食者；captured Tank melee damages real captor using normal hit geometry 斷言失敗。補上僅單人受困時跟隨實際瞄準的例外。
3. 原等待秒數測試改成每 Input Tick 觀察第一口傷害，避免 Editor 卡頓造成假失敗；測試瞄準同時設置 KCC FixedData，避免只改 Render Look 而在下一 Tick 消失。這兩項為驗證工具修正。

## 結果

- Unity 編譯完成，無本輪新增編譯錯誤／警告。編譯時可見原有 DemoController 隱藏 camera、TankPreparationRuntimeVerification 比較及 BattleHudReferenceLayout 過時 API 警告；未改動這些來源。最終 Console 僅有清除過期 MCP 測試 job 的工具警告。
- [聚焦 EditMode](focused.txt)：59／59 通過，包含捕食 38 項與 Tank 預備／衝刺 21 項。
- [隔離單人 Play Mode](single.txt)：49 項 PASS，涵蓋原地正常／提前釋放、真實步槍傷害／換彈／自救、Tank 瞄偏揮空／向下命中、預備出手但不衝刺及技能輸入封鎖。
- [同 Editor 受控 Host／Client](host-client.txt)：62 項 PASS，另含晚加入、重連、單人轉多人武器封鎖、離線剩一人恢復、Client 無權自行釋放及受困／死亡同步。
- [PurgersRegression](regression.txt)：919 通過、1 既有失敗、1 明確略過。失敗仍為 BossStageAssetTests.GameSceneContainsBossAssetWiring（Game 場景 Boss 引用），略過為需 Photon 的 AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState，未將完整回歸宣稱全綠。
- 全回歸跨 Domain Reload 後 MCP job 顯示 completed=0；使用事前 Arm 的 EnemySuicideTestResults.RunFinished 完整結果檔確認非零執行，確認 Editor 停止後才清除過期 job。
- Game.unity、_Menu.unity、Enemy_Flytrap.prefab、KCC_Player.prefab 與修改前 SHA256 相同；Editor 最後停在 _Menu、非 Play Mode、Scene 無 dirty。

## 驗證邊界

Runtime 驗證建立隔離 Runner 與地板，使用正式玩家、職業武器及捕食者 Prefab；不儲存場景。部分舊藤蔓情境會調整測試 Clone 的速度、時間以觀察邊界；實際單人反擊僅延長測試 Clone 的吞食時間至 300 秒，未放寬武器傷害、距離、扇形、遮擋或新增命中 Collider。

同 Editor 多 Runner 只證明受控 Host／Client，不等於獨立程序、多機延遲或自然多人遊玩驗收；實際關卡擺位、嘴邊落地觀感與正式動畫仍須手動確認。Host／Client 須使用同版新增 Networked 欄位。