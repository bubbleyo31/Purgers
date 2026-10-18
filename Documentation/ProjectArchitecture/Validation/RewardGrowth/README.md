# 成長獎勵、被動與技能掉落驗證

最後核對日期：2026-10-16（本功能）
核對來源：Unity 編譯狀態、TestRunner RunFinished、隔離 `RewardGrowthRuntimeVerification`、本次修改差異。
相關規則：[168](../../168_角色成長被動與技能掉落.md)。

## 編譯與 EditMode

- Unity 2022.3.62f1；Domain Reload 後確認 `EditorUtility.scriptCompilationFailed == false`。
- 初次焦點測試 **57 / 57 通過**，見 [focused-editmode.txt](focused-editmode.txt)。
- 最後 `PurgersRegression` RunFinished：**939 通過、1 失敗、1 Explicit 略過，合計 941**，見 [regression-editmode.txt](regression-editmode.txt)。
- 唯一失敗：既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring` 的 MapChunk_Boss 參照相等斷言。本次未修改 Boss 接線，不能稱全回歸綠燈。
- Explicit 略過：`AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`，需要獨立啟動其 Photon 驗證。本次專用多人驗證不等於該重連測試。
- 最後回歸包含九屬性／被動階級、獨立抽選及跨組補位、待選 XP、開局獎勵、存檔 v4 寫入讀回、屬性數值文字不超出既有卡片。
- MCP job 在測試跨 Domain Reload 後可能停留 completed=0／running；結論只採新生成的 RunFinished 檔案及 Editor 日誌，不採停滯狀態。

## 紅燈與修正證據

- 最初待選 XP 舊實作測試得到 Level 2（預期 5）；新配置抽選得到錯誤成長數。修改後納入通過的焦點測試。
- 同 tick 造成傷害後解鎖自動回血，舊零秒計時讓回血提前；[regeneration-red.txt](regeneration-red.txt) 保存失敗。修正未解鎖時不建立有效回血計時，首次解鎖完整等待。
- 空中掉落初版停在半空；[loot-fall-red.txt](loot-fall-red.txt) 保存隔離失敗。改由原 Ability Runtime 的 State Authority 做掉落位置／重力／地形判定，Client 只呈現。

## 受控 Play Mode／Host Client

最後一輪 **60 項檢查通過**，見 [host-client.txt](host-client.txt)。覆蓋：開局空特殊技能與一次抽選、待選 XP／候選穩定、Client 拒絕權威 XP、HP 與彈匣增量同步、換彈快照、致命傷攔截與無敵、全員倒地等待、自救中斷／十秒成功／隊友救起保留、倒地消耗與彈匣截斷、空中技能落地、實際 E 拾取及按鍵消耗、技能等級與冷卻延續、另一槽 Runtime 不重建、吸血依實際扣血、首次回血等待與回血升階、額外專注不換槽且保留副作用、晚加入成長同步、墜落繞過保命、失去最後自救機會後正常全滅、換關清除地上技能。

「真難抉擇」案例為縮短測試等待，暫時把該隔離玩家的 interval 設成 0.4 秒、測試 Catalog 只保留經驗技能；正式預設仍為 10 秒及完整專注池。自救使用正式 10 秒、無敵正式 3 秒、自動回血正式 7 秒，沒有縮短這些驗收時間。

驗證使用同一 Editor 內多 Runner、Photon 隱藏測試房及暫時地板／GameLogic，沿正式玩家與技能 Prefab。未寫正式玩家存檔，未儲存正式 Scene／Prefab。大部分獎勵效果由測試在權威 tick 呼叫正式領獎內部入口隔離驗證，並非全程自然三選一滑鼠操作；真正 E 拾取另外走正式輸入過濾入口。

## 邊界與手動驗收

- 本次未執行獨立程序 Host／Client、Build、效能量測與自然操作的完整一輪通關。
- 部分冷卻／近戰速度以純規則及現有回歸覆蓋，尚需自然操作確認動畫手感。
- 額外專注受控案例檢查經驗技能效果與承傷副作用；未逐項自然操作全部隨機技能組合。
- 新獎勵使用文字與既有佔位圖，未製作 15 張新美術圖示。卡片幾何檢查通過不等於美術最終驗收。
- Console 仍存在既有 DemoController.camera、TankPreparationRuntimeVerification 比較、TextureImporter.spritesheet 警告；測試用敵人沒有巡邏區會有提示。沒有歸屬本次的新編譯警告。
- 本次修改範圍 diff whitespace check 通過。工作目錄原先大量未提交內容保留；未提交、未重置或改寫無關變更。
- 自然驗收順序與 Inspector 欄位見 [168](../../168_角色成長被動與技能掉落.md)。

## 待確認的索引條目

建議搜尋詞：成長獎勵、屬性升階、永久屬性、一次性被動、不是今天、我自己來、腎上腺素、食人樹、真難抉擇、約翰維克、技能掉落、拾取冷卻。

摘要：待選 XP 持續累積；九屬性按階級替換最終值、六被動按觸發／倒地消耗；抽選格數隨等級變化、每成長格獨立 5%；技能單槽替換掉落並保留等級／冷卻。規則與設定路由為 168；實作與驗證狀態以本紀錄為準。依 AGENTS.md，新索引條目等待使用者確認，未自行加入。
