# Phase 6-E：ALT 獎勵 HUD 與輸入

> **2026-09-24 後續狀態**：使用者回報 Phase 6 目前手動測試通過；本切片驗收狀態記為「通過（使用者回報）」。本次未重新執行 Play Mode 或收到逐項獨立 Host／Client 日誌，故下方「仍待實測」清單保留為先前證據快照，不等於本輪已對每項取得可重現證據。總狀態與既有自動測試失敗見 [130](130_回歸驗證與審查狀態.md)。

> F8 快速升級與所有 Phase 6 數值調整入口、限制及驗證步驟見 [160 經驗與獎勵數值調整手冊](160_Phase6_經驗與獎勵數值調整手冊.md)。

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`Assets/Scripts/InputManager.cs`、`GameLogic.cs`、`Tool/DevelopmentLevelUpRules.cs`、`GameFlow/Stage/StageHudController.cs`、`Progression/RewardSelectionInputRules.cs`、`UI/Player/LocalPlayerRewardHUD.cs`、`Assets/Prefabs/UI/StageHUD.prefab`、`Player/Ability/Loadout/PlayerAbilityDefinition.cs`、目前四項可抽 Reward 資產與 [154 視覺規格](154_戰鬥HUD視覺配置第一版.md)。  
> 狀態：程式、獎勵說明資料、`StageHUD` Prefab UI 與 Editor 欄位接線完成；**尚未通過實際戰鬥 Play Mode／獨立 Host-Client 驗收**。

## 行為與權責

`InputManager` 只在本機 Player 存活、有同步候選、待選數大於零、HUD 已配置可顯示且游標鎖定時，接受按住左／右 ALT。此時左／中／右鍵分別送出三個候選；只有兩個候選時只用左／右鍵，中鍵忽略。選擇期間把傳給 Fusion 的 Fire／Aim／Ability1 位元清為 false，WASD、視角、跳躍、鈎索、移動及其他按鍵照常提交，世界時間也不暫停。放開 ALT 隱藏選窗；選擇期間按住的左／右／中鍵各自維持消耗狀態，必須實際放開後再按，才恢復原功能。獎勵領完、HUD 解除註冊或 ClearPendingInput 不會提前解除此狀態。場景轉換等既有全輸入鎖仍優先，並非獎勵新鎖。

Host 本地點選先排入下一個 Fusion Tick，再由主要 `GameLogic` 在 `FixedUpdateNetwork` 驗證並實際領取；Client 只送既有帶版號的 RPC，結果仍由 State Authority 決定。HUD 只讀 `GameLogic` 的同步經驗與候選，不自行加經驗、扣待選或裝備能力。未配置 `LocalPlayerRewardHUD` 時，不啟用獎勵滑鼠攔截，避免無畫面盲選。

經驗條讀同步等級／目前經驗；待選時視覺停在滿格、變亮並脈動，ALT 提示閃爍。`LocalPlayerRewardHUD` 沿用 Prefab 作者排版，不再於 Runtime 覆寫錨點。三個候選圖示在中央下方橫排，說明框位於左右外側與中央下方；兩選時隱藏中卡。卡片顯示能力名稱、Reward 說明、滑鼠圖與同一 `PlayerAbilityDefinition.HudIcon`。取消白色全畫面遮罩，背景與既有 HUD 維持原亮度，說明底板不透明；轉場黑幕仍在其上。原圖座標與比例以 [154](154_戰鬥HUD視覺配置第一版.md) 為準。

## Unity Prefab 實際配置

已透過 Unity Editor 配置 `StageHUD.prefab`；目前子階層為 `ReferenceFrame/Phase6RewardHud`。Canvas Scaler 為 `Scale With Screen Size`／`1920×1080`／Expand，sorting order 100；共同 `BattleHudReferenceFrame` 再把PDF 還原的 1920×1080 等比放入畫面。`TopLeftColumn`、小地圖與 Stage Panel 亦在同一 Frame。原有 `Tools/Purgers/Phase 6/Build Reward HUD on StageHUD` 以既有元件判斷是否已配置，避免因重整階層而重複建置。

1. `ExperienceGroup` 在原圖座標 `(50,612)`，等級圓圈與細斜經驗條取代舊大底板；只呈現同步經驗，不改血量或動能來源。
2. `BackgroundDim` 保留序列化引用但停用 GameObject、alpha=0／dimOpacity=0，不顯示白色遮罩；`ChoiceWindow` 撐滿 ReferenceFrame，內部圖示中心為 `(787.03,756.56)/(962.89,731.71)/(1138.17,756.56)`，說明框為 `(330.76,699.66)/(779.31,858.21)/(1232.92,699.66)`、366.32×113.79。兩選時隱藏中卡；能力圖示與裝備槽共用。
3. `LocalPlayerRewardHUD` 的 Catalog、經驗、提示、遮罩、選窗與三卡必填欄位皆已在 Prefab 連好，`CanPresentRewardSelection` 在 Editor 載入的 Prefab instance 為 true。CanvasGroup 與 Image 都不攔截 Raycast，因此 ALT 只由輸入規則處理，不以 UI 擋住 WASD／視角。
4. 驗收測試 `Phase6RewardHudPrefabTests.StageHudContainsWiredPhase6RewardHud` 曾在缺少節點時失敗，建立並接線後 1/1 通過。完整 `PurgersRegression` EditMode 執行 177 項，其中原有兩項 Boss 資產接線測試仍失敗；這兩項在本次 Prefab 建置前亦失敗，與本次改動無關。

## 開發者快速升級鍵

Editor Play Mode／Development Build 中，在 Game 場景已顯示獎勵 HUD 且本機輸入未被轉場鎖定時，按一次 **F8**，只為按鍵者補足升下一級所差的經驗。即使游標暫時解鎖也可觸發。Host 本機要求排到主要 `GameLogic.FixedUpdateNetwork`；Client 使用開發版專用 RPC 向 State Authority 請求。Host 再檢查玩家存活、目前同步進度和權限，沿用 `TryAwardPlayerExperience` 產生待選獎勵及候選，不由 Client 或 HUD 直接改等級。已有待選獎勵時不再加經驗，須先完成選擇才能再次按 F8；正式非 Development Build 不含此輸入／RPC 入口。

`DevelopmentLevelUpRulesTests` 先確認缺少實作時會失敗，完成後 3／3 通過；完整 `PurgersRegression` EditMode 執行 180 項，仍只有既有兩項 Boss 資產接線失敗。F8 的實際 Host／Client 按鍵、候選畫面與存檔生命週期仍需 Play Mode 驗收。

## Play Mode 驗收（仍待實測）

1. Host 單人擊殺升級：經驗條滿、ALT 提示脈動；不按 ALT 仍可射擊與瞄準，後續擊殺不再增加該玩家經驗。
2. 按住 ALT：比對 [選窗渲染圖](Validation/BattleHUD/reward-1440x810.png) 的左右／下方說明位置；WASD、視角、跳躍、鈎索仍可操作；左／右鍵不再開火／瞄準。三選測左／中／右；兩選測左／右，中鍵不得領取。
3. 點選後由 Host 確認能力真正替換、待選數減一、選窗依新同步狀態更新；領完仍按住選擇鍵、或先放開 ALT，均不得射擊／瞄準；放開該滑鼠鍵再按才恢復。過期版號或無效選擇不得扣獎勵。
4. 分開的 Host／Client 程序重測 Client 點選、候選一致、死亡重生、晚加入、成功撤離再進入與失敗重設；檢查 Console 和漏失序列化引用。這些未完成前，不能宣稱多人驗收通過。

限制：五份能力已有示意圖示及裝備後技能槽 HUD，正式美術仍待替換；未指定 Sprite 時使用六角星佔位。獎勵長按鍵預設接受左／右 ALT，可在 `InputManager` Inspector 更改，但尚未接到玩家設定與持久化重綁系統。此切片不實作越級獎勵或 Debuff；實戰畫面與獨立 Host／Client 驗收仍未完成。

## 變更紀錄

- 2026-09-24：加入僅 Editor／Development Build 的 F8 單級測試入口；輸入端只送意圖，State Authority 透過既有經驗規則決定結果。

閱讀順序：[154 視覺規格](154_戰鬥HUD視覺配置第一版.md) → [158 權威獎勵](158_Phase6-D_獎勵候選與權威領取.md) → 本文件 → `InputManager.cs` → `LocalPlayerRewardHUD.cs`。

## 2026-09-24 候選數與滑鼠隔離

- 使用者確認保留四項 Catalog：命中標記、鈎索聚集、鈎索拉近、空中緩降；不加回空中衝刺。預設 Loadout 已裝備拉近與緩降，排除後剩標記與聚集，故只顯示左右卡，中鍵不領取。若將來合格池有三項，既有中卡與中鍵路由會自動啟用；不複製獎勵補第三項。
- `InputManager.rewardMouseButtonsBlockedUntilRelease` 記錄每個已消耗滑鼠鍵；BeforeUpdate 以實際 Held 解除放開的位元，合併 NetworkButtons 後再清除被攔截的 Fire／Aim，包含尚未提交的舊輸入。中鍵目前沒有戰鬥用途，但同樣記錄到放開。
- Host／State Authority 的版本檢查、抽選、裝備與扣獎勵責任不變；多人實戰仍待驗收。
