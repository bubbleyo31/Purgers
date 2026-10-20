# 速度動能增益：滿格才顯示

日期：2026-10-19。依使用者最新指示，效果正式改稱「速度動能增益」；橘金配色保留。

## 行為與範圍

- 讀既有 `PlayerGrappleMomentumEnergy.NormalizedEnergy`，只有有效值達到 1 才將外框目標設為 1；未滿時目標為 0。沒有增加玩法 Timer、RPC 或改動充能／衰減／傷害倍率。
- 滿格以原 0.16 秒淡入；既有滿格保持使量條維持 1；離開滿格後沿原 0.65 秒淡出，剩餘能量不再維持外框。
- `momentum` 序列化欄位名稱保留，Inspector 顯示名稱／Tooltip、Editor 預覽標題同步改為「速度動能增益」。沒有更換 Prefab／Scene 資產或配色。
- Editor「蓄能→滿格」預覽也改成等待模擬滿格才亮；歷史輸出動畫保留原版本。治療、受傷與 HUD 層級未改。

## 驗證

- [red.xml](red.xml)：先觀察 7 個門檻測試因尚無 Runtime gate 而預期失敗。
- [focused.xml](focused.xml)／[摘要](focused-summary.txt)：42／42 通過（ScreenEdgeEffectsTests、HudLayoutTests）。包含 0%、50%、99.99% 不啟動，100% 啟動，非有限值安全關閉及模擬預覽時序。
- 同 Editor Host／Client 最終通過，32 筆顯式 PASS，加上等待條件完成；最終 RESULT 通過、Stage=disconnect clears，見 [host-client.txt](host-client.txt)。測試 Runner 已清除。完整回歸 1251 項：1245 通過、4 既有失敗、2 跳過，非全綠，見 [XML](regression.xml)／[RunFinished](regression-summary.txt)。失敗名稱與首次整合完全相同（Boss wiring、Flytrap timing、Reflector、ReloadCamera），沒有新增失敗。

前兩次受控嘗試分別在等待接近滿格／部分充能的注入值時逾時，詳見 [第一次](host-client-injection-attempt.txt)、[第二次](host-client-decay-attempt.txt)。原因是僅設定能量數字，正常 TickSimulation 會在複寫／取樣前衰減。測試工具後續改為在注入滿格時一併設定既有 MaximumBoostTimer，保持正式滿格狀態的不變條件；非滿格測試改為等待有能量的區間而非緊貼注入值，容許正常衰減；精確 99.99%／100% 門檻另由聚焦測試驗證。這只作用於暫時 Host 測試實例，沒有修改正式動能系統。

新一輪受控檢查包含：半滿／接近滿格不出現、滿格顯示、Host／Client 本機歸屬、重新綁定滿格恢復、退回部分能量時淡出、死亡／換人／斷線清除。動能仍屬測試注入後經實際 Fusion 同步，不代表自然鈎索充能驗收。

自然實戰強度／時序、獨立 Player、多機與 Build 待驗。共用 Shader 外觀未改，此輪不重做歷史像素圖比較。
