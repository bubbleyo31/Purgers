# Play Mode 關卡限時暫停驗證

最後核對日期：2026-10-08（局部）
核對來源：InputManager、StageFlowController、StageRules 與本次兩支驗證腳本。
設定：[143 關卡限時暫停](../../143_Phase3_Level1關卡閉環配置與驗證.md)。

## 已驗證

- 紅燈：job `d91361de0a5a45db807b1272a2ff1fca` 執行 11 項，全部因尚未存在 CanToggleDevelopmentTimerPause 而預期失敗；沒有用編譯失敗代替測試。
- 最終聚焦：`StageTimerPauseTests` 12/12 通過，包含未 Spawn 物件安全拒收。見 [focused.json](focused.json)。
- 完整 `PurgersRegression`：job `0cb0171df7d04e7bb4d14fcae9d8755f` 回報 completed=551、探索 total=566；status=failed、result=null，列出 1 項 `BossStageAssetTests.GameSceneContainsBossAssetWiring` 同名 Boss Chunk 引用不一致。不得稱為 566 項全數通過，也不由空 result 推算完整通過數。相同失敗在任務前快速架構文件已有記錄。見 [regression.json](regression.json)。
- 受控 Play Mode **Single Runner**：18 個 PASS，跨原到期點仍 Active、精確凍結／接續剩餘時間、Fusion Tick／Unity 時間繼續、timeScale 不變、撤離計時繼續、多次切換、新關卡重設、載入／不限時／到期／結束拒收。見 [runtime.txt](runtime.txt)。
- 編譯檢查 scriptCompilationFailed=false；本次最終 Console error／warning 查詢為 0。Runner Prefab 未保存新欄位時，讀到的預設值已確認是 KeyCode.F7。
- 未修改或保存 Scene／Prefab，Editor 已退出 Play Mode，回到原本 _Menu。

## 測試方法與限制

在沒有其他連線的 Play Mode 中，透過 Unity MCP execute_code 明確呼叫 `StageTimerPauseRuntimeVerification.Begin();`，再讀取 Status；報告位於 `Temp/StageTimerPauseVerification/runtime.txt`。測試使用臨時 Runner、臨時 StageFlow 與實際玩家 Prefab，不建立／寫入遊戲存檔。權威狀態注入由測試 driver 安排在 Fusion Tick。

初始 fixture 沒有存活玩家，觸發既有全滅規則與缺少存檔 Context 的提交失敗；已修正為有存活玩家後重跑通過。測試 driver 的 AddGlobal 也已調整到 Runner 啟動後。以上是測試設定錯誤，不當作玩法回歸通過證據。

Single Runner 驗證直接呼叫按鍵後的請求入口，**沒有**驗證 OS 實體按鍵、完整 InputManager 鍵盤路徑、正式 HUD 畫面、自然撤離成功／全滅／Boss 擊殺與存檔切場。本次沒有獨立 Host／Client、晚加入／重連或 Player Build 證據；這些手動步驟列於 143，不能用本結果替代。

暫停剩餘 Tick 是 Networked 欄位，各端讀值契約已接入；實際跨端同步仍待前述多人驗證。
