# Ranged C 定點光束與投射物砲台

> 最後核對日期：2026-10-02（本功能局部核對）
> 核對來源：EnemyActor、EnemyDefinition、EnemyMovementOwnership、EnemySpawnZone、EnemyEncounterDirector、EnemyPatrolAreaTestSpawner、EnemyCombatAnimatorDriver、EnemyRangedCSetup 與兩份新砲台 Prefab。
> 相關文件：[90 敵人核心](90_敵人核心感知巡邏與追逐.md)、[91 攻擊與生成](91_敵人攻擊表現與生成.md)、[164 Encounter](164_Phase10_怪物生成區與Encounter守則.md)、[130 驗證紀錄](130_回歸驗證與審查狀態.md)。

## 已確認玩法與資產

Ranged C 有兩個獨立類型，各只掛一種攻擊，不在同一隻砲台上切換光束／投射物。整隻 Root 在攻擊期間水平轉向玩家；光束砲台在持續鎖定期間的攻擊間隔／冷卻也會轉向，不巡邏、不追逐位移，也不需要地面／飛行巡邏區。位置由使用者規劃，沿用既有正式 Encounter 或開發熱鍵生成器；本次未自動放置或接線 Scene／MapChunk。

| 類型 | Prefab | Definition | 攻擊元件 |
|---|---|---|---|
| 定點投射物 | `Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_Projectile.prefab` | `Assets/_Project_Assets/Data/Enemy/ED_Ranged_C_Projectile.asset` | `EnemyRangedProjectileAttack` |
| 定點光束 | `Assets/Prefabs/Enemy/Variants/Enemy_Ranged_C_Beam.prefab` | `Assets/_Project_Assets/Data/Enemy/ED_Ranged_C_Beam.asset` | `EnemyRangedBeamAttack` |

兩者共用 Ranged 家族、Variant C、Locomotion Kind = Stationary，但有各自的資料 ID（`ranged_c_projectile`／`ranged_c_beam`）與獨立攻擊參數。C 加在列舉尾端，數值為 2；A=0、B=1 與既有 Stationary=2 不變。攻擊 Option Id 仍沿用 RangedAttackA=3／RangedAttackB=4，避免破壞 Animator 與既有攻擊協定。

造型、槍口、動畫和碰撞暫沿用來源 Ranged A／B，這是可測試的玩法 Prefab，未製作新的砲台美術。已移除 IdlePatrolBrain、PatrolNavigator、ChaseBrain、ChaseMotor，Animator Root Motion 關閉，Rigidbody 保持 Kinematic 且不使用重力。來源 A／B 資產未改。

## 責任與生命週期

- `EnemyActor.IsStationary` 只讀 Definition。`TryInitializePatrolBeforeSpawn(area)` 是正式／測試生成器共用入口；Stationary 允許 area=null，但拒絕殘留巡邏／追逐元件；Ground／FreeFlying 仍轉交原 `EnemyIdlePatrolBrain.TryInitializePatrolAreaBeforeSpawn`。只能在 Fusion `Spawned` 前使用。
- 感知與群體呼喚仍由原 Perception／Awareness 控制。內部 BrainState 可進 Chase，代表警覺作戰狀態；因沒有 Chase Motor，不表示砲台會位移。
- 攻擊選擇、TickTimer、傷害與 Root 轉向沿用原 Combat Decision／Option，State Authority 執行。光束砲台在仍有直接視線或有效鎖定保留時，冷卻／攻擊間隔持續水平瞄準；沒有目標時不自行掃視。投射物砲台維持原行為。
- `EnemyMovementOwnership.CanMove` 對 Stationary 拒絕 AI 移動，`CanRotate` 與原有互斥規則保留。沒有新增第二套移動、生命、傷害或 FOV 系統。
- `EnemyCombatAnimatorDriver` 不再強制要求 ChaseBrain；缺少時 ChaseSpeed 為 0，攻擊 Option／Phase 仍正常送往 Animator。整體 Presentation Driver 原已支援沒有移動 Brain 的情況。
- 既有血量、死亡、擊殺經驗、Attack Mark、Support Pull／Tank Gather 元件與能力規則沿用。**定點在本版代表沒有 AI 自主位移，並未新增鈎索拉動／聚怪免疫。**
- Client 接收現有 NetworkTransform 與攻擊同步狀態，不重新選目標、生成子彈或結算傷害。新 Prefab 已經 Fusion bake 並加入 Prefab Table；既有進行中的連線不熱更新 Prefab，修改後需重開 Play Mode／重新建立同版本 Host 與 Client。

## 正式 Encounter 的人工配置

1. 開啟欲配置的 MapChunk Prefab，在既有流程建立／選取 `EnemySpawnZone`。`Locomotion Kind` 選 **Stationary**；不選 Ground 或 Free Flying。
2. `Enemy Prefabs` 指定上述其中一份。若想讓此位置隨機二選一，可放兩份；每隻仍只使用其自己的攻擊。
3. 將 Zone Transform 與 `Spawn Box Center` 對齊要放砲台的位置。**Stationary 固定使用 `Transform.TransformPoint(Spawn Box Center)`**，不隨機取樣、不貼地、不查巡邏路徑。`Spawn Box Size` 不控制定點的散布；Gizmo 顯示中心球與朝向線。Zone 旋轉也是初始出生朝向。
4. `Flying Patrol Area` 留空，飛行巡邏盒設定對 Stationary 不生效。地面與飛行區的既有巡邏規則保留；只有存在有效 Ground Zone 時，Director 才等待逐 Chunk 地面巡邏區。
5. 定點建議 `Enemies Per Activation = 1`。需要多個砲台位置時建立多個 Zone；同一 Zone 的多次候選仍是同一中心，會受到原占用檢查限制。
6. Gate、全場／每區預算、全員遮擋、最近五區鎖定、逐隻生成間隔及脫戰清理全部沿用 [164](164_Phase10_怪物生成區與Encounter守則.md)。人工指定位置不代表必定生成；位置曝光、太近、被占用或不在穿越前方，仍會拒絕。
7. 由使用者檢查 Root 支撐高度、身體不穿牆／平台、槍口不埋牆。Stationary 不會使用地面或飛行導航替你校正幾何位置。出生遮擋仍是既有固定高度幾何檢查，實際大模型／高平台需目視驗證。

## 舊熱鍵測試生成器

1. 選取既有 `EnemyPatrolAreaTestSpawner`，把 `Enemy Prefabs` 指向新砲台。
2. 在同 Scene 人工放置空 Transform，拖入新增的 **Stationary Spawn Point**。此欄預設空，空白／停用／不同 Scene 時拒絕定點生成，不退回原點。
3. 定點會自動忽略 `Patrol Area` 和 `Use Runtime Generated Ground Patrol Areas`，不必建立巡邏區。Ground／FreeFlying 候選仍走原本的區域選點與出生前綁定。
4. `Spawn Position Offset`（預設 0、世界座標公尺）仍加到指定點；`Spawn Rotation = Patrol Area Forward` 在 Stationary 時代表指定出生點的水平朝向，`Random Yaw` 則保留隨機初始 Y 角度。
5. 保留原熱鍵、Client→Host RPC、開發版限制、占用遮罩與數量上限。測試時在指定點按原設定熱鍵即可；正式非 Development Build 不提供此生成入口。

## 數值調整入口

以下是新 Prefab 建立時從現有 A／B 複製的值，**不是原手冊的建議預設**。在各自新 Prefab 的對應元件調整，不改來源 A／B。對既有已生成實例不做參數同步熱更新；重開本輪測試。

| 元件／欄位 | 投射物砲台目前值 | 光束砲台目前值 | 單位／限制／效果 |
|---|---:|---:|---|
| TestDamageReceiver / Max Health | 200 | 200 | 生命上限；沿用既有生命元件，未加入 Enemy Level 成長 |
| Perception / Sight Distance | 40 | 40 | 公尺、至少 0.1；初次發現仍需符合視角與遮擋 |
| Perception / Locked Target Retention Seconds | 3 | 3 | 秒、至少 0.02；離開正常視野後的有限保留，重新親眼看見才重設 |
| Perception / Locked Target Retention Radius | 60 | 60 | 公尺、至少 0.1；直接看見後的目標保留半徑，不等於初次偵測範圍 |
| Combat Option / Maximum Start Distance | 50 | 50 | 公尺；需不小於 Minimum Start Distance。感知 40 與射程 50 是不同限制 |
| Active Delay Seconds | 0.5 | — | 秒、至少 0；投射物前搖，完成後才出彈 |
| Tracking Seconds | — | 0.4 | 秒、至少 0.02；光束追蹤階段 |
| Locked Delay Seconds | — | 0.48 | 秒、至少 0；凍結射線目標後等待發射 |
| Recovery Seconds | 0 | 0.58 | 秒、至少 0；收招期間仍由攻擊控制轉向 |
| Cooldown Seconds | 2 | 2 | 秒、至少 0；攻擊／收招完成後起算 |
| Aiming Turn Speed／Tracking Turn Speed | 240 | 180 | 度／秒、至少 1；整隻水平轉向速度，不控制位移 |
| Projectile Speed | 120 | — | 公尺／秒、至少 0.01 |
| Projectile Damage／Damage | 10 | 30 | 傷害、至少 0；沿用 DamageRequest |
| Projectile Lifetime Seconds | 5 | — | 秒、至少 0.02；未命中時的存活上限 |
| Definition / Base Kill Experience | 1 | 1 | 整數、至少 0；擊殺者額外加成仍由共用經驗規則決定 |

`Chase Kind` 在 Stationary 不使用，無須設定地面或飛行追逐。Muzzle、Projectile Prefab、Beam Hit Mask 與 LineRenderer 沿用既有來源配置，換模型時應逐項重新檢查。投射物週期約為「前搖 + 收招 + 冷卻」；光束週期約為「追蹤 + 鎖定等待 + 收招 + 冷卻」，實際仍受感知、取消與 Tick 精度影響。

## 資產建立工具

`Tools > Purgers > Enemy > Create Missing Ranged C Turrets` 對應 `Assets/Scripts/Editor/EnemyRangedCSetup.cs`。只建立缺少的兩份 Prefab／Definition，不覆寫已存在的 C 資產、不放置場景物件或接線生成區。首次複製當時來源 A／B 的視覺及攻擊參數；後續重跑不會把你已調整的 C 值重設。

## 驗證與證據邊界

- 先跑到 5 項預期失敗，確認 C 分類與 Stationary 免巡邏契約尚未存在（job `bc15f1a70b254cd5bf1566deee612794`）。早先兩次篩選工作未執行到測試，不算通過。
- 最終聚焦 **29／29 通過**（job `a782c978f1254ec9b682350a672886e8`）：Ranged C 12 項、既有 Encounter 9 項、自動飛行巡邏 8 項。包含定點中心與 Chunk 旋轉縮放、Ground／Flying 不得免巡邏、兩份 Prefab 單一攻擊、移除導航依賴、無 Missing Script、Fusion bake／Prefab Table、Root 只旋轉、測試出生點空白拒絕與位置偏移。
- 全套 `PurgersRegression` job `ef218e0db9a64600b451996ee2a7351b` 回報 **267 項執行／282 項發現，6 項失敗**；失敗時 result=null，數量來自 progress。失敗為 Boss 接線 1、經驗值／開發升級規則 4、槍口材質 1，與既有紀錄一致。本次未達全套零失敗門檻。
- 受控 Runtime 使用暫時 **GameMode.Single** Runner，僅該 Runner 的複製設定改為 Single Peer；全域仍為 Multiple。兩種新砲台均透過 `Runner.Spawn`、`onBeforeSpawned` 免巡邏入口生成，取得 State Authority，原感知／戰鬥流程對正式玩家 Prefab 發動攻擊。
- 投射物序號達 4、光束序號達 2，玩家死亡後兩者回 Idle。投射物 Root 位置兩次均為 `(9990,100.5,10000)`、Yaw 約 44.987°；光束兩次均為 `(10010,100.5,10000)`、Yaw 約 320.723°，未出現 AI 位移。此為兩砲台同場受控證據，並非逐種傷害平衡驗收。
- 初次探針因無場景的 Multiple Peer Runner 處於 busy 而無法 Spawn；改用複製的 Single Peer 設定後補入現有 Prefab sources 才完成。未修改專案 PeerMode 或為測試儲存場景。
- 結束後已退出 Play Mode，臨時 Runner／玩家／地板／砲台清除；Game Scene 仍未 dirty。`isCompiling=false`、`scriptCompilationFailed=false`，最後 Console 查詢沒有 error／warning；Editor.log 的既有 HUD `TextureImporter.spritesheet` CS0618 仍需與本功能分開記錄。
- 來源 A／B Prefab、兩份來源 Definition、Game Scene、MapChunk_Prototype 的 SHA-256 與本輪修改前一致。原始測試輸出保存在 [Validation/RangedC](Validation/RangedC/)。

## 手動驗收順序與預期

1. 分別在 Host 生成兩種砲台，不配置巡邏區；Console 不應出現缺 Patrol Area／Navigator。觀察空閒、發現、攻擊與失去目標，Root 位置應保持不變，攻擊期間整隻轉向。
2. 光束應先追蹤再凍結瞄準位置；玩家在等待期間閃開應可避開。投射物應由正確槍口生成並碰撞出傷；不應同隻同時出現光束與子彈。
3. 在正式 Stationary Zone 的指定中心配置 Gate 與遮蔽物，自然跨越門檻；檢查正向／雙向、可見時拒絕、占用、預算、五區輪替，以及遠離脫戰清理。單一區成功後鎖定，重測需新一輪或按六區輪替解鎖。
4. 獨立 Host＋Client：Client 看到同位置、轉向、光束與投射物；Client 不多生怪／子彈、不自行扣血。再測晚加入／重連、死亡 Despawn 與跨關卡清理。
5. 鈎索受控時沿用原有攻擊取消／位移規則；若後續要「固定基座完全不可拉動」，需另定能力相容規格。

上述自然 Encounter、獨立 Host／Client、晚加入／重連、模型動畫目視與完整關卡生命週期尚未驗收，不以 EditMode 或 Single Runner 結果替代。

## 2026-10-02 視野保留更新

兩种 C 沿用共用感知修正：視野外短暫保留預設 3 秒，超時或超出原有 60 公尺保留半徑解除鎖定；期間沒有牆壁遮擋時仍允許攻擊追蹤。整隻轉向若重新看見玩家會重設倒數。未修改兩份 Prefab、出生位置或 Scene；新秒數欄位使用程式初始化預設。詳細調整、共享情報與遮擋界線見 [90](90_敵人核心感知巡邏與追逐.md)。
## 2026-10-02 光束砲台冷卻期間持續瞄準

局部核對來源：`EnemyCombatDecisionController`、`EnemyCombatOption`、`EnemyRangedBeamAttack`。修正先前光束砲台完成 Recovery 後，在 Cooldown 不再面向玩家的空檔。

- Combat Controller 的 State Authority 在沒有 Active Option 時，確認 Stationary、非 Dormant、有效存活目標、直接視線或有效鎖定保留，並取得 `EnemyMovementOwnership.CanRotate`，才委派冷卻瞄準。
- `EnemyCombatOption.SupportsBetweenActionAiming` 預設 false，目前只有 Beam opt-in；以最高 Priority 選一個支援能力呼叫 `AimBetweenActions`，不讓多個能力同時寫 Root 朝向。移動型 Ranged B、投射物砲台不新增此行為。
- Beam 重用 FaceTarget，只轉動 Root，不啟動攻擊、不刷新冷卻、不更新 BeamEndPoint。攻擊期間仍由 Active Option 獨占，LockedDelay 的固定世界瞄準點維持可閃避規則。
- `Tracking Turn Speed` 同時控制攻擊中與冷卻中的水平角速度：程式預設與 C 初版值 180 度／秒、至少 1；在 **Enemy_Ranged_C_Beam Prefab → Enemy Ranged Beam Attack** 調整。增大能更快跟隨，減小保留較大的轉向延遲；不需要新增元件或重新接線。
- 旋轉鎖、外部位移控制、死亡、Dormant、失去有效目標或即時位置資格時不執行冷卻轉向。短暫視野保留期間仍可轉向；重新看見則沿用 Perception 的期限更新。

手動驗收：正常發射一次並收招後，在冷卻期間繞到側面，Root 應持續跟隨且位置不變。接著測試旋轉封鎖／外部控制期間不搶寫朝向、解除後恢復，以及目標解除後停止。光束已鎖定的射擊點不能因 Root 跟轉而改成必中。

聚焦 24/24 與受控 Single Runner 證據、完整回歸限制見 [130](130_回歸驗證與審查狀態.md)；原始輸出見 [Validation/BeamContinuousAim](Validation/BeamContinuousAim/)。本次只修改程式，未保存 Scene／Prefab 參數。