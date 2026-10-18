# 三種雷射四階段視聽驗證

最後核對日期：2026-10-18（局部）。正式規則與 Inspector：[108](../../108_雷射四階段視聽呈現.md)。

## 範圍與證據

正式配置三個 Prefab：Enemy_Ranged_B、Enemy_Ranged_C_Beam、PiercingCannon。只加入呈現參數、必要發射快照、等價前搖比例與材質；原傷害、遮罩、總前搖及顯示秒數保留。Scene 未保存或修改。四個 GameplayAudioCue 接口均留空，沒有擅自選用其他技能音檔。

- 紅燈：新規則未實作時，MCP job `bab34d3983694fa28ee15a97be7d3f6d` 實際 completed=4，四項預期失敗；該輪 result=null，不當作完整 RunFinished 結果。更早 total=0 嘗試作廢。
- 最終聚焦：[focused.xml](focused.xml)、[focused.txt](focused.txt)，**33 通過、0 失敗**（雷射相關 26 ＋既有世界循環聲 7）。包含寬度／色彩與固定端點、收細至零、粒子單次與上限、不改全域 Random、首次快照與回滾不補播、替換 VFX 的預告保留與單次事件、三個 Prefab 接線／無 Missing Scripts、既有連續瞄準與定點敵人規則。
- 測試中曾遇 LineRenderer Color32 量化差異，斷言改為合理 0.005 容差；正式 HDR 材質另外使用 MaterialPropertyBlock 的 Tint，避免以 Color32 截斷 HDR。
- 受控同 Editor Host／Client：[host-client.txt](host-client.txt)。State Authority 在 Host、Input Authority 在 Client；以 Host FixedUpdateNetwork 呼叫原技能模擬，玩家可改前搖瞄準、實際發射採最後方向、兩端一次粒子與準備／發射音、換段停止循環音、光束收細與固定端點、取消、Despawn、斷線重連不補播已過瞬間事件均通過。敵人 B／C 由原 StartOption／TickOption 受控驅動，兩端粗光束／單次粒子／固定端點／清理均通過。
- 多人探測只延長臨時實例的前搖及發射窗口，敵人傷害暫設 0，並暫停自然 AI 選招；沒有保存資產。使用音量 0 的臨時靜音 Cue 驗證 API 路徑與 handle，**不代表音檔聽感驗收**。並未測自然鍵鼠輸入經網路送達 Host。
- 初次多人失敗：[host-client-initial.txt](host-client-initial.txt)，重連時已超過測試用 15 秒前搖，等待 Aim 逾時。將該情境的臨時前搖延至 90 秒後重跑成功；正式 Cast Delay 未改。此受控通過發生於最後 custom VFX 去重與粒子 HDR 呈現收尾前，收尾及音效介面相容修復由最終聚焦、音量探測與離屏圖確認，未再重跑完整連線情境。

## 正式材質離屏圖

[unity-offscreen-stages.png](unity-offscreen-stages.png) 使用共用 Runtime、正式 BeamEnergy／BeamSparks 材質及實際 ParticleSystem，獨立 Preview Scene Camera.Render；已檢視圖像。

上排左至右：細線／中線／正式發射；下排：粗細剩 45%／8%／完全隱藏。粒子有獨立短尾跡。沒有場景背景或 Bloom，不等於最終遊戲畫面／使用者美術驗收。全程沒有開啟或聚焦 EditorWindow。

## 待驗邊界

- 正式音檔選擇、3D 衰減與實際聽感。
- 三種雷射在正式關卡、自然玩家操作與 AI 交戰中的可讀性；特別是原敵人 0.08 秒、玩家 0.15 秒發射窗口是否需要再延長。
- 獨立程序／跨機器 Host／Client、Build、封包遺失下的極短階段採樣，以及自然晚加入／斷線重連。
- 自訂 ParticleSystem 子系統的成本與自訂 fireVfx 的內部動畫不在內建參數保證範圍。

背景驗證沿既有 MCP，已核對 PlayUnfocused／Run In Background，未操作桌面鍵鼠或呼叫 Focus／Show。

## 音效相容性修復

第一次取得完整 RunFinished 的回歸為 1154 項：1141 通過、11 失敗、2 Explicit 略過，見 [原始失敗](regression-audio-compatibility-failure.xml)。其中七項 WorldAudioLoopTests 是本次新增可選參數造成舊四參數反射呼叫失配，已保留原介面並以不同名稱 StartWorldLoopWithVolume 共用原集區實作修正；沒有放寬或改寫既有測試。修復後雷射相關 26 ＋ WorldAudioLoopTests 7 聚焦全數通過。[音量探測](audio-volume.txt) 另確認 0.25 倍音量實際套用、舊入口重用同聲源恢復音量 1。

## 最終完整回歸與收尾

[regression.xml](regression.xml)、[regression.txt](regression.txt)：**1154 項，1148 通過、4 失敗、2 Explicit 略過**。結果來自 2026-10-18 14:12 的新 RunFinished；MCP job `23b6b25b502f4b6ba729c37794b8b5dd` 因 Domain Reload 留下舊進度，在確認結果檔、Editor idle 後才清除 orphan，不將舊的 completed=34／result=null 當成功。

四項失敗均已在接管前的 [AbilitySlotStates](../AbilitySlotStates/README.md)／TeamInfo 紀錄存在：

- BossStageAssetTests.GameSceneContainsBossAssetWiring：MapChunk_Boss 引用不符。
- EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules：期望 0.5，實際 0.25。
- EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners：期望大於 10，實際 10。
- ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt：NullReferenceException。

未改動這四項測試或其範圍外資產，不宣稱完整套件全綠。

[editor-final.txt](editor-final.txt)：編譯完成、播放停止、Runner=0、_Menu dirty=false、Shader 訊息=0。最後 Console 無 error，僅手動清除已完成 orphan job 的工具 warning。三個 Prefab 接線／無 Missing Scripts 已由聚焦測試檢查；未做 Build 或自然遊玩驗收。
