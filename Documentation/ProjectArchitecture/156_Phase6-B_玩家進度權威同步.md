# Phase 6-B：玩家進度權威同步

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`Assets/Scripts/GameLogic.cs`、`Assets/Scripts/GameFlow/Stage/StageFlowController.cs`、`Assets/Scripts/Progression/Save/PlayerExperienceSaveBridge.cs`。  
> 狀態：程式接線及 EditMode 測試完成；獨立 Host／Client 與切場 Play Mode 尚未驗證。

## 權責與生命週期

`GameLogic` 的 `PlayerExperiences` 是每個 `PlayerRef` 的 Fusion 同步狀態，由 State Authority 寫入，包含等級、目前經驗、待選獎勵數。它跟隨持續存在的主要 `GameLogic`，不放在死亡時會被 Despawn 的 Player 上。玩家加入時建立一筆；死亡及重生不移除；離開 Session 才移除。SafeHouse／Game 場景替換的 `GameLogic` 不建立第二份進度，仍由同一 Runner 的主要實例保存。

Host 玩家首次加入時，從 `GameSaveRuntimeContext.ActiveSave` 的 `host` 記錄還原。關卡成功提交前，`StageFlowController` 將 Host 的同步進度寫回這筆存檔；提交成功後才切場。失敗時既有 `ResetRunProgression()` 清除存檔中的循環進度，寫檔成功後才將所有連線玩家同步進度重設為 1 級／0 經驗／0 待選。寫檔失敗不切場，也不重設同步進度。

訪客的 `PlayerRef` 目前沒有跨 Session 穩定身分，因此只在本次連線及場景切換中保留；不寫進 Host 的 `host` 存檔記錄。離線／重新加入會從預設進度開始。將來要讓訪客跨 Session 保存，必須先設計可信的穩定玩家 ID 與權限邊界。

`TryGetPlayerExperience` 供 UI 讀同步快照。`TryAwardPlayerExperience` 僅允許 State Authority 對場上存活玩家發放，並使用 Phase 6-A 的純規則；待選獎勵時拒收。此切片尚無敵人擊殺呼叫方，亦無獎勵領取介面或 HUD。不能把 API 已存在視為擊殺經驗已實際發放。

## 驗證與下一步

- 新增 3 項 `PlayerExperienceSaveBridgeTests`：Host 還原、保留其他存檔欄位／訪客記錄、失敗重設後的預設值，3／3 通過。
- Unity 編譯及 Console：0 Error／0 Warning。完整 `PurgersRegression` 執行 156 項，仍有兩項既有 Boss 資產接線失敗；見 [130](130_回歸驗證與審查狀態.md)。
- 尚需獨立 Host／Client Play Mode 驗證：兩位玩家的進度各自同步、死亡／重生不變、SafeHouse／Game 切換不變、成功寫回 Host、失敗全員重設、訪客離線重入歸零，以及晚加入看到同步狀態。這些是 Runtime 驗收缺口。
- Phase 6-C 再接敵人死亡事件與經驗來源；Phase 5 繼續暫緩。

閱讀順序：[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md) → [155 Phase 6-A](155_Phase6-A_玩家經驗與存檔規則.md) → 本文件 → `GameLogic.cs` → `StageFlowController.cs` → `PlayerExperienceSaveBridge.cs`。

## 2026-10-02 鈎索使用能量局部更新

`PlayerGrappleCharges` 沿用舊元件改為玩家等級制使用能量，1 級 50、每級 +25；合法發射扣 1，尚未釋放且拉動自己時每秒扣 1。無自然／擊殺回充；耗盡不取消本次移動。Host 讀主要 `GameLogic` 的玩家等級套用容量，Input Authority 沿用預測消耗，HUD 只讀同步比例。升級補滿／補差額可在玩家 Prefab Inspector 切換；死亡重生及正式安全屋／關卡轉場補滿。左下動能增傷獨立。保留舊元件 GUID 與職業舊欄位，未重建 Scene／Prefab；設定、遷移邊界與驗收順序見 [30](30_鈎索系統.md)，證據見 [130](130_回歸驗證與審查狀態.md)。
