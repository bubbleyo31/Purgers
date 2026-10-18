> **2026-10-18 隊伍窗唯讀介面（局部）**：IPlayerAbilityCooldownSource 提供本次權威冷卻分母；ActiveBase／SupportAerial／TankAirDash 在原開始點同步，尖刺沿原 Tick snapshot。Runtime.TryReadPresentation 同時處理 transfer 冷卻與可用性，不讀 Bonus Cast 為持有。詳見 [106](106_TAB隊伍資訊窗.md)。

# 玩家能力 Loadout 架構

> **2026-10-04 E 主動技能原型已接入**：十項跨職業 E 沿用 GrappleFocus 分類及同槽互斥，現有獎勵池保留四項並追加十項。新增條件與共用 Definition 強化接口；強化仍不啟用、不清空動能，原空中緩速／衝刺維持原樣。設定、暫定規則與驗收見 [42 E 主動技能規格與驗收](42_E主動技能規格與強化預留.md)；驗證見 [紀錄](Validation/ActiveAbilities/README.md)。
>
> 最後核對日期：2026-10-04（局部核對：本輪 E 原型、原有能力／武器接點與驗證；其餘歷史紀錄保留）。

> 最後核對：2026-09-17（局部核對：本輪修正與下方補充；其餘內容沿用 2026-09-15 基線）
> 核對來源：`Assets/Scripts/Player/Ability/Loadout`、五個初始鈎索能力、`Player.cs`、`PlayerGrapple.cs`、`PlayerGrappleInteractionController.cs`  
> 相關文件：`20_玩家核心移動與狀態.md`、`30_鈎索系統.md`、`40_職業與特殊能力.md`

## 目前完成狀態

| 階段 | 狀態 |
|---|---|
| Loadout、槽位、職業限制、互斥與 Runtime 原始碼 | 已完成並通過 Runtime／Editor 編譯 |
| Unity Editor 資產遷移工具 | 已提供 |
| Definition、獨立 Runtime Prefab、預設 Loadout 實際建立 | 已建立：預設 Loadout、五組 Definition／Runtime Prefab；設定檢查通過，KCC_Player 已掛起始 Loadout |
| Play Mode／多人連線行為驗證 | 尚未完成 |

文件不得省略後兩列，否則會把已完成的程式架構誤報成已完成的 Prefab 與連線驗證。

## 設計目標與邊界

玩家的武器職業與可選能力是兩套不同配置：

```text
F1 / F2 / F3
→ PlayerProfessionRuntimeManager
→ 切換職業武器與職業固定能力

玩家能力選擇
→ PlayerAbilityRuntimeManager
→ 維持獨立 Loadout，不隨職業切換 Despawn
```

本階段只把五個鈎索相關能力納入玩家 Loadout。F 近戰、`TankQuickDashAbility`、`TankGuardAbility` 等仍留在 Profession Runtime，不因 Loadout 系統出現就自動成為可選能力。

## 資料資產

| 類型 | 職責 |
|---|---|
| `PlayerAbilityDefinition` | 能力穩定 ID、顯示名稱、分類、Runtime Prefab、職業規則、互斥、額外鈎索 Layer 與執行優先權 |
| `PlayerAbilitySlotLayoutDefinition` | 各分類實際開放幾格；容量是 0～N，不固定一專注加一命中 |
| `PlayerAbilityLoadoutDefinition` | 玩家目前裝備哪些 Definition，並驗證容量、重複、Prefab、ID 與互斥規則 |
| `PlayerAbilityRuntime` | 單一已裝備能力的 Network Runtime，只允許一個 `IPlayerAbilityRuntimeModule` |
| `PlayerAbilityRuntimeManager` | State Authority 套用 Loadout、生成／回滾／替換 Runtime、驅動 Tick 並彙整移動 Modifier |

Fusion `NetworkDictionary` 的容量固定為八格，這只是編譯期技術上限。遊戲實際開幾格必須由 Slot Layout 決定，不可把八格當成設計承諾。

## 能力分類

| 分類 | 觸發管線 | 初始能力 |
|---|---|---|
| `GrappleFocus` | 進入 GrappleAirborne 後讀取 InputButton.Ability1（預設 E） | `SupportAerialAbility`、`TankAirDashAbility` |
| `GrappleHit` | 鉤頭 Attached 事件，或模組定義的鈎索期間接觸偵測 | Mark／Gather／Pull；另有 `PlayerGrappleSpikeAbility`（見 33） |

分類由能力程式的 `IPlayerAbilityCategorized.AbilityCategory` 宣告，Definition 也保存分類。兩者必須一致，避免只靠 Inspector 把命中能力錯配到專注槽。

## 槽位與 Loadout 驗證

Slot Layout 可設定例如：

| 配置 | GrappleFocus | GrappleHit |
|---|---:|---:|
| 單一專注能力 | 1 | 0 |
| 一專注加一命中 | 1 | 1 |
| 只測試兩個命中能力 | 0 | 2 |
| 完全停用可選鈎索能力 | 0 | 0 |

Loadout 套用前必須通過：

- 總數不超過 Fusion 技術上限。
- 每一分類不超過 Layout 容量。
- Definition、Runtime Prefab 與 Category 完整且相符。
- 不同 Definition 不得共用同一穩定 Ability ID。
- 不允許重複的能力不能重複裝備。
- 直接不相容清單與互斥群組都必須通過。

## 職業限制

每份 `PlayerAbilityDefinition` 都有自己的：

- `restrictProfession`：是否啟用職業限制。
- `allowedProfessions`：允許 Attack／Tank／Support 的 Mask，可複選。

限制關閉時，任何有效職業都可使用；限制開啟且目前職業不合法時，能力保持已裝備但暫停使用，不會被卸下，也不應藉切換職業清除冷卻。

這份規則只對已進入獨立 Loadout 的能力有效。仍在 Profession Runtime 的 Quick Dash／Guard 若未來要跨職業，必須先完成 Runtime 與相依 Driver 的搬移，不能只加一個 Inspector bool。

## 互斥與執行順序

互斥有兩層：

1. `incompatibleAbilities`：指定某兩個 Definition 不能共裝，任一方宣告就生效。
2. `exclusiveGroupIds`：共享任一非空群組 ID 的能力不能共裝。

初始遷移設定：

- 兩個 GrappleFocus 共用 `GrappleFocusMovementAuthority`，因為都接管空中移動。
- 三個 GrappleHit 共用 `GrappleHitRopeAuthority`，因為都可能決定繩索後續。
- GrappleFocus 與 GrappleHit 不互斥，因此預設可以各裝一個。

若未來某些能力能安全疊加，應調整 Definition 的互斥資料，不要刪除 Runtime 的多能力穩定排序。合法的多能力執行順序固定為：

```text
executionPriority
→ Loadout 原始槽位順序
→ Ability ID
```

## 網路套用流程

```text
State Authority 收到已解析的 Loadout Definition
→ 驗證整份 Loadout
→ 按穩定順序建立 Spawn Plan
→ 先 Spawn 全部新 Runtime
   ├─ 任一失敗：Despawn 本次新 Runtime，保留舊 Loadout
   └─ 全部成功：Despawn 舊 Runtime，寫入新 NetworkDictionary
→ LoadoutRevision + 1
→ ViewModel 與查詢端重綁目前能力來源
```

採「先建立新能力、成功後才拆舊能力」是為了避免半套 Loadout。切換職業不走這個流程，也不增加 `LoadoutRevision`。

## 鈎索命中路由

`PlayerGrappleInteractionController` 不再以目前職業挑選 Mark／Gather／Pull，而是依：

1. 目前真正載入且職業規則允許的 Ability Runtime 訂閱。
2. `GrappleInteractionTarget` 公布的目標類型與 Capability。
3. 本次命中的多能力 Mask。

現有三個 GrappleHit 預設互斥，因此正常 Loadout 只會有一個繩索主導能力；Router 保留多能力 Mask 是為未來非衝突效果預留。需要擴大 Player／Enemy 目標 Layer 的能力，必須在 Definition 開啟 `includeAdditionalGrappleTargetMask`，不能再以 Support 職業作為判斷。

## SupportAerialAbility 規則

> 2026-10-03 決策：下列 SupportSMG 特殊模式連動只描述保留中的舊接口；無限彈匣、特殊射速、特殊治療量、零新增後座力目前暫時擱置，不代表要恢復接線或成為獨立技能的預設效果。空中緩速本身保持現狀。

- 分類為 `GrappleFocus`。
- Definition 預設不限制職業，因此 Attack、Tank、Support 都可裝備使用。
- `SupportSMG` 是可選連動；Runtime Root 沒有 SMG 仍可運作空中能力。
- `BeginAbility()` 只啟動 Active Timer，不啟動冷卻。
- 按下 E 啟動，放開後繼續，再次按下 E 提前結束；長按不重複觸發，提前按住再進入合法狀態／冷卻結束不自動啟動。
- 再次按下 E、操作封鎖、落地、離開 GrappleAirborne、職業規則變成不允許或持續時間結束，都集中呼叫 `EndAbility()`。
- 完整 Cooldown Timer 只在 `EndAbility()` 建立，因此能力啟用期間不會先偷扣冷卻。

Tank Air Dash 在 GrappleAirborne 收到 E 的新按下沿用既有 State Authority 判定；右鍵保留瞄準。PlayerAbilityRuntimeManager.GetEquippedRuntimeAtSlot 僅供本機 HUD 讀取已同步能力 Runtime，不參與權威裝備決策。

## 初始資產遷移

在 Unity 執行：

```text
Tools → Player Ability → 建立初始能力 Runtime 與 Loadout
```

工具執行後會：

- 建立五份 Ability Definition。
- 從舊職業 Prefab 複製現有調校值，建立五顆獨立 Network Runtime Prefab。
- 建立預設 `1 GrappleFocus + 1 GrappleHit` Slot Layout。
- 建立預設 `SupportAerial + SupportGrapplePull` Loadout。
- 將 `PlayerAbilityRuntimeManager` 與起始 Loadout 設到 Player Prefab。
- 移除 Attack／Tank／Support Profession Prefab 上已搬移的五個舊能力元件。
- 重建 Fusion Prefab Table。

工具不會在 Domain Reload 偷跑。重新執行時會沿用已存在 Runtime Prefab 上的模組調校值；但第一次執行必須能找到舊 Prefab 元件作為複製來源，因此不要先手動刪除舊能力元件。

## 後續新增能力檢查表

1. 決定能力是職業固定能力，還是玩家可選能力。
2. 可選能力實作 `IPlayerAbilityRuntimeModule` 與固定 Category。
3. 建立只有一個能力模組的 Network Runtime Prefab。
4. 建立穩定且唯一的 Ability ID。
5. 設定職業限制、重複規則、互斥與 Execution Priority。
6. GrappleHit 若需要鉤頭目標事件，補 Router 事件／Mask 與 Target Capability；玩家周圍接觸型能力沿用 SimulateAbility，不新增無用 Router 分支。
7. 若會修改移動、傷害或 Action，接入既有 Modifier／Gate，不另建平行仲裁。
8. 以單機 State Authority、Host／Client、切職業、換 Loadout、能力中途取消與冷卻延續逐項驗證。

## 2026-09-17 局部核對與架構補充

目前資產設定通過驗證；不需為了更新文件再次執行遷移選單。GameLogic 目前重生保存職業，能力使用 startingLoadout；未建立玩家自訂 Loadout 的跨死亡保存規則。此為後續選裝系統的架構邊界，不可宣稱已有持久化。

## 變更紀錄

- 2026-10-04：記錄單一 E 同池、舊空中能力維持原樣、武器資格及強化擴充契約；未修改能力分類、欄位、Runtime 或資產。

- 2026-10-03：同步槍械 E 專注獨立化的待實作方向及未定槽位規則；標明 Support 舊武器強化暫時擱置，未改現行 Loadout。

- 2026-09-15：建立玩家能力 Loadout 專用文件；記錄可變槽位、職業規則、互斥、Fusion Runtime、鈎索路由、SupportAerial 冷卻與資產遷移狀態。

- 2026-09-17：同步本輪局部修正、資產設定與驗證邊界。


## 2026-10-04 E 主動技能原型接入（局部核對）

最後核對日期：2026-10-04（僅本節責任與接點；較早章節為當時基線）。
核對來源：Assets/Scripts/ 下的 PlayerAbilityDefinition、PlayerAbilityRuntimeManager、PlayerActiveAbilityBase、PlayerActiveAbilityAssetSetup。
相關文件：[42 E 主動技能規格與驗收](42_E主動技能規格與強化預留.md)、[驗證紀錄](Validation/ActiveAbilities/README.md)。

保留 GrappleFocus 序列化值 1、原 Slot Layout 與互斥群組；十項 Definition 不限職業。Definition 新增 RequiresRangedWeapon 與空的 enhancementDefinition。Runtime 同步 Phase／timer／ActivationSequence 並提供 IPlayerAbilityHudState；條件 useConditions 預設空。已生成十份 Definition、Runtime、reward、test Loadout 與三個投射物 Prefab，已加入 Fusion PrefabTable；未改 Scene／玩家 Prefab 起始裝備。

變更紀錄：2026-10-04 同步本節結構、資產與權威邊界；強化消耗仍未實作。

## 2026-10-04 E 技能視覺與 UI 接入（局部核對）

最後核對日期：2026-10-04；本節僅核對以下呈現責任。
相關文件：[43 E 技能視覺與 UI 配置](43_E技能視覺與UI配置.md)。

核對來源：PlayerActiveAbilityBase、PlayerBlinkAbility、PlayerAbilityExtensionContracts、ActiveAbilityHudRules。

新 E Runtime 在進入 Casting、Armed、Active 時同步 PhaseDurationSeconds，供 HUD 依原階段長度計算剩餘比例。TimedHudState 是只讀契約，不計冷卻、不發動技能、不啟用強化。既有 Loadout 分類、互斥與老空中技能維持原樣。

## 2026-10-05 E 技能整合穩定化（局部核對）

PlayerAbilityRuntime 在 Spawned 開啟本機生命週期旗標，尚未 Spawn／失效時 OwnerPlayer 回傳 null。若 Owner 引用稍晚才可解析，各端 Render 會重試尚未完成的 Bind，成功後更新職業可用性；遠端 Proxy 不依賴輸入或 HUD 查詢，不重複綁定已完成的模組。權威施放仍由 Host 決定。換 Q 重建整份 Loadout 導致 E 重置，以及投出物在 Owner 消失後的存留規則，保留為待決玩法。

核對來源：本節列出的程式與本輪驗證；詳見 [驗證與待決事項](Validation/ActiveAbilityStabilization/README.md)。

## 2026-10-07 前搖施放契約（局部核對）

最後核對日期：2026-10-07。核對來源：PlayerActiveAbilityBase、PlayerProjectileAbility、PlayerPiercingCannonAbility。按鍵受理僅進入 Casting；正式發射必須在 Activate 到期入口讀當前 Owner KCC 位置／準星，未來投擲與飛行物同樣遵守。既有三項前搖技能已符合此取樣時間，本輪補驗證。未新增 Loadout、RPC 或 Networked 欄位。榴彈碰撞與自傷／友傷配置見 [42](42_E主動技能規格與強化預留.md)，證據見 [驗證紀錄](Validation/AbilityReleaseSafety/README.md)。


## 2026-10-08 衝擊扇波尺寸與位移分離（局部核對）

核對來源：PlayerShockwaveAbility、ShockwavePropagationRules、PlayerShockwaveVisual。既有跨職業 Runtime／NetInput／仲裁管線不變；Host 新增半徑與高度快照、距離改為整片位移上限，Client 只讀呈現。使用者確認固定弧形波牆、上下同寬，並進一步確認前搖結束依瞄準角度整片傾斜、以瞄準座標 XYZ 偏移配置發射點；新增 WaveRotation 同步，空中不吸附地面。設定與驗證見 [44](44_衝擊扇波推進與特效.md)。


## 2026-10-08 E 技能後座力（局部核對）

PlayerAbilityRuntime 新增序列化 Recoil 設定及 ApplyAbilityRecoil 入口，只接受 GrappleFocus、有效 Spawned／Owner／State Authority 與存活玩家。資料隨各技能 Runtime Prefab 配置，不保存到玩家存檔；不需新增元件或變更 Loadout 配置。V2 在 Runtime 新增八個 Networked 欄位：PendingCameraPitch／Yaw、CameraApplicationRemaining、AccumulatedCameraPitch、CameraRecoveryTimer、CameraRecoilSequence、LastCameraApplicationTick、LastCameraRecoveryTick；State Authority 每 Tick 推進，Proxy 沿用 KCC 結果，不補播事件。十項新 E 與兩項舊空中 E 已接成功事件；未使用的舊 AttackFocusAbility 不屬於這十二項獨立 E。詳見 [45](45_E技能後座力配置.md)。


### 2026-10-08 V3 後座力施放事件

後座力的準備與真正施放分離：閃現第一次 E 發 BeforeCast、第二次 E 成功衝刺發 AfterCast；一般新 E 的 Activate 與二次觸發 OnArmedPress 明確回傳成功，失敗不發 AfterCast。舊空中技能仍合併一次；閃現移動後座力不擴充。欄位值與資產不改，詳見 [45](45_E技能後座力配置.md)。


### 2026-10-08 V4：有限時間移動後座力

Runtime 新增 Networked RemainingMovementRecoil、MovementRecoilSeconds、LastMovementRecoilTick、MovementRecoilLifetime。成功事件排入新曲線面積，重觸發合併剩餘距離並重設時間；ConsumeMovementRecoil 僅由權威每 KCC 固定 Tick 消耗一次，Proxy 不重播。失效期限以向上取整的 Tick 加派發餘裕計算，防止正常末段被截短，也防止 KCC 長時間未執行後補發。死亡清除，卸下 Runtime 直接丟棄狀態。

舊 Timing／Speed／Direction 欄位保持，新增 Movement Decay Duration 初始 0.2 秒；無資產遷移或保存。PlayerAbilityRuntimeManager 增加彙總入口，既有仲裁與排序不變。詳見 [45](45_E技能後座力配置.md)。

## 2026-10-10 武器獎勵與換裝（局部核對）

最後核對日期：2026-10-10。核對來源：GameLogic、PlayerProfessionRuntimeManager、PlayerAbilityQualification。

武器獎勵沿用 Profession Runtime，不呼叫能力 Loadout 替換，因此換武器保留 Q／E 的實例、作用狀態與冷卻。精準鎖敵仍按實際遠程武器資格終止／封鎖。這不改變既有領取另一個 Q／E 會替換能力 Loadout 的邊界。

正式規則與 Inspector 入口見 [46 武器獎勵與換裝](46_武器獎勵與換裝.md)，證據見 [驗證紀錄](Validation/WeaponRewards/README.md)。


## 2026-10-11 空中緩速改為按下切換（局部核對）

最後核對日期：2026-10-11。核對來源：SupportAerialAbility、PlayerActiveAbilityBase、PlayerBulletTimeAbility 與兩項 Runtime Prefab（唯讀）。

使用者確認保留 GrappleAirborne 且未落地的啟動條件，改為 E 按下啟動、放開維持、再次按下提前結束。落地、離開 GrappleAirborne、職業不允許、操作封鎖與期限結束仍走 EndAbility，返還保存的原始 DynamicVelocity 並開始完整冷卻；WASD 與後座力位移倍率隨 Active 解除。正式 Loadout 使用原始按鍵歷史，因此預先按住、長按到冷卻結束與解除封鎖都不會自動啟動。

子彈時間作用中再次按下沿原 FinishAbility 提前結算，完整結束規則見 [42](42_E主動技能規格與強化預留.md)。資產不改：SupportAerialAbilityRuntime Prefab 最長 1 秒、CD 5 秒、速度倍率 0.5；BulletTime Prefab 作用 5 秒、CD 3 秒、前搖 0 秒（2026-10-11 磁碟快照；Scene Override 另計）。

驗證與限制：[本輪紀錄](Validation/AbilityToggle/README.md)。

## 2026-10-12 定點捕食原型（局部核對）

PlayerAbilityRuntime 的共用可用性加入 Owner 受困判斷，透過既有 SetProfessionAvailable(false) 結束已啟動／待施放能力，保留裝備及冷卻；清除未施加的移動後座力，釋放後恢復資格。已存在的世界投射物不抹除。

完整規則、原型設定、動畫接口與驗證邊界見 [93 定點捕食植物](93_定點捕食植物.md) 及 [驗證紀錄](Validation/FlytrapEnemy/README.md)。

## 2026-10-13 鈎索側移與 E 閃現互斥（局部核對）

最後核對日期：2026-10-13。核對來源：PlayerGrapple.SideDash、PlayerBlinkAbility。

AA／DD 側移是既有鈎索的移動操作，不新增 E 能力、不佔用 Loadout 槽。側移與 E 閃現的實際衝刺互斥；先待命的閃現仍需合法第二次 E 才發動。同 Tick 先受理側移，拒絕閃現衝刺不消耗其待命。完整規則、獨立設定及驗證見 [32 鈎索飛行雙擊側移](32_鈎索飛行雙擊側移.md) 與 [驗證紀錄](Validation/GrappleSideDash/README.md)。
## 2026-10-13 安全屋操作與玩家 HUD（局部核對）

最後核對日期：2026-10-13。核對來源：PlayerAbilityRuntimeManager、PlayerActiveAbilityBase、LocalPlayerBattleAbilityHUD。

PlayerAbilityRuntimeManager 在安全屋遮罩新舊能力的 Ability1；PlayerActiveAbilityBase 以 ActionGate.ActiveAbility 限制 IsUsable 與直接施放。Loadout／Reward 裝備入口不封鎖，拒絕按 E 不新增冷卻。LocalPlayerBattleAbilityHUD 保留裝備槽並讀 Gate 呈現禁用。

完整規則、責任、場景配置與驗收入口見 [145 安全屋](145_PrePhase4-2_安全屋鍵盤準備與HUD.md)；實際證據與限制見 [驗證紀錄](Validation/SafeHouseGameplay/README.md)。2026-10-13 後續依授權完成 SafeHouse Scene 技能／經驗 HUD 接線；Prefab 未改。

## 2026-10-14 鈎索尖刺（局部核對）

最後核對日期：2026-10-14。核對來源：PlayerGrappleSpikeAbility、GrappleSpikeRules、GrappleSpikeAssetSetup。
新增 GrappleHit 槽位的持續接觸型技能 grapple.spike。此類別是裝備分類：原 Mark／Pull／Gather 仍由 Router 的 Attached 事件觸發；尖刺透過既有 SimulateAbility 讀取有效鈎索狀態並掃掠玩家周圍，不新增 Router Mask。尖刺不加入 GrappleHitRopeAuthority，仍遵守容量與禁止重複裝備。後續新增能力檢查表第 6 點的 Router／Capability 擴充，只適用於需要鉤頭目標事件的能力。
設定與定案見 [33 鈎索尖刺](33_鈎索尖刺技能.md)；實際驗證與限制見 [紀錄](Validation/GrappleSpike/README.md)。



## 2026-10-16 倒地與隊友救援（局部核對）

最後核對日期：2026-10-16。核對來源：PlayerHealth.Rescue、GameLogic.Rescue／RescueBounds、PlayerActionGate.Rescue、Player、PlayerSlideController、PlayerAbilityRuntime、EnemyPerceptionController.Rescue、StageFlowController、LocalPlayerRescueHUD。
PlayerAbilityRuntime 的可用性新增有效已生成 Owner 的健康條件，倒地暫停模組並沿用 SetProfessionAvailable 收尾；不重建 Loadout 或冷卻。Player 分派技能前消耗救援 E，同一次按住必須放開才恢復技能用途。
完整規則、Inspector 與人工接線見 [62](62_倒地與隊友救援.md)；實際驗證見 [紀錄](Validation/PlayerRescue/README.md)。


## 2026-10-16 成長獎勵與技能掉落局部核對

起始 Loadout 只沿用槽位配置，正式新輪不套預設特殊技能；存檔裝備仍由 GameLogic 復原。同類領取只替換該 Runtime，舊物件帶等級與冷卻掉地，另一槽保留；額外專注獨立於槽位。 詳見 [168 角色成長、被動與掉落](168_角色成長被動與技能掉落.md)。

實作／資產與測試證據分開列於 168，未宣稱獨立多程序聯機或 Build 驗收。

## 2026-10-18 右下技能槽狀態呈現（局部核對）

最後核對日期：2026-10-18。核對來源：LocalPlayerBattleAbilityHUD.States、AbilitySlotHudSnapshot 與原技能 Runtime。

右下技能格沿用既有 PlayerAbilityRuntime.TryReadPresentation／IPlayerAbilityCooldownSource 的剩餘與總長。新增技能唯讀 HudCanPress／HudCanRecast／HudCanEndActive 提示，正式受理仍在原技能與 ActionGate。未變更 Loadout、冷卻起點、掉落／拾取、再按結算或權威結果。 完整定案、Inspector 與驗證邊界見 [107 右下技能槽狀態呈現](107_右下技能槽狀態呈現.md)。
