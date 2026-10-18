# 右下技能槽驗證紀錄

最後核對日期：2026-10-18（本次功能局部驗證）。
正式規則：[107 右下技能槽狀態呈現](../../107_右下技能槽狀態呈現.md)。

## 範圍與執行順序

依使用者要求，先於獨立 worktree 準備程式、測試與文件；等待「討論 TAB 玩家資訊視窗」釋放 Editor 後才接管主專案。整合時沿用該工作完成的 TryReadPresentation／IPlayerAbilityCooldownSource 及戰鬥輸入鎖，未重複新增同步計時欄位。

本次只新增本機技能格狀態顯示、技能唯讀操作資格與驗證工具。未改技能時間、傷害、施放／再次施放結算、Host Authority、掉落拾取、Scene／Prefab 位置、來源圖示與鍵帽。正式 HUD 建立原模板實例時自動補上 Graphic／文字。

## 結果

- 基準紅燈：[red.json](red.json)，新功能未合入時實際完成 24 個測試，其中 23 個按預期失敗；既有共用冷卻資料介面檢查通過。MCP 當輪缺最終 summary，因此保留原始 completed／failure 證據，不將它寫成完整 RunFinished 報告。更早的 total=0 嘗試已作廢。
- 聚焦：[focused.xml](focused.xml)、[focused.txt](focused.txt)，**24 通過、0 失敗**。涵蓋整格遮罩順時針幾何、完整分母、完成亮光一次、來源切換／隱藏／阻擋不補播、二次使用語意、正式模板整合與鍵帽／圖示保留。
- 受控 Photon Host／Client：[network.xml](network.xml)、[network.txt](network.txt)、[步驟](network-progress.txt)，**1 個情境通過、0 失敗**。同一 Editor 的 Multiple Peer，真實 Photon asia 連線；含 Host 第一次待命、Client 晚加入期限比例、proxy 無權施放、第二次開始原冷卻、斷線重連保留完整冷卻分母、Client Input Authority 經 Host 受理兩次輸入。
- 初次多人測試因測試工具動態建立 NetworkEvents 未初始化 AOI UnityEvents 而失敗，詳 [初次失敗](network-harness-initial-failure.xml)。依既有 TeamInfo／GrappleSpike 測試工具模式補齊事件初始化後重跑通過，未忽略錯誤或修改正式玩法。
- 完整 PurgersRegression：[regression.xml](regression.xml)、[regression.txt](regression.txt)，**total=1141, passed=1135, failed=4, skipped=2**。完整套件結果不是全綠；失敗名單見下方。
- 離屏預覽：[ui-preview.png](ui-preview.png)，直接使用 StageHUD 正式模板、既有 Blink 圖示及正式 PresentSlotState；已檢視冷卻 75%／25%、完成亮光、二次施放、可提前結束與條件不足。預覽標題僅為驗證說明，遊戲內不新增這排標題。

編譯與 Editor 收尾：[editor-final.txt](editor-final.txt)。編譯完成、Console 無錯誤／警告、播放已停止、Runner 為 0，原 `_Menu` 場景未變髒。

## 完整回歸失敗

- `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Expected: same as <MapChunk_Boss (MapChunk)>   But was:  <MapChunk_Boss (MapChunk)>
- `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：Expected: 0.5f   But was:  0.25f
- `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：Expected: greater than 10.0f   But was:  10.0f
- `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：System.NullReferenceException : Object reference not set to an instance of an object

接管前 TeamInfo 驗證已記錄 BossStageAsset、EnemyFlytrapAsset、EnemyReflectorAsset、ReloadCameraRuntime 上列四項失敗，詳 [前一輪紀錄](../TeamInfo/README.md)。本次不修正這些未授權的資產／攝影機問題。

## 尚未涵蓋

- 獨立 Player／Build、跨機器自然遊玩與自然斷線重連。
- 使用者實際解析度、HUD 縮放及戰鬥畫面的最終視覺驗收；離屏圖不等同自然遊玩。
- 全部技能逐一自然施放。本次受控連線情境以閃現驗證狀態／權威／期限，空中緩速與子彈時間原測試則隨完整回歸執行。

背景操作使用 MCP 與獨立 Preview Scene Camera.Render，沒有 Focus／Show／桌面鍵鼠；接管時已核對 PlayUnfocused 與 Run In Background。
