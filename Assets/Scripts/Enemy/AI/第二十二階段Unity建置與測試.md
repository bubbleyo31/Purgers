# 第二十二階段：待機、巡邏、視野與同伴警戒

本手冊只列本階段新增的程式、元件與設定。第二十一階段已通過的血量、閃白、死亡核心保留。

本階段完成後，四種敵人會原地觀察、選擇人工巡邏節點、實際移動、發現玩家，以及因同伴呼喚或受傷而進入警戒。取得呼喚權的敵人會短暫停下演出，其他同伴直接進入 Chase 狀態。

**Chase 現在是交接給下一階段的狀態：本包尚未實作跟隨玩家、包圍、攻擊、防禦。看到 Chase 後敵人停止巡邏是本階段的預期行為。**

## 1. 匯入程式：新增，不替換上一階段

先退出 Play Mode。

在 Unity Project 視窗依序開啟 Assets → Scripts → Enemy，右鍵 Create → Folder，命名為 AI。

精確目標資料夾：

`M:/UnityProject/Purgers/Assets/Scripts/Enemy/AI/`

將本包最外層的 **10 個 .cs** 全部放進此資料夾：

| 程式 | 用途 | 掛載位置 |
|---|---|---|
| EnemyPatrolArea.cs | 人工巡邏點集合 | 場景巡邏區物件 |
| EnemyIdlePatrolBrain.cs | 待機與巡邏決策 | Enemy Root |
| EnemyPatrolNavigator.cs | 導航抽象基類 | 不直接掛載 |
| EnemyGroundPatrolNavigator.cs | 地面 NavMesh 巡邏與落地重力 | 近戰 A／B Root |
| EnemyFlyingPatrolNavigator.cs | 三維直線巡邏、通道檢查 | 遠程 A／B Root |
| EnemyMovementOwnership.cs | 檢查 AI 與 Support／Tank 的位移控制權 | Enemy Root |
| EnemyPerceptionController.cs | 玩家視野與位置記憶 | Enemy Root |
| EnemyAlertDirector.cs | 場景群組呼喚協調 | 場景 EnemySystems |
| EnemyAwarenessBrain.cs | 發現、受傷警戒、共享情報 | Enemy Root |
| EnemyAwarenessAnimatorDriver.cs | 動畫與本機呼喚事件 | VisualRoot／Animator 所在物件 |

等待 Unity 編譯完畢，Console 沒有紅字後再繼續。不要把 Verification 資料夾、.csproj、.dll 放入 Assets。

本包沒有改動 EnemyActor、EnemyDefinition、EnemyStateController、EnemyActionGate 或 TestDamageReceiver。

## 2. 先準備獨立測試區

建議先在現有遊戲場景的一小塊平地測試。所有敵人仍由現有 Fusion 場景載入／Runner.Spawn 流程生成，不使用 Instantiate 代替 Network Spawn。

建議階層：

```text
GameScene
├─ EnemySystems
│  └─ EnemyAlertDirector
├─ Patrol_Ground_RoomA
│  ├─ G00
│  ├─ G01
│  ├─ G02
│  └─ G03
├─ Patrol_Air_RoomA
│  ├─ F00
│  ├─ F01
│  ├─ F02
│  └─ F03
├─ 地板與牆壁
└─ 四個 Enemy Prefab 的場景實例
```

上圖 EnemyAlertDirector 代表 EnemySystems 上的元件，不必再建一個同名子物件。

巡邏區與節點必須留在場景，不能放進 Enemy Root：否則敵人移動時，巡邏點也跟著移動。

本版目標是 Host／Server 作為敵人 State Authority；尚未處理 Shared Mode 的 AI 權限轉移或 Host Migration。本機物理查詢使用 Runner 的 PhysicsScene；地面 NavMesh 仍是 Unity 全域導航資料，同一程序內重疊的多 Runner 場景不在本次驗證範圍。ParrelSync 分開程序測試可以沿用。

## 3. 設定場景 Layer

在 Inspector 上方 Layer 下拉 → Add Layer，確認有專門用來代表場景實體障礙物的 Layer。名稱可以沿用你現有的 World／Environment；以下以 World 為例。

1. 地板、牆壁、柱子等真正應擋視線／移動的 Collider 所在物件設為 World。
2. 玩家維持 Player Layer。
3. 敵人維持 Enemy Layer。
4. 純特效、觸發區、巡邏空物件不必設為 World。
5. 檢查的是 **Collider 實際所在的物件**，只改父物件而未套用到子物件會漏判。

之後兩個 Mask 都勾相同的場景 Layer：

- EnemyPerceptionController → Occlusion Mask。
- Ground／Flying Navigator → Obstacle Mask。

不要勾 Enemy，避免掃掠碰到自己的身體。也先不要勾 Player；本階段沒有完成敵人與玩家／其他敵人的群體避讓。

Mask 留空時程式會警告並暫停該功能，不會假裝檢查過牆壁。

## 4. 建立唯一的 EnemyAlertDirector

1. Hierarchy 空白處右鍵 → Create Empty。
2. 命名 EnemySystems。
3. Add Component → EnemyAlertDirector。
4. 這個場景只放一個。
5. 不需要 NetworkObject、NetworkTransform、Collider 或 AudioListener。

所有敵人註冊到此元件，並按照 Runner 與 Alert Group ID 分組。

Prefab 資產不能保存對場景 EnemySystems 的引用。因此 EnemyAwarenessBrain 的 Alert Director 在 Prefab 裡先留空，Spawned 時會尋找同 Unity Scene 的唯一 Director。

若使用 Additive Scene，請讓敵人與其 Director 位於同一個 Scene；或在生成後、Spawned 之前建立你自己的明確綁定流程。本包沒有自動跨 Scene 配對。

## 5. 建立地面巡邏區

1. Hierarchy → Create Empty，命名 Patrol_Ground_RoomA。
2. Add Component → EnemyPatrolArea。
3. Area Id 填入：ground_room_a。
4. Maximum Point Distance 先填 15。
5. Draw Gizmos 勾選。
6. 在該物件底下建立四個空物件，命名 G00、G01、G02、G03。
7. 將它們放在地板上，距離彼此約 3～6 公尺，避開牆邊與狹窄角落。
8. 選回 Patrol_Ground_RoomA。
9. 展開 Patrol Points，Size 設 4。
10. 將 G00～G03 依序拖入 Element 0～3。

假設平地表面 Y=0，可先使用以下世界座標；依你關卡位置平移整組即可：

| 節點 | 世界座標範例 |
|---|---|
| G00 | (0, 0, 0) |
| G01 | (4, 0, 0) |
| G02 | (4, 0, 4) |
| G03 | (0, 0, 4) |

這些是人工節點，Maximum Point Distance 只是過濾太遠的節點。沒有在大球裡隨機抽位置。

不要讓同一個 Patrol Area ID 在同一個 Scene 出現兩份。需要第二區時改成 ground_room_b。

## 6. 近戰用的地面 NavMesh

我讀到目前專案是 Unity 2022.3.62f1，manifest 尚未列出 AI Navigation 套件。本包只依賴內建 UnityEngine.AI 路徑 API，不要求立刻安裝新套件。

若目前 Unity 有 Window → AI → Navigation (Obsolete)，可以先使用現有烘焙流程：

1. 開啟上述 Navigation 視窗。
2. 選取測試用地板與靜態牆壁，在 Object 分頁勾選 Navigation Static。
3. 地板使用 Walkable；不要把敵人、玩家或巡邏空物件設成導航靜態物件。
4. 到 Bake 分頁。
5. Agent Radius 先用 0.35，Agent Height 用 1.8，Max Slope 用 45，Step Height 用 0.2。
6. 按 Bake。
7. Scene 顯示藍色可走區域後，確認 G00～G03 與近戰怪腳底都落在藍色區域內。
8. 先測連通平地，不建立跳躍、攀爬或 OffMeshLink。

選單與烘焙流程可對照 [Unity 2022.3 NavMesh 建置文件](https://docs.unity.cn/Manual/nav-BuildingNavMesh.html)。

若你的 Unity 已改用 AI Navigation／NavMeshSurface，則以 Surface 的 Bake 為準：Agent Type 選 Humanoid、Use Geometry 選 Physics Colliders、Include Layers 只包含場景，完成後檢查同一片藍色可走區。**不要在同一區域重複烘焙兩套重疊 NavMesh。** 可對照 [NavMeshSurface 官方欄位說明](https://docs.unity3d.com/Packages/com.unity.ai.navigation@1.1/manual/NavMeshSurface.html)。

這次敵人不掛 NavMeshAgent。EnemyGroundPatrolNavigator 讀取烘焙資料、計算完整路徑，再由 Fusion Tick 移動 Root。

## 7. 建立飛行巡邏區

依第 5 節建立 Patrol_Air_RoomA：

- Area Id：air_room_a。
- Maximum Point Distance：15。
- Patrol Points：四個空中節點。
- 節點必須是敵人 **Root／膠囊底部** 要到達的位置，不是模型中心。

平地 Y=0 時，可先用：

| 節點 | 世界座標範例 |
|---|---|
| F00 | (0, 3, 0) |
| F01 | (4, 4, 0) |
| F02 | (4, 5, 4) |
| F03 | (0, 3, 4) |

飛行版不需要 NavMesh。它能做 XYZ 移動，先掃掠整段膠囊通道；被牆遮住的候選點不採用，會檢查下一個節點。

目前不是完整三維繞障尋路：想繞過 L 形轉角，請把中間節點放在轉角外的可通行空間，讓敵人能先到該點、稍後再選轉角後的點。此版不保證指定順序，也不會搜尋通往牆後任意目標的全域路線；這部分留給後續飛行追逐導航。

## 8. 修改 Enemy Base Prefab：只加共用元件

在 Project 雙擊上一階段的 Enemy Base，進入 Prefab Mode。

選擇具有 NetworkObject、NetworkTransform、EnemyActor 的 **Root**，Add Component 加入：

1. EnemyMovementOwnership。
2. EnemyIdlePatrolBrain。
3. EnemyPerceptionController。
4. EnemyAwarenessBrain。

Enemy Base 先不加 Ground 或 Flying Navigator，因為四個變體的位移方式不同。Base 不用直接生成測試，請使用正式 Variant。

在 Root 下 Create Empty，命名 EyePoint：

- Local Position 先設 (0, 1.5, 0)。
- Local Rotation 設 (0, 0, 0)。
- Local Scale 設 (1, 1, 1)。
- EyePoint 放在穩定 Root 下，不放在會左右晃動的動畫頭骨下。
- 敵人模型的前方應對齊 Root 藍色 Z 軸；本版視野依 Root 朝向判斷。

若敵人高度不同，各 Variant 調整 EyePoint 的 Local Y。

## 9. Enemy Root 的碰撞與 Rigidbody

本 Motor 以 Root 腳底為基準，Root Scale 維持 (1,1,1)。

如果 Root 有 Rigidbody：

| 欄位 | 本階段設定 |
|---|---|
| Is Kinematic | 勾選 |
| Use Gravity | 關閉 |
| Interpolate | None，位移呈現交給 NetworkTransform |

地面版由 Navigator 提供向下重力；飛行版保持懸浮。Support Pull／Tank Gather 執行時，新 Motor 會讓出位移與旋轉控制；解除後地面怪若在空中會往下落，不會立即傳送到 NavMesh。

Root 可以沒有 Rigidbody；是否保留，依既有命中／鈎索設定決定。不要為了此包刪掉原本的碰撞器或生命元件。

若使用 CapsuleCollider，初始尺寸可與導航膠囊一致：

- Direction：Y Axis。
- Radius：0.35。
- Height：1.8。
- Center：(0, 0.9, 0)。
- 是否 Is Trigger 沿用原本戰鬥設計；導航掃掠另外檢查場景。

如果模型不符合此尺寸，同時調整 Collider、Navigator Body Radius／Height，以及地面 NavMesh 烘焙尺寸。

停用任何原本會移動／旋轉該 Root 的測試腳本、NavMeshAgent 與 Animator Root Motion。不要讓它們與新 Motor 同時控制位置。

## 10. 四個 Variant 分別加 Navigator

依序打開四個正式 Prefab Variant。

| Variant | 新增元件 | Definition Locomotion | Patrol Area ID |
|---|---|---|---|
| 近戰 A | EnemyGroundPatrolNavigator | Ground | ground_room_a |
| 近戰 B | EnemyGroundPatrolNavigator | Ground | ground_room_a |
| 遠程 A | EnemyFlyingPatrolNavigator | FreeFlying | air_room_a |
| 遠程 B | EnemyFlyingPatrolNavigator | FreeFlying | air_room_a |

每個 Root 只能有一個 Navigator，不要同時加 Ground 與 Flying。

Ground／Flying 共用欄位：

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Patrol Speed | 地面 2、飛行 2.5 | 公尺／秒 |
| Rotation Speed | 180 | 度／秒 |
| Obstacle Mask | 場景實體 Layer | 不含 Enemy／Player |
| Body Radius | 0.35 | 與身體寬度一致 |
| Body Height | 1.8 | 至少兩倍半徑 |
| Collision Skin | 0.02 | 保留碰撞間距 |

Ground 額外欄位：

| 欄位 | 起始值 | 說明 |
|---|---:|---|
| Agent Type Id | 0 | 預設 Humanoid；自訂 Type 要填真實 ID |
| Nav Mesh Snap Distance | 0.3 | 腳底與節點找最近 NavMesh 的距離 |
| Maximum Step Projection | 0.25 | 單步投影容差 |
| Gravity | 25 | 離地向下加速度 |
| Terminal Fall Speed | 30 | 向下速度上限 |
| Grounded Probe Distance | 0.08 | 腳底接地容差 |

先測平地。陡坡、階梯、動態地板、大落差與狹窄轉角尚需實際場景驗證；膠囊掃掠過不去時會停止該次巡邏，回 Idle 重選，不會持續硬擠。

## 11. EnemyIdlePatrolBrain 欄位

在四個 Variant 的 Root 分別設定：

| 欄位 | 起始值／拖入物件 |
|---|---|
| Enemy Actor | 同 Root 的 EnemyActor |
| Patrol Area | Prefab 資產留空；場景實例可拖對應區域 |
| Patrol Area Id | 按第 10 節填 ground_room_a 或 air_room_a |
| Navigator | 同 Root 的 Ground 或 Flying Navigator |
| Minimum Idle Decision Seconds | 1.5 |
| Maximum Idle Decision Seconds | 3.5 |
| Patrol Decision Chance | 初測 1；通過後改回 0.3 |
| Turn Around Chance | 0.15 |
| Minimum Look Turn Degrees | 25 |
| Maximum Look Turn Degrees | 75 |
| Turn Speed Degrees Per Second | 120 |
| Patrol Arrival Distance | 0.35 |
| Maximum Patrol Seconds | 30 |
| Debug Idle Patrol | 初測勾選 |

驗證待機轉向時把 Patrol Decision Chance 設 0。驗證巡邏時設 1。正式再回到 0.3。

本版巡邏中途受阻會回 Idle，等待一段時間後重新選擇；沒有合法點時留在 Idle，不做無限抽選。受到外部移動控制後會重建路徑，不沿著被拉動之前的舊拐點硬走。

## 12. EnemyPerceptionController 欄位

| 欄位 | 起始值／拖入物件 |
|---|---|
| Enemy Actor | 同 Root 的 EnemyActor |
| Eye Point | Root 下的 EyePoint |
| Fallback Eye Height | 1.5；只有 Eye Point 空白時使用 |
| Sight Distance | 20 |
| Horizontal Field Of View | 120 |
| Vertical Field Of View | 100 |
| Occlusion Mask | 場景實體 Layer |
| Player Target Height | 1.1 |
| Scan Interval Seconds | 0.1 |
| Target Memory Seconds | 3 |
| Current Target Preference | 2 |
| Debug Perception | 初測勾選 |

玩家來源沿用 GameLogic 已建立的 Runner 玩家綁定，因此不需要給 Player 加新腳本或 Tag。

Player Target Height 是從玩家 Root 向上算，請依你 Player Root 的實際位置調整；避免觀測點落在腳下或頭頂上方。

視野目前使用單一胸口觀測點；玩家露出一隻手但胸口被遮住時，可能仍判為不可見。後續可擴充多觀測點，這不影響現在的目標記憶介面。

## 13. EnemyAwarenessBrain 欄位

| 欄位 | 起始值／拖入物件 |
|---|---|
| Enemy Actor | 同 Root 的 EnemyActor |
| Perception | 同 Root 的 EnemyPerceptionController |
| Idle Patrol Brain | 同 Root 的 EnemyIdlePatrolBrain |
| Alert Director | Prefab 留空；場景實例可指定 EnemySystems |
| Announcement Duration | 0.8；之後對齊實際發現動畫 |
| Alert Group Id | 同房間四種敵人先都填 room_a |
| Alert Radius | 18 |
| Group Announcement Cooldown | 4 |
| Damage Alert Interval | 1 |
| Investigation Seconds | 4 |
| Debug Awareness | 初測勾選 |

Patrol Area ID 跟 Alert Group ID 是兩件事。地面、飛行有不同巡邏區，仍可透過相同 room_a 互相呼喚。

請讓同群組敵人的 Group Announcement Cooldown 使用一致值，且不要小於最長發現動畫時間。

行為規則：

1. 自己初次看見玩家 → 分享情報。
2. 取得群組演出權 → Alert／Announcing，短暫停止巡邏、攻擊與防禦。
3. 演出期間結束 → Chase／Alerted。
4. 同伴收到共享情報 → 不播放發現演出，直接 Chase。
5. 護盾尚未實作；目前有效非致死扣血會在下一個 AI Tick 通知附近同伴。補血不觸發。
6. 已在攻擊／防禦中的敵人受傷只分享情報，不打斷動作。
7. 呼喚冷卻只抑制演出，不抑制新的情報分享。
8. 收到共享情報的同伴不再次轉播，避免一路喚醒整張地圖。
9. 玩家遮擋／走出視野 → 保留當時的位置 3 秒。
10. 記憶失效 → 原地 Investigate 4 秒，之後回 Idle。
11. 期間重新看見玩家 → 重新警戒。
12. 玩家死亡、離線、Despawn → 清除舊物件；不把同 PlayerRef 的重生玩家直接接成舊目標。

本次 Investigate 是原地警戒計時；真正前往最後位置搜索、再返回出生區的路徑會隨追逐階段加入。

## 14. 動畫設定：先使用最小可測 Controller

每一種模型可使用自己的 Animator Controller，參數名稱保持一致即可。若已有 Controller，新增以下參數與轉場，不必刪除原本動畫。

若要建立新 Controller：

1. Project 到你存放 Enemy 動畫的資料夾。
2. 右鍵 Create → Animator Controller。
3. 命名 AC_Enemy_Melee_A；其他變體可各自建立。
4. 拖到該模型 Animator 的 Controller 欄位。
5. Animator 的 Apply Root Motion 關閉。
6. 骨架、Avatar 沿用模型原設定；此包不要求 Humanoid。
7. 雙擊 Controller，開啟 Animator 視窗。

Parameters 新增四個：

| 名稱，大小寫必須一致 | 型別 | 初始值 |
|---|---|---|
| IsAlive | Bool | true |
| IsAlerted | Bool | false |
| IsAnnouncing | Bool | false |
| MoveSpeed | Float | 0 |

本版沒有 Discover Trigger；持續狀態用 IsAnnouncing 控制，避免錯過一次 Trigger 就無法呈現正確狀態。

新增五個 State，名稱可照下列設定：

| State | Motion | Loop |
|---|---|---|
| Idle | 待機 Clip | 開 |
| Patrol | 走路／飛行移動 Clip | 開 |
| Discover | 發現／呼喚 Clip | 關 |
| AlertIdle | 戰鬥待機；還沒有可先用 Idle Clip | 開 |
| Dead | 死亡 Clip；測試時也可先用空 State | 關 |

右鍵 Idle → Set as Layer Default State。

Clip 的 Loop 設定在 Project 選取動畫來源資產 → Animation 分頁 → 選對 Clip → Loop Time → Apply；不是只改 Animator State。

### 14.1 一般轉場

每條轉場點選白色箭頭，在 Inspector 設定：

- Has Exit Time：關。
- Fixed Duration：開。
- Transition Duration：0.1 秒。
- Conditions 按表格加入；同一列條件是全部同時滿足。

| From → To | Conditions |
|---|---|
| Idle → Patrol | IsAlive=true；IsAlerted=false；MoveSpeed Greater 0.05 |
| Patrol → Idle | IsAlive=true；IsAlerted=false；MoveSpeed Less 0.05 |
| Idle → AlertIdle | IsAlive=true；IsAlerted=true；IsAnnouncing=false |
| Patrol → AlertIdle | IsAlive=true；IsAlerted=true；IsAnnouncing=false |
| Discover → AlertIdle | IsAlive=true；IsAnnouncing=false；IsAlerted=true |
| Discover → Idle | IsAlive=true；IsAnnouncing=false；IsAlerted=false |
| AlertIdle → Idle | IsAlive=true；IsAlerted=false |
| Dead → Idle | IsAlive=true；只在你有測試復活時需要 |

MoveSpeed 在完成巡邏時會回到 0。

### 14.2 發現與死亡的 Any State 轉場

Any State → Discover：

- Has Exit Time：關。
- Fixed Duration：開。
- Transition Duration：0.05。
- Can Transition To Self：關。
- Conditions：IsAlive=true、IsAnnouncing=true。

Any State → Dead：

- Has Exit Time：關。
- Fixed Duration：開。
- Transition Duration：0.05。
- Can Transition To Self：關。
- Conditions：IsAlive=false。

如果 Animator 中已經有死亡轉場，沿用原有流程，避免建立兩條互搶的死亡路徑。死亡應優先於一般移動／警戒轉場。

Discover Clip 必須關 Loop Time。當 Clip 比 Announcement Duration 短，會維持最後一幀等待狀態結束；當 Clip 比它長，會被時間提前切走。

例如動畫長 1.2 秒、Animator State Speed=1.5，實際長度為：

```text
Announcement Duration = 1.2 / 1.5 = 0.8 秒
```

不要用 Animation Event 改變警戒計時；State Authority 的 TickTimer 決定正式結束時間。

### 14.3 掛載動畫橋接器

選擇 Animator 所在的 VisualRoot／模型物件，Add Component → EnemyAwarenessAnimatorDriver。

| 欄位 | 拖入內容 |
|---|---|
| Awareness | Enemy Root 的 EnemyAwarenessBrain |
| Animator | 此模型 Animator |
| Actor | Enemy Root 的 EnemyActor |
| Patrol | Enemy Root 的 EnemyIdlePatrolBrain |
| Alive Bool Name | IsAlive |
| Alerted Bool Name | IsAlerted |
| Announcing Bool Name | IsAnnouncing |
| Move Speed Name | MoveSpeed |
| On Announcement | 先留空 |

如果還沒有動畫，整支 Driver 可以暫時不掛；先用 Debug 訊息測試邏輯。不要為了缺少 Clip 而延後視野測試。

### 14.4 可選的呼喚叫聲測試

本階段保留本機事件；若暫時要測「一群只叫一隻」：

1. 在敵人模型上增加專用 AudioSource。
2. AudioClip 指定呼喚音效。
3. Play On Awake 關閉，Loop 關閉。
4. Spatial Blend 設為 1，作為世界 3D 音效。
5. Output 指向你既有的 World SFX Mixer Group。
6. Min Distance 先用 2，Max Distance 先用 20。
7. 展開 Driver 的 On Announcement，按 +。
8. 把此 AudioSource 拖入物件欄。
9. 函式選 AudioSource → Play()。

這是基礎呈現接點，尚未接你完整 AudioManager 的聲音定義。每台電腦觀察到新的演出 Sequence 後播放自己的 3D AudioSource；只有取得演出權的那隻會觸發。

不要再用 Animation Event 播放同一段叫聲，也不要替每隻敵人加 AudioListener。中途加入不補播歷史叫聲，動畫 Bool 仍會更新成當前狀態。

## 15. CombatDeathHandler 的新增注意事項

請保留上一階段已完成的設定。

本階段的 AI NetworkBehaviour 需要收到死亡後的 Tick 才能清除自己的狀態；因此 **不要新增** 下列元件到 Behaviours To Disable On Death：

- EnemyAwarenessBrain。
- EnemyPerceptionController。
- EnemyIdlePatrolBrain。
- EnemyMovementOwnership。
- Ground／Flying Navigator。

這些元件自己檢查死亡或由 Brain 停止呼叫，不會讓屍體巡邏。

如果要使用本包 Animator 死亡轉場，EnemyAwarenessAnimatorDriver 與 Animator 也必須保持啟用，VisualRoot 不可死亡瞬間關閉。

EnemyDeathLifecycleController 的 Death Animation Duration 要至少覆蓋死亡動畫的實際長度；這個計時器控制屍體階段，Animator 的 Dead 不會反向呼叫 Despawn。

## 16. 保存並更新 Fusion 資料

1. 保存 Base Prefab。
2. 保存四個 Variant。
3. 確認每個 Variant 的 Definition、Navigator 類型與巡邏 ID 正確。
4. 場景敵人若有舊的 Inspector Override，確認沒有覆蓋掉新設定。
5. 保存場景。
6. 按上一階段方法重新執行 Fusion Prefab Table 的 Rebuild Prefab Table。
7. 讓 Unity 完成 Fusion 編織與重新編譯後再進 Play。

這次有新增 NetworkBehaviour，Prefab 和場景中的組件結構必須在 Host／Client 一致；兩端都要更新。

## 17. 按順序測試，一項通過再測下一項

### A. 待機轉向

只放一隻近戰 A。Patrol Decision Chance=0。玩家待在 Sight Distance 外。

預期：每隔 1.5～3.5 秒產生新的左右觀察角度，偶爾轉身，Root 不亂飄。

若完全不轉，先看 Initial Brain State 是否 Idle、生命是否存活，以及是否還處於外部控制。

### B. 地面巡邏

把 Patrol Decision Chance 改成 1，玩家仍在視野外。

預期：Idle → Patrol，沿合法 NavMesh 路徑到人工節點 → Idle。重複數次。

若只待機，檢查：Area ID、節點陣列、節點是否在 NavMesh、Root 腳底是否接地、Obstacle Mask、Rigidbody 是否動態、Definition 是否 Ground。

### C. 飛行巡邏

改放遠程 A，使用空中節點，Patrol Decision Chance=1。

預期：高度會跟著節點改變，牆後不通的節點被略過。中途放入障礙物應停止該次巡邏並回 Idle，不穿牆。

### D. 自己發現玩家

只放一隻敵人；為了控制朝向，先把 Idle Decision 的最短／最長時間都調到 100。

走進正前方 20 公尺內。

預期：只有這隻進入 Discover／Announcing，約 0.8 秒後變成 AlertIdle／Chase。此時還不跟隨玩家，因為追逐屬於下一階段。

再把玩家移到牆後：Has Direct Sight 應在約 0.1 秒掃描內變 false；約 3 秒後目標清除、4 秒 Investigate 後回 Idle。最後已知位置在遮擋期間不隨玩家移動。

### E. 同伴呼喚

放三隻同群組 room_a 的敵人，距離在 18 公尺內。讓一隻面向玩家，另外兩隻背對玩家。把待機決策時間暫時調大。

預期：首隻發現演出；兩隻背對玩家者直接 Alerted／Chase，不一起 Discover。

再測三隻都面向玩家：同群組同一時間仍只有一個呼喚演出，不出現三段重疊叫聲。

把其中一隻 Alert Group Id 改成 room_b，再跑一次。該隻不應被 room_a 呼喚，但仍可自行看見玩家。

### F. 背後受傷與防止連鎖

三隻都背對玩家。先用低傷害單發射擊其中一隻，保證沒有秒殺。

預期：受傷閃白照常、附近同群組同伴取得攻擊者情報。補血不能引起呼喚。

將 A、B、C 排成一列，使 A→B 在 Alert Radius 內、A→C 在外。打 A：B 收到，C 不因 B 再轉播而醒來。

致死一擊不讓屍體再執行呼喚；其他敵人仍可憑自己的視野發現玩家。

### G. 既有鈎索與死亡

巡邏時用 Support 拉怪、Tank 聚怪。

預期：AI 不與外部移動搶位置；控制解除後重新計算巡邏路線。地面怪在空中會落下，沒有合法地面路徑時保持 Idle 重試。

在發現演出期間打死敵人：屍體不應繼續巡邏，應進 Dead，再照原定死亡時間消失。

### H. Host／Client

使用你原本正常的兩程序聯機測試。檢查：

- 兩邊看到相同的敵人位置與同一隻發現演出。
- 玩家死亡／離線不產生 Networked property 或空參考錯誤。
- 晚加入看到當前狀態，不重播之前發生過的叫聲。
- 多次死亡重生後，敵人只在重新偵測到新玩家物件時才取得新目標。

通過後關閉各 Debug 選項，恢復待機時間與 Patrol Decision Chance=0.3。

## 18. 目前驗證狀態與範圍

已以目前專案的 Assembly-CSharp、Fusion DLL、Unity 2022.3 DLL 完成獨立 C# 編譯：0 錯誤、0 警告。

這不等於已在 Unity 執行：此處尚未完成 Unity 內的 Fusion Weaver、場景 Play Mode、真實碰撞與雙端聯機測試；請依第 17 節驗收。

本版地面導航使用完整 NavMesh 路徑與逐步碰撞檢查；飛行使用可直達的人工節點。尚未包含動態路徑重算繞行、人群避讓、跨平台連結、全域空中路徑、追逐／包圍，以及攻擊／防禦。巡邏遇到不能安全通過的路徑會回到待機。

下一階段會在這套 Perception／Awareness 基礎上接地面追逐 A、飛行追逐 B，以及失去視野後真正的搜索移動。
