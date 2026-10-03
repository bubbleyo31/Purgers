# Phase 6-A：玩家經驗與存檔規則

> 數值調整入口、Inspector／C# 分工及驗證步驟見 [160 經驗與獎勵數值調整手冊](160_Phase6_經驗與獎勵數值調整手冊.md)。本文件以下「尚未實作」描述的是 A 切片交付時的狀態，後續進度以 156～160 與實際程式為準。

> 最後核對日期：2026-09-24（局部核對）  
> 核對來源：`Assets/Scripts/Progression/PlayerExperienceRules.cs`、`Assets/Scripts/Progression/Save/GameSaveData.cs`、`Assets/Scripts/Progression/Save/JsonGameSaveRepository.cs`、相關 EditMode 測試。  
> 狀態：純規則與存檔切片完成；實際擊殺發放、網路同步、獎勵三選一和 HUD 尚未實作。

## 已實作規則

- 每隻敵人提供一筆基礎經驗。當下連線且存活的玩家各取得完整基礎經驗；擊殺者額外預設 1 點。無有效玩家擊殺者時不發放。
- 新玩家等級 1；升級門檻預設依序 10、20、40。基礎需求、成長倍率與擊殺加成都由純規則參數控制。
- 同一筆經驗超過門檻時保留超額，可一次跨過多級並增加同數量的待選獎勵。
- 玩家只要還有待選獎勵，之後的經驗不再加入；領完最後一份才恢復。
- 對於極大整數輸入，需求與經驗計算使用飽和保護，避免整數溢出或倍率為 1 時逐級跑大量迴圈。

## 資料與權限

`PlayerExperienceRules` 只有純計算，不讀 Unity 場景、Fusion 或存檔，也不授權 Client 修改結果。未來 Phase 6-B／C 的 State Authority 應提供玩家存活快照、敵人經驗及擊殺者，再使用此規則更新權威玩家進度。HUD 只讀同步結果。

`PlayerRunProgressionData.PendingRewardCount` 隨本輪進度保存。Host 存檔版本升為 2；舊版 v1 讀入時補零並轉為 v2，之後寫回使用 v2。失敗呼叫既有 `ResetRunProgression()` 時待選數歸零。Client 仍無本機寫檔權。

## 驗證

- 先建立測試及暫時未完成實作，Unity EditMode 實際跑 4 個測試、出現 3 個預期失敗。
- 完成後經驗與存檔聚焦測試 10／10 通過，job `47b8430959db4c40bb9501fc541b9e31`。
- 完整 Category 執行 153 項，151 項通過，兩項既有 Boss 資產接線測試失敗；詳見 [130 回歸驗證](130_回歸驗證與審查狀態.md)。Unity Console 讀取 0 Error／0 Warning。
- 本切片沒有 Scene 或 Prefab 變更，也沒有 Host／Client Play Mode 證據。不得將純規則測試當成擊殺發放或 UI 已驗證。

## 下一切片與閱讀順序

Phase 6-B 應先讓玩家等級、經驗與待選數由 State Authority 持續保存並同步，跨死亡重生及 SafeHouse／Game 場景切換仍正確。其後 Phase 6-C 再從 Enemy 死亡事件發放經驗。Phase 5 暫緩，Enemy 經驗先提供獨立數值入口，日後再接 Enemy Level。

閱讀順序：[140 長期藍圖](140_遊戲流程存檔關卡成長與長期藍圖.md) → 本文件 → `PlayerExperienceRules.cs` → `GameSaveData.cs`／`JsonGameSaveRepository.cs` → `PlayerExperienceRulesTests.cs`／`GameSaveRepositoryTests.cs` → [154 HUD 設計](154_戰鬥HUD視覺配置第一版.md)。
