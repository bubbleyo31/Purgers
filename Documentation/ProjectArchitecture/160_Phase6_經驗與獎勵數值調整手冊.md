# Phase 6：經驗與獎勵數值調整手冊

> 最後核對日期：2026-10-17（逐級設定局部；其餘保留原範圍）
> 核對來源：`PlayerExperienceRules`、`DevelopmentLevelUpRules`、`EnemyDefinition`、`EnemyDeathLifecycleController`、`GameLogic`、`PlayerRewardDefinition`、`PlayerRewardCatalog`、`RewardDraftRules`、`LocalPlayerRewardHUD`、Host 存檔橋接及目前 Unity 資產。  
> 用途：說明設計者手動調整升級門檻、擊殺經驗、獎勵候選及快速測試的方法；2026-10-17 已接入逐級設定，初始值保留舊曲線。程式與資產是最終真相，實戰及獨立 Host／Client 驗收仍待完成。

## 先看：每個數值在哪裡

| 想調整的內容 | 目前入口 | 是否可在 Inspector 改 | 影響範圍 |
|---|---|---|---|
| 升下一級所需經驗 | Assets/Resources/Progression/PlayerExperienceSettings.asset | **是，逐級正整數** | Host 升級與 F8 共用快照，HUD 讀同步門檻；表尾固定 |
| 擊殺各種敵人的基礎經驗 | `Assets/_Project_Assets/Data/Enemy/ED_*.asset` 的 `Base Kill Experience` | 是 | 該 Definition 對應的所有敵人；存活隊友各得完整基礎值 |
| 擊殺者額外經驗 | `PlayerExperienceRules.cs` 的 `DefaultKillerBonus = 1` | 否 | 有效擊殺者在基礎值之外多得的點數 |
| 某能力是否可被新抽到／抽中權重 | `Assets/_Project_Assets/Data/Progression/Reward_*.asset` 的 `Weight` | 是；`0` 代表不進**新候選** | 新產生的獎勵選單，不會回頭改已抽選單 |
| 能力從幾級開始候選、是否可重複 | 同一份 `Reward_*.asset` 的 `Minimum Player Level`、`Repeat Policy` | 是 | 後續候選篩選；已裝備能力仍不會重複成為候選 |
| 可供查找的獎勵清單 | `Assets/Resources/Progression/PlayerRewardCatalog.asset` 的 `Rewards` | 是，但一般平衡**不要刪項目** | 抽池建立，也供已抽、已取得及重生還原時查找 ID |

## 1. 調整玩家升級門檻

最後核對日期：2026-10-17（本節）。核對來源：PlayerExperienceSettings、PlayerExperienceSettingsEditor、PlayerExperienceRules、GameLogic、LocalPlayerRewardHUD、DevelopmentLevelUpRules。
相關文件：[155 純規則](155_Phase6-A_玩家經驗與存檔規則.md)、[驗證紀錄](Validation/ExperienceTable/README.md)。

### Inspector 操作

1. 在非 Play Mode 使用 **Tools → Purgers → Progression → 開啟逐級經驗設定**，或選取 [PlayerExperienceSettings.asset](../../Assets/Resources/Progression/PlayerExperienceSettings.asset)。
2. 每列直接標示 **Lv.1 → Lv.2** 等區間。填入本級需要新增的經驗，不是歷史累計總數。
3. 範圍 **1～2,147,483,647**；不要求逐級遞增。小於 1 的資料在 Inspector、快照與純規則入口皆限制為 1。
4. **新增下一級**在末尾加一列並複製最後一筆；**移除最後一級**縮短表格，至少保留一列。支援 Undo。
5. **超出表格後，所有等級固定沿用最後一筆；不因表格長度而設滿級。** 例如只保留 5、15、30，1→2 要 5、2→3 要 15，3→4 及後續每級都要 30。
6. 儲存資產並重新啟動 Session。切換 Game／SafeHouse 不重讀表；Play Mode 修改不會即時影響當前 Session。

### 初始值與設定位置

正式資產：Assets/Resources/Progression/PlayerExperienceSettings.asset。不需 Scene、玩家 Prefab 或 HUD 接線；Host 的主要 GameLogic 載入一次並複製。
初始 30 列完整保留舊曲線：5、10、20、40、80……，第 29 列為 1,342,177,280，第 30 列及後續為 int.MaxValue。這是相容初始值，不是建議的最終平衡；想用短表時可移除末尾列。

缺少資產或空表時 Host 明確警告並退回舊曲線；正式資產測試要求存在且非空。工具選單只建立缺少的資產並選取，不覆寫已有表格、不保存 Scene／Prefab。

### 權責、同步與存檔

- PlayerExperienceSettings 保存作者資料並建立獨立快照；PlayerExperienceRules 維持純計算，接受逐級表，保留超額、連升與待領累加。表尾採整除，避免大額經驗逐級跑大量迴圈。
- GameLogic 的 State Authority 使用 Session 快照處理正式所得與 F8；F8 不套能力經驗倍率。
- NetworkPlayerExperience.RequiredExperience 與等級、經驗、待選數一起同步。TryGetPlayerExperience 新增取得門檻的 overload，原查詢保留。HUD 包含安全屋及觀戰來源都讀同步門檻，不再用本地常數計算。
- 初始化／讀檔、領獎、失敗重設與升級皆經同一個同步狀態建構入口；晚加入讀既有狀態。新增同步欄位要求 **Host／Client 使用同版程式**。
- 舊公式 overload 保留相容測試與缺設定備援；正式升級不需再修改 DefaultBaseRequirement。
- 存檔 schema 不變，仍保存等級、經驗、待領獎勵，不保存表格或分母。新 Session 使用目前資產，不回算歷史進度。
- 舊經驗高於新門檻時，載入不自動升級；下次正式取得正經驗才結算。F8 已達門檻時不補發；既有候選不因改表重抽。

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

Host 每次新建候選最多抽三個且同次不重複；合格項目只有兩個就顯示兩個。**已經產生、尚待選擇的候選不會因改權重自動重抽**，Host 存檔也可能保留該候選。若把所有合格項目排除到一個都沒有，待選獎勵會保留，後續經驗仍可累積；不要用「清空池」作為停用整套升級的方式。Game 場景現有 `GameLogic` 未指定覆蓋 Catalog，使用 `Resources/Progression/PlayerRewardCatalog`；如果日後另指定 Catalog，先確認 Host 與 HUD 指向同一份資料。

## 4. 快速測試與驗收

1. 在 **Edit Mode** 完成資產／程式調整並確認已儲存；有未儲存 Inspector 變更時，先決定保留或還原，別讓測試結果與磁碟上的 `.asset` 不一致。
2. 若改 C# 門檻／加成，等 Unity 編譯完成並檢查 Console；跑 `PlayerExperienceRulesTests`、`DevelopmentLevelUpRulesTests`。若改 Enemy Definition，跑 `EnemyExperienceSourceTests`；若改獎勵池，跑 `RewardDraftRulesTests`、`PlayerRewardCatalogTests`。最後跑非零數量的完整 `PurgersRegression` EditMode。既有 Boss 資產接線失敗須和新失敗分開記錄。
3. Editor Play Mode／Development Build 進入戰鬥 Game Scene，玩家存活且獎勵 HUD 有效時按一次 **F8**：Host 應補足到下一級，產生待選獎勵；已有待選時再按仍可升級並追加待領。F8 是**測選窗與領獎流程**的捷徑，不是檢驗擊殺發放速度的替代品。
4. 按住 ALT 查看候選，三選用左／中／右鍵、兩選用左／右鍵；不鎖 WASD／視角。領完後可再按 F8。另以真實擊殺核對各隊員與擊殺者經驗差、死亡隊友不領、`Weight = 0` 不出現在**新候選**、成功／失敗結算及重生。
5. 多人平衡或 RPC 改動須用**獨立 Host／Client 程序**驗證。EditMode 通過和單一 Editor Play Mode 均不能代替多人驗收。

## 權責與存檔邊界

`EnemyDeathLifecycleController` 確認致死事件 → 主要 `GameLogic` 在 State Authority 篩選存活玩家與計算個別獎勵 → `PlayerExperienceRules` 計算升級／超額／待選 → `RewardDraftRules` 由 Host 產生候選 → 本地 HUD 只讀顯示。單次超額經驗保留，可一次產生多份待選獎勵；已有待選時，之後的擊殺經驗仍正常累積。Host 存檔目前 schema v4，成功提交關卡前寫回 Host 進度；失敗寫檔成功後重設本輪進度。訪客沒有跨 Session 的穩定存檔身分。任何平衡調整都不要從 HUD、Client 或存檔 JSON 直接寫入網路權威值。

閱讀順序：[155 經驗純規則](155_Phase6-A_玩家經驗與存檔規則.md) → [157 擊殺發放](157_Phase6-C_敵人擊殺經驗發放.md) → [158 獎勵池與領取](158_Phase6-D_獎勵候選與權威領取.md) → [159 ALT HUD 與 F8](159_Phase6-E_ALT獎勵HUD與輸入.md) → 本手冊及其程式／資產入口。

## 變更紀錄

- 2026-09-24：首次整合 Phase 6 門檻、擊殺發放、獎勵池、F8、存檔與驗證操作；只新增文件，未修改遊戲平衡值。


## 2026-10-04 E 主動技能原型接入（局部核對）

最後核對日期：2026-10-04（僅本節責任與接點；較早章節為當時基線）。
核對來源：Assets/Scripts/ 下的 GameLogic、PlayerAbilityExperienceRules、PlayerExperienceAbility。
相關文件：[42 E 主動技能規格與驗收](42_E主動技能規格與強化預留.md)、[驗證紀錄](Validation/ActiveAbilities/README.md)。

經驗增加作用 5 秒，只把該玩家的正式所得經驗乘 2；F8 補足一級不乘倍。子彈時間延後擊殺以 base XP + 1 均分固定初始名單中的有效玩家，再套各自當下倍率；整數餘數依 PlayerRef 排序，pending 玩家仍獲得自身份額，不轉給別人。門檻常數不變。

變更紀錄：2026-10-04 同步本節結構、資產與權威邊界；強化消耗仍未實作。

## 2026-10-05 門檻文件與測試校正（局部）

實際 DefaultBaseRequirement 早已為 5，本輪對齊上方公式、表格及四項過期測試，不改遊戲數值。基線與後續結果見 [E 技能穩定化](Validation/ActiveAbilityStabilization/README.md)。


## 2026-10-10 武器獎勵與換裝（局部核對）

最後核對日期：2026-10-10。核對來源：GameLogic、Progression、PlayerProfessionRuntimeManager、LocalPlayerRewardHUD。

Reward_WeaponAttack／Tank／Support 皆為最低等級 1、權重 1、Repeatable。Weapon 分類設定 Weapon Profession、Ability Definition 留空；目前武器排除，換掉可再抽。與技能混池，不保證每次有武器。

正式規則／Inspector 入口見 [46](46_武器獎勵與換裝.md)，證據見 [驗證紀錄](Validation/WeaponRewards/README.md)。


## 2026-10-16 成長獎勵與技能掉落局部核對

屬性階級值、5% 被動機率、六被動消耗及新欄位設定統一記錄於 168；既有 18 裝備不改權重，新 15 成長目前同權重。 詳見 [168 角色成長、被動與掉落](168_角色成長被動與技能掉落.md)。

實作／資產與測試證據分開列於 168，未宣稱獨立多程序聯機或 Build 驗收。
