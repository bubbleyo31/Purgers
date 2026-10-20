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

## 2026-10-21 近距離鎖定框修正

最後核對日期：2026-10-21（局部核對）。修正已整合至主專案，沒有保存或修改 Scene／Prefab。規則與設定見 [48](../../48_Tank預備衝刺與共用敵人準心.md#2026-10-21-近距離部位框修正局部核對)。

### 問題與修正

專案內 Fusion.Runtime.dll 的 CapsuleTopCenter／CapsuleBottomCenter 以 `Max(CapsuleExtents × 0.5, AbsCapsuleRadius) - AbsCapsuleRadius` 決定球端中心，證實 CapsuleExtents 為完整高度。原 UI 把完整高度加半徑再乘二，A／B 正式長觸鬚的局部框高約為 Collider 高度的 2.49～2.74 倍；近距離投影後再將四邊夾至相機 pixelRect，形成螢幕四角框。

現改為正確局部膠囊包絡，保留原部位 Offset 與矩陣；投影返回未夾到螢幕的真實框界，由 Runtime 自動生成的 RectMask2D 裁切相機 viewport 外框線。四角全在畫面外時沒有可見框線，不額外建立標記，也不改原 Host 衝刺／攻擊／傷害。

### 本輪證據

所有結果保存在 [TankLockFrameFix_20261021](../../../../deliverables/TankLockFrameFix_20261021/)；測試在已整合修正的 `M:\UnityProject\Purgers` Editor 執行，不把主專案 Editor 的結果冒充隔離工作樹驗證。

- Test-only 舊版：33 項中 25 通過、8 失敗；新增尺寸入口、原始投影邊界及 viewport mask 的案例皆實際失敗。[XML](../../../../deliverables/TankLockFrameFix_20261021/before.xml)／[摘要](../../../../deliverables/TankLockFrameFix_20261021/before-summary.txt)。job `134090517f5d4631910c05371ce3ec3c`。
- 修正後聚焦：60／60 通過，含提示 33、原衝刺 9、衝刺時序 9、預備 3、水母資產配置 3、水母姿勢 3。[XML](../../../../deliverables/TankLockFrameFix_20261021/focused.xml)／[摘要](../../../../deliverables/TankLockFrameFix_20261021/focused-summary.txt)。job `4855ace8107940f09ceac9c44fb86c66`。確認測試結果中的新案例確實執行，且整合後類型已載入。
- 完整 PurgersRegression：實際 1322 項，1316 通過、4 失敗、2 跳過，**不是全綠**。[XML](../../../../deliverables/TankLockFrameFix_20261021/regression.xml)／[摘要](../../../../deliverables/TankLockFrameFix_20261021/regression-summary.txt)。job `bcee7ca75af04cc981719f3a98d82d4d` 的 MCP 進度停在 34，採本輪 UTC `2026-10-20T16:49:56Z` RunFinished 寫出的 XML，不採過期 polling 數字。
- A／B 正式 Prefab 暫時 Clone：每隻 160 個動畫膠囊取樣符合實際 Collider 尺寸；每個取樣測 5 個相機到部位中心距離（0.4／2／4／8／12）、3 個俯仰（-50／0／50）與 3 個水平角（-70／0／70），各 7200 組投影。全部有限值，沒有框界被壓成相機 pixelRect。[探測程式](../../../../deliverables/TankLockFrameFix_20261021/VisualAudit.cs.txt)／[結果](../../../../deliverables/TankLockFrameFix_20261021/visual-audit.txt)。這是幾何取樣，不表示每組都具備正式選敵／衝刺資格。
- 正式 Graphic／RectMask2D 離屏渲染：正常框 1175 個顯色像素、部分出畫面 569、四角全在外 0；三者螢幕邊緣 20 px 範圍皆無顯色像素。[正常](../../../../deliverables/TankLockFrameFix_20261021/normal.png)／[部分出畫面](../../../../deliverables/TankLockFrameFix_20261021/partial-offscreen.png)／[超大框](../../../../deliverables/TankLockFrameFix_20261021/oversized.png) 均已目視檢查。首次探測相機未設定 preview scene，導致正常框也空白，屬探測配置失敗；指定 camera.scene 後三個控制案例通過。
- 同 Editor 受控 Host／Client：34 個檢查點通過，含 Host 權威／Client Input Authority、本機唯一框、預覽不扣血、牆遮擋、正常 Jump 輸入、放開收起、晚加入旁觀者無他人框，以及換武器銷毀。[紀錄](../../../../deliverables/TankLockFrameFix_20261021/host-client.txt)／[離屏圖](../../../../deliverables/TankLockFrameFix_20261021/host-client.png)。沿既有測試器，目標為 Enemy_Melee_A 的受控子 Collider；不冒充 A／B 自然多人實戰驗收。

### 完整回歸的四項失敗

- `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Boss Chunk 同名但資產引用不同。
- `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：期待 0.5，實際 0.25。
- `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：期待 >80，實際 75。
- `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：NullReferenceException。

這四項在本輪仍失敗，本次未修改相關程式或資產；不宣稱與所有歷史失敗訊息完全相同。兩項跳過為 AbilityToggleInputTests 的 Explicit 聯機測試，不算通過。

### 收尾與待驗

本次修改未產生新的編譯錯誤；編譯階段仍有既有 BattleHudReferenceLayout 的 TextureImporter.spritesheet obsolete 警告。受控多人測試另產生暫時 Enemy 無 Patrol Area、Attack Runtime 同 Tick Spawn／Despawn 警告；最後 Console 無 error。

背景測試前已查核既有 Game View 為 PlayUnfocused 且 Run In Background 開啟，沒有呼叫 Focus／Show 或桌面點擊。退出本輪 Play Mode，Runner 已清理；自然 A／B 靠近與切換部位、自然斷線重連、Standalone Build／獨立程序仍待驗，不能由本輪幾何／受控測試替代。

工作樹保留於 `M:\CodexWorktrees\tank-lock-frame-fix\Purgers`（Codex 相容路徑經 C: Junction 指向 M:）；本輪未建立額外 Unity Library，結果檔在主專案 M: deliverables。修正檔在工作樹與主專案一致；02 職責總表只在原檔精準更新本次三行，保留主專案其他未提交文件內容。未提交、未自動封存工作樹。
