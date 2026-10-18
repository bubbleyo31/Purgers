# 鈎索尖刺 V2 Icon 與獎勵池接線

日期：2026-10-14（本輪資產接線與 EditMode 驗證）。
使用者已確認第二版圖示，並授權套用及加入獎勵池。

## 已完成

- 原始核准 PNG 位於 deliverables/GrappleSpikeIcon_v2/GrappleSpike_v2.png，正式圖檔為 Assets/_Project_Assets/UI/GrappleAbilityIcons/05_GrappleHit_Spike.png；兩者 SHA256 相同，沒有重畫或修改像素。
- Unity Editor 匯入 Single Sprite，Alpha Is Transparency 開啟、Mipmap 關閉、最大尺寸 1024、Uncompressed。Definition.HudIcon 指向正式 Sprite；Reward.HudIcon 透過原 Definition 取得相同 Sprite。
- 新增 Reward_GrappleSpike.asset（Assets/_Project_Assets/Data/Progression）：ID grapple.spike，GrappleHit，最低等級 1、權重 1、Repeatable。
- 正式 Resources/Progression/PlayerRewardCatalog 僅追加一項，從 17 增至 18。before.json 與 final-audit.json 核對原 17 項順序、引用及序列化值不變。
- 原有兩項 Catalog／WeaponReward 測試的項目數更新為 18 獎勵／15 技能；未修改 Runtime Gameplay。
- 現有 Scene 檔案 SHA256 與本輪前一致；_Menu 最終無未儲存變更，未自動裝備玩家。

## 驗證

| 項目 | 結果 | 證據 |
|---|---|---|
| 前置失敗 | 1 項；期望 18、實際 17 | red.json |
| 最終聚焦 | 49 Passed、0 Failed | focused.xml、focused-summary.txt |
| 完整 PurgersRegression | 891 Passed、1 Failed、1 Skipped，實際共 893 | regression.xml、regression-summary.txt |
| 資產／純抽選核對 | 原項保留、圖示共用、Lv.1 可候選、換掉後可重抽、裝備排除通過 | final-audit.json |
| 獎勵卡版面 | 三張原有說明框皆容納新說明，標題通過 | ApprovedHudVisualTests，包含於聚焦與全回歸 |
| 編譯 | 測試來源更新後編譯完成，最終 Editor 非編譯／非 Play Mode | editor-final.json |
| Scene 檔案 | 與本輪前 SHA256 一致，改動數 0 | scene-hashes-before.json |

唯一失敗為既有 BossStageAssetTests.GameSceneContainsBossAssetWiring（Boss Prefab 引用比較）；未改動該 Scene。Skipped 為 Explicit 的 AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState，需要另行 Photon 多人驗證。

MCP 全回歸 job 在 Domain Reload 後仍顯示 completed=34／running；最終採 TestRunner RunFinished 回呼輸出的 XML，沒有把 stale job 或零測試數當作通過。確認 Editor.log 已 RunFinished 後清除失效的 MCP job 狀態。

第一次套用使用 Undo，Test Runner 的 PerformUndoTask 回復同群組導致 Editor 記憶體暫回舊值，磁碟仍有已存的新引用。已改以針對目標資產的 SerializedObject.ApplyModifiedPropertiesWithoutUndo 重新套用，並切開 Undo 群組；最終全回歸結束後重新查找成功，圖示、18 項池與原項保留均再次通過。卡片原說明超高，已縮短文案後通過三框檢查；未變更字級、卡片結構或技能規則。

Console 的 MCP 最後讀取僅回報清除 stale job 的工具警告；此查詢可能受 Console 篩選影響，因此不宣稱全 Console 零錯誤。未見本次新增 C# 編譯錯誤；全回歸既有失敗另列如上。

## 尚未由本輪證明

未做自然 F8／ALT 選卡後的完整遊戲流程、獨立 Host／Client、舊存檔晚加入或 Build 驗證。原技能受控多人證據見上層紀錄；不能以它代替本次獎勵流程實戰驗收。既有候選不因入池自動重抽，需後續新候選才可能抽到。

