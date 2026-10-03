# Phase 6-C：敵人擊殺經驗發放

> 各敵人 `Base Kill Experience`、擊殺者加成、升級門檻與測試方式統一見 [160 經驗與獎勵數值調整手冊](160_Phase6_經驗與獎勵數值調整手冊.md)。以下未完成敘述為 C 切片交付當時的狀態。

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`Assets/Scripts/Enemy/Core/EnemyDefinition.cs`、`EnemyDeathLifecycleController.cs`、`Assets/Scripts/GameLogic.cs`、`Assets/Scripts/Combat/Damage/TestDamageReceiver.cs`。  
> 狀態：權威死亡事件接線與 EditMode 資產檢查完成；獨立 Host／Client 擊殺 Play Mode 尚未驗收。

## 資料與發放流程

`EnemyDefinition.BaseKillExperience` 是 Phase 5 暫緩時的獨立基礎經驗入口，預設 1、Inspector 可依敵人類型調整，0 表示不發。既有四份正式 Definition 在 Editor 讀到的值都是 1。現有 `Enemy_Boss` 共用 `ED_Melee_A`，因此目前也為 1；Boss 專屬數值應等獨立 Definition／平衡設計確定後配置，不在此切片私自改 Prefab。

State Authority 上的 `TestDamageReceiver.Died` 攜帶最後一筆真正致死的 `DamageResult`。`EnemyDeathLifecycleController` 只在首次從 Alive 進入 Dying、且傷害確實被接受並造成擊殺、同 Runner 關卡仍為 Active 時，把攻擊者 `PlayerRef` 與基礎經驗交給主要 `GameLogic`。單靠下一 Tick 的死亡狀態補救路徑沒有可靠擊殺者，因此不補發經驗；環境傷害／無有效連線玩家擊殺者也不發。

`GameLogic` 先確認擊殺者仍是本次 Session 的連線玩家，再逐一檢查每個連線隊友目前是否存活。存活隊友各取得完整基礎值，擊殺者額外 +1；死亡／等待重生者不領。若擊殺者當下已死亡但仍連線，其他存活隊友仍領基礎值，擊殺者不領。每位玩家再由既有 `PlayerExperienceRules.Award` 個別判定：有待選獎勵者拒收後續擊殺經驗，其他隊友不受影響。結果只由 State Authority 寫入 Phase 6-B 的 Fusion 進度，Client／HUD 不自行決定。

## 驗證與邊界

- 聚焦 `EnemyExperienceSourceTests` 7／7：新 Definition 預設值、可設定獨立數值、五個正式敵人 Prefab 均具死亡管理元件與正數基礎經驗。
- 四份既有 Definition 在 Editor 實際讀取均為 1；五個正式 Variant 均有 `EnemyDeathLifecycleController` 與非空 Definition。
- 這些 EditMode 證據不等於實際擊殺發放已通過 Host／Client。須在獨立進程驗證：Host 擊殺、Client 擊殺、存活／死亡／待選混合隊伍、同一隻敵人只發一次、環境擊殺不發、關卡非 Active 時不發、晚加入同步及成功撤離寫回 Host 存檔。
- 尚無選獎勵的輸入、候選、效果或 HUD。玩家升級後仍會依 Phase 6-A 規則停止收後續經驗，直到後續切片實作領取。

閱讀順序：[155](155_Phase6-A_玩家經驗與存檔規則.md) → [156](156_Phase6-B_玩家進度權威同步.md) → 本文件 → `EnemyDeathLifecycleController.cs` → `GameLogic.cs` → [140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md)。
