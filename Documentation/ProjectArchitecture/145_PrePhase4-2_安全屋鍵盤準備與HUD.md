> **2026-10-18 TAB 共鍵（局部）**：SafeHouseHudController 透過 LocalTeamInfoPanel.ReadyPressed 共鍵仲裁；ReadyCheck／Countdown 只準備／取消準備。開始投票時關資訊窗，原本按住不視為新準備，結束投票不自動重開。提示沿 PlayerBinding.Ready。詳見 [106](106_TAB隊伍資訊窗.md)。

# Phase 4 前置-2：安全屋鍵盤 Ready Check 與左上 HUD

> 最後核對：2026-10-10（局部；安全屋出發與投票計時）
> 核對來源：`SafeHouseFlowController`、`SafeHouseHudController`、`SafeHouseReadyRules`、`PlayerControlLocks`、`SafeHouse.unity`  
> 相關文件：[Phase 2 安全屋](142_Phase2_安全屋灰盒配置與驗證.md)、[Phase 4 前置-1 共用控制](144_PrePhase4-1_共用控制與轉場.md)

## 行為

1. Host 仍需靠近開始裝置按 E，State Authority 重新驗證 Host 身分與距離後開啟 Ready Check。
2. Ready Check 開啟後，各端按 TAB 切換自己的準備／取消準備；滑鼠按鈕只保留為可選操作。
3. 一次投票預設維持 7 秒，由 `readyCheckTimeoutSeconds` 可調。逾時時 State Authority 清除所有準備狀態並回到 `WaitingForHost`；Host 必須重新靠近按 E 才能再次開始。
4. 投票期間任何在線玩家按 ESC 都能取消整次投票；Client 經 `RPC_RequestCancelReadyCheck` 請求，只有 State Authority 能真正關閉投票。
5. Client 不直接寫入 `ReadyByPlayer`。Client 送出 `RPC_RequestToggleReady`，State Authority 只使用 Fusion 提供的 `RpcInfo.Source`，並驗證玩家仍在線及 Phase 可接受切換。
6. 每位在線玩家在 HUD 有一顆 LED：綠色亮燈為已準備，暗色為未準備；同時顯示已準備與未準備人數。未全員準備時顯示投票剩餘時間；全員準備後只顯示出發倒數。
7. 全員準備後，Host State Authority 建立網路 `TickTimer` 並進入 `Countdown`，出發倒數預設／目前場景皆為 1 秒，同時保存剩餘投票 Tick 並停止投票 Timer；同 Tick 全員準備優先於投票逾時。任何玩家取消準備或新玩家加入，Authority 清除出發 Timer、恢復原投票剩餘 Tick 並回到 `ReadyCheck`。ESC 仍取消整次投票；投票逾時僅在 `ReadyCheck` 生效。
8. 倒數完成後才進入 `LoadingStage`，沿用 `GameLogic.PrepareForAuthoritativeSceneTransition` 與 Fusion Scene Authority 切場。
9. Ready HUD 位於 SafeHouse HUD 左上第三列；Phase 4 前置-3 已遷移至 `TopLeftColumn/ReadyCheckSlot/ReadyCheckPanel`，尺寸與位置以 [146](146_PrePhase4-3_HUD排列與資訊精簡.md) 為準，不再使用初版 `(20, -200)` 的直接 Canvas 定位。

## 輸入與表現責任

```text
本機 TAB
→ SafeHouseHudController（僅採集意圖；AllInput 時拒絕）
→ SafeHouseFlowController.RequestToggleLocalReady
├─ Host／State Authority：直接驗證與切換自身 NetworkDictionary 項目
└─ Client：RPC_RequestToggleReady → State Authority 驗證與切換
→ Render 讀取 ReadyByPlayer／Countdown TickTimer
→ LED、已準備／未準備數、投票與出發倒數文字

本機 ESC
→ SafeHouseHudController（投票顯示時採集取消意圖；AllInput 時拒絕）
→ SafeHouseFlowController.RequestCancelLocalReadyCheck
├─ Host／State Authority：直接驗證並取消整次投票
└─ Client：RPC_RequestCancelReadyCheck → State Authority 驗證並取消整次投票
```

Ready HUD 顯示期間**不取得任何 `LocalPlayerControl` 票證**：玩家仍可移動、觀看、攻擊與操作既有介面。它只在既有 Phase 4 前置-1 `AllInput` 轉場鎖存在時拒絕 TAB／ESC 意圖，不改動 Phase 4 前置-1 的鎖定或 Photon Fusion connect UI 過場順序。

## Networked schema

- `ReadyByPlayer`：既有 `NetworkDictionary<PlayerRef, NetworkBool>`，在線玩家加入為未準備、離線時移除。
- `StageStartCountdown`：新增 `TickTimer`，只由 State Authority 建立、取消與清除。
- `ReadyCheckTimeout`：State Authority 開票時建立的 `TickTimer`；預設 7 秒且可由 `readyCheckTimeoutSeconds` 調整，全員準備後設為 None。
- `PausedReadyCheckTicks`：新增同步 int；State Authority 保存全員準備當下的剩餘 Tick。取消出發時用同一份餘額恢復 Timer；開新票、整票取消與切場時清零。Client 只讀，不增加 RPC。
- `SafeHousePhase.Countdown`：全員準備後、正式載入前的獨立 Phase；保留既有 enum 數值，新增值附加為 3。

## 2026-09-18 歷史驗證紀錄（3 秒版本）

- TDD：新增整次投票取消權限與逾時規則案例，完成 RED → GREEN。
- `PurgersRegression` EditMode：59／59 通過，job `d3dc55e21bd748cc99277204c05f1c8b`；完整 EditMode 65／65 通過，job `2815b071619347aab00d7d407679849b`。
- Windows Development Build：最終乾淨建置 `build-a3d47159ab` 為 0 errors、0 warnings。
- Editor Host＋獨立 Windows Client 實測同一 Session：7 秒自動取消、Client 整票取消 RPC、Client Ready RPC、一般取消、全員 Ready 啟動 3 秒倒數、倒數中 Client 取消、重新 Ready、兩端切入 Game 均通過。
- Host／Client 兩端均讀回：2 位玩家對應 2 顆 LED、已準備／未準備數與投票秒數正確、左上第三列位置正確，且 Ready HUD 互動期間 `LocalPlayerControl` mask 為 `None`。
- 另以真正 `FusionMenuUIMain` Quick Play 路徑查核 Phase 4 前置-1：connect UI 顯示時 `LocalSceneTransition` fade alpha 全程維持 0，connect UI 關閉後才恢復場景 fade，結果 PASS。
- Client Player log 搜尋 `Exception|Error|Assert|FAIL` 為 0；Editor Console Error／Warning 為 0。

證據：[Host.txt](Validation/Phase4-2/Host.txt)、[Client.txt](Validation/Phase4-2/Client.txt)、[MenuConnectAnimation.txt](Validation/Phase4-2/MenuConnectAnimation.txt)、[MenuConnectAnimation.png](Validation/Phase4-2/MenuConnectAnimation.png)、[verification.txt](Validation/Phase4-2/verification.txt)。驗證器直接呼叫與 TAB／ESC 相同的本機入口，以隔離鍵盤硬體注入差異；實體鍵仍應依下列清單複驗。

## 手動複驗

1. Editor Host 與獨立 Client 進入 SafeHouse，Host 按 E。
2. 兩端 Ready HUD 都位於左上第三列，LED 數等於在線玩家數，初始皆熄燈，顯示已準備 0、未準備 2 與約 7 秒投票時間；同時確認移動、觀看及攻擊未被鎖定。
3. 不操作直到 7 秒結束；兩端投票 UI 都關閉，Host 必須再按 E 才重新出現。
4. 重新開始投票，Client 按 ESC；兩端投票 UI 都立即關閉。
5. 再次開始投票，Client 按 TAB：兩端變成一顆亮燈／一顆熄燈與 1／1；再按 TAB 回到 0／2。
6. Host 與 Client 依序按 TAB；兩端顯示約 1 秒出發倒數，原投票秒數不再顯示、計時暫停。
7. 倒數中任一端按 TAB，兩端立即回到 Ready Check，場景不可載入；倒數中按 ESC 則整次投票關閉。
8. 重新開票並全員準備；倒數完成後兩端進入 Game。
9. 從 Menu 的 Quick Play 再進一次 SafeHouse，確認 Photon Fusion connect 動畫完整顯示，動畫結束後才接手本機場景 fade。

## 2026-10-10 設定與驗證

- 設定位置：`Assets/Scenes/SafeHouse.unity > SafeHouseFlow > SafeHouseFlowController`。
- `Countdown Duration Seconds`：目前 **1 秒**，C# 預設 1，最小有效值 0.1；全員準備後的出發等待。透過 Unity Editor 將場景既有 3 改為 1，其他序列化值未改。
- `Ready Check Timeout Seconds`：目前／預設 **7 秒**，最小有效值 0.1；只累計未全員準備的投票時間。取消出發恢復剩餘時間，並非重給 7 秒；多次取消不會增加預算。
- 暫停餘額以 Fusion Tick 同步，秒數 = 剩餘 Tick × Runner.DeltaTime；兩計時器均不更動 Time.timeScale。Host 新開票／新開始出發時讀取各自設定，不寫存檔。
- 修改操作：在上述 Inspector 修改秒數、儲存 SafeHouse 場景，再用新 Host／Client 場次驗收。若要本次需求，出發值保持 1。
- 複驗：讓最後一人在投票剩餘不到 1 秒時準備，確認仍完成完整 1 秒出發且兩端進入 Game；倒數中取消準備時恢復剩餘投票秒數。再測 ESC、晚加入／重連加入為未準備，以及未全員準備的自然逾時。
- 本輪測試及多人證據邊界見 [安全屋出發驗證](Validation/SafeHouseReady/README.md)。歷史 3 秒版本證據不代表本輪實測。
## 2026-10-13 安全屋 Q／E 限制、玩家傷害與完整玩家 HUD

最後核對日期：2026-10-13（本節局部）。
核對來源：SafeHouseFlowController、SafeHouseGameplayRules、PlayerActionGate、Player、PlayerAbilityRuntimeManager、PlayerActiveAbilityBase、DamageReceiverUtility、PlayerHealth、SafeHouseHudController、LocalPlayerBattleAbilityHUD、LocalPlayerRewardHUD。

### 已確認規則

- 安全屋整段期間（含等待、投票與出發倒數）禁止 Q 鈎索、E 主動技能；拒絕按鍵不消耗鈎索能量、不啟動技能冷卻。Q／E 槽位保留，使用既有禁用底色，鈎索另保留不可用標記。
- 一般射擊、近戰、瞄準、換彈、F 武器動作、移動與視角照常。終端機的 E 互動、TAB／ESC 投票、聊天與 ALT 選獎勵保留；轉場與其他原有封鎖仍優先。
- 玩家對玩家傷害與自傷皆為零，不扣 HP 或護盾，涵蓋子彈、近戰、技能、爆炸、持續傷害。判定依玩家歸屬，不以 DamageType 白名單決定；未歸屬玩家的敵人／環境傷害仍走既有規則，本次沒有把安全屋改成全面無敵。
- 顯示真實等級／經驗、待選獎勵、已裝備能力、血量／護盾、武器／彈藥資訊。安全屋原有地圖與準備資訊保留，不顯示假的關卡倒數或撤離提示。
- 正式關卡恢復原規則；移除安全屋限制不得清除暈眩、捕食、死亡等其他來源。

### 實作責任

`SafeHouseFlowController` 在 Spawned／Despawned 維護按 Runner 分隔的活動安全屋索引；不依赖 ActiveScene 名稱，也不新增 Networked schema。這兼容 Fusion 將 Scene 物件搬到 Runner Scene，以及晚加入／重連。

`PlayerActionGate.CurrentBlockedActions` 合併安全屋的 Grapple 與新增 ActiveAbility（bit 7）遮罩，與 QuickAction（F）分開。尚未 Spawn 的 Gate 回傳 None，避免 HUD 讀取未初始化的 Networked 欄位。Player 與 AbilityRuntimeManager 分別保護職業及獨立新舊 E 路由；PlayerActiveAbilityBase 的可用性與直接施放入口也遵守 Gate。

`PlayerHealth.RejectsSafeHousePlayerDamage` 提供唯一玩家來源政策查詢。`DamageReceiverUtility.TryApplyDamage` 在子彈時間延後前導向拒絕；`TryResolvePreparedDamage` 在目標 Modifier／格擋／護盾之前再次查詢，回傳 FriendlyFireBlocked 並保留既有來源結果通知。直接 `PlayerHealth.ReceiveDamage` 同樣拒絕。來源首選 DamageRequest.Attacker，即使來源物件已消失仍保有玩家歸屬；有效來源 NetworkObject 的 InputAuthority 或來源物件父階層 Player 為補充。未填任何歸屬的自訂傷害來源必須修正其 DamageRequest，不能憑 DamageType 猜測來源。

HUD 沿用既有 Prefab 子樹。`SafeHouseHudController.HudRunner` 提供當前有效本機 Runner，`LocalPlayerBattleAbilityHUD` 與 `LocalPlayerRewardHUD` 優先使用父層 SafeHouseHudController，再沿用原 StageHudController 路徑。子 HUD 不另取得 StageHudLifetimeRegistry ownership；切場隨安全屋主 HUD 清理。Reward HUD 繼續只讀 GameLogic 同步狀態，ALT 領取仍走既有權威管線。

### 正式場景配置（2026-10-13 已依授權自動接線）

已將 StageHUD 的 ReferenceFrame（只含 BattleAbilityHUD／Phase6RewardHud）接到 SafeHouse.unity 的 SafeHouseHUD，保留原安全屋 TopLeftColumn。Unity Editor 儲存後重開確認引用仍完整；以下步驟保留供日後重建／核對，現在不需再手動配置。

1. 停止 Play Mode，開啟 `Assets/Scenes/SafeHouse.unity`，保留既有 `SafeHouseHUD`、SafeHouseHudController、LocalMinimapController 與 TopLeftColumn／投票引用。
2. 從 `Assets/Prefabs/UI/StageHUD.prefab` 複製完整 `ReferenceFrame` 至 `SafeHouseHUD` 子層。保留它的 RectTransform 與 BattleHudReferenceFrame，讓既有 1920×1080 版型等比縮放。
3. 在這份新複製的 ReferenceFrame **只保留 BattleAbilityHUD 與 Phase6RewardHud 子樹**，移除副本內的 TopLeftColumn 等關卡／小地圖節點；不要刪除原安全屋的 TopLeftColumn。不要複製 StageHUD 最上層 Canvas、StageHudController 或 LocalMinimapController，否則會與安全屋爭奪 HUD ownership。
4. 核對 BattleAbilityHUD 的槽位模板、Q 能量填色／禁用標记與中央技能條引用；核對 Phase6RewardHud 的經驗、等級、ALT 提示、三張候選卡及 Catalog 引用。整組複製應保留內部引用，不能只新增空 Component。Reward HUD 的 Runner 可留空，它會使用父層安全屋的 HudRunner。
5. `_Menu/GameplayHUD Canvas/PlayerHUDCanvas` 的既有血量、護盾、武器／彈藥 UI 沿用，不複製第二組。
6. 儲存你確認的安全屋配置，從正常選單進入安全屋驗收。單獨開 SafeHouse Scene 按 Play 不代表已建立正式 Runner／GameLogic。

### 人工驗收

- 安全屋內按 Q／E：不施放，能量與冷卻不因拒絕按鍵改變；槽位仍可見且禁用。所有已裝備 E（包含舊空中能力）適用。
- 射擊、一般近戰、ADS、R、F、移動照常；玩家互射／互砍及自爆不扣生命或護盾，也不顯示有效傷害回饋。
- 等級、XP、武器及血量資料與目前玩家一致；有待選獎勵時 ALT 可顯示並領取，裝備後的 Q／E 仍禁用。
- Host 靠近終端機按 E 仍能發起投票；TAB／ESC、投票剩餘時間和全員準備後 1 秒出發沿用原規則。
- 安全屋→Game→安全屋、晚加入及重連：沒有重複 HUD、錯誤玩家資料或殘留限制。正式關卡恢復原有友傷／自傷政策。

實際測試結果、測試場景與人工驗證邊界見 [SafeHouseGameplay 驗證紀錄](Validation/SafeHouseGameplay/README.md)。正式場景已接線；Single Runner Play Mode 已確認 HUD、禁用 Q／E、XP 更新及 ALT 提示。自然鍵鼠領獎、多人切場與最終視覺接受仍依驗證紀錄區分。

## 2026-10-17 安全屋小地圖與投票外觀（第一張預覽）

最後核對日期：2026-10-17（左上 UI 局部核對）。
核對來源：SafeHouseHudController、SafeHouseHudStyleSetup、SafeHouse.unity。

使用者指定第一張預覽：切角小地圖外框、深橄欖資訊／投票底板、細金邊、米白文字與獨立 TAB／ESC 鍵帽。後兩張圓角修訂不作此次定案來源。沿用 MenuBrushV1/UtilityPlate.png、NotoSansTC-Bold HUD SDF 與 ApprovedV2/SkillSquare.png；不從 AI 圖裁切素材。

僅調整 SafeHouseHUD 原 TopLeftColumn，移至既有 ReferenceFrame；同層 BattleAbilityHUD、Phase6RewardHud、其他玩家 HUD 與 Game StageHUD 均不重排。根 Canvas／CanvasScaler 不變，避免更動其他 HUD 或展開地圖。局部工具對範圍外場景元件的序列化值進行前後比對。

SafeHouseHudController 新增可選 Ready Timer Label。接線後秒數獨立呈現，狀態列僅顯示準備／未準備人數；準備按鈕文字只顯示「準備／取消準備」，鍵帽由場景提供。未接線時保留舊合併文字，沒有更名或刪除原引用。新增文字會在 Disable／切場清理。LED 仍按在線玩家的權威準備狀態顯示，綠色實心／灰白空心區分；多人時自動縮字以保留每位玩家的燈號。

鍵盤與準備按鈕仍走原請求入口；ESC 鍵帽是提示，沒有新增滑鼠取消入口。沒有新增 RPC、Networked schema、狀態機或本機倒數。7 秒投票、全員準備後 1 秒出發、投票時間暫停／恢復與取消規則不變。小地圖沿用安全屋 Cartography、35／55 公尺、M 展開 600×600、原玩家／隊友標記與生命週期；不修改 LocalMinimapController 或圖資。

### Inspector 與後續人工調整

路徑：Assets/Scenes/SafeHouse.unity > SafeHouseHUD > ReferenceFrame > TopLeftColumn。以下位置以父層左上為原點，向下列為正值，Unity anchoredPosition.y 為負。

| 物件 | 位置／尺寸 | 作用 |
|---|---|---|
| TopLeftColumn | (45.47,40.60)，522.14×575 | 整組安全邊距；隨既有 ReferenceFrame 等比縮放 |
| MinimapSlot | (0,0)，247.67×252.54 | UtilityPlate 切角底板；Mask 裁切，保留外框 |
| MinimapContent | 四邊內縮 20，伸展錨點 | 沿用原控制器引用；只影響收合呈現，世界視野不變 |
| StageLevelPanel | (0,264.98)，522.14×99.88 | 現有關卡／任務文字，沒有加入撤離或假計時 |
| ReadyCheckSlot／ReadyCheckPanel | (0,376.86)，522.14×190 | ReadyCheck／Countdown 期間顯示 |
| VoteTimer | 面板內 (350,18)，150×38，25 字級 | 指定至主控制器 Ready Timer Label；右對齊，只讀權威秒數 |
| Title | 面板內 (22,18)，322×38，12～25 自動字級 | 依在線人數顯示燈號，最多仍由既有權威容量決定 |
| Status | 面板內 (22,69)，478×44，25 字級 | 準備／未準備人數；字框高於字型 36.21 行高，避免垂直裁切 |
| ReadyButton | 面板內 (22,136)，188×40 | 原按鈕引用／Click 保留；TAB 鍵帽與文字分開 |
| CancelKeycap／CancelHint | 面板內 (216,136)／(310,136) | 靜態 ESC 取消投票提示，隨面板一起隱藏 |

需要調整色彩時，先修改這些物件的 Image／TMP；共用 Sprite 不應直接改檔，避免影響其他 UI。一般調整不要重跑作者工具；它遇到既有 Ready Timer Label 接線會拒絕套用，防止覆蓋人工值。

已透過 Editor 套用並儲存正式 SafeHouse 場景。驗證狀態另見 [驗證紀錄](Validation/SafeHouseStyle/README.md)；隔離 Unity UI 渲染不等於自然多人遊玩畫面。

## 2026-10-18 ESC 與玩家設定（正式整合／局部核對）

2026-10-18 使用者明確授權後，已接入正式主選單與遊戲 ESC。Q／E 群組改鍵、投票優先、每玩家模式與本機保存沿 [104 完整規則](104_ESC選單與玩家設定.md)；實際責任入口、資產及驗證邊界見 [正式整合紀錄](Validation/PlayerSettings/README.md)。舊固定鍵名僅表示初始綁定，舊 ESC／Enter 切換游標與第二次跑步鍵不取消的描述已被取代。