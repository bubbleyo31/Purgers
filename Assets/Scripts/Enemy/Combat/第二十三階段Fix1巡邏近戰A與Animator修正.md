# 第二十三階段 Fix 1：巡邏、地面追逐、近戰 A 與 Animator

本文件只列出這次新增或改變的部分。遠程 A／B 與近戰 B 尚未實測，因此本次不修改它們的數值與行為。

## 1. 問題結論

### 巡邏大部分時間不發動

原版 `Patrol Decision Chance` 預設 0.3，代表每次待機判斷有 70% 只會原地轉頭。若抽到巡邏但所有點暫時無法取得完整 NavMesh 路徑，又會回到轉頭，因此很像永久卡住。

Fix 1 增加 `Maximum Consecutive Look Decisions`：連續觀察達到上限後，下一次會強制嘗試巡邏，直到找到合法路徑。仍保留隨機待機感，不把巡邏機率硬改成 100%。

同時修正兩項容錯：

- `TryBegin()` 會自行刷新 Grounded，不再依賴不同 NetworkBehaviour 的執行先後。
- 只有成功切換成 Patrol State 後才提交目的地，避免留下「有巡邏目的地但 Brain 還是 Idle」的半完成狀態。

### 玩家跳躍後地面怪停止 Chase

`G00～G03` 的 Y=0 並不是錯誤。地面巡邏點本來就應位於 NavMesh 表面。

真正原因是原版 Ground Chase 把玩家正在空中的 Y 也拿去 `NavMesh.SamplePosition()`。玩家高度超過 `Nav Mesh Snap Distance` 後，地面怪會認為目標附近沒有 NavMesh，導致 `ChaseSpeed` 歸零。

Fix 1 改成：

- 看得到玩家時，只取玩家的 XZ。
- Y 使用敵人目前所在 NavMesh 表面高度。
- 看不到玩家時，使用最後已知位置，不讀取牆後玩家的即時 Transform。

所以地面怪不需要也不應改讀 `Patrol_Air_RoomA`。

## 2. 替換與新增檔案

先退出 Play Mode。

### 替換三支既有程式

| Fix 1 檔案 | 覆蓋到 Unity 位置 |
|---|---|
| EnemyIdlePatrolBrain.cs | `M:/UnityProject/Purgers/Assets/Scripts/Enemy/AI/EnemyIdlePatrolBrain.cs` |
| EnemyGroundPatrolNavigator.cs | `M:/UnityProject/Purgers/Assets/Scripts/Enemy/AI/EnemyGroundPatrolNavigator.cs` |
| EnemyGroundChaseMotor.cs | `M:/UnityProject/Purgers/Assets/Scripts/Enemy/Combat/EnemyGroundChaseMotor.cs` |

### 新增兩支程式

| Fix 1 檔案 | 放入 Unity 位置 |
|---|---|
| EnemyMeleeSwingAttack.cs | `M:/UnityProject/Purgers/Assets/Scripts/Enemy/Combat/EnemyMeleeSwingAttack.cs` |
| EnemyPresentationAnimatorDriver.cs | `M:/UnityProject/Purgers/Assets/Scripts/Enemy/Presentation/EnemyPresentationAnimatorDriver.cs` |

等待 Unity 編譯完成，Console 沒有紅字後再改 Prefab。

## 3. 巡邏 Inspector 新設定

在近戰 A 的 `EnemyIdlePatrolBrain` 設定：

| 欄位 | 建議值 |
|---|---:|
| Minimum Idle Decision Seconds | 1.5 |
| Maximum Idle Decision Seconds | 3.5 |
| Patrol Decision Chance | 0.6 |
| Maximum Consecutive Look Decisions | 2 |
| Patrol Arrival Distance | 0.35 |
| Maximum Patrol Seconds | 30 |
| Debug Idle Patrol | 測試時開啟 |

預期節奏：敵人仍可能先原地觀察，但最多連續兩次；再下一次決策會強制嘗試巡邏。

如果 Console 出現「本次所有巡邏點都太近、超出範圍、未落在 NavMesh，或沒有完整路徑」，逐項檢查：

1. `Patrol Area Id` 必須和場景 `EnemyPatrolArea.Area Id` 完全相同。
2. G00～G03 不可放在 Enemy Root 底下。
3. G00～G03 要落在藍色 NavMesh 表面；平地是 Y=0 就維持 Y=0。
4. `Maximum Point Distance` 必須涵蓋敵人到節點的三維距離。
5. `EnemyGroundPatrolNavigator.Obstacle Mask` 必須包含地板與牆壁，不含 Enemy／Player。
6. Root 的 Rigidbody 若存在，必須 `Is Kinematic=true`。
7. Root 不可同時啟用 `NavMeshAgent`。

測試通過後再關閉 `Debug Idle Patrol`。

## 4. 近戰 A 改成原地揮擊

打開近戰 A Prefab Variant 的 Root：

1. 移除 `EnemyMeleeDashAttack` 元件。
2. 新增 `EnemyMeleeSwingAttack`。
3. 近戰 B 保留 `EnemyMeleeDashAttack`，不要跟著移除。

可在模型胸口或武器前方建立穩定空物件：

```text
Enemy_MeleeA_Root
└─ VisualRoot
   └─ MeleeAttackOrigin
```

建議 `MeleeAttackOrigin` 位於胸腹高度，跟著角色朝向即可。若武器骨架擺動幅度很大，不要掛在刀尖骨架；傷害判定應保持穩定。

### EnemyMeleeSwingAttack 設定

戰鬥選項：

| 欄位 | 建議值 |
|---|---:|
| Option Id | MeleeAttackA |
| Priority | 10 |
| Minimum Start Distance | 0 |
| Maximum Start Distance | 2.2 |
| Requires Direct Sight | 開啟 |

時間：

| 欄位 | 建議值 | 說明 |
|---|---:|---|
| Active Delay Seconds | 依動畫接觸幀計算 | 從攻擊開始到真正碰到玩家 |
| Active Window Seconds | 0.1 | 只判定一次，不會持續扣血 |
| Recovery Seconds | 0.45 | 收招時間 |
| Cooldown Seconds | 1.2 | 整個攻擊完成後才開始 |
| Startup Turn Speed | 420 | 前搖可以繼續面向玩家 |
| Start Facing Half Angle | 80 | 玩家須位於正面 160 度內 |

判定：

| 欄位 | 建議值 |
|---|---:|
| Attack Origin | MeleeAttackOrigin；也可留空 |
| Swing Hit Mask | Player + World，不含 Enemy |
| Hit Radius | 0.55 |
| Attack Reach | 1.6 |
| Hit Center Height | 0.9；Attack Origin 留空時才使用 |
| Close Range Backstep | 0.35 |
| Damage | 18 |

行為會是：

```text
Chase A 靠近
→ 距離與正面角度合法
→ 停止移動並播放 Startup
→ Active Delay 到期時鎖定揮擊方向
→ SphereCast 只判定一次
→ Active Window
→ Recovery
→ 完成後開始 Cooldown
→ 回 Chase
```

牆與玩家同時位於揮擊方向時，較近的牆會擋住攻擊。Animation Event 只能放揮刀聲，不能扣血。

### Active Delay 公式

```text
Active Delay Seconds
= 動畫接觸幀 ÷ 動畫 FPS ÷ Animator State Speed
```

例如第 8 幀接觸、24 FPS、State Speed 1.0：

```text
8 ÷ 24 ÷ 1.0 = 0.333 秒
```

## 5. Animator：刪除重複職責

截圖中的確有重複：

- `rig idle` 是零速待機。
- `rig walk` 是巡邏移動。
- 右側 `BlendTree` 又同時包含零速／移動。
- `MoveSpeed` 與 `ChaseSpeed` 分別由兩顆 Driver 寫入。

這會讓多條 Transition 同時成立，也讓 Idle 動畫在獨立 State 與 Blend Tree 內重複出現。

Fix 1 改成由單一 `EnemyPresentationAnimatorDriver` 寫入全部呈現資料。

### 5-1. VisualRoot 元件

在近戰 A 的 Animator 所在物件：

1. 移除元件 `EnemyAwarenessAnimatorDriver`。
2. 移除元件 `EnemyCombatAnimatorDriver`。
3. 不用刪除兩支 `.cs`，其他尚未修改的 Prefab 暫時仍可使用。
4. 新增 `EnemyPresentationAnimatorDriver`。
5. Animator 引用拖入近戰 A 的 Animator。
6. 其他引用可以留空自動搜尋。
7. `Apply Root Motion` 關閉。

若舊 Driver 的 UnityEvent 已經接了聲音，先把相同事件重新接到新 Driver 的 `On Announcement`／`On Combat Action Started`，再移除舊元件。

### 5-2. Animator Parameters

保留或新增：

| 名稱 | Type |
|---|---|
| IsAlive | Bool |
| IsAlerted | Bool |
| IsAnnouncing | Bool |
| MoveSpeed | Float |
| BrainState | Int |
| IsUsingCombatAction | Bool |
| CombatOptionId | Int |
| CombatActionPhase | Int |

確認沒有 Transition 再使用 `ChaseSpeed` 後，可以刪除舊的 `ChaseSpeed` Parameter。新 Driver 的 `MoveSpeed` 已經取巡邏速度與追逐速度兩者中的有效值。

### 5-3. 建議狀態機

不要再保留獨立的 `rig idle`、`rig walk`、`AlertIdle` 加上一個通用 `BlendTree`。改成兩個職責清楚的 Locomotion Blend Tree：

```text
Entry
  ↓
Locomotion_Unaware ←→ Locomotion_Alerted
        ↑                    ↑
        └──── MeleeA ────────┘

Any State → Detection
Any State → MeleeA
Any State → Dead
```

#### Locomotion_Unaware

建立 1D Blend Tree：

- Parameter：`MoveSpeed`
- Threshold 0：`rig idle`
- Threshold 2：`rig walk`

它同時處理 Idle 與 Patrol，不需要另外做一顆 `rig idle` State 與一顆 `rig walk` State。

#### Locomotion_Alerted

建立另一個 1D Blend Tree：

- Parameter：`MoveSpeed`
- Threshold 0：`AlertIdle`
- Threshold 5：Chase／Run 動畫

它同時處理站立警戒與追逐。玩家跳起時，Ground Chase 仍會朝玩家 XZ 接近；只有已經到達玩家正下方、確實無法再靠近時，速度才合理地回到 0。

### 5-4. Transition 條件

`Locomotion_Unaware → Locomotion_Alerted`：

```text
IsAlerted == true
IsUsingCombatAction == false
```

`Locomotion_Alerted → Locomotion_Unaware`：

```text
IsAlerted == false
IsUsingCombatAction == false
```

`Any State → Detection`：

```text
IsAnnouncing == true
IsAlive == true
```

`Detection → Locomotion_Alerted`：

```text
IsAnnouncing == false
IsAlerted == true
```

`Any State → MeleeA`：

```text
IsUsingCombatAction == true
CombatOptionId == 1
IsAlive == true
```

`MeleeA → Locomotion_Alerted`：

```text
IsUsingCombatAction == false
IsAlerted == true
```

`MeleeA → Locomotion_Unaware`：

```text
IsUsingCombatAction == false
IsAlerted == false
```

上述 Transition 全部先關閉 `Has Exit Time`，Duration 使用 0.03～0.08。`Any State → MeleeA` 關閉 `Can Transition To Self`，防止同一次動作反覆重進 State。

Dead 的 Transition 沿用原本設定，但它應擁有最高優先權：

```text
IsAlive == false
```

不要把 State 連到紅色 `Exit`；一般角色 Animator Controller 不需要靠 Exit 結束角色狀態機。

## 6. 本輪測試範圍

這次只測近戰 A：

1. 不讓玩家出現，觀察 30～60 秒；敵人最多連續兩次原地觀察後應嘗試巡邏。
2. 每次抵達 G 點後應回到 Unaware Locomotion 的 Idle，稍後再次決策。
3. 玩家跳躍與鈎索時，地面怪仍沿 NavMesh 朝玩家 XZ 追逐，不改用 Air Room。
4. 近戰 A 靠近後停下揮擊，Root 不應向前 Dash。
5. 揮擊一次最多扣一次血；躲出判定或隔著牆應揮空。
6. Host 與 Client 看到相同 Brain／攻擊狀態，只有 State Authority 套用傷害。

近戰 A 通過後，再開始測近戰 B 與遠程 A／B。這次先不要為了讓未測項目看起來正常而一起修改它們。
