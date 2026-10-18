# 第一人稱護目鏡驗證 — 2026-10-16

正式設定：[103](../../103_第一人稱護目鏡效果.md)。使用者已確認柔邊內襯外觀，並明確授權直接配置 Unity。

## 實作與序列化

- 新增兩個本機呈現元件、獨立 Prefab，以及 _Menu/GameplayHUD Canvas/PlayerHUDCanvas/FirstPersonGoggles。
- 設定預設 Thickness 1、Opacity 0.94、Edge Softness 5；周邊幾何，中央沒有三角形。單一子 Canvas 排序 -10，Graphic 與 CanvasGroup 不接收點擊，無 GraphicRaycaster。
- 配置時比對 1,742 個既有元件，見 [接線報告](wiring.txt)。測試前保留完整 _Menu 基線於 Temp/GogglesValidation/_Menu.before.unity；最後逐段比對只新增護目鏡 PrefabInstance、RectTransform 及 parent child 引用。測試保存造成的 WholeHealthValue 空字串已經由 Editor API 還原原值 100 / 100。
- 未改動既有 UI 座標、尺寸、輸入、相機、FOV、玩法或網路權威。其他工作區既有修改保留。

## EditMode 與編譯

- 紅燈：首次有效執行 7 項因尚無呈現型別而失敗；之前 total=0 不算執行。
- 最終聚焦：9／9 通過，見 [原始結果](focused-tests.json)。涵蓋最大厚度中央安全區、透明／零厚度、alpha、Raycast、無 Runner、Prefab／正式 Scene 接線、超寬 Stretch、CanvasRenderer、無額外網路元件。
- 完整 PurgersRegression：**928 通過、1 失敗、1 略過**，見 [完整 RunFinished](regression-tests.txt)。唯一失敗為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring（MapChunk_Boss 引用）；不是全綠。探索數 945 不等於完成葉節點數，本次回呼結果共 930。
- MCP job 跨 Domain Reload 遺失持續進度；確認 Editor TestRunner 結束後，採現有 AbilityToggleTestResults 的本輪 RunFinished 落盤結果，檔案時間 2026-10-16 19:36:27；不以 completed=0 或舊 job 當通過。
- 本次曾修正生成曲線端點負浮點數進入小數次方造成 NaN、Prefab 缺少 CanvasRenderer、預覽 Canvas 的縮放／模式問題及測試反射歧義；修正後才取得上述最終結果。

## 實際 Unity 渲染

已檢視以下渲染，現有血量、速度、技能、武器、關卡與獎勵位置保留。靜態圖使用既有 BattleHudReferenceLayout 的示意值，不表示當前遊戲資料；底色僅為看清邊緣。

- [1920×1080 預設](goggles-1920x1080-t1.00-a0.94.png)
- [1280×720 薄／半透明](goggles-1280x720-t0.60-a0.50.png)
- [2560×1080 超寬](goggles-2560x1080-t1.00-a0.94.png)
- [1920×1080 最大厚度＋獎勵](goggles-1920x1080-t2.00-a1.00-rewards.png)
- [已確認概念](approved-concept.png)

厚度 2 用來檢查極限；建議 0.6～1.2。高厚度會增加世界遮擋，並改變底部字樣的背景對比，即使 HUD 排序保持在護目鏡上方。

## Play Mode

正式 Scene 實例搭配既有 SpectatorRuntimeVerification 的隔離 Photon Host／Client 場景。共用同一 Editor／相機，因此明確 BindRunner 指定目前觀看端；沒有保存臨時玩家或 Runner。流程將救援關閉以涵蓋既有死亡觀戰與自動重生，不代表已驗證自然救援／跨關。

最終 10 項檢查通過：無 Runner 隱藏、既有觀戰流程、Host、本機晚加入 Client、死亡觀戰、重生、無存活目標、斷線清理、不擋點擊及 HUD 下層排序。結束後 Console error／warning 查詢為 0；編輯器預覽階段曾有既有字型缺字與 MCP 連線警告，未列為本功能編譯錯誤。結果見 [受控 Host／Client](playmode-host-client.txt)，畫面見 [實際觀戰](playmode-observing.png)。初次每 50ms 取樣漏掉短暫重生窗口，保留 [初次紀錄](playmode-first-sampling-failure.txt)；最後採逐幀可恢復的 1ms await 取樣，不修改玩法流程或強制執行呈現更新。

## 未驗證範圍

獨立程序 Host／Client、重連、自然操作與完整跨關、正式戰鬥全部 HUD 狀態、目標平台 Build、Profiler／draw calls、最終主觀外觀均未據此宣稱通過。幾何為 1,568 頂點／2,688 三角形，無新增貼圖／材質／shader；數量不等同實測效能。

