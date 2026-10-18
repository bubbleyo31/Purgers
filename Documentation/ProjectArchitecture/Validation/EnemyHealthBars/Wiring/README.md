# 敵人血條自動接線驗證 — 2026-10-14

使用者「自動接線」授權之後，_Menu/GameplayHUD Canvas/PlayerHUDCanvas 下保存唯一 EnemyHealthBarDisplay；9 個一般敵人已新增 Target、根節點錨點、身體盒並 Bake NetworkObject，Boss／共用基底雜湊未變。

- RED：EnemyHealthBarWiringTests 實際 11 項，10 項因未接線失敗、Boss 排除 1 項通過（job 4a5cb629cd444df68053944390200531）。
- GREEN：EnemyHealthBarTests、VisibilityTests、WiringTests 共 42/42（job 1e58973ea6584255a14f956fdb965dea）。包含有限尺寸檢查，已排除離線 BakeMesh 的骨架比例錯誤。
- 完整回歸：[regression-tests.txt](regression-tests.txt)，863 passed／1 failed／1 skipped。既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗，AbilityToggleInputTests 網路測試 Explicit。MCP 工作因 Domain Reload 狀態停留，結果以實際 TestRunner 跨 Reload 收集器為準，確認 TestRunner 停止後清除過期工作。
- 正式資產隔離 Play Mode：[prefab-runtime.txt](prefab-runtime.txt)，9 個實際 Prefab 經 Runner.Spawn，讀同步生命並顯示、State Authority 傷害後 76%、Despawn 清理，共 27 項通過。為固定觀察，測試副本停用巡邏、感知與追擊決策，沒有保存測試改動；不是自然戰鬥驗收。
- 接線數值：[wiring.txt](wiring.txt)。樹怪與水母 Large，反射怪／捕蠅草／自爆怪 Normal。
- 已查看 [樹怪](wired-Enemy_Melee_A.png)、[水母](wired-Enemy_Ranged_A.png)、[自爆怪](wired-Enemy_Suicide.png) 的 Unity 截圖。
- 無新增編譯錯誤。隔離 Play Mode 出現 6 筆既有 Enemy Patrol 缺少 Patrol Area 警告，不是血條錯誤，也不宣稱 Console 全零。
- Boss 與共用基底對本輪 before 快照雜湊一致；接線工具重跑後 12 份受查 Scene／Prefab 雜湊不變。返回 _Menu Edit Mode，場景乾淨且僅一份 HUD。

本輪未重跑正式敵人資產的 Host／Client；先前同 Editor 25 項多人檢查仍是盒形測試對象。獨立程序多人、正式關卡自然操作、動畫中定位、Build 與效能仍待驗收。