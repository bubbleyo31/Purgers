# SafeHouse HUD 與平面圖修正

最後核對日期：2026-09-22。核對來源：StageHudController、SafeHouseHudController、LocalMinimapController、MapCartographySetup、實際 Fusion NetworkSceneManagerDefault 與 SafeHouse Scene。

## 根因與修正

上一輪 HUD 清理在 Awake 記錄原始 Unity Scene handle；Fusion Multi-Peer 將整個場景合併進 Runner Scene 時，原始 Scene 被卸載，但 HUD 仍應存活。舊清理回呼誤將它銷毀，造成 SafeHouse 左上整組資訊與 TAB／ESC 處理一起消失。現在收到卸載事件時，也核對 HUD 目前的 Scene，搬移後不銷毀；真正離場沿用 Fusion scene root 銷毀、Flow 失效與 Registry 接管。

安全屋沿用 LocalMinimapController／MinimapTileCache，新增 SafeHouse Flow＋獨立 Cartography 資料來源，不建立 NetworkMapState 或 AI 導航。安全屋圖為全圖可見的本地呈現，範圍 35／55 公尺、M 展開；不保留上一關敵人、撤離或探索狀態。

## Inspector 與更新流程

- SafeHouseHUD 上的 LocalMinimapController：map 留空；safeHouse 指向同 Scene Flow；safeHouseCartography 指向 Assets/Prefabs/Map/SafeHouse_Cartography.asset。
- compactContent／placeholder 沿用 TopLeftColumn/MinimapSlot；UI 隨 SafeHouseHUD 清理。
- 地形改動後執行 Tools > Purgers > Map > Bake SafeHouse Cartography。
- 目前烘焙限 GrayboxEnvironment／StartTerminal 的 BoxCollider，Floor 為高度基準；不高於地板 0.6m 的低平台填為地面，其餘家具／牆面亮色。這是單層灰模平面圖，不代表導航可達性，也不支援封閉屋頂或多層室內。

## 驗證

- 搬移回歸紅燈：兩種 HUD 都在遷移場景後被卸載回呼誤刪；修正後通過。
- 完整 PurgersRegression：143 項執行、141 通過、2 項失敗，job 936268a1b15d463697b7c37f1962e62a。失敗仍是既有 BossChunk Cartography 空引用及 Game bossChunkPrefab 失效引用；未變更 Boss 資產。
- Editor Play Mode 使用正式 Fusion Multiple Peer 設定、GameMode.Single：SafeHouse -> Game -> SafeHouse，逐次確認當前 HUD=1、另一場景 HUD=0、小地圖=1、TerrainReady=True。
- 在終端距離內呼叫 E 使用的正式 RequestOpenReadyCheck，ReadyCheck 面板顯示；TAB 的 RequestToggleLocalReady 將本機設為 Ready 並進 Countdown；ESC 的 RequestCancelLocalReadyCheck 回 WaitingForHost，Ready 歸零。這是請求入口驗證，未模擬實體鍵盤事件。
- Editor 測試暫將投票／出發時間延長到 120s 以便截圖，停止 Play Mode 後還原；未保存到場景。
- 獨立 Windows Host／延遲 10s Client：Level1 Game -> SafeHouse Level2 -> Level2 Game 兩端全部 PASS，包含當前 Flow、唯一 HUD、SafeHouse 自己的小地圖綁定與離場清理。使用隔離測試存檔，紀錄在 Network/Host.log、Network/Client.log。
- Play Mode 與往返後 Console 0 errors／0 warnings；縮小與展開畫面已人工檢視本目錄 PNG。
- 最終 Windows Development Build：Builds/SafeHouseHudFinal/Purgers.exe，Succeeded、0 errors／0 warnings。雙程序驗證使用警告修正前的同功能 Build；最後僅將探針反射結果明確轉型為 SafeHouseFlowController，移除 CS0252。

## 修改入口

先讀 Assets/Scripts/GameFlow/SafeHouse/README.md、Assets/Scripts/UI/Player/README.md，再讀上述四個腳本。修改場景生命週期時一併參照 Assets/Photon/Fusion/Runtime/NetworkSceneManagerDefault.cs 的 OnSceneLoaded/MergeScenes；不能把原始 Unity Scene handle 視為 Fusion 關卡生命週期。
