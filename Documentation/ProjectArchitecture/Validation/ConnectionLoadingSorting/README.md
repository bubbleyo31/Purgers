# 連線載入動畫 UI 排序驗證

日期：2026-10-16。僅涵蓋本次 MenuUIController 的 Loading Canvas 排序；不代表獨立 Host／Client 端到端連線驗收。

## 變更與根因

正式 Loading Canvas 原為 overrideSorting=false／order=0；SafeHouseHUD（含小地圖）order=100。MenuUIController 在 SDK Show 啟用後同幀設定原 Canvas 的 overrideSorting=true、最高 Sorting Layer、order=32766。黑幕沿用 32767。未修改 Photon SDK、Scene、Prefab、動畫或玩法／連線流程。

## EditMode 與編譯

- 初次測試匯入未完成，回報 total=0（job 0bc8c5fe9de64b15972f6a25332d0b9f），不計為通過；明確匯入並重編譯後重跑。
- 修正前聚焦 job c458e620014241379813eca38dda1f6f：5 個測試實際執行，均因未獨立排序失敗，重現問題。
- 中間檢查確認 Unity 未啟用的 Canvas 讀取 overrideSorting=false；因此正式實作必須在 SDK Show 啟用後設定，測試同樣由停用的 Loading 開始、父 Canvas 保持啟用。
- 最終聚焦 job f3895b99c397491595d59a441f1ac816：total=5、passed=5、failed=0。涵蓋 order 0／100／200、重複顯示／隱藏，以及低於轉場黑幕；父 Canvas 排序保持原值。
- 完整 PurgersRegression：944 通過、1 失敗、1 略過（946 個結果；探索集合 961 並非執行數）。唯一失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`。非全綠。
- MCP job d3622bf3ccf4448388eddd58b76c3773 在 Domain Reload 後狀態停滯，不當作完成證據；啟用專案既有 EnemySuicideTestResults.RunFinished 記錄器後，以 job 98ffee6473b54117be7a70c7c5f135f6 重跑。有效完整結果見 [regression.txt](regression.txt)。
- Unity 已重編譯載入新方法；最終 Play Mode 前後 Console 查詢 error／warning 為 0。

## 受控 Play Mode 顯示

從正式 `_Menu` 進入 Play Mode，以正式 `MenuUIController.Show<FusionMenuUILoading>()` 顯示連線載入視窗，沒有發起網路連線或建立遊戲存檔。之後建立 order=100 的本機洋紅色矩形作 HUD 探針，以驗證稍後建立的 UI 仍被覆蓋。

- 正式載入視窗：IsShowing=true、overrideSorting=true、order=32766、Connect gate=true。
- [修正後畫面](loading-above-hud.png)：原背景、準備行動面板、旋轉圖示與取消按鈕正常顯示；洋紅 HUD 被覆蓋。
- [原排序重現](baseline-hud-above-loading.png)：只在 Play Mode 暫時把同一 Canvas overrideSorting 關閉，洋紅 HUD 蓋住載入動畫。
- 再次呼叫正式 Show：恢復 overrideSorting=true。
- 切回主選單時 Loading 仍在 Hide 動畫中；動畫結束後 activeInHierarchy=false、Connect gate=false。再次 Loading：IsShowing=true、order=32766。
- 已移除測試物件並退出 Play Mode，沒有保存 Runtime Scene 變化。

## 未驗證邊界與設定

本次沒有實際 Quick Play／Continue／Join、取消網路連線、連線失敗、獨立 Host／Client 或慢速載入驗收；小地圖資料與網路權威完全未改。Play Mode 圖像證據使用受控 HUD 探針，不宣稱是真實多人安全屋小地圖畫面。

設定及手動驗收步驟見 [144 連線載入動畫 UI 排序](../../144_PrePhase4-1_共用控制與轉場.md)。不需 Inspector 接線或場景遷移。
