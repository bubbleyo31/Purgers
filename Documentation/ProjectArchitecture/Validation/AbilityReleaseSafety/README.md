# 技能前搖射出與榴彈玩家碰撞驗證

最後核對日期：2026-10-07。
核對來源：Unity MCP 官方 Test Runner、Editor Console、ActiveAbilityReleaseVerification 的真實 Fusion 物件。
相關文件：[設定與手動驗收](../../42_E主動技能規格與強化預留.md)。

| 層級 | 結果與證據 |
|---|---|
| 修改前完整 PurgersRegression | [baseline.json](baseline.json)：實際 completed=468；唯一回報失敗為 BossStageAssetTests.GameSceneContainsBossAssetWiring。 |
| 新聚焦測試修改前 | [red.json](red.json)：8 項實際執行、8 項預期失敗，尚無設定與一致的射出起點過濾。 |
| 修改後聚焦 EditMode | [focused.json](focused.json)：48/48，包含 ActiveAbilityPlayerCollisionTests、ActiveAbilityShotOriginTests、ActiveAbilityPhysicsTests、ActiveAbilityGizmoTests。 |
| 修改後完整 PurgersRegression | [regression.json](regression.json)：completed=477，仍為同一項既有 Boss 資產失敗，無新增回報失敗；不是全綠。工具完整回歸 result=null，total=492 是發現數，不能把它當已執行數。 |
| Play Mode 受控 Single Runner | [SingleRunner.txt](SingleRunner.txt)：24/24。三技能前搖中移動／轉向、玩家飛行排除、出生重疊、世界接觸、原自傷／友傷、倍率歸零與恢復玩家碰撞。 |
| 編譯與 Console | 最後 Editor isCompiling=false、scriptCompilationFailed=false；Console error／warning 0。 |
| 清理 | 測試 Runner 已關閉（運行數 0），已退出 Play Mode，_Menu.isDirty=false；未保存任何 Scene／Prefab。 |

第一次聚焦查詢曾回傳 total=0，不列為通過；強制匯入及確認測試程序集後重新執行。另一工作未初始化逾時也不列為測試結果。首次 Single Runner 探針在首個 Tick 前 Spawn 導致 Fusion RuntimeConfig 未就緒；測試啟動已改為等待首個 Tick，最後完整重跑通過。

## 驗證方法與界線

開啟 _Menu 的 Play Mode，確認沒有其他運行中 Runner，再透過 Unity MCP execute_code 明確呼叫 `ActiveAbilityReleaseVerification.Begin()`。輪詢 Status，完整紀錄寫入 `Temp/ActiveAbilityReleaseVerification.txt`。工具建立及清理自己的 Runner／玩家／牆，沒有自動啟動或持久化配置；結束後退出 Play Mode。

Release 探針使用真實 Spawned Runtime 的 SimulateAbility 與 Fusion 前搖計時，暫設實例前搖 0.3 秒，飛行速度 0.1 m/s、重力 0 以便準確觀測出生位置；前搖中把 Fixed/Render KCC 位置移動 4 m 並轉向 90 度。它證明實際施放從當前位置／方向取樣，**不是自然鍵鼠移動或回滾測試**。碰撞探針對真實網路投射物同步呼叫正式 Sweep；不代表獨立 Client 封包／渲染證據。

State Authority 的正式結果經 Single Runner 驗證；獨立 Host／Client、Input Authority 自然操作、延遲／回滾、晚加入／重連、遠端呈現及 Windows Build 尚未執行。保留 [設定文件](../../42_E主動技能規格與強化預留.md) 的多人手動驗收步驟。

沒有修改既有 Boss 資產測試、場景接線、玩家 Prefab 或榴彈 Runtime 原本 0.1 秒前搖。新增三個配置欄位以程式初值提供預設，既有 GrenadeProjectile Prefab 的 Editor 讀取與實際 Spawn 都驗證為啟用玩家排除。
