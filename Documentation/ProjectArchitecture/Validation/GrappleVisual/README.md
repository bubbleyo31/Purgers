# 鉤索波浪與鉤爪呈現驗證

最後核對日期：2026-10-05（Asia/Taipei；結果檔使用 UTC）。
核對來源：PlayerGrappleVisual、PlayerLocalView、GrappleRopeVisualTests、GrappleVisualRuntimeVerification 與 Unity MCP。
相關文件：[配置與責任](../../30_鈎索系統.md)、[回歸狀態](../../130_回歸驗證與審查狀態.md)。

## EditMode 與回歸

修改前新增 11 項聚焦測試，確認因缺少波形／鉤爪行為失敗；實作後 11/11 通過，再增加模型偏移、曲線及實例重用清理案例，最終 **14/14** 通過，見 [focused.json](focused.json)。

完整 PurgersRegression 實際執行 **468 項，467 通過、1 失敗、0 跳過**，見 [regression.json](regression.json)。唯一失敗是 BossStageAssetTests.GameSceneContainsBossAssetWiring：兩個名稱同為 MapChunk_Boss 的引用不相同；前一個落地修正切片亦記錄同項失敗。本輪不修改 Boss 資產，不宣稱全回歸通過。

補測時 MCP run_tests 曾兩次未能初始化、沒有開始測試，未計為通過。改由 execute_code 呼叫現有 MCPServiceLocator.Tests.RunTestsAsync，沿用 Unity Test Runner、MCP callback 與結果收集，取得上述非零結果，未修改工具套件。執行前確認 Scene 無 dirty，避免服務自動儲存。

## 受控 Runtime 與網路界線

探針只編入 Editor，Play Mode 明確呼叫 GrappleVisualRuntimeVerification.Begin(false) 或 Begin(true)，不可與另一輪探針重疊。結果輸出到 Temp/GrappleVisualVerification。建立暫時 Fusion Runner 與正式 KCC_Player Prefab，等首個模擬更新後才 Spawn／存取 Networked。沒有自動啟動、正式存檔或 Scene／Prefab 儲存。

探針暫停測試角色的 Player 模擬入口，由 Host 注入既有同步 phase／anchor／timer；檢查 LineRenderer 的端點、點數、波形與鉤爪 active／銷毀。鉤爪是沒有 Mesh 的臨時物件，證明生成、Transform 與生命週期，不代表作者模型外觀已驗收。Menu 缺少完整遊戲相機流程時，明確呼叫 Input Authority 的既有 after-camera 入口。

Host／Client 使用同一 Editor 內兩個實際 Photon Fusion Runner、獨立物理 Scene 與臨時 session。測試用 SceneManager 繼承 Default manager，讓未 LoadScene 的空隔離場景接受測試物件；不載入或替換正式場景流程。Proxy 資料透過 Fusion 接收，探針不直接寫 Client Proxy 的 Networked 值。

自然按 Q、命中與拉動、Input Authority 預測／延遲回滾、獨立程序／電腦、Windows Build，以及作者鉤爪 Mesh 的遮擋與美術效果仍未驗收。Multi-Peer 受控通過不能取代這些項目。

## 手動配置與驗收

1. Edit Mode 開啟 Assets/Prefabs/KCC_Player.prefab，在 Player Grapple Visual 指定自己的純視覺 Hook Prefab，再設定大小、前向校正與繩結接點。Third Person Rope Origin 指向角色世界模型手部／裝備，留空使用根偏移。欄位範圍與強烈波浪試調值見 30。
2. 同版本建立獨立 Host／Client，輪流操作角色。發射期間兩端應看到鉤爪在繩頭，波浪持續流動，繩子兩端保持接合。
3. 測遠／近距離、向上／下與快速轉向。Wave Amplitude 增加起伏；Wave Travel Speed 只改波峰流動，不改飛行時間。關閉 Enable Waves 後應直線伸縮，鉤爪保留。
4. 命中後在 Hit Tighten Duration 內拉直，0 秒立即拉直。中途取消從目前長度收回鉤爪，不能先瞬間延伸至目標；收回後不得殘留。
5. Host 附著時讓 Client 晚加入，應直接看到直線與掛點鉤爪。斷線重連、死亡重生及正式場景轉換後再次出鉤，確認沒有舊鉤爪或重複實例。
6. 本機 Rope Origin 跟隨第一人稱發射點，旁觀角色跟隨其世界模型。再測延遲／回滾與多人同時出鉤：波相允許各端不同，掛點與階段應一致。

本輪未指定鉤爪 Prefab 或寫入 Scene／Prefab 配置。既有 Rope Origin 為 GrapplePoint，Hook Prefab／Third Person Rope Origin 為 None；新波浪預設已經 Editor 讀回核對。


## 本輪結果與中斷狀態

- Single Runner：15/15，見 [single-runner.txt](single-runner.txt)。證明有效 Spawned 物件、本機／旁觀角色繪圖、收斂、隱藏及 Despawn 清理。
- 同 Editor Host／Client：22/22，見 [host-client.txt](host-client.txt)。包含 Host Input Authority、旁觀呈現、Client 接收 phase／anchor、射出／拉直／收回、晚加入、Despawn 與重連綁定。
- 最後在探針新增四項 Client 自有角色檢查（Input Authority 綁定、收到同步射出狀態、Host 觀看該角色、網路 Despawn 鉤爪清理）。已透過反射確認新增案例編入 Editor，且編譯未失敗、Console error／warning 為 0；但正式補跑途中 MCP 回報 no_unity_session，沒有產出新報告，因此這四項仍待執行確認。現有 22/22 不包含它們。
- 已保存的 Runtime 結果來自先前完成輪次；沒有把未完成補測覆蓋成成功。最後完整回歸之後只增修 Editor 探針，正式 PlayerGrappleVisual／PlayerLocalView 沒有再改。
- MCP 中斷後 Unity 程序仍存在，透過 computer-use 開啟 MCP 面板並送出退出 Play Mode 快捷鍵，但截圖擷取逾時（FrameArrived／window capture timed out），MCP instances 仍為 0。無法確認最後是否退出 Play Mode、清理完暫時 Runner 或 Scene dirty 狀態；請在 Unity 確认停止 Play，再於 MCP 面板恢復連線，才能補測。沒有強制關閉 Unity 或儲存場景。
- 中斷前最近一次檢查 Scene 無 dirty；最終 git 檢查沒有 Scene／Prefab／ProjectSettings 變更。鉤索使用 Player Layer（6），目前 WorldCamera 包含該 Layer，WeaponCamera 不包含。
