# Phase 4 前置-1 驗證證據

> 此資料夾名稱保留舊階段標籤，作為歷史驗證證據路徑；正式名稱已校正為「Phase 4 前置-1」。

詳見 [功能與驗證文件](../../144_PrePhase4-1_共用控制與轉場.md)。

- Host.txt／Client.txt：最後一次雙程序流程的每秒狀態及檢查結果。
- black.png：Game View 的全黑覆蓋；工具單獨使用，不依賴 AllInput 鎖隱藏 HUD。
- visual.txt：Time.timeScale=0 的淡入／淡出、UI 攔截與待提交輸入清除。
- respawn.txt：安全屋 PlayerHealth 傷害 → GameLogic Despawn／新 Player Spawn 的實測結果。

測試期間 GameLogic 的 respawnDelay 使用場景原有 1 秒設定。工具直接建立測試 Runner，使用專用存檔資料夾，不代替真人鍵鼠逐鍵複驗或後續多 Chunk 網路拓撲測試。
Unity EditMode 最終 job：201ddf8fa8424d598a764e49e854868d，49／49。Windows Development Build 最終 job：build-437bd9f7a7，0 errors、0 warnings，13:19:03 UTC 完成。

最終雙程序複驗於 2026-09-18 13:21:36 UTC 完成；Client 原始紀錄的 exception／error／assertion 搜尋結果為 0，Editor Console error／warning 為 0。證據目錄保留 ClientPlayer.txt 全文，父節點修正後未再出現斷言。
