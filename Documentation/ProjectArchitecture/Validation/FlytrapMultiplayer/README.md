# 捕食多人武器反擊驗證

日期：2026-10-17（局部核對）。正式規則／設定：[93 定點捕食植物](../../93_定點捕食植物.md)；索引：ENEMY-FLYTRAP。

## 變更

- 成功抓住後，由 State Authority 將既有 CaptureWeaponAllowed 設為 true，移除依房間人數更新資格與取消武器的分支；保留 Networked 結構、捕捉瞬間的舊動作中斷及免控判斷。
- 單人／多人皆只放行主武器 Fire／Reload。技能、Aim、移動、鉤索、Quick Action 仍禁止，其他來源的控制鎖不會被清除。
- Tank 沿既有 CanUseWeaponWhileCaptured 使用完整瞄準方向反擊；PreparedDashDisplaced 繼續封鎖受困位移。
- 吞食單人 50%／多人 100% 最大生命傷害快照、抵達後計時、原地釋放及冷卻維持。Scene／Prefab 無配置修改。

## 先失敗再修正

[修改前多人案例](expected-failure.txt) 建立真實 Host／Client，捕捉並同步至 Client 後，在 multiplayer capture permits actual weapon counterattack 斷言失敗。修正後使用相同捕捉入口與正式 Prefab 通過。

初次嘗試舊整套 Runtime 入口時，在既有 wall after aim lock 情境等待逾時，尚未到多人權限斷言；本輪未將該情境視為通過，也未改其戰鬥邏輯。另新增 counterattackOnly 聚焦流程驗證本輪行為，保留舊整套入口。

## 本輪結果

- 編譯完成，聚焦測試前 Console 無 error；最終僅測試 Undo 耗時及 MCP 清除過期 job 警告，無本輪新增編譯錯誤。
- [聚焦 EditMode](focused.txt)：59／59 通過（捕食規則／資產／藤蔓／輸入、Tank 預備與衝刺）。
- [單人聚焦 Play Mode](single.txt)：12 項 PASS，主武器傷害／換彈／擊殺原地解救、Tank 向下命中／瞄偏揮空、技能及位移封鎖。
- [同 Editor Host／Client](host-client.txt)：31 項 PASS。Host 與 Client 各有 room players=2 的獨立實際武器檢查；另驗證加入／退出不取消預備、晚加入／重連權限、Client 不可自行釋放，以及多人總傷害與冷卻。
- [PurgersRegression](regression.txt)：981 通過、2 失敗、1 Explicit 略過，未宣稱全綠。跨 Domain Reload 後 MCP session／job 進度失效，使用事前 Arm 的 EnemySuicideTestResults.RunFinished 落盤結果確認實際數量；確認 Editor 非 Play Mode 後才清除過期 job。
- 失敗一：BossStageAssetTests.GameSceneContainsBossAssetWiring，既有 Game 場景 Boss 引用問題。
- 失敗二：ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt，Field 的 NullReference（測試第 80 行）。唯讀核對兩個正式槍械 ViewModel 已各有 1 個 FirstPersonReloadCameraMotion，而測試仍 AddComponent 該 DisallowMultipleComponent 型別後反射寫欄位；屬本輪未改動的換彈鏡頭測試／資產配置不一致，不改動該系統。
- 略過：AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState，需另外執行 Photon 情境的 Explicit 測試。
- [資產雜湊](asset-hashes.json)：_Menu、Game、Enemy_Flytrap、KCC_Player 均與本輪修改前 SHA256 相同；結束於 _Menu，非 Play Mode、無 dirty。

## 測試條件與邊界

EnemyFlytrapRuntimeVerification.Begin(true, true) 跑同 Editor 多 Runner；Begin(false, true) 跑單人。武器驗證經各自 Runner.OnInput 送出 NetInput，由 Host 正式武器 Resolver 處理傷害；捕捉使用真實藤蔓命中與 KCC 拉入，不直接強設 IsCaptured。

隔離地板與測試角色使用正式 Prefab。為保留操作時間，反擊情境只把 Runtime Clone 的吞食時間改成 300 秒；末段驗證將吞食壓縮成 1 秒、每 0.2 秒一次。未放寬武器距離、扇形、命中遮罩或新增命中 Collider；瞄準／位置是測試 fixture。多人輸入來源、命中、換彈、原地解救、瞄偏揮空、向下命中、禁止衝刺、晚加入／重連與權限都單獨檢查。

同 Editor 多 Runner 不等於獨立程序、多機延遲或自然多人驗收；未重跑 Build、效能、所有舊藤蔓 Play Mode 情境或正式動畫驗收。歷史 [FlytrapSolo](../FlytrapSolo/README.md)／[FlytrapStrike](../FlytrapStrike/README.md) 保留，不改寫舊測試結果。