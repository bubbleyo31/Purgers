# Tank 連點輸入鎖驗證

日期：2026-10-10。正式規則與設定：[47](../../47_Tank預備攻擊與動畫幀時序.md)。

- 修正範圍：整段攻擊（含完整 Recovery）完成前不預存下一刀。只在 Idle 接受新的左鍵按下；攻擊中按住跨過完成時點也不自動出手。
- Red Play Mode：修改正式 Gameplay 前，使用原有 TankPreparationRuntimeVerification、真實 NetInput／Fusion Single Runner 重現快速連點排入第二次 AttackSequence，[red.txt](red.txt) 於 lock complete 的序號斷言如預期失敗。測試分身刻意將舊 allowAttackInputBuffer 設為 true，涵蓋舊資產設定。
- 聚焦 EditMode：38/38 通過，job 35f79f33e9214f4fab8053cfa777a979；原本幀時序、預備、衝刺與動畫資產檢查維持通過。
- 本次輸入鎖的直接行為證據來自上述 Runtime 探測；既有 EditMode 中 Buffered 命名測試只覆蓋純進度資料的初始秒數相容性，不代表正式 Tank 仍接受緩衝。
- 程式移除私有 Networked 緩衝狀態與排隊消費入口。allowAttackInputBuffer 欄位保留舊序列化且隱藏，不需改 Scene／Prefab。
- 重跑入口：空閒 Play Mode 執行 TankPreparationRuntimeVerification.Begin(false/true)，報告輸出至 Temp/TankInputLock。測試只改可拋棄 Runtime Clone。
- 不宣稱獨立執行檔／WAN 延遲或使用者最終手感已驗收。
- 全 PurgersRegression：job 59afca5d5ff24c5eb933d2e4a16a4a51，completed=646／discovered=661，result=null；只列出既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用不一致失敗。不宣稱全套通過，也不把 discovered total 當成完成數。
- 修正後 Single Runner：27 項 PASS，[single.txt](single.txt)。確認快速連點不增加第二次 AttackSequence、收招中按住跨過結束不啟動下一刀、完成後新 Press 可接正確段數並預備；同時覆蓋 F、死亡、換武器與原本動畫停格／震動行為。
- 唯讀確認正式 TankProfessionRuntime Prefab 三段 UseAnimationFrames 皆 true，舊 allowAttackInputBuffer=true；本次未改資產值，程式固定忽略此舊開關。
- 第一輪 Host／Client 尚未進入 Client 連點案例，即於既有「await Task.Delay(100) 後仍應在前搖」斷言失敗，[原始報告](host-client-attempt1.txt)。此斷言使用牆鐘等待，未記錄模擬時間，不能據此定位 Gameplay 退化。測試已改成在真實 OnInput 回呼送出單 Tick 點按並逐 Tick 記錄放開前搖／是否提前出傷；沒有為此改動正式 Gameplay。後續成功紀錄另列，不把失敗嘗試計為通過。
- 第二輪同 Editor Host／Client：39 項 PASS，[host-client.txt](host-client.txt)。逐 Tick 點按前搖檢查通過；Host 與真正 Client NetInput 分別確認連點不排隊、跨完成持續按住不觸發、新按鍵仍接正確段數，Client 最終 AttackSequence 與 Host 收斂。另含預備中晚加入、完成後重連、F、死亡與換裝。
- 實作檔案相對本輪備份的 diff --check 通過；正式文件與索引相對連結檢查通過。既有受傷指示等其他工作未重設或覆寫。
- 最終 Editor：isCompiling=false、scriptCompilationFailed=false、isPlaying=false、_Menu dirty=false；Console error 查詢 0 項。
