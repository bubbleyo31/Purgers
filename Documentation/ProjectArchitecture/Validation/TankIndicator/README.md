# 電鋸本機距離圈與目標框驗證

日期：2026-10-18。Unity 2022.3.62f1；背景不聚焦；所有 Runtime 探測只使用暫時 Runner、相機與 Clone，沒有保存 Scene／Prefab。

## 實作與設定

正式程式已接入 Assets。由 TankMeleeCombo.Render 自動建立本機提示，不需場景掛線。TankProfessionRuntime Prefab → Tank Melee Combo → 本機預備距離圈與目標框可調視覺。完整規則與初值見 [48](../../48_Tank預備衝刺與共用敵人準心.md)。

原 TankMeleeCombo.cs、TankMeleeCombo.PreparedDash.cs、PlayerEnemyAimQuery.cs、LocalPlayerEnemyCrosshair.cs 與接入前 SHA256 一致。沒有改動權威攻擊／衝刺、紅色敵人辨識、Networked 或 RPC。

## 聚焦 EditMode

- Test-only baseline：25 個新增案例實際失敗，原因為尚未加入目標型別／介面（job 878fb99535d64f1c87c30586c59e643c）。
- 首次全四類測試 46 項中 1 項投影邊界驗證失敗：相機像素寬為 800.000061。修正測試以 camera.pixelRect 與浮點容許誤差驗證，未放寬產品邊界。
- 最終四類 46/46 通過，duration 1.7466334s，job 567160da94d043c687033420c46cd555。涵蓋 TankPreparedDashIndicatorTests 及原 TankPreparedDashTests、TankDashTimingTests、TankPreparationTests。
- 先前 total=0 的呼叫不計入通過。

## 完整回歸

首次 job dc26bcc99d3d4014b70be78fbe3e15bb 經 Domain Reload 後只留下 completed=34 的過期進度，Editor 日誌已 RunFinished，但沒有結果內容；不算通過。新增明確啟用一次的 TankIndicatorTestResults 結果存檔後重跑，於 2026-10-18T06:30:38Z 取得 RunFinished：[regression-summary.txt](regression-summary.txt)、[regression.xml](regression.xml)。總計 1179 項，1173 通過、4 失敗、2 跳過；本功能 25 項全通過。此結果不是全綠。

四項既有失敗（均與 [前一輪 AbilitySlotStates](../AbilitySlotStates/README.md) 相同）：

- BossStageAssetTests.GameSceneContainsBossAssetWiring：Boss Chunk 接線物件不同。
- EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules：期待 0.5，資產為 0.25。
- EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners：期待 >10，資產為 10。
- ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt：既有 Field reflection NullReferenceException。

## Runtime 與驗證邊界

最終 Single [26 個檢查點通過](single.txt)，Host/Client [34 個檢查點通過](host-client.txt)（包含 Runner／狀態等待）。受控 Multi Peer 使用 asia 私有 Session，實際 Host 權威／Client Input Authority；晚加入觀察者不建立他人的提示。已驗停格單一框、無文字、預覽不攻擊／扣血、選中的大型受擊盒、距離限制與原紅準心獨立、牆遮擋／恢復、放開收起、真實 Jump 輸入離地後圈完全淡出、落地恢復、換武器後本機元件銷毀。

首次離地探測直接 SetPosition 未造成可觀測的 KCC 離地而逾時，保留 [失敗紀錄](airborne-harness-initial-failure.txt)；改用原 NetInput Jump 重跑通過，未改產品地面／移動規則。

[單機離屏圖](single.png)、[Client 離屏圖](host-client.png) 已檢視橘色無文字四角線與地面圈。捕捉時暫時把同一 Canvas 切成相機模式，讀取自然渲染的 RenderTexture 後還原；沒有焦點操作、Scene View 截圖或正式資產修改。受控 Collider 不是完整美術輪廓，圖片不代表自然遊玩視覺驗收。

測試使用真實 KCC_Player／Tank Runtime／Enemy_Melee_A，但為隔離幾何規則替敵人 Clone 建立受控子 Collider；這不代表每個正式敵人資產均完成自然遊玩視覺驗收。本機 PhysX 提示與 Host Lag Compensation 使用不同時間快照，網路延遲或目標移動後仍以放開時原權威結果為準。

本次變更無新增編譯錯誤／警告；先前既有 DemoController、TankPreparationRuntimeVerification、LocalHealthSegmentView、BattleHudReferenceLayout 編譯警告未動。最終 Console 無錯誤，兩則 Enemy Patrol 無指定 Patrol Area 警告來自隔離敵人 Clone。

收尾：Runtime 驗證 Status=Passed、Runner=0、已退出 Play Mode；Assets/Scenes/_Menu.unity isDirty=false。新檔 .meta 均由 Unity 產生；沒有保存 Scene／Prefab。

未涵蓋：Standalone Build、兩個獨立程序、自然斷線重連、自然遊玩及每個正式大型敵人資產視覺驗收。死亡／控制封鎖有資格規則測試，本輪未另做自然死亡／觀戰輸入操作。