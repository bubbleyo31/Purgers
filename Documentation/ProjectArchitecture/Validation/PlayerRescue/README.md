# 倒地救援驗證紀錄

日期：2026-10-16。涵蓋倒地／救援、敵人重新鎖定、撤離人數、戰鬥死亡等待、安全屋原自動復活、地圖底部死亡與圓環 HUD。

| 層級 | 實際結果 |
|---|---|
| 聚焦紅燈 | 新生命狀態／救援 owner／直接死亡入口尚未存在時，3 項預期失敗；job `36542776db4b4dddbdf23898b387436c`。 |
| 聚焦綠燈 | 最終 10/10，0 失敗，job `c1b1d41c4f064f719f947f8214ac2f28`；含暫停與正常倒數、非負值、35% 最大 HP 與撤離排除。 |
| 完整 PurgersRegression | [Unity TestRunner 最終回呼](regression-tests.txt)：901 通過、1 既有失敗、1 Explicit 略過，共 903；不是全綠。唯一失敗為 `BossStageAssetTests.GameSceneContainsBossAssetWiring`。MCP job `26bb042dda764619ac31111226857ba7` 在 Reload 後停留 completed=34，以本輪 12:07:39 寫出的完整回呼檔為準；確認 TestRunnerApi.IsRunActive=false 後清除 orphan job。 |
| 編譯 | Editor 編譯完成，沒有本輪新增編譯錯誤。Console 留有原 DemoController.camera、TankPreparationRuntimeVerification 引用比較與 BattleHudReferenceLayout.spritesheet 三項警告。 |
| 同 Editor 受控 Host／Client | [36 項 PASS](host-client.txt)。真實 Fusion Host／Client＋一次晚加入，沿用正式玩家 Prefab 與敵人感知；不是獨立程序測試。 |
| UI | [實際 Unity 三態渲染](rescue-ring-states.png)，人工檢視空白／半滿／全滿、空心區域與 E 方形按鍵。純視覺使用既有 Image Radial360，不新增網路或時鐘。 |
| 資產 | 僅建立並遷移獨立 `PlayerRescueHUD.prefab`；未將它放入正式 Scene。測試前後 _Menu dirty=false，未保存正式 Scene。 |

## 受控多人實際涵蓋

- State Authority 控制倒地、計時、救起與死亡；Client 不得直接改結果。晚加入可讀倒地狀態與剩餘時間。
- 致命傷只倒地不發 Died、不移除；倒地不再受傷。實際 StageFlow 收集包含倒地、排除死亡。
- Client E 持續輸入五秒，救援期間倒地時計不減；受傷不中斷、放開／超距／牆壁中斷歸零，進度同步至 Client。
- 同一角色物件恢復 35% 最大 HP；救起後維持按 E 不漏給技能。
- 原目標敵人倒地時 ForgetTarget；未正常偵測時等待五秒，正常視野／無遮擋攻擊距離可提前重鎖。
- 低於地圖門檻時直接死亡，只發一次 Died；戰鬥不走舊自動重生；全隊倒地等待兩秒。
- 保留健康隊友時真實等待三十秒死亡；SceneLoadDone 原入口重建角色；實際 SafeHouseFlow 啟用時沿用原自動復活。

隔離場在遠處建立臨時地板、GameLogic 與 StageFlow，敵人原攻擊決策暫停以免干擾感知時序；五秒恢復使用正式參數，後續提前重鎖子案例將救援時間縮短以專注判定。場景返回只呼叫正式 SceneLoadDone 接點，未執行完整正式撤離／存檔／跨場景資產載入。

## 測試過程與限制

初次擴充案例在晚加入端離線尚未完成時就算撤離人數，修正為等 Host ActivePlayers 回到兩人。將 UI 建立／字型暖機插入連續救援的中途曾造成輸入時序中斷；後續將 UI 暖機分成獨立階段，最終 36 項通過。這不代表網路遺失輸入容錯已完成：目前任一 Tick 缺失有效 E 意圖就撤銷救援，未新增按住緩衝或延遲放鍵規則。

圓環改版後重跑聚焦與全套 EditMode，三態圖使用靜態預覽進度；先前 36 項網路玩法驗證未因純 UI 改版重跑。HUD 不改玩法或 Networked 值。

尚待：獨立程序 Host／Client、真實玩家自然爬行／操作、三人同時爭搶救援、救援者斷線／重連、正式地圖底部判定與上下層地形、完整撤離回安全屋、觀戰運動重影的最終視覺驗收、Build／效能。爬行目前沿用低姿 KCC／鏡頭，未新增爬行動畫。不能將同 Editor 受控成功視為這些項目皆通過。

正式設定與人工接線：[62 倒地與隊友救援](../../62_倒地與隊友救援.md)。


## 後續正式接線

2026-10-16 使用者要求後已接入並保存正式 _Menu 戰鬥 Canvas 的 ReferenceFrame，取代上述「尚未放入正式 Scene」的歷史資產狀態。正式實例 Play Mode 顯示、原始結果及截圖見 [接線驗證](Wiring/README.md)。
