# 第一人稱觀戰驗證紀錄

日期：2026-10-14。範圍：觀戰相機回授修正、隊友第一人稱 ViewModel／戰鬥 HUD。倒地救援仍為討論，未改玩法。

## 問題與修正

既有 `_Menu/CameraRig/CameraEffectsRoot/LocalSpectatorCameraController` 是 CameraRig 的子物件，卻由 CameraFollow 跟隨該子物件。原第三人稱 LateUpdate 移動子物件後，父物件再次跟隨，造成位置回授。現改為 `SetWorldPose` 的世界姿態快照，owner 只作身分識別；位置與 Pitch／Yaw 取被觀戰者眼睛。

模型與 HUD 沿用既有實作，視覺來源可切到同 Runner 隊友。觀戰不寫遠端武器的槍口引用，也不代操作獎勵。

## 實際驗證

| 層級 | 結果／證據 |
|---|---|
| 聚焦紅燈 | 3 項如預期失敗，尚無世界姿態 API；[原始結果](focused-red.json)。首次尚未匯入測試的 total=0 已排除，不能當通過。 |
| 聚焦綠燈 | 首批 3/3；再加入身體還原及無效來源，共 5/5，job `e5b48474947641b38976e4c8fb6acee6`。全回歸記錄亦包含五項結果。 |
| 完整 PurgersRegression | 852 通過、1 失敗、1 略過，共 854；[完整回呼結果](regression-tests.txt)。失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`，不是全綠。MCP job `45ca6ea25d434305b9f27be61adf9be9` 在 Domain Reload 後停留 completed=0，改以當輪既有 TestRunner 回呼檔取證；確認 Unity TestRuns 已為空後清除 orphan job。 |
| 編譯／Console | 編譯完成、Console 無 Error。保留既有 DemoController.camera、TankPreparationRuntimeVerification 引用比較及 BattleHudReferenceLayout.spritesheet 三項編譯警告；本輪程式未新增警告。 |
| 同 Editor 受控 Host／Client | [20 項通過](host-client.txt)：Client 觀戰 Host 的 replica、保留 pitch/yaw、20 次姿態無回授、body shadow 還原、ViewModel 只讀綁定、血量／彈藥來源、ADS FOV、原自動重生、觀戰目標死亡後清除模型與 HUD。 |
| 實際畫面 | [Client 觀戰截圖](observed-client.png)，已人工檢視：單一第一人稱武器、開鏡畫面、被觀戰者 75/100 生命與 30/30 彈數。截圖是隔離測試場、朝向天空，不能代表正式關卡所有視覺效果或運動中的最終驗收。較早截到主選單的圖未當作觀戰證據。 |

## 驗證隔離與限制

- 使用既有 `_Menu` 相機／手武器／HUD；臨時 GameLogic、地板及 Runner 僅在 Play Mode 建立。暫時將測試實例重生等待改為 18 秒以完成觀戰檢查；正式數值未修改。
- 同一 Editor 的 Host／Client 共用本機相機單例，所以固定以 Client 作觀看端，停用測試 Host 端的視覺元件以避免單例互搶；Host 權威模擬仍運行。這不是兩個獨立程序的多人證據。
- 第一人稱呈現來自既有同步角色／武器狀態，不是畫面串流。Input Authority 專用命中、受傷、回血 RPC、鏡頭震動與私有 UI 尚未轉送給旁觀端；不能宣稱逐幀完全相同。
- 獨立程序 Host／Client、長時間高速運動、換武器／技能效果、三人以上自動換目標、正式關卡小地圖探索與安全屋往返仍需人工驗收。
- 未修改或保存正式 Scene／Prefab／材質／Renderer 設定；測試前後 `_Menu` dirty=false，最後已退出 Play Mode。工作區原有使用者改動保留。

## 重跑入口

1. Unity EditMode 跑 `SpectatorPresentationTests`，再跑 `PurgersRegression`，必須取得非零完成數。
2. 無既有 Runner 的 `_Menu` Play Mode，透過 Editor C# 呼叫 `SpectatorRuntimeVerification.Begin()`。
3. 查看 `Status`／`Stage`，結果落在 `Temp/SpectatorVerification/host-client.txt`，畫面為 `observed-client.png`；測試清理後退出 Play Mode，不保存場景。
4. 正式 Inspector 與獨立多人步驟見 [100_UI觀戰與聊天](../../100_UI觀戰與聊天.md) 的第一人稱觀戰章節。
