# EnemySpawnZone 混合生成驗證

日期：2026-10-14；Unity 2022.3.62f1；本輪沒有保存正式 Scene／Prefab，也沒有自動調整既有生成上限。

## 已確認需求

同一 Zone 混放 Ground／FreeFlying／Stationary。每項獨立出現機率，抽中後生成使用者指定數量；同次可能多項都中，也可能全不中。巡邏依實際敵人種類自動分配。原有預算／安全檢查／啟動期限繼續生效。

## 測試證據

- 先加入 `ZoneExposesIndependentChanceAndCountWithoutDiscardingLegacyPrefabs`，確認因缺少 `enemyEntries` 預期失敗（MCP job `e7f6cce05b4f4136a6ee6cbf9327d8d6`）。第一次探索 total=0 不算通過。
- 修正後聚焦 50／50 通過（job `14ffa62cd5244a44b6e0a1ede3f0fe07`）：13 個混合生成案例，加上既有 Encounter、飛行規劃、遮擋與定點案例。測試組件實際已載入新版才記錄此結果；先前只跑到 38 個舊案例的結果不作為完整證據。
- 完整 PurgersRegression：891 通過、1 失敗、1 Explicit 略過，共 893 個葉節點案例。既有失敗為 `BossStageAssetTests.GameSceneContainsBossAssetWiring`；略過為需獨立啟動 Photon 的 AbilityToggle Host/Client 案例。[原始完成回呼](regression.txt)。MCP job `c349baa84c964fea93dbdaad25637a87` 卡在舊進度，確認 TestRunnerApi 已無執行中測試後清除殘留 job；採 Unity RunFinished 回呼結果，不用 succeeded／探索總數冒充結果。
- Single Runner：19 項通過，實際同 Zone 生成 Ground 2、FreeFlying 2、Stationary 1，檢查自動 Ground／Flying Area、出生前綁定、數量快照完成與成功區 FIFO。[原始紀錄](single.txt)。

## 受控條件與邊界

`Editor/EnemySpawnCompositionVerification.cs` 在沒有現存 Runner 的 Play Mode 執行 `Begin(false)`（Single）或 `Begin(true)`（同 Editor Host/Client）。建立臨時 Runner 與 MapChunk_Prototype 複本，載入既有預烘焙導航；透過反射驅動 Director 候選建置及待生成流程，沒有模擬自然 Gate 跨越。測試將上限提高、時間放寬、生成遮擋關閉，讓合法出生與多類數量可受控驗證；這些值只作用於臨時物件。

不代表正式 Scene／Prefab 已遷移，不代表獨立程序 Host/Client、自然輸入、最終配置、Build 或效能驗收。正式地圖仍需按 [164 設定手冊](../../164_Phase10_怪物生成區與Encounter守則.md) 手動填入清單與驗收。定點敵人仍共用人工出生中心，占用可能使後續同中心生成取消。

## 同 Editor Host／Client

24 項受控檢查通過：[原始紀錄](host-client.txt)。Host 產生相同的 2 Ground＋2 FreeFlying＋1 Stationary；Client 晚加入與重連皆收到全部五隻，敵人副本沒有 State Authority，Client 場景沒有自動巡邏區。這是同 Editor 隔離 Runner 證據，不是獨立程序多人驗收；未以 Client 自然輸入跨 Gate。

## 最終來源重驗

- 補上 Ground 就緒預檢，避免混合區在 Ground 導航未就緒時每 Tick 重建飛行 Area；0% Ground 項目不會阻擋 Flying／Stationary。
- 最終聚焦仍為 50／50（job `f3e48fe7eee64319886271292e036619`）；最終完整回歸仍為 891 通過、1 項既有 Boss 引用失敗、1 項 Explicit 略過（job `aaeac71ca4b6407d894b6614ab344baa`），見 [最終 RunFinished 原始紀錄](final-regression.txt)。
- 最終原始碼可成功編譯，`EditorUtility.scriptCompilationFailed=false`。既有編譯警告為 Cinematic Explosions `DemoController.camera`、`TankPreparationRuntimeVerification` 參考比較與 `BattleHudReferenceLayout` obsolete spritesheet；本次沒有新增對應警告。MCP 的一般 Console 查詢未包含曾出現的編譯器錯誤，因此也核對 Editor.log、編譯旗標與新版測試方法實際載入。
- 最終同 Editor Host／Client 重驗 26 項通過，另含「依實際混合 Prefab 判斷 Ground 等待」與「0% Ground 不阻擋其他種類」；仍實際生成 2 Ground＋2 FreeFlying＋1 Stationary，晚加入及重連收到完整組合。[最終多人原始紀錄](final-host-client.txt)。
- 測試後退出 Play Mode；正式場景未由本輪保存或配置。手動配置與自然 Gate 跨越驗收仍保留給使用者。

## Locomotion Kind Inspector 澄清

使用者確認「Locomotion Kind 自動指派」指的是按各 Prefab 自動辨識類型，不需手選。新增 `EnemySpawnZoneEditor`／`EnemySpawnEntryDrawer`，在每個混合項目顯示唯讀類型，並在混合清單啟用時隱藏舊版的手動類型／Prefab 清單／總數欄位。本次只改 Editor 顯示，Runtime 仍使用上一輪已驗證的 Definition 判定。

編譯成功（scriptCompilationFailed=false）；隔離 Preview Scene 實際核對 Fusion GUID 的 serialized fixed buffer 讀取、Inspector 綁定、展開欄位高度與三份 Prefab：Enemy_Melee_A → Ground、Enemy_Ranged_A → FreeFlying、Enemy_Ranged_C_Projectile → Stationary。未指定 Prefab 顯示「尚未指定 Prefab」。沒有修改正式 Scene／Prefab 或 EnemyDefinition；本次未重跑完整回歸與多人測試，前節結果屬上一輪 Runtime 改動。

## 2026-10-14「只有飛行敵人」配置診斷

本次重讀使用者已修改的 MapChunk_Prototype：8 個 Zone 均依序配置 Ranged A 飛行 1、Ranged B 飛行 1、Suicide 地面 5、Reflector 地面 1、Flytrap 定點 1，全部 100%，總需求 9。Game 的單區上限 3、全場 12、間隔 0.75 秒、期限 3 秒仍有效。因此不能把抽中 9 隻當成實際保證出現 9 隻；前面條目會先消耗預算與時間。

- 原始出生盒尺寸 50.8 × 2.07 × 49.1；隔離 Single Runner 複製實際 Prefab、載入原本烘焙導航，以固定種子每區取 2000 點。導航、貼地、盒內、巡邏路徑依原順序檢查，8 區各有 1250～1974 點通過。這一步沒有玩家／前方／占用篩選，不能視作自然生成成功率。[原始位置診斷](authored-zone-diagnostic.txt)。
- 全部 Gate 平面穿過定點出生中心。玩家正向或反向跨過後，固定中心不能滿足沿穿越方向至少領先 2 公尺。定點只取人工中心，不會像地面／飛行改抽盒內其他位置。要保留前方規則，人工配置時必須把出生中心放在預期穿越方向的前方；同一固定點不能保證雙向進入皆有效。
- 在 Zone (7) 保留實際敵人清單及尺寸，用受控玩家快照測試。剛過 Gate 0.1 公尺時，地面兩種各 100/100 次找到合法點；前進 20 公尺時降為 43/100，30 公尺時全部類型 0/100。定點在全部測點均 0/100。每次仍最多 16 個候選；此為受控位置診斷，不是使用者那次自然路線的追蹤。
- 相同 Zone 的實際 Runner.Spawn 驗證：玩家固定在剛過 Gate 的位置、維持原上限 3／期限 3 秒，生成 Ranged A 1 隻與 Suicide 2 隻；Ranged B 因候選位置不通過而跳過，後續 Suicide／Reflector／Flytrap 達單區上限取消。[原始 Gate 診斷](authored-gate-diagnostic.txt)。證明實際資產地面生成可用，不能據此聲稱已重現或修好使用者那次「只生成飛行」的完整路線。
- Runtime 只加診斷：候選快取 Prefab 名稱；成功／預算拒絕會記名稱與 Locomotion Kind；候選全失敗會輸出 NavMesh、貼地、盒外、巡邏路徑、最遠距離、前方、占用、玩家距離／遮擋的第一拒絕原因次數及玩家位置／穿越方向。保持原有短路檢查順序，不放寬任何出生條件，正式 Scene／Prefab 未保存。
- 新版編譯成功；聚焦測試 50/50 通過。[RunFinished](diagnostics-focused.txt)。尚需使用者原路線重測，以細分紀錄確認當次拒絕原因；此輪未重跑多人。
完整 PurgersRegression 重驗：891 通過、1 項既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗、1 項 Explicit 略過，見 [本輪 RunFinished](diagnostics-regression.txt)。MCP job 2f632998fad243e3aab9ab30ef74a18d 停在 34，原生 TestRunnerApi 已無工作且完成回呼落盤後才清除殘留；不以 job 進度冒充測試結果。測後 _Menu 非 Play、未變髒，編譯失敗旗標 false，Console error/warning 查詢 0。
## 2026-10-14 使用者修訂：所有敵人在 Box 內散布

使用者明確要求「怪物出生範圍在 Box 範圍內隨機散布，以防位置重疊不生成」，取代前述 Stationary 固定中心契約。EnemySpawnZone 移除 Stationary 中心例外，舊清單／混合清單都在局部 Box XYZ 隨機取點，再轉到 Chunk 世界座標；定點只表示出生後不自主位移。Gizmo 一律顯示完整出生盒，Inspector 提示同步修正。Stationary 不自動貼地，人工需配置盒體高度及可用範圍。

沿用 Director 已有的占用後重新取點流程，最多 Candidate Attempts 次（預設 16）；Ground 仍需 NavMesh／支撐面／巡邏路徑，Flying 仍需可達巡邏路徑，所有種類仍受前方、玩家距離、遮擋、預算與期限限制。沒有關閉占用檢查，也沒有更改正式 Scene／Prefab 的盒子、預算或遮罩。

- 先將混合區測試改成 Stationary 也需散布，確認因回傳中心而預期失敗：[修改前紀錄](scatter-before.txt)。後續同步舊版 Stationary 測試，驗證 64 次取樣均位於旋轉／非等比縮放後的 Box 內且位置分散。
- 修改後聚焦 50/50 通過：[完成回呼](scatter-focused.txt)。
- 從使用者當時開啟的 Game Scene 跑完整回歸：890 通過、2 失敗、1 略過。除既有 Boss 引用失敗外，AbilityToggleInputTests 因命中回饋缺 FirstPersonHitCameraShake.Singleton 而失敗；該回饋系統未修改。[Game 場景紀錄](scatter-regression.txt)。
- 在原有 _Menu 測試環境重驗：891 通過、1 項既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗、1 項 Explicit 略過。[Menu 完成回呼](scatter-regression-menu.txt)。MCP 的進度卡在 34，兩次都取得原生 RunFinished 並確認 TestRunnerApi 無工作後才清除殘留 job。
- 驗證工具增加 stationaryCount 參數，以及「首個隨機點放入實體阻擋物，再走正式 TrySelectSpawnPosition」測試。首次捕捉到工具誤用不存在的 Enemy Layer，故未通過首點占用斷言；修正工具使用現有 Player／Hit Mask。正式 Game 的遮罩 576 原本已包含這兩層，未改資產。[夾具失敗證據](scatter-harness-mask-failure.txt)。前輪使用此工具取得的數量／巡邏／網路證據不能當作敵人占用防重疊驗證。
- 修正遮罩後，同 Editor Host／Client **33 項受控檢查通過**：[原始紀錄](scatter-host-client.txt)。實際生成 Ground 2、FreeFlying 2、Stationary 3；首個 Stationary 候選點被實體球占用時，正式選點改抽另一個仍在 Box 內且距阻擋中心超過 2.75 公尺的點。3 隻 Stationary 的 Root 都在 Box 內、位置各異；Client 晚加入與重連後三隻位置均與 Host 相差小於 0.05 公尺，沒有 State Authority，Client 不建立巡邏區。
- 上述工具仍使用臨時放寬的生成預算與期限，目的是隔離散布／占用／同步規則；不代表正式 Game 單區 3 隻上限可以容納清單要求的 9 隻。未驗收自然 Gate 穿越、獨立程序多人、Build 或最終模型落地觀感。
- 最終編譯成功；驗證後退出 Play Mode，還原使用者原先開啟的 Game Scene，未保存正式 Scene／Prefab。本輪修改正式模組文件的現行規則，舊節固定中心說明屬修訂前診斷，以上述散布規則取代；關鍵詞索引未新增條目。