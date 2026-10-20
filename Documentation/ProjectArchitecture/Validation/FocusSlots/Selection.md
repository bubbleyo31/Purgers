# 專注技能每次指定槽位

最後核對日期：2026-10-21；取代前版自動填空及滿槽才選位置的互動。

## 行為

- 獎勵：按住 ALT 選取專注技能 →「選擇裝備位置」→ E／Z／X 確認。三槽皆空也需選擇；放開 ALT 取消。
- 地面：按 E 開啟選槽 → 放開開窗鍵 → E／Z／X 確認；右鍵取消。保持距離與朝向，離開可拾取條件自動取消。確認前掉落物保留世界狀態。
- 空槽顯示「放入此槽」，占用槽顯示「替換此技能」。可以保留空槽、刻意替換另一格。已持有的重複技能仍拒絕。
- 選槽面板以深色背景、已選技能標題與三個等寬槽位區隔獎勵卡片，未修改原獎勵選單版面。
- 不新建玩法權威：獎勵走既有 GameLogic；世界技能透過 NetInput 送 NetworkId／目的槽／LoadoutRevision，Host 重新檢查身分、版本、距離、遮擋、存活、救援優先、資格與等級，再轉移原 Runtime，保留冷卻／等級。重放確認不會取得第二次。
- 設定鍵位沿既有 PlayerSettingsService；確認鍵直到放開前不會施放／丟棄，選槽期間阻擋戰鬥輸入。

## 設定與檔案

LocalPlayerRewardHUD 的 focusSelectionPanelSize 預設 920×450；replacementTitleOffset 沿用為面板偏移 (0,-80)。獨立面板執行時由原 HUD 建立，字型沿用既有資產；本次不需修改 Scene／Prefab YAML。Editor 預覽入口 FocusSlotsPreview.RenderReplacement(path, occupiedSlots)。

## 驗證

- 隔離分支先確認舊規則在三槽全空時失敗；修正後離線 32 個空滿／指定槽組合通過。
- Runtime 與 Editor 程式離線編譯通過（不代表 Unity weaving／Play Mode 已驗）。
- Unity 編譯無新增錯誤。聚焦 16/16，fresh XML：[selection-focused.xml](selection-focused.xml)。
- 受控同 Editor Host／Client／late join 共 48 PASS：[selection-host-client.txt](selection-host-client.txt)。包含 Client 透過 NetInput 在 Z 空著時選占用的 X，Host 正確替換 X、Z 保持空槽；確認 UI 不以鍵鼠模擬測試。
- 完整 PurgersRegression：1324 項、1318 通過、4 失敗、2 略過，並非全綠。fresh RunFinished XML：[selection-full-regression.xml](selection-full-regression.xml)。剩餘 Boss 接線、Flytrap 時序、Reflector 資產與 ReloadCamera NullReferenceException 四項失敗，與接手前同名失敗一致，本輪未修改這些目標檔案。
- 正式字型／技能圖示離屏預覽：[全空槽](selection-empty.png)、[全占用槽](selection-full.png)。確認獨立面板、已選技能、三鍵與放入／替換標示，未改原獎勵卡片。
- Editor 已停止播放並回到乾淨 _Menu；本輪沒有 Scene／Prefab 遷移。
- 自然鍵鼠遊玩與獨立程序 Build 未驗，不以合成 NetInput 或離屏圖替代。

## 工作保存

延用 M:/CodexWorktrees/focus-three-slots/Purgers，基線與整合雜湊保留在 .task-baseline/selection-v2。未另建 Library，不清理前版成果。
