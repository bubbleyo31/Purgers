# Phase 10：怪物生成區與 Encounter 守則

> **2026-10-02 Ranged C 定點砲台**：新增獨立光束／投射物 Prefab，整隻原地轉向、不巡邏、不追逐位移。Stationary 的 Encounter 使用人工出生中心，測試生成器使用指定 Transform，皆不需地面／飛行巡邏區。聚焦 29/29 與受控 Single Runner 攻擊／位置不變已驗證；獨立 Host／Client 與自然 Encounter 待驗。完整回歸仍有既有 6 項失敗。設定與證據見 [165 Ranged C 定點砲台](165_RangedC_定點砲台與生成配置.md)。

> 最後核對日期：2026-09-30（自動飛行巡邏與 EnemySpawnZone 整合局部核對）
> 核對來源：`Assets/Scripts/Enemy/Encounter/`、`MapRuntimeNavigationPrototype.cs`、`StageFlowController.cs`、`NetworkMapState.cs`、`MapChunk_Prototype.prefab`、`Enemy_Melee_A.prefab`、`Game.unity`
> 相關文件：[90 敵人核心](90_敵人核心感知巡邏與追逐.md)、[91 敵人攻擊與生成](91_敵人攻擊表現與生成.md)、[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md)

## 已定玩法

高速玩家跳過部分戰鬥是合法策略。「怪物生成區」是設計者放在 `MapChunk` Prefab 內的獨立區域；一個地圖 Chunk 可以包含多個怪物生成區，兩者不共用數量或生命週期。普通關卡才執行此 Encounter；Boss 由既有 `BossStageObjectiveController` 管理。

每個區域有可調出生盒，並有一個以上可手動擺放的**定向啟動門檻**。Host 使用玩家上／本 Fusion Tick 的位置線段判定穿越。預設只接受背面到正面；Gate 的「雙向穿越」選項開啟後也接受正面到背面。正向採用 `Transform.forward`，反向採用 `-Transform.forward` 作為本次前方生成方向，並保留至該次分批生成結束；不使用接近球體。跨越只產生一次候選機會。所有候選仍須通過全場數量、同區數量、出生盒、導航、占用、前方位置、距離以及預設開啟的**所有存活玩家實體遮擋**檢查（各 Zone 可取消「生成需要視野遮擋」供測試）；任一條件不成立就不生成，沒有補發佇列。每次啟動可分批產生多隻；過期或玩家跑遠即取消剩餘數量。

遮擋檢查從每名玩家 Root 上方約 1.5 公尺，射向出生點的三個高度；這是 Host 的保守幾何檢查，不是每個 Client 真正相機畫面的像素判定。牆角、透明材質、薄遮蔽物及飛行高度仍須在 Host／Client Play Mode 逐一目視驗證。

至少成功生成一隻，該區才進入最近成功區域的 FIFO 鎖定表。預設保留最近 **5** 區；第 6 區成功時解鎖最早的一區。解鎖只恢復下一次跨門檻的資格，不會即時生成。沒有成功的嘗試不占名額；同一份 MapChunk Prefab 在一輪內的每個實際複本，具有獨立區域身分。

只有此 Director 生成的普通敵人可被落後清理。若敵人與所有存活玩家都夠遠、沒有感知目標、沒有戰鬥動作與外部控制、對所有玩家都有遮擋，並持續達清理時間，Host 直接 `Runner.Despawn`。此路徑不呼叫死亡事件與擊殺經驗；玩家正式擊殺仍由既有死亡流程處理。Client 只接收 NetworkObject 的 Spawn／Despawn，不執行判定。

## 程式責任

| 程式 | 責任 |
|---|---|
| `EnemySpawnZone` | Prefab 局部出生盒、同類型敵人候選、一次啟動數量、自動飛行巡邏盒與可選人工 Area；不自行生成。 |
| `EnemySpawnApproachGate` | 無 Collider 的單向／雙向矩形平面、穿越方向與 Scene Gizmo；不直接觸發 `Runner.Spawn`。 |
| `EnemyEncounterRules`／`RecentZoneLock<T>` | Tick 線段穿門檻、地面出生點的短距離 Collider 投影，以及最近成功區的固定容量輪替。 |
| `EnemyEncounterDirector` | 普通關卡的 Host 權威候選、預算、可見性、逐隻生成及落後清理；建立／清理本輪 Zone 自動飛行 Area 並在出生前綁定。 |
| `EnemyFlyingPatrolPlanner` | 有界立體取樣、出生盒可達的最大連通集合、保留連通性的點數限制；不移動敵人。 |
| `MapRuntimeNavigationPrototype.TrySampleGroundPosition` | 使用原有 Agent Type 在預烘焙 NavMesh 投影地面出生點；不執行 Runtime BuildNavMesh。 |

正式 Encounter 沿用 `EnemyIdlePatrolBrain.TryInitializePatrolAreaBeforeSpawn`，在 `Runner.Spawn` 的 `onBeforeSpawned` 指定實際巡邏區。地面區使用當次 Chunk 對應的 Runtime Ground Patrol Area；飛行區的 `Flying Patrol Area` 留空時，由 Host Director 在該 Zone 的立體巡邏盒內自動生成 `EnemyPatrolArea`；有指定時保留同一 Chunk 內的人工巡邏區。區域的 `Enemy Prefabs` 必須全部與其 `Locomotion Kind` 相同；遇到不一致，Host 會立即 Despawn 該次不合規物件並報錯。

地面出生先取樣該 Chunk 的預烘焙 NavMesh，再以 `World Occlusion Mask` 射向距離不超過 0.25 公尺的實體地板，將 Root 放到 Collider 表面；找不到地板、超過落差、離開出生盒或沒有通往巡邏點的完整路徑，都拒絕該候選。此檢查不會在 Runtime 重烘焙導航。

### 已配置的 Prototype 實例

`MapChunk_Prototype.prefab` 的 `EnemySpawnZone1` 目前局部位置為 `(-23.9, 1.05, 220.2)`，出生盒尺寸 `(12, 2.07, 12)`；子物件 `EnemySpawnApproachGate` 局部位置為 `(25, 26.95, 17)`、Y 旋轉約 179.98°，寬 64.8、高 55.8。Gate 的方向與尺寸沿用使用者配置，調整 Zone 時保持 Gate 在 Chunk 內的位置不變。Game 中 Zone 世界位置為 `(-53, 1.05, -43)`，Gate 為 `(-28, 28, -26)`，玩家需從北往南穿越。這些是此 Prototype 的配置值，不是所有 Zone 的預設。Ground 區仍使用所在 Chunk 的 Runtime Patrol Area，敵人為 `Enemy_Melee_A`。該敵人的 Ground Patrol Navigator 膠囊已對齊同 Prefab 追逐設定：半徑 0.35、高 1.8 公尺。

此 Prefab 引用 Editor 重新烘焙的 `NavMesh-MapChunk_Prototype_Phase10.asset`。`Game.unity` 原先額外掛載的舊整場景 NavMesh 與 Chunk 資料重疊，會讓出生點取樣到高於實體地板的舊面；現已在 Unity Editor 清除該 Scene 的 NavMesh 參照。普通地圖仍由每個 Chunk 的預烘焙資料及 Runtime Link 提供導航。

## 手動配置順序

1. 在現有 Game Scene、已有 `StageFlowController`／`NetworkMapState` 的 **Scene NetworkObject** 上增加 `EnemyEncounterDirector`。指定同一 Runner 的 `Stage Flow`、`Map State` 和本場景 `Map Selection`。不建立第二個 Stage owner 或新的 NetworkObject。Boss Scene 不必配置。
2. 在欲使用的 `MapChunk` Prefab 下建立一或多個 `EnemySpawnZone` 子物件。設定出生盒中心與完整尺寸，並用 Scene Gizmo 確認位於可藏身的地方。地面區需讓盒體包含可用的烘焙 NavMesh；飛行區調整自動巡邏盒（青色 Gizmo），或指定該 Prefab 內人工巡邏區。
3. 在每個 Zone 下建立至少一個 `EnemySpawnApproachGate` 子物件。門檻的 `Transform.forward` 朝出生盒方向，將平面放在玩家可走路線上的轉角或遮擽物之前。調整寬／高，涵蓋滑鏟、鈎索及跳躍可能穿過的高度；不同接近路線可設多個門檻。它不用 Collider／Trigger。需要兩面啟動時勾選「雙向穿越」；出生盒需在兩個接近方向的前方各有合法遮擋點，單側盒不保證反向也能生成。雙向時 Gizmo 會顯示兩條方向線。
4. 在 Zone 設定已登記 Fusion Prefab Table 的 `Enemy Prefabs`、`Locomotion Kind` 與 `Enemies Per Activation`。不要把 Boss Prefab 加入普通 Zone。地面／飛行不混放在同一區。
5. 在 Director 設定 `World Occlusion Mask` 為**真正不透明、可遮住敵人身體**的地圖實體 Layer；設定 `Spawn Occupancy Mask` 為會占用出生點的 Player／Enemy Layer。兩者空白時系統拒絕生成。玻璃等看得穿的表面不可當成遮擋。
6. 首次 Play Mode 先保持 Director 的預設預算；確認實際敵人數、可見性與效能後，才調整各數值。若不希望既有 F4 開發測試生成干擾觀察，測試時不要按 F4；正式 Build 的測試生成原本已由 `DevelopmentToolsPolicy` 禁用。

### 數值入口與目前程式預設

| Inspector 欄位 | 目前預設／有效值 | 調整效果與依賴 |
|---|---|---|
| `雙向穿越`（`bidirectional`） | 預設關閉；布林開關 | 開啟後正反跨越皆可提出候選；前方檢查沿本次穿越方向。兩面共用同一 Zone 的鎖定與預算，不會因換邊就重置。 |
| `Gate Width`／`Height` | 程式預設 12／8 公尺；此 Prototype 64.8／55.8，皆須大於 0 | 定向穿越平面的寬高，不是敵人出生盒。太小會漏掉高動能路線。 |
| `Spawn Box Size` | 程式預設 (12, 4, 12)；此 Prototype (12, 2.07, 12)，三軸皆須大於 0 | 候選點盒；地面高度要同時涵蓋 NavMesh 與 Collider 地板，避免選到上層導航。 |
| `Enemies Per Activation` | 2，至少 1 | 一次成功跨越後最多逐隻生成的數量。 |
| `Maximum Alive Enemies`／`Per Zone` | 12／3，皆至少 1 | 前者統計同 Runner 所有存活 Enemy；後者只統計此 Director 在該區生成的存活敵人。 |
| `Maximum Owned Network Objects` | 20，至少 1 | 包含此 Director 尚未 Despawn 的屍體物件，避免只限制活敵而忽略網路物件量。 |
| `Minimum Seconds Between Spawns`／`Activation Lifetime Seconds` | 0.75／3 秒，分別至少 0.02／0.1 | 全場逐隻間隔、單次啟動到期時間；到期不補發。 |
| `Recent Locked Zone Count` | 5，至少 1 | 第 6 個不同區成功時解鎖最早的區。少於 6 個可成功的區域時，已用區仍保持鎖定。 |
| `Maximum Sweep Distance` | 40 公尺／Tick，至少 0.1 | 超過此位移視作傳送，不沿線啟動；高速合法移動若真能超過此值，需以實測速度調整。 |
| `Maximum Spawn Distance From Trigger`／`Minimum Forward Distance` | 55／2 公尺，分別須大於 0／至少 0 | 玩家衝過出生盒時不補生在背後；門檻方向必須擺對。 |
| `Candidate Attempts`／`Ground Sample Distance` | 16 次／2 公尺，分別至少 1／大於 0 | 候選點有限嘗試、地面 NavMesh 投影距離；失敗即放棄本次機會。 |
| `Minimum Player Distance`／`Spawn Occupancy Radius`／`Center Height` | 8／0.75／0.9 公尺，前兩者須大於 0，高度至少 0 | 任何存活玩家太近或 Player／Enemy Collider 占用都拒絕。 |
| `Cleanup Distance`／`Delay Seconds`／`Check Interval` | 70／8／0.5，皆須大於 0 | 只對本 Director 生成的落後且脫戰敵人有效；需持續全員遮蔽。 |

## Play Mode 驗證順序與預期

1. **Host 單人慢速**：穿過配置門檻，敵人只在盒內合法導航點、遮蔽物後逐隻出現；未穿越與直接站在平面同側均不生成；單向時反向穿越也不生成。雙向時分別從兩側跨越，前方有合法遮擋候選才可生成；測另一側時用新一輪或已解鎖區，避免 FIFO 鎖定干擾。Console 無缺引用／遮罩錯誤。
2. **高速通過多門檻**：只見符合當前位置與預算的生成；玩家已衝過出生點或超過啟動期限時不補發、沒有停下後大量突然生成。
3. **遮擋與占用**：站在能直接看見出生盒的位置，或用玩家／敵人占住盒內可用點，本次應跳過；轉角後若看不到合法點才可生成。檢查飛行與地面配置各自的 Patrol Area。
4. **六區輪替**：依序使 A～F 各成功生成至少一隻，F 成功後 A 才解鎖；在 A 再次朝正向穿門檻後成功生成時，B 解鎖。僅靠靠近 A 或解鎖當下均不會自動生成。
5. **落後清理**：跑離且不再看見／交戰時，等待清理條件滿足；Host／Client 兩端敵人消失，沒有擊殺經驗。正在被攻擊、外部位移控制或仍被任何玩家看到的敵人不應清理。
6. **獨立 Host＋Client**：兩人分頭、同 Tick 跨不同門檻、晚加入／重連、死亡重生與跨關卡。只有 Host 生成與維護鎖定順序；Client 不多生一份，所有玩家視線都參與遮擋檢查。Boss 仍走原有唯一 Boss 生成流程。

## 驗證邊界（2026-09-26）

透過 Unity MCP 進入 Editor 後，`EnemyEncounterRulesTests` EditMode 為 **5/5 通過**；地板投影測試曾先確認預期失敗，再完成修正。`PurgersRegression` 工作回報 237 項完成／252 項發現、6 項失敗，失敗在 Boss 資產、經驗值規則與槍口材質測試，未見 Phase 10 測試失敗；此工作未達零失敗門檻。

單人 Host 從既有 Stage 1 存檔進入 Game，確認 Zone 登記、地板與 NavMesh 高度接近；用受控的跨 Gate 前後玩家位置呼叫正式 `TryActivateCrossedZone`，`Runner.Spawn` 成功且 `Enemy_Melee_A` 綁定 Ground Patrol Area。其 `TryBegin` 成功、`TickMove` 回傳 Moving，位置實際改變；後續數秒也觀察到敵人移動。這屬於受控 Host 驗證，尚未完成玩家自然跑過門檻、遮擋目視、獨立 Host＋Client、晚加入／重連及六區輪替的端到端驗證。最後 Play Mode Console 僅仍有兩筆既存的 `AttackFocusAbility` 缺失錯誤，沒有新增 Encounter／Patrol 警告。

## 變更紀錄

- 2026-09-26：建立 Phase 10 權威生成區、方向門檻、五區輪替與落後清理的程式切片及手動配置契約。
- 2026-09-26：Unity MCP 修正 Prototype Zone／Gate、過期 Scene NavMesh、Chunk 預烘焙資料與 Melee A 巡邏膠囊；地面出生新增 Collider 投影，並取得 EditMode 與受控 Host 證據。
## 2026-09-28 穿越無生成的重現與修正

從主選單讀取 Stage 1 存檔進入 Host，確認 Director 的三個引用有效、Stage Active、登記一區並持續追蹤一名玩家。透過 MCP 受控改變實際玩家 KCC 位置，由正常 `FixedUpdateNetwork` 收集位置、判定跨越與生成；未直接呼叫 Director 私有生成方法。

原 Zone 世界位置 `(-29, 1.05, -57)`，玩家從 `(-28, 0.18, -25)` 到 `(-28, 0.18, -27)` 穿越時，300 個候選全部通過導航、地板、盒內、巡邏路徑與占用檢查，卻全部無法通過全員遮擋。因此只加大 Gate 無法解決出生盒露在視線中的問題。本次將 Zone 移到附近遮蔽物後，保留 Gate 位置、方向與尺寸；同一路線透過正常 Tick 成功產生 2 隻存活且移動的敵人，並已存回 Prefab。

Game Scene 的 `Debug Encounter` 已開啟，可從 Console 查看成功或跳過訊息。寬 Gate 的不同穿越位置仍有不同的安全檢查結果，不能保證整片平面每個位置都生成。單區成功後會保持 FIFO 鎖定；目前只有一區，需重開本輪才能重測首次生成。這符合最近五區輪替規則，不是穿越故障。

資產核對：Director 三個引用有效，Chunk 子階層 Missing Script 為 0。`EnemyEncounterRulesTests` 本次 5/5 通過（首次測試工作未啟動，重試後取得非零結果）。這仍是受控玩家移動的單人 Host 證據，鍵盤自然移動與獨立 Host／Client、晚加入尚未驗證。

本次全套 `PurgersRegression` 再跑完成 237 項、發現 252 項，仍有相同 6 項失敗：Boss Scene 資產引用 1 項、經驗值／開發升級規則 4 項、槍口材質貼圖 1 項。未達全套零失敗；本次只修改 Zone 位置、Gate 相對位置與 Scene 診斷開關，未修改上述系統。

## 2026-09-28 雙向門檻選項

新增 Gate 的序列化布林 `bidirectional`，Inspector 顯示「雙向穿越」，預設關閉以保留現有配置。既有 `TryCross` 呼叫保持相容，新 overload 回傳穿越方向。Director 使用該方向檢查第一隻與後續分批出生點；Host 權威、遮擋與 FIFO 規則維持原契約。本次未自動勾選任何 Scene／Prefab 的開關。

雙向聚焦測試先以缺少選項確認 4 項預期失敗，實作後 `EnemyEncounterRulesTests` 為 9/9 通過，包含預設單向、雙向正反穿越、旋轉平面、越界、同側／從平面起步不觸發與回傳方向。全套 `PurgersRegression` 完成 241 項（發現 256 項），仍為上列相同 6 項失敗。

單人 Host 從主選單讀檔後，於 Play Mode 暫時將 Gate 旋轉 180°，讓已驗證有遮擋的南向路線成為反向跨越。實際玩家 KCC 受控從 `(-28, 0.18, -25)` 移至 `(-28, 0.18, -27)`：選項關閉時 owned=0；開啟後由正常 `FixedUpdateNetwork` 生成 2 隻存活且移動的敵人，`pendingForward=(0,0,-1)`。沒有直接呼叫 Director 生成方法。退出 Play Mode 後 Gate 原旋轉與 Scene／Prefab 預設關閉皆已確認；未做獨立 Client 或自然鍵盤穿越驗證。


## 自動飛行巡邏（2026-09-30）

### 使用方式

1. `EnemySpawnZone.Locomotion Kind` 設為 `Free Flying`，候選只能放已登記的飛行 Enemy Prefab。
2. `Flying Patrol Area` **留空即自動規劃**；原有人工引用仍優先使用，不必移除、不改欄位名稱。
3. 設定真正位於空中的出生盒，再調整「自動飛行巡邏」的中心／尺寸。青色盒代表巡邏 Root 範圍，紅色盒代表出生點；兩者必須重疊。
4. Play Mode 普通關卡 Active 後，Host 會在各有效飛行 Zone 下建立 `RuntimeFlyingPatrolArea/PatrolPoints`；節點不足時警告並停用該區，不阻止其他地面／飛行區。
5. 跨 Gate 後仍須通過遮擋、前方與預算。飛行出生點還必須能用實際膠囊直接到達至少一個巡邏點；不能只因出生點沒有牆就生成。

| Inspector 欄位 | 預設／限制 | 效果與驗證 |
|---|---|---|
| `Flying Patrol Box Center` | `(0, 6, 0)` | Zone 局部中心，隨 Chunk 旋轉；不是出生高度自動補正。 |
| `Flying Patrol Box Size` | `(40, 12, 40)`；各軸至少 0.1 | 巡邏 Root 的局部立體範圍；膠囊可超出此 Root 範圍，但仍須無碰撞。 |
| `Flying Patrol Samples Per Axis` | 5；3～6 | 每軸格數，另加出生盒 27 點，最多 243 候選；密度高較能找到窄通道，也提高每輪首次規劃成本。 |
| `Flying Patrol Maximum Points` | 16；2～64 | 限制最終節點數；至少 2 點。保留 BFS 前綴以避免刪掉連通中繼點。 |
| `Flying Patrol Minimum Spacing` | 4 公尺；至少 0.1 | 世界距離，實際也會大於候選敵人的抵達距離；太大會找不到足夠點。 |

### 責任與限制

- 地面巡邏仍由 `MapRuntimeNavigationPrototype` 按 Chunk 建立；飛行巡邏按 **Zone** 規劃，因為不同 Zone 有不同飛行範圍與敵人膠囊。沒有新增 Map owner 或移動 owner，也不執行 Runtime NavMesh Bake。
- Director 在既有 State Authority／Server 檢查之後、地圖與地面導航就緒時，一次讀取候選 Fusion Prefab；取其 `EnemyFlyingPatrolNavigator` 的實際 Body Radius、Height、Skin、Obstacle Mask。全部候選都必須能通過，不使用另一套固定小尺寸探針。
- 共用現有 Navigator 的膠囊 Overlap／Cast；以零長度查詢排除占用點，再檢查雙向直線通道。選擇至少包含一個合法出生樣本的最大連通集合；規劃結果對相同幾何／設定為固定結果。
- AI 繼續由 `EnemyIdlePatrolBrain` 選點及 `EnemyFlyingPatrolNavigator` 移動，每次移動仍檢查動態障礙；自動點集合不是完整三維尋路系統，不保證能繞到指定目的地。障礙變動不會每 Tick 重建整區。
- `onBeforeSpawned` 仍直接綁定實際 Area。Client 不建立自動巡邏點、不執行 AI；接收既有 NetworkObject 移動同步。Director Despawn 時只銷毀自己建立的 Area，人工引用保留；Chunk／場景卸載也會銷毀其子階層。
- Ground 忽略所有飛行欄位。現有 Ground Zone 不會被自動改成 Flying；本次不替換使用者配置的怪物或出生盒。


### 本次驗證

- 新規劃器先以空實作確認 3 項預期失敗，再完成實作；最終 `EnemyFlyingPatrolPlannerTests` 8/8、既有 `EnemyEncounterRulesTests` 9/9，合計 **17/17**。包含實際 Physics 膠囊遇牆／屋頂、未 Spawn Prefab 查詢、人工引用保留、連通集合、旋轉／縮放與節點截斷。
- `PurgersRegression` 完成 **249** 項（發現 264），仍為既有 6 項失敗：Boss Scene 資產 1、經驗值／升級規則 4、槍口材質 1。未達全套零失敗門檻。
- Unity MCP 單人 Host 從主選單 Stage 1 存檔進 Game。只在 Play Mode 建立臨時 `FlyingPatrol_MCP_Verification` Zone、指定 `Enemy_Ranged_A`，人工 Area 留空；正常 `FixedUpdateNetwork` 規劃出 16 點，連同原有 4 個區域共登記 5 區。
- 原臨時出生盒候選有 42/300 點可直達巡邏點、15/300 通過遮擋；首輪有限嘗試合法失敗。收窄臨時出生盒到已確認可用空間後，以受控 KCC 玩家位置跨 Gate，正式 `Runner.Spawn` 分批產生 2 隻 FreeFlying Enemy，均綁定該自動 Area。敵人從出生範圍移至各自巡邏目標附近（如 `(-53.07,4.18,-58.68)` 與 `(-68.69,4.18,-50.90)`），之後也出現新的 PatrolDestination；沒有直接呼叫私有生成方法。
- 測試退出後臨時 Zone／Area 清除，Game Scene 未儲存任何測試配置，作者配置的 Zone 仍為 Ground。Unity `isCompiling=false`、`scriptCompilationFailed=false`。未進行獨立 Host＋Client、晚加入／重連或自然鍵盤飛行 Zone 驗收；本次證據是受控單人 Host。

## 2026-10-02 生成遮擋 Inspector 開關

局部核對來源：`EnemySpawnZone.cs`、`EnemyEncounterDirector.cs`、`EnemySpawnOcclusionTests.cs`。

在 **MapChunk 的生成區物件 → Enemy Spawn Zone → 生成條件** 可調整 **生成需要視野遮擋**（`requireSpawnOcclusion`），預設 **勾選**。此為每個 Zone 各自的布林設定，Ground／Free Flying／Stationary 都適用；不在敵人 Prefab、Approach Gate 或 F4 測試生成器上。

- 勾選：沿用全體存活玩家到候選出生點三個高度的實體遮擋檢查。
- 取消：允許玩家直接看到怪物生成，只略過出生遮擋 Raycast。`Minimum Player Distance`（Director 預設 8 公尺）、最遠／前方距離、占用、導航、預算、方向門檻、分批期限與 FIFO 鎖定仍有效。
- World Occlusion Mask 仍需設定：地面貼地與落後清理仍使用此遮罩。落後敵人清理永遠要求全員遮擋，不受本開關影響。
- 每次選擇出生點會讀取 Host 上該 Zone 的設定，含分批生成；沒有新增 RPC 或 Client 生成權限。若在正式 Prefab 取消並保存，設定也會帶入正式 Build，不是僅 Editor 有效。

測試時先在目標 MapChunk Prefab 的 Zone 取消勾選，再重新開始本輪並穿越 Gate；或者只改 Play Mode 中 Host 實際生成的 Chunk／Zone，結束後不保存。直接可見且通過其他條件時應可生成；保持勾選、相同可見點應被拒絕。兩種設定下，離任一玩家太近或被占用的點皆應被拒絕。已成功的 Zone 仍在輪替鎖定內，切換開關不會解鎖或直接生成；重測首次啟動需重開本輪。

本次未改 Scene／Prefab 設定。新增 8 項 EditMode 測試先確認失敗，再與 Encounter／Stationary 合計 29/29 通過，含真實 PhysicsScene 的有牆／無牆、開關、任一玩家最小距離。完整回歸及 Play Mode／多人驗證邊界見 [130](130_回歸驗證與審查狀態.md)。
