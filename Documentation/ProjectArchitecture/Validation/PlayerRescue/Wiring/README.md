# 救援 HUD 正式接線驗證

日期：2026-10-16。使用者明確要求接入正式戰鬥 Canvas 並確認遊戲內顯示。

## 已保存接線

`Assets/Scenes/_Menu.unity` → `GameplayHUD Canvas/PlayerHUDCanvas/ReferenceFrame/PlayerRescueHUD`。

僅新增一份既有 Prefab instance 與父節點 child reference；保留 `PlayerRescueHUD.prefab` 來源連結，沿用 `BattleHudReferenceFrame` 的 1920×1080 等比例縮放。根定位中央下方 180；Runner 留空供正式單端自動解析，Content／Status／Time／ProgressRing 引用完整、無 Missing Script。不再需要人工拖入。既有 Game／SafeHouse 場景檔案雜湊維持不變。

## Play Mode 驗證方式

`PlayerRescueRuntimeVerification.BeginWiredHud()` 使用正式場景已保存的唯一 HUD，不建立替代 Canvas 或 UI。隔離的同 Editor Host／Client 使用正式 KCC_Player、PlayerHealth 救援流程及 Client E 輸入；測試場只建立臨時地板與 GameLogic，沒有載入完整正式關卡或更動存檔。

Multi-Peer 共用一個 Editor 視窗，測試固定 Client 為觀看端，暫停 Host 呈現並隱藏 Host 同座標角色 Renderer，避免兩端模型重疊。主選單僅在 Play Mode 暫時隱藏，模擬已進入遊戲的顯示狀態；結束後還原。未改正式攝影機或玩家 Renderer 資產。

## 結果

- 正式場景唯一一份救援 HUD、內部引用完整、無 Missing Script；沒有本輪新增編譯錯誤。
- [Play Mode 原始結果](playmode.txt)：11 項 PASS（含兩端連線）。無 Runner／健康無目標／超距隱藏，靠近顯示 E 且圓環為零，按住後讀取同步進度，放開歸零，完成後恢復 35% HP 並隱藏，自己倒地顯示倒數，死亡切入觀戰時清除提示。
- [靠近](nearby.png)、[救援中](rescuing.png)、[放開](released.png)、[自己倒地](downed.png) 為實際 Game View 擷取；已人工檢視圓環、E 與文字、既有 HUD 層級。
- 本輪為資產接線及 Editor-only 驗證工具，未改救援玩法或 UI 運行邏輯；未重跑整套回歸。上一輪 901 通過／1 既有 Boss 接線失敗／1 略過仍是上一輪證據。
- 不代表獨立程序 Host／Client、正式關卡完整撤離轉場或所有解析度自然操作已驗證。

首次截圖含另一 Runner 的重疊模型，調整的是驗證 fixture 的渲染隔離；最終截圖以隔離後的重跑為準。

縮小截圖曾被誤判為 E 缺字；逐像素檢查按鍵內深色字形及診斷文字 mesh 後確認存在，已移除探索性的 ForceMeshUpdate 改動，不把誤判當成產品問題修正。

最終恢復原 HUD 顯示邏輯後，重新從 Play Mode 冷啟動跑完 11 項；四張截圖中央 E 字形皆有 262 個深色像素，與單獨啟用同一字型的字形一致。Play Mode Console 最終讀取無 Error。
