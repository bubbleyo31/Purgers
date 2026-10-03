# Assets/Scripts/Editor/Tests 程式導覽

Phase 4 前置-1 新增 `PlayerControlLocksTests`：同名票證不互相覆蓋、重複釋放、獨立移動／鏡頭遮罩、全輸入清除與 tick 遮罩重用。多人載入屏障另由 `Tool/Testing/Phase41NetworkVerification.cs` 在獨立 Host／Client 驗證，不能以 EditMode 取代。

Phase 4-D 新增 `NetworkMapTopologyRulesTests`：驗證多 Chunk 父連線、入口／出口、封口遮罩、唯一最後 Chunk 與撤離索引範圍。Runtime 晚加入與權威隔離另由 `Tool/Testing/Phase4DNetworkVerification.cs` 在兩個獨立 Windows Player 驗證。

Phase 4-E 擴充 `StageRulesTests`、`GameSaveRepositoryTests` 與 `HudLayoutTests`：驗證可調 CycleLength Snapshot、Level 1／2／3／5、Boss 不進普通拓撲及 HUD 格式。`HudLayoutTests` 也鎖定 StageHUD 必須是 Game Scene 單一 root、MapChunk 不得攜帶畫面 UI，以及同 Runner 新 StageLevel 會取代舊 HUD。成功提交與 SafeHouse 往返另由 `Tool/Testing/Phase4ENetworkVerification.cs` 以獨立 Host／Client 驗證。

遊戲程式的總入口；從下列主要子系統開始找。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [FirstPersonMuzzleFlashTests.cs](./FirstPersonMuzzleFlashTests.cs) | FirstPersonMuzzleFlashTests | 射擊去重、重播容量、清理、鏡頭跟隨／畫面尺寸、素材與未 Spawn 防護。 |
| [CombatRegressionTests.cs](./CombatRegressionTests.cs) | CombatRegressionTests | `CombatRegressionTests` 的主要實作入口。 |
| [HudLayoutTests.cs](./HudLayoutTests.cs) | HudLayoutTests | Phase 4 前置-3 數字倒數、Ready 顯示 Phase 與場景 HUD 三列配置。 |
| [GameSaveRepositoryTests.cs](./GameSaveRepositoryTests.cs) | GameSaveRepositoryTests | `GameSaveRepositoryTests` 的主要實作入口。 |
| [MenuSaveFlowTests.cs](./MenuSaveFlowTests.cs) | MenuSaveFlowTests | `MenuSaveFlowTests` 的主要實作入口。 |
| [MapTopologyPlannerTests.cs](./MapTopologyPlannerTests.cs) | MapTopologyPlannerTests | Phase 4-A Seed、連接拓撲、回頭限制、矩形重疊、有限重抽與最後 Chunk 身分。 |
| [NetworkMapTopologyRulesTests.cs](./NetworkMapTopologyRulesTests.cs) | NetworkMapTopologyRulesTests | Phase 4-D 網路拓撲完整性、封口遮罩、唯一尾端與撤離索引。 |
| [SafeHouseReadyRulesTests.cs](./SafeHouseReadyRulesTests.cs) | SafeHouseReadyRulesTests | `SafeHouseReadyRulesTests` 的主要實作入口。 |
| [StageRulesTests.cs](./StageRulesTests.cs) | StageRulesTests | `StageRulesTests` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


## 命中特效測試（2026-09-25）

[WeaponImpactEffectsTests.cs](WeaponImpactEffectsTests.cs) 覆蓋獨立 Layer、每玩家 FIFO、表面跟隨／停用清理、火花容量／完成／超時、核准素材及未 Spawn 防護。多人 RPC 實戰需另做 Host／Client 驗收。
