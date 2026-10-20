# 撤離點避開出生入口與地圖接縫

最後核對日期：2026-10-21

規則：[150 最後 Chunk 撤離，第 6 節](../../150_Phase4-C_最後Chunk撤離.md)。

## 實作範圍

- 單 Chunk 排除本輪玩家出生入口方向及實際出生位置所在撤離範圍。
- 多 Chunk 只允許最後 Chunk，排除入口方向與 OpenSides 接縫。
- 方向由所屬 Chunk 的四個 Connector 與候選水平距離辨識；平移／Y 旋轉不改局部 Side。缺少、重複或等距方向不參與抽選。
- Host 篩選後沿原 Run Seed 選出完整候選清單的索引；Client、HUD、小地圖讀同一結果。沒有合法候選時保持 Initializing。
- 本輪未修改 Scene／Prefab、RPC／Networked 欄位、NavMesh 生成、撤離倒數或結果提交。

## 自動化證據

隔離基準：`123817f`，工作樹 `M:\CodexWorktrees\extraction-placement\Purgers`。
Unity `2022.3.62f1`，獨立 batchmode／nographics EditMode；未使用主 Editor 執行這些隔離測試。

| 階段 | 結果 |
|---|---|
| 先紅燈 | Unity 編譯確認缺少新增規則／方向辨識 API；不是測試通過證據。 |
| 初次聚焦 | 32 通過、3 失敗；失敗在直接讀取 Prefab 資產的父階層，修正為建立實例後檢查。 |
| 最終聚焦 | `StageRulesTests` 35／35 通過、0 略過；含本次新增 16 項案例。 |
| 完整回歸 | 1247 項：1235 通過、10 失敗、2 略過，不能稱為全綠。 |
| 原基準回歸 | 暫時撤下本次 patch，在相同隔離工作樹與環境重跑：1231 項，1219 通過、10 失敗、2 略過；失敗案例集合完全一致。之後已恢復 patch。 |
| 主專案整合 | 已獲使用者授權並套用精準 patch；四份程式檔 SHA256 與已測隔離成果一致。主 Editor 完成重新編譯／Domain Reload，Console Error 0；聚焦 35／35 通過、0 略過，job `367b3fad2fe348ac9421d9400950aa23`，RunFinished 非零，Editor 未聚焦。 |

聚焦包含單／雙／三 Chunk、出生 Chunk／非法末端拒絕、接縫遮罩、無效方向、四種 Y 旋轉及等距歧義。
實際地圖 Prefab 實例的四個入口方向各保留三個合法候選，對單／雙／三 Chunk 各以 128 個 Seed 抽選，共 1536 組選點檢查。
這些是 EditMode 規則與資產實例證據，未假稱為真實多地圖連線或完整地面路徑實測。

修改前後完整回歸的相同失敗案例：

1. `AbilitySlotPresentationTests.ExistingPrefabTemplateCreatesWholeSlotOverlayWithoutChangingSourceArtOrKeyLayout`
2. `AbilityToggleInputTests.BulletTimeSecondPressUsesNormalFinishAndResolvesDamageExactlyOnce`
3. `BossStageAssetTests.GameSceneContainsBossAssetWiring`
4. `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`
5. `EnemyHealthBarWiringTests.SavedMenuHasExactlyOneConfiguredScreenSpaceHud`
6. `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`
7. `HudLayoutTests.LeftColumnReservesMapBeforeStatusAndReady("SafeHouse","SafeHouseHUD","StageLevelPanel")`
8. `HudLayoutTests.SpeedVisualAnchorsToCanvasAndHidesBonusWithGauge`
9. `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`
10. `TankPreparedDashIndicatorTests.ProjectionFitsObjectAndClipsAgainstCameraViewport`

其中三項回報 Noto SDF 字型資產載入錯誤；隔離基準與無圖形環境的結果不得直接宣稱為目前主專案自然遊玩結果。

原始 log、XML 與 patch 保留於 `M:\CodexData\extraction-placement`；[聚焦 XML](focused-final.xml)、[修改後回歸 XML](regression.xml)、[原基準回歸 XML](baseline.xml)、[主 Editor 聚焦結果](main-focused-summary.json) 隨本紀錄保存。
隔離 Library 位於 `M:\CodexUnityCaches\extraction-placement`，工作樹的 Library 為 Junction。
本輪不自動封存／刪除工作樹或快取，保留供重跑與審閱。

## 待驗

- 主專案新開普通 Level 1／2／3，自然生成後確認出生方向、最後 Chunk、接縫方向及合法可達候選。
- 獨立 Host／Client、晚加入／重連：核對相同 `SelectedExtractionIndex`、撤離視覺與小地圖標記，Client 不重做選點。
- 自然走入撤離區、全員撤離、結果提交與回安全屋；本輪未執行 Windows Build。
- 本次聚焦與隔離回歸不等於上述 Runtime／網路／自然遊玩驗收。
