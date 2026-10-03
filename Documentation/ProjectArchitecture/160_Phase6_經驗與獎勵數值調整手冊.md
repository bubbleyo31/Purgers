# Phase 6：經驗與獎勵數值調整手冊

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`PlayerExperienceRules`、`DevelopmentLevelUpRules`、`EnemyDefinition`、`EnemyDeathLifecycleController`、`GameLogic`、`PlayerRewardDefinition`、`PlayerRewardCatalog`、`RewardDraftRules`、`LocalPlayerRewardHUD`、Host 存檔橋接及目前 Unity 資產。  
> 用途：說明設計者手動調整升級門檻、擊殺經驗、獎勵候選及快速測試的方法；**本文件沒有改動任何遊戲數值或資產**。程式與資產是最終真相，實戰及獨立 Host／Client 驗收仍待完成。

## 先看：每個數值在哪裡

| 想調整的內容 | 目前入口 | 是否可在 Inspector 改 | 影響範圍 |
|---|---|---|---|
| 升下一級所需經驗 | `Assets/Scripts/Progression/PlayerExperienceRules.cs` 的 `DefaultBaseRequirement = 10`、`DefaultGrowthMultiplier = 2` | **否，現在是 C# 共用常數** | 所有玩家、HUD 分母與 F8 測試鍵都使用同一套門檻 |
| 擊殺各種敵人的基礎經驗 | `Assets/_Project_Assets/Data/Enemy/ED_*.asset` 的 `Base Kill Experience` | 是 | 該 Definition 對應的所有敵人；存活隊友各得完整基礎值 |
| 擊殺者額外經驗 | `PlayerExperienceRules.cs` 的 `DefaultKillerBonus = 1` | 否 | 有效擊殺者在基礎值之外多得的點數 |
| 某能力是否可被新抽到／抽中權重 | `Assets/_Project_Assets/Data/Progression/Reward_*.asset` 的 `Weight` | 是；`0` 代表不進**新候選** | 新產生的獎勵選單，不會回頭改已抽選單 |
| 能力從幾級開始候選、是否可重複 | 同一份 `Reward_*.asset` 的 `Minimum Player Level`、`Repeat Policy` | 是 | 後續候選篩選；已裝備能力仍不會重複成為候選 |
| 可供查找的獎勵清單 | `Assets/Resources/Progression/PlayerRewardCatalog.asset` 的 `Rewards` | 是，但一般平衡**不要刪項目** | 抽池建立，也供已抽、已取得及重生還原時查找 ID |

目前沒有「玩家經驗倍率」或「升級門檻」的 ScriptableObject／Inspector 欄位。若將來要由企劃不改程式就調門檻，需要另設共用設定資產、權威讀取與既有存檔遷移規則；不能只改 HUD 顯示數字。

## 1. 調整玩家升級門檻

打開 [`PlayerExperienceRules.cs`](../../Assets/Scripts/Progression/PlayerExperienceRules.cs)，修改 `DefaultBaseRequirement`（第一級門檻）和／或 `DefaultGrowthMultiplier`（逐級整數倍率）。目前的公式是「**目前等級 L → 下一級**所需新增經驗 = `10 × 2^(L−1)`」，極大值會飽和到 `int.MaxValue`，不是把所有歷史門檻相加後才顯示在經驗條上。

| 目前等級 | 升下一級所需 | 從 Lv.1／0 經驗累計到下一級 |
|---|---:|---:|
| 1 | 10 | 10 |
| 2 | 20 | 30 |
| 3 | 40 | 70 |
| 4 | 80 | 150 |
| 5 | 160 | 310 |

例如只想改成 1→2 級要 20 點、其後維持倍增，就將 `DefaultBaseRequirement` 改為 `20`，保留 `DefaultGrowthMultiplier = 2`；新門檻變成 20、40、80……。成長倍率目前是整數，不支援直接填 1.5；不規則逐級表也不是改這兩個常數就能達成。改完要同步更新 `PlayerExperienceRulesTests.RequirementDoublesFromTen` 等門檻斷言及必要的平衡測試，不要為了讓測試通過而忽略新需求。

`GameLogic` 的 State Authority 呼叫 `PlayerExperienceRules.Award` 更新同步等級／經驗／待選數；[`LocalPlayerRewardHUD.cs`](../../Assets/Scripts/UI/Player/LocalPlayerRewardHUD.cs) 只呼叫同一 `RequiredExperience` 顯示分母；開發版 F8 的 [`DevelopmentLevelUpRules.cs`](../../Assets/Scripts/Tool/DevelopmentLevelUpRules.cs) 也依同一門檻補足差額。因此不要分別修改 HUD 或 F8 的數字。既有存檔的等級、目前經驗和待選獎勵不會因修改公式自動重算；若舊經驗已高於新門檻，下一次正式加經驗可能連升多級，而 F8 在這種狀態下不補發。平衡變更應先用新一輪／乾淨測試檔驗證，再決定正式存檔遷移策略。

## 2. 調整擊殺經驗與擊殺者加成

在 Unity Project 視窗開啟 `Assets/_Project_Assets/Data/Enemy/`，選擇 `ED_Melee_A`、`ED_Melee_B`、`ED_Ranged_A` 或 `ED_Ranged_B`，在 Inspector 的「玩家經驗」區調整 `Base Kill Experience`。目前四份 Definition 的讀值都是 `1`。一份 Definition 可能被多個敵人 Prefab 共用；目前 `Enemy_Boss` 也使用 `ED_Melee_A`，所以改它會連 Boss 一起改。若 Boss 要獨立數值，應先建立並正確接線專屬 Definition，而不是以為只改 Boss Prefab 外觀就能分開。

只有正式 Enemy 的權威死亡流程確認有效玩家擊殺、關卡仍為 Active 時才發放。存活且連線的每名隊友各得基礎值，擊殺者再多得 `DefaultKillerBonus`（目前 `1`）；死亡／待重生者不領。沒有有效玩家擊殺者不發。`Base Kill Experience = 0` 代表**整次擊殺不發經驗**，連擊殺者加成都不會單獨發；目前 `EnemyExperienceSourceTests` 對正式 Prefab 有「經驗必須大於 0」的測試，若有意讓正式敵人給 0，須一併修訂該產品規則與測試。Phase 5 尚未實作，此值不讀 Enemy Level，也沒有隱藏的等級倍率。

若只想調「擊殺者多一點」的差額，改 `PlayerExperienceRules.DefaultKillerBonus`，而不是逐個提高敵人 Definition；後者會讓**全隊**同時多拿經驗。這個加成也是 C# 共用常數，修改後需更新 `PlayerExperienceRulesTests`。

## 3. 調整獎勵池，而不破壞舊獎勵

在 Unity Project 視窗開啟 `Assets/_Project_Assets/Data/Progression/`，選取對應的 `Reward_*.asset`：

- 不想讓它出現在**新抽選單**：將 `Weight` 設成 `0`；要恢復設回正數。目前磁碟上的五份資產權重都是 `1`。
- 想讓它較晚出現：提高 `Minimum Player Level`。它只限制候選資格，不會改升級門檻。
- 想調整相對機率：將 `Weight` 設為正整數；權重只在目前合格、未被排除的池中比較，不代表固定百分比。
- 想限制領過後再抽：檢查 `Repeat Policy`。`Repeatable` 可在領過後再候選，`OncePerRun` 會排除本輪已領 ID；**目前裝備中的能力**仍被排除。

截至本次磁碟核對，[`PlayerRewardCatalog.asset`](../../Assets/Resources/Progression/PlayerRewardCatalog.asset) 列出五份現有鈎索獎勵。通常不要從 Catalog 刪除項目、刪 `.asset`、改 `Stable Reward Id` 或把能力 Definition 清空：Catalog 不只用於抽池，還用於查找已抽候選、已領取獎勵和重生後的能力還原。即使 `Weight = 0`，Catalog 仍要求該項目是有效、可實作的獎勵；空值、重複 ID 或未實作類別會令整個抽池建立失敗。現有 `PlayerRewardCatalogTests` 也驗證五份可用獎勵，移除項目會使該測試失敗。

Host 每次新建候選最多抽三個且同次不重複；合格項目只有兩個就顯示兩個。**已經產生、尚待選擇的候選不會因改權重自動重抽**，Host 存檔也可能保留該候選。若把所有合格項目排除到一個都沒有，待選獎勵會保留、後續經驗繼續被鎖住；不要用「清空池」作為停用整套升級的方式。Game 場景現有 `GameLogic` 未指定覆蓋 Catalog，使用 `Resources/Progression/PlayerRewardCatalog`；如果日後另指定 Catalog，先確認 Host 與 HUD 指向同一份資料。

## 4. 快速測試與驗收

1. 在 **Edit Mode** 完成資產／程式調整並確認已儲存；有未儲存 Inspector 變更時，先決定保留或還原，別讓測試結果與磁碟上的 `.asset` 不一致。
2. 若改 C# 門檻／加成，等 Unity 編譯完成並檢查 Console；跑 `PlayerExperienceRulesTests`、`DevelopmentLevelUpRulesTests`。若改 Enemy Definition，跑 `EnemyExperienceSourceTests`；若改獎勵池，跑 `RewardDraftRulesTests`、`PlayerRewardCatalogTests`。最後跑非零數量的完整 `PurgersRegression` EditMode。既有 Boss 資產接線失敗須和新失敗分開記錄。
3. Editor Play Mode／Development Build 進入戰鬥 Game Scene，玩家存活且獎勵 HUD 有效時按一次 **F8**：Host 應補足到下一級，產生待選獎勵；已有待選時再按不會加經驗。F8 是**測選窗與領獎流程**的捷徑，不是檢驗擊殺發放速度的替代品。
4. 按住 ALT 查看候選，三選用左／中／右鍵、兩選用左／右鍵；不鎖 WASD／視角。領完後可再按 F8。另以真實擊殺核對各隊員與擊殺者經驗差、死亡隊友不領、`Weight = 0` 不出現在**新候選**、成功／失敗結算及重生。
5. 多人平衡或 RPC 改動須用**獨立 Host／Client 程序**驗證。EditMode 通過和單一 Editor Play Mode 均不能代替多人驗收。

## 權責與存檔邊界

`EnemyDeathLifecycleController` 確認致死事件 → 主要 `GameLogic` 在 State Authority 篩選存活玩家與計算個別獎勵 → `PlayerExperienceRules` 計算升級／超額／待選 → `RewardDraftRules` 由 Host 產生候選 → 本地 HUD 只讀顯示。單次超額經驗保留，可一次產生多份待選獎勵；已有待選時，之後的擊殺經驗對該玩家直接拒收，隊友仍可領。Host 存檔目前 schema v3，成功提交關卡前寫回 Host 進度；失敗寫檔成功後重設本輪進度。訪客沒有跨 Session 的穩定存檔身分。任何平衡調整都不要從 HUD、Client 或存檔 JSON 直接寫入網路權威值。

閱讀順序：[155 經驗純規則](155_Phase6-A_玩家經驗與存檔規則.md) → [157 擊殺發放](157_Phase6-C_敵人擊殺經驗發放.md) → [158 獎勵池與領取](158_Phase6-D_獎勵候選與權威領取.md) → [159 ALT HUD 與 F8](159_Phase6-E_ALT獎勵HUD與輸入.md) → 本手冊及其程式／資產入口。

## 變更紀錄

- 2026-09-24：首次整合 Phase 6 門檻、擊殺發放、獎勵池、F8、存檔與驗證操作；只新增文件，未修改遊戲平衡值。
