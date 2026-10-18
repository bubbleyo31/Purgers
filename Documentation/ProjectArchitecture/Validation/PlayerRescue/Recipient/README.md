# 被救援者 HUD 提示

日期：2026-10-16。被救援者保留同步空心圓環與剩餘秒數，隱藏整個 E 按鍵／方形背景，顯示「正在被救援」。倒地等待或救援中斷仍不顯示 E；救援者仍顯示 E。既有生命與計時規則未變。

LocalPlayerRescueHUD 新增 keyPrompt 引用，PlayerRescueHUD.prefab 已透過 Editor 接到 Content/Key，正式 _Menu instance 自動繼承，未改正式 Scene 排版。建立工具同步接線。

同 Editor 受控 Host／Client 的正式 Canvas Play Mode 檢查 [13 項通過](playmode.txt)，包含救援者按鍵、倒地者無按鍵、Client 受 Host 救援時的文字與同步圓環，以及中斷返回等待。已檢視 [被救援者畫面](being-rescued.png)：0 HP、正在被救援、4.8 秒、剛起始的金色圓弧，中央無按鍵背景。Console 無 Error，無新增編譯錯誤。仍不代表獨立程序或正式關卡全流程驗證。

完整 PurgersRegression：901 通過、1 項既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗、1 Explicit 略過，合計 903，並非全綠。包含既有 PlayerRescueTests 10/10。[原始回呼結果](regression-tests.txt) 為本輪 14:34:43 寫入。首次 MCP 初始化逾時不算執行；重試 job d520dd087cdd4da7b298c7093d736f3c 在 Reload 後停留舊進度，已確認 TestRunnerApi.IsRunActive=false 後清除 orphan 工作。
