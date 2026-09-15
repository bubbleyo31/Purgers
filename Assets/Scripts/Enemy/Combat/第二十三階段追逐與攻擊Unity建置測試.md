# 第二十三階段：追逐 A／B 與四種攻擊

本手冊只列出本階段新增加的程式、元件與設定，不重複第二十一、二十二階段已完成的血量、死亡、閃白、待機、巡邏、視野與群體警戒設定。

完成本階段後：

- 近戰 A／B 使用 Chase A：依 NavMesh 完整路徑接近玩家，並以個體角度差異減少全部敵人擠向同一個 Root 點。
- 遠程 A／B 使用 Chase B：太遠接近、太近後退、射程內側移，並用距離遲滯避免玩家只移動一點就讓敵人來回抖動。
- 近戰 A 使用短距離快速衝撞。
- 近戰 B 使用長前搖直線衝刺；玩家連續 1.5 秒離開固定衝刺方向的前方 180 度後，敵人才會停止衝刺。
- 遠程 A 使用 Network Projectile 單發子彈。
- 遠程 B 使用「追蹤 1 秒 → 鎖定世界點 0.5 秒 → 瞬間 Beam／Hitscan」。
- 所有傷害都寫入既有 `DamageRequest`／`PlayerHealth` 管線。
- 傷害時機由 Fusion `TickTimer` 決定；Animation Event 只留給音效或特效，不可呼叫傷害。

本階段尚未加入防禦 A／B。近戰 B 與遠程 B 的防禦會在下一個獨立階段接入同一個戰鬥選項仲裁器。

## 1. 匯入順序與精確資料夾

目前實際專案的 `Assets/Scripts/Enemy/` 內只有第二十一階段共用核心；因此必須依下列順序匯入，不能只放第二十三階段。

先退出 Play Mode。

### 1-1. 若尚未匯入第二十二階段

建立資料夾：

`M:/UnityProject/Purgers/Assets/Scripts/Enemy/AI/`

將 `EnemyAwarenessPhase22` 最外層的 10 支 `.cs` 放入 AI 資料夾。不要放入 `.md`、`Verification`、`.csproj`、`bin` 或 `obj`。

等待 Unity 編譯完成，確認 Console 沒有紅字，再匯入第二十三階段。

### 1-2. 匯入本階段

建立資料夾：

`M:/UnityProject/Purgers/Assets/Scripts/Enemy/Combat/`

將本包最外層的 12 支 `.cs` 放入 Combat 資料夾：

| 程式 | 用途 | 掛載位置 |
|---|---|---|
| EnemyCombatCore.cs | 戰鬥 ID、階段、選項基底、共用傷害入口 | 不直接掛載 |
| EnemyCombatDecisionController.cs | 所有攻擊與未來防禦的唯一仲裁者 | Enemy Root |
| EnemyChaseMotor.cs | Chase A／B 的抽象移動基底 | 不直接掛載 |
| EnemyGroundChaseMotor.cs | 地面 Chase A | 近戰 A／B Root |
| EnemyFlyingChaseMotor.cs | 自由飛行 Chase B | 遠程 A／B Root |
| EnemyChaseBrain.cs | 追逐目標、重新規劃與網路狀態 | Enemy Root |
| EnemyMeleeDashAttack.cs | 近戰 A／B 共用可調式直線衝擊 | 近戰 A／B Root |
| EnemyProjectile.cs | 遠程 A 的權威網路子彈 | Projectile Prefab Root |
| EnemyRangedProjectileAttack.cs | 遠程攻擊 A | 遠程 A Root |
| EnemyRangedBeamAttack.cs | 遠程攻擊 B | 遠程 B Root |
| EnemyBeamPresenter.cs | 遠程 B 的 LineRenderer 與發射呈現事件 | 遠程 B VisualRoot |
| EnemyCombatAnimatorDriver.cs | 寫入追逐與攻擊 Animator 參數 | 各敵人 VisualRoot |

不要把 `Verification` 資料夾放入 Unity。

本包不替換第二十一、二十二階段的任何程式。

## 2. 先確認四份 EnemyDefinition

打開四種敵人各自使用的 `EnemyDefinition` 資產，確認：

| Variant | Locomotion Kind | Chase Kind |
|---|---|---|
| 近戰 A | Ground | ChaseA |
| 近戰 B | Ground | ChaseA |
| 遠程 A | FreeFlying | ChaseB |
| 遠程 B | FreeFlying | ChaseB |

`EnemyChaseBrain` 會在 `Spawned()` 驗證 Definition 與 Motor 是否一致。錯配時會主動停止追逐並輸出錯誤，不會偷偷使用另一種移動方式。

## 3. Enemy Base 新增兩個共用元件

打開 Enemy Base Prefab，選取具有下列既有元件的 Root：

```text
NetworkObject
NetworkTransform
EnemyActor
EnemyStateController
EnemyActionGate
EnemyMovementOwnership
EnemyPerceptionController
```

在同一個 Root 新增：

1. `EnemyCombatDecisionController`
2. `EnemyChaseBrain`

兩顆元件的核心引用可以先留空，程式會從同 Root 自動取得。若 Prefab Inspector 因序列化保留舊引用，請確認它們確實指向同一個 Enemy Root，不要指到場景實例。

不要在 Base 直接加入 Ground／Flying Motor，也不要放任一具體攻擊。這些由四個 Variant 分別決定。

## 4. 所有追逐 Motor 的共用設定

地面與飛行 Motor 都使用以下欄位：

| 欄位 | 地面起始值 | 飛行起始值 | 說明 |
|---|---:|---:|---|
| Chase Speed | 5.0 | 4.0 | 實際 Root 位移速度，公尺／秒 |
| Rotation Speed | 300 | 220 | 非攻擊時的水平轉向速度，度／秒 |
| Obstacle Mask | World | World | 只包含牆、地板、柱等實體；不含 Enemy／Player |
| Body Radius | 0.35 | 0.35 | 與巡邏 Motor 及實際身體寬度一致 |
| Body Height | 1.8 | 1.8 | Root 視為膠囊腳底 |
| Collision Skin | 0.02 | 0.02 | 防止掃掠貼死牆面的保留距離 |

`Obstacle Mask` 不可留空，也不可勾 Enemy。若勾 Enemy，掃掠很容易先撞到自己或同伴；若勾 Player，追逐器可能把玩家視為不可抵達障礙而停在攻擊距離外。玩家是否受攻擊由各攻擊元件自己的 Hit Mask 判斷。

追逐、巡邏、Support Pull、Tank Gather、攻擊衝刺都不能同時控制 Root。本版以 `EnemyMovementOwnership`、`EnemyActionGate`、`EnemyStateController` 做控制權交接：

```text
外部拉動 > 攻擊動作 > 追逐 > 巡邏
```

若敵人被 Support 鈎索或 Tank 集怪接管，現行攻擊會取消並進入該攻擊的取消冷卻。

## 5. 近戰 A／B：加入 Chase A

打開近戰 A Variant 與近戰 B Variant，兩者 Root 都新增：

- `EnemyGroundChaseMotor`

同一個 Variant 上必須只有一顆 `EnemyChaseMotor`。原本第二十二階段的 `EnemyGroundPatrolNavigator` 保留；追逐時由狀態與控制權讓巡邏停止，不要刪除巡邏元件。

Ground 額外欄位：

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Agent Type Id | 0 | 必須與地面巡邏及烘焙 NavMesh 的 Agent Type 相同 |
| Nav Mesh Snap Distance | 0.5 | 將自己與接近點投影到 NavMesh 的最大距離；多樓層場景不要設太大 |
| Attack Range Approach Ratio | 0.7 | 接近到攻擊距離帶內約 70%，留下網路與目標移動緩衝 |
| Surround Variation | 0.35 | 每個敵人的接近方向差異；0 代表完全沿最短方向 |

Chase A 每次仍要求 `NavMeshPathStatus.PathComplete`。找不到完整路徑就等待下一次重新規劃，不會穿牆直線追人。

目前 `Surround Variation` 只是分散接近角度，不是正式站位預約。大量敵人仍可能在狹窄門口重疊；之後要再加群體位置 Reservation，不應在此階段用物理推擠硬解。

## 6. 遠程 A／B：加入 Chase B

打開遠程 A Variant 與遠程 B Variant，兩者 Root 都新增：

- `EnemyFlyingChaseMotor`

原本第二十二階段的 `EnemyFlyingPatrolNavigator` 保留。

Flying 額外欄位：

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Range Hysteresis | 1.0 | 射程邊界緩衝，避免玩家微移就切換前進／後退 |
| Ideal Range Ratio | 0.6 | 理想位置偏向攻擊距離帶的外側 |
| Orbit Step Distance | 2.0 | 已在射程內時，每次規劃的側移距離 |
| Vertical Search Offset | 2.0 | 射擊點被擋時，額外測試上下候選位置 |
| Clockwise Chance | 0.5 | 生成時決定每個個體順／逆時針環繞方向 |

Chase B 的規則：

1. 超過最大攻擊距離加緩衝：接近玩家。
2. 小於最小攻擊距離減緩衝：後退。
3. 位於距離帶內：沿固定個體方向側移。
4. 沒有直接視線：嘗試移往同時具有移動通道與玩家視線的候選點。
5. 找不到合法候選點：停下並等待下一次重新規劃，不穿過牆。

這是區域候選點規劃，不是完整三維 A*。封閉房間、迷宮與 L 型走廊仍需要之後的飛行導航圖；本階段先驗證開放戰鬥空間。

## 7. 四個 Variant 的 EnemyChaseBrain

Enemy Base 已有 `EnemyChaseBrain`，請在各 Variant Override：

| Variant | Repath Interval Seconds | Target Movement Repath Distance | Destination Stopping Distance |
|---|---:|---:|---:|
| 近戰 A | 0.18 | 0.75 | 0.15 |
| 近戰 B | 0.22 | 1.0 | 0.18 |
| 遠程 A | 0.35 | 1.25 | 0.25 |
| 遠程 B | 0.4 | 1.5 | 0.25 |

遠程更新刻意比較慢，避免玩家每移動一點，飛行怪就像黏在玩家身上一樣同步平移。`Target Movement Repath Distance` 設 0 會只依固定間隔，不建議第一輪測試這樣做。

`Chase Motor` 引用留空可自動抓同 Root 唯一 Motor。若 Inspector 手動指定，四個 Variant 必須對應自己的 Ground 或 Flying Motor。

## 8. 近戰 A：短距離快速衝撞

在近戰 A Root 新增一顆：

- `EnemyMeleeDashAttack`

### 戰鬥選項

| 欄位 | 起始值 |
|---|---:|
| Option Id | MeleeAttackA |
| Priority | 10 |
| Minimum Start Distance | 0 |
| Maximum Start Distance | 3.2 |
| Requires Direct Sight | 開啟 |

### 近戰衝擊與傷害

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Startup Seconds | 0.25 | 短前搖 |
| Maximum Active Seconds | 0.35 | 最長衝刺時間 |
| Maximum Braking Seconds | 0.15 | 非瞬間停下 |
| Recovery Seconds | 0.35 | 收招不可攻擊／防禦 |
| Cooldown Seconds | 1.4 | 完整動作結束後才開始 |
| Dash Speed | 10 | 公尺／秒 |
| Maximum Dash Distance | 3 | 雙重停止條件之一 |
| Braking Deceleration | 50 | 每秒減速量 |
| Startup Turn Speed | 420 | 前搖可追蹤轉向 |
| Start Facing Half Angle | 75 | 玩家必須在正面 150 度內才開始 |
| Dash Hit Mask | Player + World | 不可包含 Enemy |
| Hit Radius | 0.55 | 依模型寬度再調 |
| Hit Center Height | 0.9 | Root 為腳底時的胸腹高度 |
| Damage | 18 | 第一輪手感值 |
| Cancel When Target Behind Seconds | 0 | 近戰 A 不使用 1.5 秒規則 |

`Dash Hit Mask` 必須同時包含 Player 與場景實體：第一個撞到的物件會結束有效衝刺。這代表玩家躲到柱子後，敵人會撞柱減速，不會隔柱傷害玩家。

## 9. 近戰 B：長前搖直線突進

在近戰 B Root 新增一顆：

- `EnemyMeleeDashAttack`

### 戰鬥選項

| 欄位 | 起始值 |
|---|---:|
| Option Id | MeleeAttackB |
| Priority | 10 |
| Minimum Start Distance | 2.5 |
| Maximum Start Distance | 8.5 |
| Requires Direct Sight | 開啟 |

### 近戰衝擊與傷害

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Startup Seconds | 0.65 | 給玩家讀取動作與閃避時間 |
| Maximum Active Seconds | 1.2 | 無碰撞時的最長有效衝刺 |
| Maximum Braking Seconds | 0.3 | 停止條件成立後平滑減速 |
| Recovery Seconds | 0.6 | 躲過後的反擊窗口 |
| Cooldown Seconds | 3.5 | 完整動作結束後才開始 |
| Dash Speed | 12 | 公尺／秒 |
| Maximum Dash Distance | 10 | 達到即進 Braking |
| Braking Deceleration | 30 | 比近戰 A 慢停，保留重量感 |
| Startup Turn Speed | 300 | 前搖可追蹤，Active 瞬間鎖方向 |
| Start Facing Half Angle | 70 | 玩家須在正面 140 度內 |
| Dash Hit Mask | Player + World | 不可包含 Enemy |
| Hit Radius | 0.6 | 依實際模型調整 |
| Hit Center Height | 0.9 | Root 為腳底時的胸腹高度 |
| Damage | 35 | 第一輪手感值 |
| Cancel When Target Behind Seconds | 1.5 | 玩家回到前方時重新計時 |

近戰 B 進入 Active 後完全不可轉向。停止條件為任一成立：

- 撞到玩家。
- 撞到場景實體。
- 達到 Maximum Active Seconds。
- 達到 Maximum Dash Distance。
- 鎖定玩家連續 1.5 秒位於固定衝刺方向後方。
- 敵人死亡、目標失效、或外部拉動接管。

停止後先進 Braking，再進 Recovery；不是瞬間把速度清成零。

## 10. 遠程 A：建立 Network Projectile Prefab

先建立子彈 Prefab，不要先掛攻擊元件。

### 10-1. 建立階層

```text
EnemyProjectile_RangedA                 ← Root，Scale 必須 (1,1,1)
├─ FlyingVisual                         ← 模型、Trail、飛行特效
└─ ImpactVisual                         ← 命中特效，預設 SetActive(false)
```

### 10-2. Root 元件

在 Root 加入：

1. `NetworkObject`
2. `NetworkTransform`
3. `EnemyProjectile`

不用再加 Collider、Rigidbody 或本地移動腳本。程式每個 Fusion Tick 使用 `SphereCast`，避免高速子彈只靠 Trigger 而穿透。

`EnemyProjectile` 設定：

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Hit Mask | Player + World | 不可含 Enemy，否則可能出生後命中射手 |
| Collision Radius | 0.08 | 依子彈視覺寬度調整 |
| Impact Lifetime Seconds | 0.08 | 命中後保留一小段網路時間 |
| Flying Visual Root | FlyingVisual | 可留空，但建議指定 |
| Impact Visual Root | ImpactVisual | 可留空；指定時初始必須關閉 |

將 Root 做成 Prefab，例如放在：

`M:/UnityProject/Purgers/Assets/Prefabs/Enemy/Projectiles/EnemyProjectile_RangedA.prefab`

建立或移動 Network Prefab 後，依目前 Fusion 專案使用的方式重新掃描／重建 Network Prefab Table。若 Play 時 `Runner.Spawn` 回傳空值，先檢查這一步，而不是更改攻擊程式。

## 11. 遠程 A：掛載攻擊

在遠程 A 模型的穩定武器位置建立：

```text
EnemyRoot
└─ VisualRoot
   └─ Muzzle_RangedA
```

`Muzzle_RangedA` 可以跟著持槍骨架，但不要放在會被關閉的 VFX 物件底下。Local Z 藍軸朝槍口外。

在遠程 A Root 新增：

- `EnemyRangedProjectileAttack`

### 戰鬥選項

| 欄位 | 起始值 |
|---|---:|
| Option Id | RangedAttackA |
| Priority | 10 |
| Minimum Start Distance | 5 |
| Maximum Start Distance | 14 |
| Requires Direct Sight | 開啟 |

### 遠程 A

| 欄位 | 起始值 |
|---|---:|
| Muzzle | Muzzle_RangedA |
| Projectile Prefab | EnemyProjectile_RangedA |
| Active Delay Seconds | 0.35 |
| Recovery Seconds | 0.4 |
| Cooldown Seconds | 1.5 |
| Aiming Turn Speed | 240 |
| Projectile Speed | 18 |
| Projectile Damage | 15 |
| Projectile Lifetime Seconds | 5 |
| Muzzle Forward Offset | 0.1 |

`Active Delay Seconds` 就是動畫中真正出彈的時間。動畫長度與播放倍率改變時，調整這個欄位，不要用 Animation Event 呼叫 `Fire()`。

## 12. 遠程 B：Beam 與 LineRenderer

在遠程 B 建立：

```text
EnemyRoot
└─ VisualRoot
   ├─ Muzzle_RangedB
   └─ BeamLine
```

在 `BeamLine` 加 `LineRenderer`：

| 欄位 | 起始設定 |
|---|---|
| Use World Space | 開啟 |
| Position Count | 2；程式也會維持為 2 |
| Loop | 關閉 |
| Width | 先用 0.02～0.04 |
| Material | 使用你自己的瞄準線材質 |
| Cast Shadows | Off |
| Receive Shadows | Off |

LineRenderer 初始是否 Enabled 不重要，Presenter 會依網路階段控制。但建議 Prefab 編輯狀態先關閉，避免場景裡永遠掛著一條線。

在遠程 B Root 新增：

- `EnemyRangedBeamAttack`

### 戰鬥選項

| 欄位 | 起始值 |
|---|---:|
| Option Id | RangedAttackB |
| Priority | 10 |
| Minimum Start Distance | 8 |
| Maximum Start Distance | 18 |
| Requires Direct Sight | 開啟 |

### 遠程 B

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Muzzle | Muzzle_RangedB | Beam 起點 |
| Beam Hit Mask | Player + World | 不含 Enemy |
| Tracking Seconds | 1.0 | 線端持續跟隨玩家 |
| Locked Delay Seconds | 0.5 | 線端固定在世界座標，玩家可閃避 |
| Recovery Seconds | 0.5 | 發射後收招 |
| Cooldown Seconds | 4.0 | 完整攻擊結束後開始 |
| Cancelled Cooldown Seconds | 0.8 | Tracking 被遮擋時的短冷卻 |
| Shot Beam Visible Seconds | 0.08 | 發射線的顯示時間 |
| Tracking Turn Speed | 180 | Tracking 水平轉向速度 |
| Damage | 35 | Hitscan 基礎傷害 |
| Ray Extension Distance | 1.0 | 鎖定距離後方的小幅延伸 |

在 `BeamLine` 或 `VisualRoot` 新增：

- `EnemyBeamPresenter`

設定：

- Beam Attack：拖入 Root 的 `EnemyRangedBeamAttack`，或留空讓程式往父階層搜尋。
- Combat Decision：拖入 Root 的 `EnemyCombatDecisionController`，或留空自動搜尋。
- Line Renderer：拖入 `BeamLine` 的 LineRenderer。
- On Beam Fired：只接世界槍聲、Muzzle Flash 或 VFX。不可接玩家扣血、狀態切換或 Spawn 第二發子彈。

Tracking 階段失去直接視線會取消並進短冷卻。進入 Locked Delay 後，終點不再跟隨玩家；玩家成功離開那條固定射線就能躲過傷害。此時即使玩家走到另一邊，程式也不會重新瞄準。

## 13. Animator 精確建置

在每種敵人的 `VisualRoot` 或 Animator 所在物件加入：

- `EnemyCombatAnimatorDriver`

引用可留空自動尋找；`Animator` 建議明確拖入正確模型 Animator。Animator 的 `Apply Root Motion` 必須關閉，位置由 Fusion 權威邏輯控制。

### 13-1. 新增 Animator Parameters

保留第二十二階段已有的參數，再新增：

| Parameter | Type | 數值來源 |
|---|---|---|
| ChaseSpeed | Float | 實際追逐公尺／秒 |
| IsUsingCombatAction | Bool | 是否正在執行任一攻擊／防禦 |
| CombatOptionId | Int | 1 近戰A、2 近戰B、3 遠程A、4 遠程B |
| CombatActionPhase | Int | 0 None、1 Startup、2 Active、3 Braking、4 Recovery、5 Tracking、6 LockedDelay |

參數名稱必須完全相同，包含大小寫。若要改名，必須同時修改 `EnemyCombatAnimatorDriver` Inspector 內對應字串，不要直接改 Enum 數值。

### 13-2. Chase State

建立 `Chase` State，放入對應移動動畫，建議使用 Blend Tree：

- Blend Parameter：`ChaseSpeed`
- 0：Idle／停止腳步
- 地面約 2：Walk
- 地面約 5：Run
- 飛行則依動畫資源設 0 與飛行前進值

從第二十二階段的 Alert／Investigate／Patrol 狀態進入 Chase，可用：

```text
IsUsingCombatAction == false
ChaseSpeed > 0.05
```

本階段程式不新增 `IsChasing` Bool。`ChaseSpeed` 為 0 時可以回到警戒待機動畫；實際 AI 狀態仍由 Networked State 決定。

### 13-3. 四個攻擊 State

建立四個 State：

```text
Attack_MeleeA
Attack_MeleeB
Attack_RangedA
Attack_RangedB
```

從 Any State 建立四條 Transition，全部關閉 `Has Exit Time`，Transition Duration 先用 0.05：

| 目標 State | Conditions |
|---|---|
| Attack_MeleeA | IsUsingCombatAction = true；CombatOptionId = 1 |
| Attack_MeleeB | IsUsingCombatAction = true；CombatOptionId = 2 |
| Attack_RangedA | IsUsingCombatAction = true；CombatOptionId = 3 |
| Attack_RangedB | IsUsingCombatAction = true；CombatOptionId = 4 |

每個攻擊 State 回到警戒／Chase 的 Transition：

```text
IsUsingCombatAction == false
```

關閉 Has Exit Time，否則伺服器已完成攻擊時，動畫仍可能把角色留在攻擊姿勢。

不要在動畫 Clip 勾 Loop Time。攻擊實際結束由 TickTimer 控制；如果 Clip 比動作總時間短，Animator 可以停在最後一幀，直到 Bool 關閉。

遠程 B 若想把三段動畫拆開，可建立 Sub-State Machine：

| State | Condition |
|---|---|
| RangedB_Tracking | CombatOptionId = 4；CombatActionPhase = 5 |
| RangedB_Locked | CombatOptionId = 4；CombatActionPhase = 6 |
| RangedB_FireRecovery | CombatOptionId = 4；CombatActionPhase = 4 |

第一輪若只有一支完整攻擊動畫，先只做 `Attack_RangedB`，不必強行拆段。

### 13-4. Animation Event 的限制

可以用 Animation Event 呼叫：

- 起手音效。
- 蓄力聲。
- 腳步聲。
- 槍機細節聲。
- 非權威 Muzzle Flash。

不可用 Animation Event 呼叫：

- 玩家扣血。
- 生成 Network Projectile。
- Beam Hitscan。
- 改 EnemyState。
- 開始冷卻。

多人連線下，不同客戶端 Animator 的取樣時間不保證完全一致；傷害若綁 Animation Event，可能重複或漏掉。

## 14. NetworkObject 與死亡設定

上述 Root 上的 `EnemyCombatDecisionController`、`EnemyChaseBrain`、各具體攻擊都是 `NetworkBehaviour`。儲存 Prefab 後，確認 Unity／Fusion 有重新整理 NetworkObject 的 Behaviour 清單。

若專案的 Fusion Inspector 有 `Networked Behaviours`／`Sort`／`Bake` 或類似按鈕，依既有 Prefab 流程重新執行。不要任意重新排列已上線 Prefab 的 NetworkBehaviour；目前仍在開發期可以重建，但 Host 與 Client 必須使用同一份 Prefab。

死亡時不要只停用整個 Enemy Root，否則 Networked 狀態與死亡 Despawn 計時也會停止。沿用第二十一階段的死亡流程即可：

- `EnemyActor.IsAlive` 變 false。
- Chase 自動停止。
- Active Combat Option 自動取消。
- 死亡呈現由既有流程處理。
- 到設定時間後由 State Authority Despawn。

## 15. 建議測試順序

一次只測一個 Variant，Console 的 Debug 欄位平常關閉，發現問題才打開。

### 測試 1：近戰 A 追逐與短衝撞

1. 只生成一隻近戰 A。
2. 站在可見、NavMesh 可連通位置。
3. 敵人應由 Alert 進 Chase，沿完整路徑靠近。
4. 進入 3.2 公尺內且面向合法後，停止 Chase 並開始攻擊。
5. 0.25 秒後鎖定方向並衝刺。
6. 撞牆不得穿過；撞玩家只扣一次血。
7. Recovery 結束後才回 Chase，Cooldown 結束前不得立刻再攻擊。

### 測試 2：近戰 B 閃避規則

1. 只生成一隻近戰 B。
2. 距離保持約 6 公尺。
3. 看到 0.65 秒前搖後橫向閃避。
4. Active 後敵人不可追著玩家轉彎。
5. 玩家只短暫穿過後方再回前方，不應立刻取消。
6. 玩家連續 1.5 秒保持在固定方向後方，敵人應進 Braking。
7. 撞牆、達最大距離或最大時間也都應進 Braking。

### 測試 3：遠程 A Projectile

1. 只生成一隻遠程 A。
2. 敵人在 5～14 公尺距離帶內應側移，不應每幀跟玩家同步。
3. 發射時只有 State Authority Spawn 一顆 Network Projectile。
4. Host 與 Client 都看見同一顆子彈。
5. 子彈撞牆先顯示 Impact，再 Despawn。
6. 子彈撞玩家只由 State Authority 扣一次血。

### 測試 4：遠程 B Beam

1. 只生成一隻遠程 B。
2. Line 跟隨玩家 1 秒。
3. 接著 Line 終點固定 0.5 秒。
4. 玩家留在線上時受到一次 Hitscan 傷害。
5. 玩家在 0.5 秒內離開固定線時不受傷。
6. Tracking 期間躲到完整牆後，攻擊取消並進 0.8 秒短冷卻。
7. Host 與 Client 看見一致的瞄準線與發射線。

### 測試 5：控制權與生命週期

依序測試每一種敵人：

1. 追逐中被 Support 鈎索。
2. Startup／Tracking 中被 Support 鈎索。
3. 近戰 Active 衝刺中被外部拉動。
4. 攻擊中死亡。
5. 目標玩家死亡或 Despawn。

預期結果都是：攻擊安全取消，不繼續扣血，不與外部拉動同時寫位置，之後依既有狀態恢復或 Despawn。

### 測試 6：兩個程序

最後才使用 ParrelSync／獨立 Build：

- Host 作為敵人 State Authority。
- Client 移動與閃避。
- 兩邊敵人追逐目的地、動作 ID、Beam 線與 Projectile 一致。
- PlayerHealth 只扣一次。
- Client 不會自行 Spawn 額外子彈。

## 16. 第一輪不要同時調整的項目

為了能判斷錯誤來源，第一輪先不要：

- 開 Animator Root Motion。
- 用 Animation Event 補第二套傷害。
- 同一 Variant 同時掛兩顆 Chase Motor。
- 同一 Variant 使用重複 Combat Option ID。
- 把 Enemy 或 Player 加入 Chase Motor 的 Obstacle Mask。
- 把 Enemy 加入攻擊的 Hit Mask。
- 同時加入舊版追逐／攻擊測試腳本。
- 一開始就生成大量敵人。
- 在本階段自行加入防禦 A／B；其選擇優先度與取消規則尚未接入。

先讓四個單體流程分別通過，再測群體。若發生錯誤，請保留完整 Console 第一條紅字、對應 Variant Inspector 截圖，以及當時 Host／Client 身分；不要只看後續連鎖錯誤。
