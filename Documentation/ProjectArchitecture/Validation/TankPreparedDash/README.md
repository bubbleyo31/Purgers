# Tank 預備衝刺與敵人準心驗證

日期：2026-10-10。正式規則與設定：[48](../../48_Tank預備衝刺與共用敵人準心.md)。

- Red：6 個預備衝刺規則測試在實作前依預期失敗，job 883d1a03945448e99c3ae2c5d9fac416。
- 聚焦 EditMode：29/29 通過，job ae0b7d0f7b5b4a918344b20db1ff848c；包含原預備／幀時序、觸發條件、停靠與最後一步、UI 原色／透明度恢復、非敵人拒絕。
- PurgersRegression：job cc598dfabc7446e6aa5b2a9069836007；completed=614、discovered total=629、result=null。工具列出既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 的引用不一致失敗。不可宣稱全回歸通過，也不能將 discovered total 當成完成數。
- 受控 Single Runner：見 [single.txt](single.txt)。使用正式 Player／Tank Runtime／Enemy 生命與傷害接收者、NetInput、Fusion Runner、KCC；測試分身替換為固定球體碰撞幾何，沒有保存資產。
- 測試幾何限制：現行 Enemy_Melee_A 有大型模型碰撞器，正前射線會通過腿間；本測試不證明正式模型每個姿勢的可瞄準性或最終手感。正式敵人資產未修改。
- 本地 UI 顏色與透明度已由聚焦測試驗證；Runtime 查詢使用自己 Runner 的碰撞資料。正式 HUD 元件需人工掛載，因此尚未宣稱正式場景畫面完成驗收。
- 重跑入口：Editor 空閒 Play Mode 呼叫 TankPreparedDashRuntimeVerification.Begin(false/true)。它只建立可拋棄 Runner 與 Runtime Clone，不保存場景／Prefab。
- 先前診斷中出現的 total=0、測試未初始化、測試 Prefab 路徑錯誤與 EditMode 手動生命週期呼叫方式錯誤，均不計為通過證據。

- 受控 Single Runner：18 項檢查通過。
- 同 Editor 受控 Host／Client：21 項檢查通過，見 [host-client.txt](host-client.txt)。Client Input Authority 真正送 NetInput，Host State Authority 執行衝刺與傷害；檢查停格同步、牆阻擋與重連後狀態。測試的 Client 碰撞幾何也只建於拋棄式分身。
- 尚未做獨立執行檔 Host／Client、正式模型與 HUD 畫面驗收；空中、敵人途中死亡與正式死亡／換裝中斷仍列人工場景驗收，不以一般 Reset 探測冒充逐項通過。
- 最終 Editor：scriptCompilationFailed=false、isCompiling=false、isPlaying=false；Console error 查詢為 0。移除新測試工具的未使用欄位 CS0414，沒有新增正式 Gameplay 編譯錯誤。新檔案 .meta 已由 Unity 產生。
- 場景注意：開始觀察 _Menu dirty=true，最終 dirty=false；檔案時間在第一輪 Test Runner 啟動期間更新。目前 Git 差異為 Tank 輕擊震動三項值（0.25 秒／位置幅度 1／旋轉幅度 1.5）。本輪沒有呼叫 SaveScene，無法斷言是哪個 Editor／測試動作儲存；保留現值，不回退使用者工作。這些震動數值不屬本功能調整。
- 文件連結及本輪程式差異檢查通過。