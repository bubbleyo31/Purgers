# 受傷來源指示 V1 驗證紀錄

日期：2026-10-10。工作區：M:/UnityProject/Purgers。正式規格：[49](../../49_玩家受傷來源指示.md)。

## 聚焦與回歸

- 先加入反射式四項行為測試，確認規則尚不存在而失敗（job `712ba1f5ddb9453f817bbf24092e45ac`，4 項失敗）。更早一次未刷新新檔的 total=0 不算執行或通過。
- 最終聚焦 **23/23 通過**，job `0fcaf09cd34544d0b5d012025bb934e7`；見 [原始 MCP 結果](focused.json)。包括前後／高低方向、fade 到期、移動牆入出、隱藏恢復、相機在牆內、來源／接收者排除、獨立 PhysicsScene、無標記／畫面外零查詢、每幀預算與輪流排程、配置即時修改、Sprite 替換、NetworkObject 移動不追蹤、同來源新擊才更新、明確來源座標零值合法、容量縮減。
- 完整 PurgersRegression 最終 job `710b07c0dade4393ab41f4003272dae7`：MCP 探索 661、完成 646；`status=failed`、`result=null`，列出 1 項失敗 `BossStageAssetTests.GameSceneContainsBossAssetWiring`（MapChunk_Boss 引用比較），與先前紀錄相符，本次未改 Boss 資產。不能宣稱完整回歸通過或將探索數當執行數。見 [原始 MCP 結果](regression.json)。

## 編譯與資產

- 最終 `EditorUtility.scriptCompilationFailed=false`；Console 無 error，仍有既有 BattleHudReferenceLayout 的 TextureImporter.spritesheet 過時警告及 MCP WebSocket 重連警告；未見本次檔案警告。
- 過程曾修正驗證工具直接引用 Editor assembly 的編譯錯誤；最終工具與正式程式已編譯成功。
- 新設定資產、新 DamageIndicators Prefab 及 _Menu 子物件已經 Editor 寫入，不手改 YAML。Prefab／Scene 新物件 Missing Scripts=0；Scene 內 LocalPlayerDamageIndicator=1。
- 最終序列化核對：FadeThroughWalls、牆後不透明度 .25、interval .05、capacity 4、budget 2、兩層 duration 1 秒；Canvas 啟用。已退出 Play Mode，_Menu 無未儲存修改。
- 工作區原有大量未提交變更；本輪未回退／提交其他工作。ActiveAbilityPhysics 的既有射出起點修改保留，只在本輪補爆炸來源快照。

## Play Mode 與多人

- Single Runner：9 個明確 Check 通過，另有可見→牆後→隱藏→恢復及 HUD 綁定等待條件通過。真實 PlayerHealth／DamageReceiverUtility／RPC，不以直接 UI 呼叫取代網路驗證。見 [single.txt](single.txt)。
- 同 Editor、兩個 Runner、真實 Photon Host／Client：16 個明確 Check 通過。受傷者 Input Authority 收到事件，其他玩家不收到；Client ReceiveDamage 拒絕；晚加入／重連不補播；Client 所屬 PhysicsScene 遮擋生效；Despawn／重生清理重新綁定。見 [host-client.txt](host-client.txt)。
- 完全阻擋採驗證用 IDamageRequestModifier，經正式 DamageReceiverUtility 的 FullyBlocked 路徑，確認回饋一次且 HP 不變；這不等於自然技能護盾實戰驗收。
- 兩次小場景牆移入反應測量為 **47.5 ms / 52.6 ms**，使用 20 ms 輪詢，包含幀排程與測量粒度；不是硬即時保證。
- 預算測試確認每幀最多 2 組／4 次物理查詢，沒有標記／畫面外／3D 關閉時為零。使用固定 16 格緩衝，未執行大型 Encounter 的 Profiler／GC 壓力測試。
- 尚未做獨立程序 Host／Client、打包、自然多人實戰、最終美術接受確認；受控兩 Runner 不能取代這些證據。

## 實際 UI 圖形截圖

以下為 Play Mode 中使用正式 Prefab／設定副本／正式繪圖器的控制場景，經測試入口固定時間取樣；場景物件為膠囊／牆，並非最終關卡美術，也不是自然戰鬥截圖。測試只改 Runtime Clone，退出 Play 後恢復原場景。

- [來源可見：0.9](visible.png)
- [遮擋淡色：0.25](occluded.png)
- [遮擋隱藏：0，2D 保留](hidden.png)

## 重跑入口

1. EditMode：DamageIndicatorTests、DamageIndicatorBehaviorTests，再跑 PurgersRegression。
2. _Menu 進入 Play Mode，明確呼叫 `DamageIndicatorRuntimeVerification.Begin(false)` 或 `Begin(true)`；讀 Status／Stage，報告寫至 Temp/DamageIndicator。不得在已有正式 Session 時啟動。
3. 測試結束退出 Play Mode；正式 Inspector 步驟見 [49](../../49_玩家受傷來源指示.md)。

補充：實際 _Menu 的巢狀 Canvas 使用非中心 Pivot，已以中心錨點換算補償；加入 0／0.5／1 Pivot 測試（修正前兩項失敗，修正後通過）。新 Prefab／Scene 實例縮放已設為 (1,1,1)，加入資產測試。Play Mode 使用實際已配置 HUD 投影核對，倒三角尖端與來源投影誤差 0 px，見 [實際 HUD 截圖](authored-hud.png)。原 9／16 網路檢查在這項 UI 座標補正前通過；補正後重跑 UI 聚焦與完整回歸，未將其描述成重新執行網路測試。

程式與文件的 scoped git diff --check 通過；_Menu 整份相對 HEAD 的 diff 仍含本輪之前 LocalPlayerEnemyCrosshair 序列化區塊的兩個空字串尾端空白，未手改 YAML 或清理其他工作。
