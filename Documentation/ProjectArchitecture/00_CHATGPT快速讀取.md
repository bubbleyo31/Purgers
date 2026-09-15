# 《除草機》ChatGPT 專案架構快速讀取

> 文件基線：2026-09-15  
> 核對來源：`M:/UnityProject/Purgers/Assets/Scripts`，共 149 支 C#。  
> 用途：新對話先讀本檔，再依任務讀對應模組文件；本檔不是原始碼的替代品。

## 1. 專案技術與權威原則

- Unity 2022.3 LTS。
- Photon Fusion 網路同步；玩家、敵人、投射物、測試生成器等使用 `NetworkObject`／`NetworkBehaviour`。
- 會改變遊戲結果的操作由 `State Authority` 決定：生成、傷害、生命、死亡、Enemy AI、技能正式狀態。
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
- Support 職業 Runtime：`SupportSMG`、`SupportQuickMelee`、`SupportRifleHealingAbility`。
- Tank 職業 Runtime：`TankMeleeCombo`、`TankQuickDashAbility`、`TankGuardAbility`。
- 玩家 Loadout：`SupportAerialAbility`、`TankAirDashAbility` 屬 GrappleFocus；Mark／Pull／Gather 屬 GrappleHit。
- `PlayerQuickActionController` 只調度實作 `IPlayerQuickActionAbility` 的當前職業能力。
- 槽位容量由 `PlayerAbilitySlotLayoutDefinition` 設為 0～N；Fusion 八格只是技術上限。每個 Definition 均可自行開關職業限制與互斥群組。
- `SupportAerialAbility` 已是跨職業 GrappleFocus 能力，冷卻從能力關閉後起算，且不強制依賴 `SupportSMG`。
- 原始碼已支援新 Loadout，但獨立 Definition／Runtime Prefab 要先執行一次 Unity 遷移選單才會建立；遷移完成前仍是舊 Prefab 掛載狀態，不可誤判為資產已搬完。

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

- 地面與飛行路徑分流：Ground 使用 NavMesh；Flying 使用自由飛行探測。
- `EnemyPatrolArea` 是人工節點集合，不是球形隨機點。
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
| 射擊、換彈、瞄準 | `50_武器瞄準與射擊.md` |
| 傷害、治療、死亡、命中回饋 | `60_傷害生命死亡與回饋.md` |
| ViewModel 與 Animator | `70_第一人稱模型與動畫.md` |
| 本地／世界聲音 | `80_聲音系統.md` |
| Enemy 感知、巡邏、追逐 | `90_敵人核心感知巡邏與追逐.md` |
| Enemy 攻擊、死亡表現、測試生成 | `91_敵人攻擊表現與生成.md` |
| HUD、聊天、觀戰 | `100_UI觀戰與聊天.md` |
| 連線、選單、場景工具 | `10_網路連線與輸入.md`、`110_場景工具與選單.md` |

## 10. 新對話工作規則

1. 先讀本檔。
2. 再讀任務對應的模組文件。
3. 文件與實際 C# 不一致時，以實際原始碼為準，先指出漂移，不可直接沿用過時敘述。
4. 改動跨模組公開介面、控制權或資料流時，同步更新本檔、對應模組文件與 `02_程式職責總表.md`。
5. 不因類名不漂亮就建立第二套平行系統；先確認既有仲裁層與遷移成本。
6. 新增 Networked 欄位、RPC 或 Spawn 流程時，明確標示 State Authority、Input Authority、Render 三者的責任。
