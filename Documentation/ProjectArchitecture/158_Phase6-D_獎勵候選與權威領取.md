# Phase 6-D：獎勵候選與權威領取

> 獎勵 `Weight = 0` 停止新抽、最低等級、重複規則與既有候選／存檔的關係，統一見 [160 經驗與獎勵數值調整手冊](160_Phase6_經驗與獎勵數值調整手冊.md)。

> 後續進度：ALT 本機 HUD／輸入程式由 [159](159_Phase6-E_ALT獎勵HUD與輸入.md) 承接；下文「尚未完成」是此 D 切片交付當時的狀態，Scene／Prefab 目前已配置，實戰仍待驗收。

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`Assets/Scripts/Progression/RewardDraftRules.cs`、`PlayerRewardDefinition.cs`、`PlayerRewardCatalog.cs`、`PlayerRewardNetworkState.cs`、`Save/PlayerRewardSaveBridge.cs`、`Assets/Scripts/GameLogic.cs`、`Player/Ability/Loadout/PlayerAbilityRuntimeManager.cs`。  
> 狀態：程式、五份獎勵資產與 EditMode 驗證完成；多人 Play Mode、輸入及 HUD 尚未驗收。

## 本切片的實際範圍

第一批只使用現有五個鈎索能力 Definition。`Assets/Resources/Progression/PlayerRewardCatalog.asset` 目前收錄四份可抽獎勵資料（使用者確認不加回空中衝刺）；`GameLogic` 可用序列化欄位覆蓋，未指定時從 Resources 載入。尚未實作的武器、角色加成與一次性能力只保留分類，不放入可抽清單。每份資料有穩定 ID、分類、最低玩家等級、權重與重複規則；目前四份可抽資料均為等級 1、權重 1、可再次抽到，但正在裝備的能力不會成為候選。

State Authority 在有待選升級且 Player 能力 Loadout 已初始化時，對合格池按穩定 ID 排序，再以固定 Seed 加權不重複抽出最多三個。若合格者只有兩個，就只顯示兩個；若完全沒有候選，保留待選狀態且不扣獎勵。候選 ID、選單版號及兩個已選鈎索能力 ID 在主要 `GameLogic` 的 Fusion 狀態中同步；Client 僅展示。每張獎勵的越級 25% 判定與隨等級增加的越級上限是後續規格，本切片未啟用，因尚無對應 Debuff 效果，不能先送出沒有代價的越級獎勵。

選擇請求帶選單版號與索引送往 State Authority。Host 依 RPC 來源玩家、待選數、版號、當前候選與實際 Player 能力管理器重新驗證；成功透過既有 `PlayerAbilityRuntimeManager` 的槽位／互斥檢查，先交易式生成並替換該分類的 Runtime，才扣一個待選獎勵並寫入已領／已裝 ID。失敗不扣獎勵。多個待選獎勵依序產生新候選。死亡重生或換場景後，主要 `GameLogic` 在新 Player 預設 Loadout 建立後重新套用已選能力。

Host 存檔升至 v3，新增待選候選 ID、已領 ID、已裝鈎索能力 ID；v1／v2 讀入時空候選可由 Host 重新抽。只有 Host 自己的存檔寫入；訪客進度僅在本次 Session。失敗循環提交後連同經驗一起重設。

## 尚未完成與驗證

- 本切片沒有可供玩家操作的 ALT 視窗或 L／M／R 滑鼠輸入；下一切片依 [154 戰鬥 HUD 視覺配置](154_戰鬥HUD視覺配置第一版.md) 接入，ALT 期間不鎖 WASD／視角，按住 ALT 時滑鼠選擇優先於開火／瞄準。
- EditMode 聚焦測試 10／10 通過。完整 173 項回歸中仍有先前同名的兩項 Boss 資產接線失敗，並非本切片變更。
- 尚需獨立進程 Host／Client 驗證候選一致、Client 選擇請求、非法／過期請求、玩家死亡重生、晚加入、成功撤離存檔再進入與失敗重設。未取得這些證據前，不宣稱多人流程驗收完成。

閱讀順序：[155](155_Phase6-A_玩家經驗與存檔規則.md) → [156](156_Phase6-B_玩家進度權威同步.md) → [157](157_Phase6-C_敵人擊殺經驗發放.md) → 本文件 → [154](154_戰鬥HUD視覺配置第一版.md)。


## 2026-10-04 E 主動技能原型接入（局部核對）

最後核對日期：2026-10-04（僅本節責任與接點；較早章節為當時基線）。
核對來源：Assets/Scripts/ 下的 GameLogic、PlayerAbilityQualification、PlayerAbilityExperienceRules、PlayerRewardCatalog。
相關文件：[42 E 主動技能規格與驗收](42_E主動技能規格與強化預留.md)、[驗證紀錄](Validation/ActiveAbilities/README.md)。

現有 Assets/Resources/Progression/PlayerRewardCatalog.asset 保留原四項權重與引用，追加十項 active.* reward，合計十四；新項權重 1、最低等級 1、Repeatable，已裝同類技能仍被排除。AirDash 原未入池維持不變。精準鎖敵於產生候選及領取前都重查遠程武器，失格領取不消耗 pending；其他裝備驗證仍由原 RuntimeManager 負責。

變更紀錄：2026-10-04 同步本節結構、資產與權威邊界；強化消耗仍未實作。
