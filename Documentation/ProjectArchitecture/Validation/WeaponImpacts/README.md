# 命中特效驗證紀錄

日期：2026-09-25；Unity 2022.3.62f1，Purgers Editor MCP。範圍為 [163 命中特效](../../163_槍械命中特效與彈孔.md)。

## 測試證據

- RED：`8b46e0902a2d49eb989672bb115c55e6`，1 項預期失敗，缺少 `WeaponImpactSettings`。
- 初版 GREEN：`4d6f0c830622494fb70998dc3db30f2b`，1／1 通過。
- 容量及資產測試：`fd7c3d1abead462e8c10195fc7adbbbf`，5／5 通過。
- 最終聚焦：`a37b777eaf31408589445d766c6d140e`，7／7 通過，0 failed、0 skipped。覆蓋獨立 LayerMask、每射手 FIFO 與降額、移動表面／停用清理、火花容量／完成／超時／Dispose、三款核准素材、無表面／無效座標、未 Spawn 防護。
- 中途 `65034f2a6115467cbdc428a3ae7adef5` 的粒子自然結束測試失敗。Editor 實查指出 `ParticleSystem.Simulate` 會重啟發射狀態；測試在模擬後恢復 `StopEmitting`（保留粒子），最終測試確認 0.1 秒粒子仍活著，模擬 2 秒後清除。這是 EditMode 模擬證據，不宣稱真實時間 Play Mode 已驗證。
- 完整 `PurgersRegression`：`8f8463f07c6240d58da34323a6170e97`，`progress.completed=227`，6 項失敗。失敗時工具 `result=null`；`progress.total=242` 是探索數量，不能當成執行數。

完整回歸失敗名稱：

1. `BossStageAssetTests.GameSceneContainsBossAssetWiring`
2. `DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel`
3. `FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies`
4. `PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward`
5. `PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards`
6. `PlayerExperienceRulesTests.RequirementDoublesFromTen`

上述六項與 130 文件中本次修改前的 Tracer 回歸紀錄相同。本次未重設相關資產或經驗值，未宣稱完整回歸通過。

## Editor／資產核對

- Unity 完成匯入、C# 與 Fusion RPC 編譯；Console 無 Error，未見本功能新增編譯警告。既有紀錄仍含 MCP WebSocket、測試 Player Required Components 及 BattleHudReferenceLayout.cs:255 的 CS0618。
- `Purgers/BulletHole` Shader 無編譯錯誤。三份材質／設定透過 Unity AssetDatabase 與 SerializedObject 建立、保存；無手改 Unity YAML。
- 設定只引用 Compact、Radial、Fractured；原 Oblique PNG 保留但不使用。
- Spark 指向使用者指定的 `Assets/Art/VFX/SparkOnWall_vfx/Spark_vfx.prefab`；來源無 NetworkObject，兩個一次 Burst 粒子系統。
- 只檢查既有 Enemy／Map Prefab Layer，沒有修改其 Layer、Scene 或玩家 Prefab。
- `git diff --check` 於本次涉及的已追蹤檔案通過；文件連結另作存在性檢查。

## 尚待驗收

KCC_Player 手動掛載與 Fusion Prefab 重新匯入、實機畫面、Build、獨立 Host／Client、延遲／晚加入／重連、換關清理與高射速效能尚待依 163 操作。不能以七項 EditMode 或 RPC 編譯替代多人端到端證據。
