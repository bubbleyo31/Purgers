# 《除草機》ChatGPT 專案架構快速讀取

> **2026-10-04 已確認 HUD V2**：獎勵保留原有三框／六角／折角結構，改米白底與淡金折角；Q／E 縮小方形。生命與護盾每 20 點累積增減一整格，淡金護盾緊接可見生命格，同一水平基線。新版規格、設定與驗證以 [167](167_已確認HUD美術與整格血量.md) 為準；下方舊版部分裁切／飛散描述已被取代。

> **2026-10-03 子視窗 V2 已落地**：Settings／Continue／Party／Name／刪除確認／Loading／Popup 已套用深橄欖面板與共用筆刷，保留正式事件與資料。Continue 新增只讀 MenuSaveSummaryView、使用獨立 SaveSlotRowBrushV2；多人代碼仍為實際 8 碼。聚焦 9/9 通過，完整回歸仍有 6 項既有失敗；UI Play Mode 已驗證，未做實際開房／刪檔／Host Client。詳見 [166](166_Menu筆觸視覺與配置.md)／[驗證](Validation/MenuWindowsV2/README.md)。
> **2026-10-03 主選單筆觸 V1**：已確認的 MenuV2 右側排版與四個無框筆觸按鈕落在 `_Menu` Scene Instance；保留六個入口、Fusion 事件與 Continue 接線。專用 Show／Hide 只淡入淡出，局部互動由 `MenuBrushButtonVisual` 呈現。範圍、Inspector 與驗證見 [166 主選單筆觸視覺](166_Menu筆觸視覺與配置.md)。

> **2026-10-04 E 技能視覺已接入**：V4 圖示、中央施法／待命／增益條、護盾血格、四種 LGG 與模型／特效接口；配置及驗收見 [43 E 技能視覺與 UI 配置](43_E技能視覺與UI配置.md)。

> **2026-10-04 E 主動技能原型已接入**：十項跨職業 E 沿用 GrappleFocus 分類及同槽互斥，現有獎勵池保留四項並追加十項。新增條件與共用 Definition 強化接口；強化仍不啟用、不清空動能，原空中緩速／衝刺維持原樣。設定、暫定規則與驗收見 [42 E 主動技能規格與驗收](42_E主動技能規格與強化預留.md)；驗證見 [紀錄](Validation/ActiveAbilities/README.md)。

> **2026-10-02 Ranged C 定點砲台**：新增獨立光束／投射物 Prefab，整隻原地轉向、不巡邏、不追逐位移。Stationary 的 Encounter 使用人工出生中心，測試生成器使用指定 Transform，皆不需地面／飛行巡邏區。聚焦 29/29 與受控 Single Runner 攻擊／位置不變已驗證；獨立 Host／Client 與自然 Encounter 待驗。完整回歸仍有既有 6 項失敗。設定與證據見 [165 Ranged C 定點砲台](165_RangedC_定點砲台與生成配置.md)。

> **2026-09-30 自動飛行巡邏**：Free Flying 的 `EnemySpawnZone` 在人工 Area 留空時，由 Host Director 按 Zone 立體範圍自動規劃巡邏點；使用候選 Enemy Prefab 實際膠囊與障礙遮罩，保留出生盒可達連通集合，沿用既有 Brain／Navigator／出生前綁定。Ground 與人工引用相容；最新驗證與使用方式見 [164](164_Phase10_怪物生成區與Encounter守則.md)。

> **2026-09-26 Phase 10 局部實作與驗證**：`MapChunk` Prefab 可放獨立怪物生成區與定向門檻；普通關卡 Host Encounter 以高速線段穿越、全員遮擋與全場預算決定是否生成，最近五個成功區輪流鎖定，僅清理自己生成且落後脫戰的普通敵人。`MapChunk_Prototype`／Game Scene 已配置並移除重疊舊 NavMesh，地面出生會貼到鄰近實體地板；EditMode 5/5 與受控單人 Host 生成／移動已驗證，獨立 Host＋Client 仍待驗。見 [164 Phase 10 怪物生成區](164_Phase10_怪物生成區與Encounter守則.md)。

> **2026-09-25 命中特效局部實作**：Rifle／SMG 正式命中由 Host 廣播所有玩家；每射手 FIFO 彈孔、火花完成清理及獨立 Layer 遮罩。三款素材與設定資產已提供，KCC_Player 根元件需手動掛載，獨立 Host／Client 實戰尚待驗收。見 [163 槍械命中特效與彈孔](163_槍械命中特效與彈孔.md)。

> **2026-09-25 槍口火焰局部實作**：`FirstPersonMuzzleFlash` 只讀當前 AttackRifle／SupportSMG 成功射擊序號，在 ViewModel 槍口重播單一 Billboard，狀態改變清除，畫面尺寸可調；不讀左鍵、不改武器 Gameplay。圖集／材質已提供，Scene／Prefab 手動接線與實戰驗收尚待進行。設定與證據見 [162](162_第一人稱槍口火焰.md)。

> **2026-09-24 Phase 4／Phase 6 驗收狀態更新**：使用者回報兩階段均已手動測試通過。此為使用者驗收結論，不是本輪重新執行的 Unity 自動測試或逐項 Host／Client 日誌；較早段落的「待實測」描述為當時切片紀錄。最新狀態與證據邊界以 [130 回歸驗證與審查狀態](130_回歸驗證與審查狀態.md) 為準，既有自動回歸失敗仍保留。

> **2026-09-24 技能圖示與獎勵輸入局部修正**：技能槽 Icon／Fallback 改為中心錨點與中心 pivot，Icon 保留比例；舊 E 槽圖示偏移不再套用。選獎勵期間按住的滑鼠鍵在視窗消失後仍被攔截，須放開再按才恢復射擊／瞄準。使用者確認維持四項獎勵池；預設裝備排除後只剩左右兩項，中卡僅於三個有效候選時顯示。詳見 [154](154_戰鬥HUD視覺配置第一版.md)、[159](159_Phase6-E_ALT獎勵HUD與輸入.md)。

> **2026-09-24 武器 HUD 清理後修復**：新版只需 `CurrentAmmoText`／`MagazineCapacityText`，舊 `weaponValueText` 可空，不再阻止 Update 或造成 NullReference。`ReloadProgressRoot/Fill` 統一用 Simple Image＋RectTransform 寬度，按武器 Snapshot 每幀更新；不再依賴無 Sprite 時無效的 fillAmount。完成、取消或來源失效隱藏。

> **2026-09-24 HUD 細節修正**：部分血格改成固定輪廓裁切，保留斜率；加入以螢幕像素換算的透明邊緣抗鋸齒。戰鬥 HUD 除彈藥使用 MuseoModerno-Bold 外，皆用 NotoSansTC-Bold；ALT 全畫面白色遮罩已停用。`ApplyReadabilityFixes` 是不重設座標的局部 Editor 遷移入口。規格見 [154](154_戰鬥HUD視覺配置第一版.md)。

> **2026-09-24 HUD 排版校正**：依PDF 還原的 1920×1080 重設間距、斜形與圓角。`StageHUD` 與玩家 Canvas 各有 `BattleHudReferenceFrame`，只對同一參考平面做等比縮放；獎勵窗使用 Prefab 排版，Runtime 不再覆寫位置。具體座標、Editor 渲染圖與驗證邊界見 [154](154_戰鬥HUD視覺配置第一版.md)／[圖像紀錄](Validation/BattleHUD/README.md)。舊有概略錨點不可再覆蓋這版。

> **2026-09-24 戰鬥 HUD 第一版局部接線**：StageHUD.prefab 已有充能圓環與隨 Loadout 變動的 Q／E 能力槽，五份 Definition 共用獎勵卡與技能槽示意 Sprite；_Menu.unity 玩家 Canvas 已配置左下動能／分格血量、右下武器彈數／容量與中央換彈進度。InputButton.Ability1 預設 E，觸發 SupportAerial／TankAirDash；右鍵維持瞄準。資料來源與實戰／多人畫面驗收邊界見 [154](154_戰鬥HUD視覺配置第一版.md)。

> **Phase 6 數值手冊（2026-09-24 局部核對）**：玩家升級門檻目前是 `PlayerExperienceRules` 的 C# 常數，不在 Inspector；敵人基礎擊殺經驗與獎勵權重／最低等級可於各自 Definition 資產調整。操作、F8、存檔與驗證注意事項集中在 [160 經驗與獎勵數值調整手冊](160_Phase6_經驗與獎勵數值調整手冊.md)。本次只新增文件，未改平衡值。

> **2026-09-24 Phase 6-E Prefab／開發測試鍵**：`InputManager` 只在已配置獎勵 HUD、待選且 ALT 按住時攔截選擇滑鼠鍵；WASD／視角不鎖。`LocalPlayerRewardHUD` 已加入 `StageHUD.prefab`，顯示同步經驗、閃爍提示與二／三選一，選窗共用 PDF 的 1920×1080 參考平面（依 154 的精確座標）。Editor／Development Build 可按 F8 請 Host 為按鍵玩家補足一級經驗，待選獎勵期間不生效。五份獎勵已補說明。Game Scene 既有 StageHUD instance 可繼承新介面，尚缺正式圖示及實戰／獨立 Host-Client Play Mode 驗收；見 [159](159_Phase6-E_ALT獎勵HUD與輸入.md)。

> **2026-09-24 Phase 6-D 局部實作**：五個現有鈎索能力已納入獎勵清單。主要 `GameLogic` 的 State Authority 保存候選、版號與已裝能力；選擇成功才透過既有能力 Loadout 真正替換 Runtime 並扣待選數，重生後可還原。Host 存檔升至 v3。ALT 選窗／滑鼠輸入、越級 Debuff 與獨立 Host／Client 驗證仍未完成；見 [158](158_Phase6-D_獎勵候選與權威領取.md)。

> **2026-09-24 Phase 6-A／B 局部實作**：`PlayerExperienceRules` 已建立全隊存活玩家共享、擊殺者 +1、10 起逐級倍增、同次超額保留及待選期間拒收後續經驗的純規則。Host 存檔 v2 保存 `PendingRewardCount` 並可讀入 v1。主要 `GameLogic` 現保存並同步每名玩家的等級／經驗／待選數，死亡及跨場景不依賴 Player 物件；關卡成功寫回 Host 記錄、失敗寫檔後全員重設。實際 Enemy 擊殺於 Phase 6-C 接入；154 戰鬥 HUD 與 Host／Client Runtime 驗證仍待後續；見 [155](155_Phase6-A_玩家經驗與存檔規則.md)、[156](156_Phase6-B_玩家進度權威同步.md)。

> **2026-09-24 Phase 6-C 局部實作**：正式 Enemy 的 State Authority 死亡事件現將致死傷害的攻擊者交給主要 `GameLogic`，按 `EnemyDefinition` 獨立基礎經驗（目前四份均為 1）發給存活隊友、擊殺者 +1；環境／無有效玩家擊殺者不發。僅 Active 關卡發放，待選獎勵者個別拒收。尚無獨立 Host／Client 實機擊殺驗收，也尚無三選一 UI；見 [157](157_Phase6-C_敵人擊殺經驗發放.md)。

> **2026-09-22 SafeHouse HUD 修正與平面圖**：關卡 HUD 不放入 MapChunk；Fusion Multi-Peer MergeScenes 後原始 Scene 卸載不能銷毀已搬移 HUD。SafeHouse 現沿用 LocalMinimapController 顯示自己的預烘焙灰模平面圖、本機玩家與 M 展開。Editor 投票／往返與獨立 Host／Client Level1→SafeHouse2→Level2 通過；完整回歸仍有兩項既有 Boss 資產引用失敗。詳見 [驗證紀錄](Validation/SafeHouseHUD/README.md)。

> **2026-09-22 Phase 4-F 程式與資產接線完成、Runtime 待驗證**：Boss 規格由 `StageRuntimePlan` 集中發布，地圖、Enemy、HUD 不自行判斷 Boss。`MapChunk_Boss` 已具備四向 Connector、玩家／Boss 錨點、碰撞、獨立 NavMesh 與 146×146 Cartography；Game Scene 已指定該 Chunk、Catalog 與暫用 `Enemy_Boss` Network Prefab。大型 Cartography payload 已從預設 Inspector 隱藏，避免點選 171,810 Cells 時長時間重建。`PurgersRegression` 136／136；Play Mode、獨立 Host／Client、晚加入與自然擊殺仍待驗證。詳見 [153](153_Phase4-F_Boss關第一個可玩垂直切片.md)。

> **2026-09-22 Phase 4-D 完成**：`NetworkMapState` 現在發布可驗證的完整線性多 Chunk 拓撲（父連線、入口、出口、開口遮罩）、唯一最後 Chunk 與撤離選擇；Client 在建立任何 Replica 前先驗證整份封包，只重建 Chunk／Collider／Cartography／封口並停用自己的 NavMeshSurface。`PurgersRegression` 118／118、Windows Development Build 0 error／warning，且獨立 Host＋延遲 20 秒晚加入 Client 已驗證三 Chunk 一致與 Host-only AI 導航。詳見 [151](151_Phase4-D_NetworkMapState完整拓撲同步.md)。

> **2026-09-22 Phase 4-C 程式完成、Runtime 待驗證**：`NetworkMapState` 現在由 Host 發布唯一 `FinalChunkIndex`；`StageFlowController` 只收集該 Chunk 的撤離候選，State Authority 從關卡入口以完整地面 NavMesh 路徑篩選，再使用本輪 Seed 提交選中索引。Client 與小地圖只解析同步結果，不執行 NavMesh 或重新抽選。現有起始 Chunk 場景撤離點會以局部座標投影到最後 Chunk，不需手改 Prefab YAML。C# 編譯已通過；Unity Console、EditMode、Host／Client、晚加入與自然撤離仍待 Editor 驗證。詳見 [150](150_Phase4-C_最後Chunk撤離.md)。

> **2026-09-21 Phase 4-B 局部進度**：Host 已把 `StageRules.GetAdditionalChunkCount` 接到 Phase 4-A Planner，Level 1／2／3 分別提交 1／2／3 Chunk 線性鏈；沿用 `ConnectorAlignmentPrototype` 完整對齊與封口、`MapRuntimeNavigationPrototype` 的逐 Chunk 預烘焙 NavMesh 與 N−1 Link，並把 `RuntimeGeneratedMapRoot` 放在 Fusion `[Game]` 下。`NetworkMapState` 可發布完整 Host 清單供既有 Client 重建。未自動修改 Scene／Prefab；最後 Chunk 撤離限制、Level 4 Boss 與獨立 Client／晚加入實機證據仍未完成。詳見 [149](149_Phase4-B_Host多Chunk生成與導航.md)。

> **2026-09-21 Phase 4-A 局部進度**：已新增不依賴 Unity 場景的 `MapTopologyPlanner`，完成穩定 Seed、線性多 Chunk 拓撲、禁止從入口原路退出、矩形重疊拒絕、每塊有限重抽及唯一最後 Chunk 身分。現有 `MapRunSelectionPrototype`／`ConnectorAlignmentPrototype` 序列化入口未變，Planner 尚未接入 Runtime；三塊以上生成、導航、Client 同步與最後 Chunk 撤離仍未完成。詳見 [148](148_Phase4-A_決定性多Chunk拓撲核心.md)。

> **2026-09-21 命名校正**：原「Phase 4-1～4-4」已更名為「Phase 4 前置-1～4」，定位為進入 Phase 4「完整 Cycle 地圖」之前的整備工作，不計入 Phase 4 完成度。正式 Phase 4 仍以 [140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md) 的完整 Cycle 地圖為準。

> **2026-09-19 Phase 4 前置-4 優先補充**：小地圖已由獨立 `NetworkMapState`／`LocalMinimapController` 接入。Host 發布現有一／兩塊地圖配置，Client 重建區塊與封口；不再沿用下方歷史段落「第二 Chunk／阻擋屋不提供 Client 同步」的現況判定。個人探索紀錄分開保存，Host 可即時切換全隊共享；M 展開玩家置中、較大範圍鳥瞰。底圖使用每 Chunk 預烘焙 Cartography，Client 不執行 AI 導航規劃。三塊以上生成、多樓層及最終 Chunk 撤離仍未完成。配置與驗證以 [147](147_PrePhase4-4_小地圖與戰爭迷霧.md) 為準。 修訂：道路預設可見，飛行探索下方；迷霧限制目標資訊，敵人紅點讀取同 Runner 的 EnemyActor 同步位置與存活狀態。 效能修訂：區塊底圖快取、探索 dirty 上傳、封口空間索引，改用分段高度灰階；Texture Resolution 已停用。

> 文件基線：2026-09-18（局部核對：本輪修正、動能、地圖原型與安全屋 Phase 2；完整索引另見 02）  
> 核對來源：`M:/UnityProject/Purgers/Assets/Scripts`，共 187 支 C#。
> 用途：新對話先讀本檔，再依任務讀對應模組文件；本檔不是原始碼的替代品。

## 1. 專案技術與權威原則

> **2026-09-26 Tracer 滿動能換色**：`BulletTracerLineDrawer` 由職業 Runtime 的 Owner 只讀鈎索動能，滿值時新發射改用可調強化色；每發固定外觀，保留漸層與隱藏期正常飛行。設定／驗證見 [50](50_武器瞄準與射擊.md)。

> **2026-09-25 變異荊棘美術**：新增三款深褐密刺球塊、三級 LOD 與 `MutantThornVisual` 本機呼吸／受擊抽動。尚未加入 Scene、Collider、傷害或生成器；模型位置、Inspector 調整與驗證邊界見 [161](161_變異荊棘模型與視覺動畫.md)。

- Unity 2022.3 LTS。
- Photon Fusion 網路同步；玩家、敵人、投射物、測試生成器等使用 `NetworkObject`／`NetworkBehaviour`。
- 會改變遊戲結果的操作由 `State Authority` 決定：生成、傷害、生命、死亡、Enemy AI、技能正式狀態。
- 長期存檔只由 Host 本機建立與寫回；Client 經 Party Menu 加入並讀取 Host 同步的權威 Runtime State，不直接讀寫該存檔。
- `Input Authority`／本機玩家負責採集輸入與第一人稱呈現，不得各自 `Instantiate` 網路敵人或自行確定傷害。
- `[Networked]` 屬性只能在對應 `NetworkBehaviour.Spawned()` 後存取。切換職業或 ViewModel 時，表現層必須先檢查 `Object != null && Object.IsValid`。
- 本地表現（ViewModel、HUD、相機、速度線、本地細節音效）和世界結果（傷害、世界槍聲、敵人狀態）分開。

## 2. 必須保留的仲裁層

| 仲裁層 | 職責 | 禁止繞過方式 |
|---|---|---|
| `PlayerActionGate` | 集中封鎖瞄準、射擊、換彈、Quick Action 等玩家行為 | 技能不得只在自己腳本裡用零散 bool 封鎖其他系統 |
| `PlayerProfessionRuntimeManager` | 啟用當前職業 Runtime、套用移動輸入 Modifier | 不得讓所有職業能力同時讀輸入 |
| `PlayerAbilityRuntimeManager` | 依 Loadout 啟用獨立能力 Runtime、驗證分類／容量／職業限制／互斥 | 不得把玩家可選能力重新綁死在 Profession Runtime |
| `PlayerIncomingDamageModifierBridge` | 集中套用玩家受傷 Modifier，例如 Tank Guard | Damage 來源不得直接硬查 Tank 技能 |
| `EnemyActionGate` | 管理 Enemy 行動鎖與來源 | 攻擊、受控、死亡不可互相搶控制權 |
| `EnemyMovementOwnership` | 決定 Patrol／Chase／受控位移誰可寫位置 | Navigator 與攻擊 Dash 不可同 Tick 同時移動 |
| `EnemyCombatDecisionController` | 選擇並執行 `EnemyCombatOption` | 各攻擊不可各自無條件監聽距離並同時發動 |

## 3. 玩家主資料流

```text
InputManager → NetInput → Player.FixedUpdateNetwork
├─ PlayerMovement / PlayerGrapple / PlayerSlideController
├─ PlayerWeaponController / PlayerAimController / PlayerQuickActionController
├─ PlayerProfessionRuntimeManager
│  └─ 當前職業 Runtime Driver
│     └─ 武器、F 近戰／Quick Action、Tank Guard／Quick Dash
└─ PlayerAbilityRuntimeManager
   └─ 玩家裝備的 GrappleFocus / GrappleHit Runtime
```

`Player` 是玩家 Prefab 的組裝根與更新協調者；`PlayerMovement` 是 KCC 移動核心，但輸入是否有效還會受到滑鏟、鈎索、職業 Modifier 與 Action Gate 影響。

## 4. 職業架構

```text
PlayerProfession + ProfessionDefinition
→ PlayerProfessionRuntimeManager
→ PlayerProfessionRuntime
→ 對應 PlayerProfessionRuntimeDriver
```

- Attack 職業 Runtime：`AttackRifle`、`AttackQuickMelee`、武器用 `AttackFocusAbility`。
- Support 職業 Runtime：`SupportSMG`（含治療射擊）、`SupportQuickMelee`；`SupportRifleHealingAbility` 是保留的 Rifle Hit Override 模組，非目前 Prefab 的必要掛件。
- Tank 職業 Runtime：`TankMeleeCombo`、`TankQuickDashAbility`、`TankGuardAbility`。
- 玩家 Loadout：`SupportAerialAbility`、`TankAirDashAbility` 屬 GrappleFocus；Mark／Pull／Gather 屬 GrappleHit。
- `PlayerQuickActionController` 只調度實作 `IPlayerQuickActionAbility` 的當前職業能力。
- 槽位容量由 `PlayerAbilitySlotLayoutDefinition` 設為 0～N；Fusion 八格只是技術上限。每個 Definition 均可自行開關職業限制與互斥群組。
- `SupportAerialAbility` 已是跨職業 GrappleFocus 能力，冷卻從能力關閉後起算，且不強制依賴 `SupportSMG`。
- 預設 Loadout、五組能力 Definition／Runtime Prefab 已建立並完成設定檢查，KCC_Player 已指向起始 Loadout；Play Mode 與多人連線仍須分開驗證。

## 5. 鈎索資料流

```text
PlayerGrapple（飛行、命中、拉動、釋放、GrappleAirborne）
→ PlayerGrappleInteractionController（依已載入命中能力與目標 Capability 路由）
→ AttackGrappleMarkAbility / SupportGrapplePullAbility / TankGrappleGatherAbility
→ 目標 Receiver 或狀態元件
```

- 世界掛點：`GrappleAnchor`。
- 互動目標描述：`GrappleInteractionTarget`。
- Support 拉 Enemy：`SupportGrapplePullReceiver`。
- Support 拉 Player：`SupportGrapplePlayerPullReceiver`，使用 KCC，不能套 Enemy Rigidbody 寫法。
- Tank 聚怪：`TankGatherMovementReceiver`。
- 鈎索視覺：`PlayerGrappleVisual`；UI 判定：`LocalPlayerGrappleAimIndicator`。

## 6. 傷害、生命、死亡與回饋

```text
武器／近戰／Enemy 攻擊
→ DamageRequest
→ DamageReceiverUtility
→ IDamageReceiver
→ PlayerHealth 或 TestDamageReceiver
→ DamageResult
→ PlayerCombatFeedbackRelay
→ HitMarker / CameraShake / FirstPersonDamageFeedbackAudio
```

- `DamageSystem.cs` 是協定與解析入口，不是某把武器專用程式。
- `HealthSystem.cs` 定義治療、復活、生命事件協定。
- `PlayerHealth` 是玩家正式生命元件。
- **`TestDamageReceiver` 雖然名稱含 Test，目前是 Enemy 正式生命／治療／死亡事件來源。沒有完成遷移前不可刪除或另建平行生命系統。**
- 傷害回饋優先序由結果決定：擊殺 > 暴頭 > 一般命中。

## 7. Enemy 分層

```text
EnemyActor（組裝根）
├─ EnemyDefinition（類型資料）
├─ EnemyStateController（Brain / Action / Control 狀態）
├─ EnemyActionGate（動作鎖）
├─ EnemyPerceptionController（掃描候選玩家）
├─ EnemyAwarenessBrain（發現、記憶、呼喚）
├─ EnemyIdlePatrolBrain（Idle ↔ Patrol）
├─ EnemyChaseBrain（追逐目標與 Chase Motor）
├─ EnemyCombatDecisionController（攻擊選擇與階段）
└─ Presentation Drivers（只讀狀態驅動動畫／材質／Line）
```

- 地面與飛行路徑分流：Ground 使用 `NavMesh.SamplePosition`／`CalculatePath` 與 Fusion Tick 位移，不掛 `NavMeshAgent`；Flying 使用自由飛行探測。
- 場景 `EnemyPatrolArea` 可保留人工節點；MapChunk Prefab 保存預先烘焙的 `NavMeshSurface/NavMeshData`，Runtime 依 Chunk 位置與旋轉載入並用雙向 NavMesh Link 接合。阻擋屋用 Carving `NavMeshObstacle` 封閉未使用入口，再依 Connector 邊界自動取樣、篩選最大可連通集合並為每個 Chunk 建立地面巡邏區。
- `EnemyAlertDirector` 負責群體呼喚節流，避免同區同時播放發現動作與音效。
- 攻擊實作繼承 `EnemyCombatOption`：近戰揮砍、近戰 Dash、遠程投射物、遠程 Beam。
- `EnemyMovementOwnership` 必須在 Patrol、Chase、Dash、Grapple Control 間維持單一位移寫入者。

## 8. 武器、表現與聲音

- `PlayerWeaponController`：依職業路由 Attack Rifle／Support SMG，處理開火、換彈與中斷。
- `PlayerAimController`：瞄準狀態與 FOV；換彈期間瞄準由 Action Gate 阻擋。
- `ProfessionViewModelManager`：只顯示當前職業 ViewModel。
- `FirstPersonViewModelActionAnimator`：Idle／Shoot／Reload／Melee／Appear。
- `FirstPersonViewModelAimAnimator`：開鏡正播、關鏡倒播。
- `FirstPersonViewModelMotionController`：Idle Motion／Walk Bob 等程序位移；瞄準時停用這些位移，不是把整個 Profile 數值永久改成零。
- `GameplayAudioService`：本地聲音播放中心。
- `NetworkPlayerAudioEmitter`：世界可聽聲音的網路轉送與跟隨發聲者。
- 第一人稱動畫細節音只走本地；槍聲、Enemy 攻擊等世界聲音才走網路 Emitter。

## 9. 文件路由

| 修改內容 | 必讀文件 |
|---|---|
| 玩家移動、跑步、滑鏟、二段跳 | `20_玩家核心移動與狀態.md` |
| 鈎索與職業鈎索效果 | `30_鈎索系統.md` |
| Attack／Support／Tank 職業能力 | `40_職業與特殊能力.md` |
| 玩家自選能力、槽位、職業限制與互斥 | `41_玩家能力Loadout.md` |
| 十項 E 主動技能、同池規則、滿動能強化契約（尚未實作） | [42_E主動技能規格與強化預留.md](42_E主動技能規格與強化預留.md) |
| 射擊、換彈、瞄準 | `50_武器瞄準與射擊.md` |
| 傷害、治療、死亡、命中回饋 | `60_傷害生命死亡與回饋.md` |
| ViewModel 與 Animator | `70_第一人稱模型與動畫.md` |
| 本地／世界聲音 | `80_聲音系統.md` |
| Enemy 感知、巡邏、追逐 | `90_敵人核心感知巡邏與追逐.md` |
| Enemy 攻擊、死亡表現、測試生成 | `91_敵人攻擊表現與生成.md` |
| Phase 10 正式怪物生成區、方向門檻、輪替與手動配置 | `164_Phase10_怪物生成區與Encounter守則.md` |
| HUD、聊天、觀戰 | `100_UI觀戰與聊天.md` |
| 戰鬥 HUD 第一版視覺位置、狀態表現、獎勵選擇與未決設計 | `154_戰鬥HUD視覺配置第一版.md`（第一版已接線，實戰畫面待驗收） |
| 連線、選單、場景工具 | `10_網路連線與輸入.md`、`110_場景工具與選單.md` |
| Phase 1-B Continue 按鈕、Overlay、Row Prefab 實際配置與驗證 | `141_Phase1B_Menu存檔手動配置與驗證.md` |
| Phase 2 安全屋灰盒、開始裝置、Ready Check 與 Host／Client 驗證 | `142_Phase2_安全屋灰盒配置與驗證.md` |
| Phase 3 Level 1 關卡、撤離、時間與回安全屋 | `143_Phase3_Level1關卡閉環配置與驗證.md` |
| Phase 4 前置-1／前置-2 共用輸入鎖、轉場、鍵盤 Ready Check 與左上 HUD | `144_PrePhase4-1_共用控制與轉場.md`、`145_PrePhase4-2_安全屋鍵盤準備與HUD.md` |
| Phase 4 前置-3 左上三列、小地圖空容器、中下方速度與傷害加成 | `146_PrePhase4-3_HUD排列與資訊精簡.md` |
| 長期遊戲流程、存檔、安全屋、關卡循環、成長與裝備遷移 | `140_遊戲流程存檔關卡成長與長期藍圖.md` |
| Phase 4-A 決定性多 Chunk 拓撲核心與 Runtime 接線邊界 | `148_Phase4-A_決定性多Chunk拓撲核心.md` |
| Phase 4-B Host Level 1～3 多 Chunk 生成、封口、導航與 Inspector 接線 | `149_Phase4-B_Host多Chunk生成與導航.md` |
| Phase 4-C 最後 Chunk 撤離、地面 NavMesh 可到達性與權威選點 | `150_Phase4-C_最後Chunk撤離.md` |
| Phase 4-D NetworkMapState 完整拓撲、晚加入、封口、最後 Chunk 與撤離同步 | `151_Phase4-D_NetworkMapState完整拓撲同步.md` |
| Phase 4-E 可調 CycleLength、Level 1／2／3／5、Boss 分流與 SafeHouse 循環 | `152_Phase4-E_CycleLength與Runtime循環.md` |
| Phase 4-F Boss 專用 Chunk、擊敗即成功、Unlimited 與資產接線 | `153_Phase4-F_Boss關第一個可玩垂直切片.md` |
| Unity 套件、效能量測、AI Agent 與驗證流程 | `150_開發工具與AI_Agent工作流.md` |

## 10. 新對話工作規則

1. 先讀本檔。
2. 再讀任務對應的模組文件。
3. 文件與實際 C# 不一致時，以實際原始碼為準，先指出漂移，不可直接沿用過時敘述。
4. 改動跨模組公開介面、控制權或資料流時，同步更新本檔、對應模組文件與 `02_程式職責總表.md`。
5. 不因類名不漂亮就建立第二套平行系統；先確認既有仲裁層與遷移成本。
6. 新增 Networked 欄位、RPC 或 Spawn 流程時，明確標示 State Authority、Input Authority、Render 三者的責任。
7. 產出或修改 C# 時，所有提供給 Unity Inspector 調整的序列化欄位，依功能分組加上有意義的 `[Header]`，並為每個欄位加上繁體中文 `[Tooltip]`；說清楚用途、單位、有效範圍、特殊值與調整後的效果。程式類別、重要方法、非 Inspector 參數與較難理解的判斷，使用清楚的繁體中文標題／註解說明意圖、資料流及限制；不要只把程式碼逐行翻譯成註解，也不要把不適用於方法或常數的 Inspector Attribute 硬加上去。
8. 每完成一項功能，交付對應的「設定與調整說明書」，放在該功能的模組文件或獨立文件，並在交付說明中附上路徑。以使用者能自行調整為標準，逐項列出：要調整的數值及作用、目前預設值、實際有效範圍與單位、數值間的公式或相依關係、具體修改位置（Scene／Prefab／ScriptableObject 的階層與 Inspector 欄位，或程式檔案與常數）、操作步驟、建議起始值／調整範例、儲存或多人連線的生效時機，以及 Play Mode 檢查方法與預期結果。
9. 例如升級經驗，說明每級門檻如何計算、可調範圍由哪個驗證或限制決定、應到哪個資產或程式欄位修改，以及改動後既有存檔與 Host／Client 會如何表現。若目前沒有 Inspector 入口或明確的允許範圍，要如實標示需改程式與重新編譯，不能捏造可調範圍或聲稱已驗證。

## 2026-09-17 局部核對與架構補充

`DevelopmentToolsPolicy` 統一開發入口：F1–F3 職業切換與敵人測試生成在正式非 Development Build 停用，客戶端及權威入口皆檢查。離線地圖 Marker、F9 與 N 鍵入口遇任何 NetworkRunner 即不執行；Fusion 模式每個 Host Runner 產生一次 Run Seed，從四個入口隨機固定本輪出生點與不同出口，並從候選池抽選、對齊唯一一個第二 Chunk。同一輪死亡重生沿用入口。兩個 Chunk 對齊後，可為除了接合出口／入口以外的六個 Connector 生成阻擋屋；玩家出生入口也會封閉。第二 Chunk 與阻擋屋目前不是 NetworkObject，不提供 Client 同步或第三塊生成。`WeaponHitUtility` 共用最近命中與距離衰減。動能增傷與回歸驗證分別見 [31](31_鈎索動能與傷害倍率.md)、[130](130_回歸驗證與審查狀態.md)。

`Progression/Save` 已有 Host 本機 JSON 存檔、永久／循環進度分界、版本 1 驗證、存檔清單、原子取代寫入、確認刪除、循環失敗重設，以及掛在各自 NetworkRunner 的 Host／Client 存取 Context；不得改回 process-global static，否則 Multi-Peer 會互相覆蓋。Continue 每列最右側 X 只開啟確認 Modal，取消不改檔案，確認後才刪除並重整清單。`MenuConnection` 已把 Quick Play 固定為新存檔 Host，並會把 PlayerPrefs 中已失效的舊場景選擇校正為 MenuConfig 第一個有效場景；Client Join 不具本機寫檔權。`SafeHouse` 的 Host 近距離 E、全員 Ready 與 Authority 切入 Game 已完成。`StageFlowController` 現在負責 Level 1 的 10 分鐘 Timer、四點撤離抽選、全體存活玩家 3 秒撤離、全滅／逾時仲裁、成功升級或失敗重設後回安全屋；`GameLogic` 只在權威切場期間暫停重生，不接管關卡規則。Host 成功與限時失敗已實測，獨立 Client 與 Level 2 以上多 Chunk 仍待 Phase 4。規格與操作見 [140](140_遊戲流程存檔關卡成長與長期藍圖.md)、[142](142_Phase2_安全屋灰盒配置與驗證.md)、[143](143_Phase3_Level1關卡閉環配置與驗證.md)。


- 2026-09-17：同步本輪局部修正、資產設定與驗證邊界。


## 2026-09-18 Phase 4 前置-1 局部核對

共用控制票證、黑幕與所有連線玩家載入完成後啟動計時，詳見 [144_PrePhase4-1_共用控制與轉場.md](144_PrePhase4-1_共用控制與轉場.md)。本輪核對 187 支 C#；多人驗證狀態以 144 的實際紀錄為準。

## 2026-09-18 Phase 4 前置-2 局部核對

安全屋 Ready Check 改為 TAB 雙向切換；State Authority 在全員準備後建立 3 秒網路倒數，任何取消立即中止。左上第三列 HUD 依在線玩家顯示 LED 與已準備／未準備數，Ready 顯示期間不持有輸入票證，轉場 `AllInput` 仍會阻擋 TAB／ESC。Editor Host＋獨立 Client 已驗證 Ready、取消、倒數中取消與切場；詳見 [145](145_PrePhase4-2_安全屋鍵盤準備與HUD.md)。

## 2026-09-19 Phase 4 前置-3 局部核對

左上固定小地圖空容器、等級／純數字時間／任務、Ready Check 三列；後者只在安全屋投票與出發倒數顯示。中下方沿用速度 Slider 並讀取既有動能倍率顯示傷害加成，詳細開發 HUD 預設關閉。沿用最高排序轉場黑幕，沒有小地圖邏輯或新的網路欄位；配置與驗證見 [146](146_PrePhase4-3_HUD排列與資訊精簡.md)。

## 2026-10-02 鈎索使用能量局部更新

`PlayerGrappleCharges` 沿用舊元件改為玩家等級制使用能量，1 級 50、每級 +25；合法發射扣 1，尚未釋放且拉動自己時每秒扣 1。無自然／擊殺回充；耗盡不取消本次移動。Host 讀主要 `GameLogic` 的玩家等級套用容量，Input Authority 沿用預測消耗，HUD 只讀同步比例。升級補滿／補差額可在玩家 Prefab Inspector 切換；死亡重生及正式安全屋／關卡轉場補滿。左下動能增傷獨立。保留舊元件 GUID 與職業舊欄位，未重建 Scene／Prefab；設定、遷移邊界與驗收順序見 [30](30_鈎索系統.md)，證據見 [130](130_回歸驗證與審查狀態.md)。
