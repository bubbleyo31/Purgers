# Tank 預備攻擊與共用動畫幀時序

最後核對日期：2026-10-10（局部核對）。
核對來源：TankMeleeCombo、TankMeleePreparationProgress、TankQuickDashAbility、TankProfessionRuntimeDriver、FirstPersonTankViewModelAnimator、AnimationFrameTiming，以及本次使用者定案。
相關文件：[40 職業](40_職業與特殊能力.md)、[50 武器](50_武器瞄準與射擊.md)、[70 動畫](70_第一人稱模型與動畫.md)、[46 換武器](46_武器獎勵與換裝.md)、[關鍵詞索引](關鍵詞規則索引.md)、[驗證紀錄](Validation/TankPreparation/README.md)。

## 已確認玩法

- atk1／atk2／atk3 各自支援點按與按住。按下先播放前搖；到停格前放開，依該段原有時序繼續到出傷幀。仍按住到停格點才等待放開。
- 停格後放開，無合法衝刺目標則直接跳到出傷幀；若選到可衝刺敵人，動畫從停格點恢復播放，依「出傷幀－停格幀」換算衝刺時間，在出傷幀抵達，詳見 [48 預備衝刺](48_Tank預備衝刺與共用敵人準心.md)。判定一次傷害後完成揮擊與收招。可無限預備，不增加傷害、範圍，不限制移動／視角／一般鈎索。
- 兩個時間分別保留：`ButtonHeldSeconds` 從該次左鍵開始；`PreparedSeconds` 從到達停格點開始。未來特殊能力讀後者，並應以 IsPreparing 與既有權威能力入口判斷，避免收招後留存的時間被當成仍在預備；這次不實作門檻、特殊能力或傷害加成。
- 提早放開是不可逆的本次點按決定。前搖／預備／衝刺／出手／收招期間再次按下左鍵一律丟棄，不預存下一刀，也不能將已放開的當前刀重新轉成預備。
- 整段動畫（含完整 Recovery）結束、回到 Idle 後，才接受新的左鍵按下邊緣。收招中按住跨過完成時點也不自動出手，須放開再按。完成的同一個模擬 Tick 內提前按下仍丟棄；原本當前刀的按住／放開預備維持有效。
- 左鍵仍按住、尚未出傷時，F 成功啟動及扣充能後取消該刀；包括停格前的前搖。沒有充能或其他既有 F 啟動條件不符（空中、控制鎖、衝刺／後僵直等），不取消。已放開的普通攻擊、出傷後與收招不能由此取消。
- F 取消後保留被取消的當前段，清空按住／預備資料，不啟動連段接續逾時。例如取消 atk2，下一次仍是 atk2；原有 F 位移、充能與僵直規則保留。
- 右鍵不能取消預備，也不能在預備／攻擊期間啟動防禦。右鍵若持續按到整刀完成，仍由既有 Hold 型防禦規則處理。
- 攻擊完整完成後才開始原本 Combo 接續期限。atk2 預備再久，完成後仍接 atk3；完成後超時才重設 atk1。
- 死亡、切換武器、Gameplay／AllInput 控制封鎖或被特殊位移中斷，重設攻擊、Combo、兩種時間及強制重擊狀態。一般 Movement-only 鎖不另改近戰規則。
- 基礎出手震動與世界揮擊聲在正式出傷入口播放，揮空也播；按下、停格及取消不播。命中敵人後的命中回饋仍由原傷害回饋流程處理。
- 2026-10-20 局部修正：三段電鋸揮擊聲在整個音檔播放期間跟隨 Player Audio Origin，沿既有 Following World OneShot 路徑；移動／鈎索期間不留在出手位置。出聲時機與權威規則維持上述定義，詳見 [80 聲音系統](80_聲音系統.md)。

## 共用動畫幀座標

`AnimationFrameTiming` 是純資料與換算，不是另一套 Animator、輸入、技能或傷害系統。

### 專案共用規則（2026-10-10 使用者定案）

未來新增或修改玩家武器、玩家技能、敵人等**需要配合動畫的出傷或事件時機**時，統一採用「原始總秒數＋來源 FPS＋播放倍率＋指定幀」配置，沿用共用 `AnimationFrameTiming` 自動換算秒數與動畫位置，不要求使用者自行計算後填入事件秒數。

- Gameplay 與動畫呈現使用同一份幀時序資料；播放倍率只套用一次，避免 Animator／Controller 與 Gameplay 各乘一次造成錯位。
- Inspector 提供繁體中文欄位／提示、換算結果與合法性檢查。各模組清楚命名事件幀及其意義，例如出傷、開始收招或動作完成；不要以含糊的「收招」混用開始與結束。
- 各模組依自身動作定義事件幀及先後順序；共用方法不要求其他技能或敵人具備 Tank 的預備停格、F 取消或 Combo 規則。下方三個事件幀的順序與收招定義屬於 Tank 接入規格。
- 傷害與其他遊戲結果仍由既有 Host／State Authority 流程決定；本地 Animator 或 Animation Event 不另行決定網路傷害。原有冷卻等不依附動畫的計時維持各自責任。
- 這是後續相關功能工作的正式配置規則。本次實作僅接入 Tank；既有其他系統尚未遷移，日後修改時再於明確範圍內接入，保留序列化相容性及人工設定。
- 使用者已確認採用此方法作為共用規則；這項定案不代表其他系統已完成實作或驗證。各系統接入後仍需各自驗證權威時序、動畫呈現及中斷清理。

### 座標與 Tank 事件定義

- `ClipDuration`：原始 Clip 在 1 倍速下的總秒數。
- `FramesPerSecond`：來源動畫 FPS，不是遊戲渲染 FPS。
- `PlaybackSpeed`：實際播放倍率，大於 0；2 表示兩倍速。
- 指定幀採 **0 起算**，可填小數幀；總幀數 = 原始秒數 × FPS。
- 事件秒數 = 幀 ÷ FPS ÷ 倍率；normalizedTime = 幀 ÷ 總幀數。
- 有效條件：有限正數的長度、FPS、倍率，以及 `0 ≤ 預備幀 ≤ 出傷幀 ≤ 開始收招幀 ≤ 總幀數`。
- 出傷幀 → 開始收招幀為 Active；開始收招幀 → 動畫總長為 Recovery。**收招幀表示開始收招，不是整刀完成**。整刀完成時間由原始總長／倍率決定。
- Inspector 會直接顯示換算後的預備、出傷、開始收招、完成秒數。無效配置會顯示錯誤並拒絕啟動該段，不默默退回舊秒數。

範例（只供理解，不是本專案推薦幀位）：原始 2 秒、30 FPS、2 倍速，預備 6、出傷 18、收招 30 → 預備 0.1 秒、出傷 0.3 秒、收招 0.5 秒、完成 1 秒。預備等待時間額外插入；無衝刺目標時放開跳至 18 幀；有衝刺目標則依 [48](48_Tank預備衝刺與共用敵人準心.md) 繼續播放 6 至 18 幀並接近敵人。

## Tank 手動設定

本輪不更動 Scene／Prefab／Controller 的人工設定。新欄位預設未啟用，舊秒數及序列化欄位保留；**需要手動啟用各段，該段才使用幀模式與按住預備**。新幀資料的程式初值 1 秒／24 FPS／1 倍／三個幀皆 0 只是占位，必須填入自己的實際設定。

1. 開啟 `Assets/Prefabs/ProfessionRuntime/TankProfessionRuntime.prefab`，選根物件的 **Tank Melee Combo**。
2. 分別展開 **Light Attack 1／Light Attack 2／Heavy Attack**，勾選 **啟用幀時序與按住預備**。
3. 填入原始總長、來源 FPS、播放倍率、預備停格幀、出傷幀、開始收招幀。其餘傷害、距離、角度保持原本人工值。
4. 檢查下方自動換算與合法性提示。幀模式會隱藏舊的三個秒數欄位，但不清除數值；取消幀模式仍使用原資料。
5. 第一人稱 Tank ViewModel 的 **First Person Tank View Model Animator** 可配置三段與 Idle 的完整 State 路徑。現行預設為 `Tank_Base Layer.attack1`、`Tank_Base Layer.attack2`、`Tank_Base Layer.attack3_001`、`Tank_Base Layer.idle`。只有 Controller 名稱不同才需改。
6. 幀模式直接以同一份 Gameplay 時間座標取樣 Animator，**只使用上述播放倍率，不再乘 Controller State Speed 或 Animator.speed**。其他 F／Guard／Appear 動畫仍由原 Trigger／Bool 控制；攻擊完成、取消、解除綁定及停用時恢復原 Animator.speed。
7. 不修改 Animator Controller 的既有倍率，不手動新增傷害 Animation Event。幀取樣不依賴動畫事件；需要揮擊音效／傷害效果時沿用正式 Active 回饋入口。
8. 儲存自己的 Prefab 設定後，重新生成 Runtime；Play Mode 中修改 Clone 不會保存回 Prefab。

### 本次讀取的既有資產參考值

以下是改動前透過 Unity Editor 讀到的現值，僅作配置對照；不是已遷移的新幀設定。

| 段數 | Clip 名稱 | 原始總長 | FPS | Controller Speed | 舊 Startup／Active／Recovery |
|---|---|---|---|---|---|
| atk1 | attack1 | 約 1.04166675 秒 | 24 | 2.5 | 0.09／0／0.328 秒 |
| atk2 | attack2 | 約 1.04166675 秒 | 24 | 2.5 | 0.09／0／0.328 秒 |
| atk3 | attack3.001 | 約 1.04166675 秒 | 24 | 0.8 | 0.271／0.271／0.72 秒 |

2026-10-10 連點修正：`Allow Attack Input Buffer` 已停用並隱藏，舊序列化值只保留相容性，無論原值 true／false 都不能預存下一刀；不需手動調 Prefab。Combo Reset Duration 保留人工值，只有完整攻擊結束後才開始倒數。

來源：上述 Tank Runtime Prefab 與 `Assets/_Project_Assets/Models/Player/Profession/Tank/Tank.controller`。新模式依你填入的幀重新算時序；若填不同於舊時序的幀，點按節奏也會跟著新設定改變。

連點修正的原始重現與驗證見 [TankInputLock](Validation/TankInputLock/README.md)。

## 責任與網路

- `TankMeleeCombo` 仍擁有段數、傷害、聲音與出手回饋。新增 `Preparing = 4`，保留其他列舉值；既有定義與秒數欄位不移除／改名。
- `TankMeleePreparationProgress` 是可重播的 Networked 前搖／停格狀態。每個 Fusion Tick 計算兩種時間，跨過停格點的當 Tick 剩餘部分算入預備；誤差以 Tick 粒度為界。完成後可讀取最後一次計時，下一次攻擊或中斷清除。
- `SwingSequence` 記錄正式出手，`InterruptedSequence` 記錄中斷；實際傷害仍只能由 State Authority 的既有 `MeleeDamageResolver` 執行。Client／Input Authority 可做既有預測與本地震動。
- `IPlayerQuickActionInputContext` 為可選接口，由共用 F Controller 傳入該 Tick 經過濾的 NetInput，讓 Tank 分辨當 Tick 是否仍按住左鍵。共用 Controller 不增加 Tank 類型分支。
- F 仍通過 ActionGate 與原能力條件，只有 `ConsumeCharge()` 成功後才呼叫取消；不因失敗 F 丟失預備。
- ViewModel 只讀 Gameplay 時間與段數，依設定 State 取樣。資料尚未 Spawn 不讀 Networked 欄位，晚綁定能呈現目前停格，解除來源時恢復動畫速度。

## 手動驗收

逐段檢查點按不提早出傷、長按在自選幀停住、放開跳到出傷幀，敵人只受一次傷害；揮空仍有一次基礎出手震動。預備時移動及轉向應正常，右鍵不能格擋；有充能 F 取消且下刀同段，無充能 F 保持預備。atk2 長按超過 Combo 接續期限後釋放，仍應接 atk3；整刀完成後放置超時則回 atk1。檢查攻擊中快速連點只出一刀、收招中按住跨過結束不自動出刀、回到 Idle 放開再按可接下一段，以及死亡與換武器，確認不殘留停格／按鍵／震動。

Host／Client 分別操作，並做預備期間晚加入及完成後重連；確認傷害歸屬、每端動畫與本地震動。自動換算／受控測試不等於使用者配置後的最終動畫手感驗收。實際執行結果見驗證紀錄。


## 2026-10-16 成長獎勵與技能掉落局部核對

近戰攻速每次起刀取樣 SwingSpeedSnapshot，前搖進度與出手／收招按同一速度換算，停格蓄力仍累積真實秒數。 詳見 [168 角色成長、被動與掉落](168_角色成長被動與技能掉落.md)。

實作／資產與測試證據分開列於 168，未宣稱獨立多程序聯機或 Build 驗收。
