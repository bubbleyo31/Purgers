# 藤蔓實際命中後捕食：驗證紀錄

日期：2026-10-13。本次取代「指定必中」，沿用原捕食傷害／解救與 Host 權威。

## 自動測試與編譯

- 先修改隔離 Play Mode 檢查「discovery starts tracking without locking player」，在舊版實際失敗，證据 [expected-failure.txt](expected-failure.txt)。原 harness 的 Stage 字串仍是 player ready，實際 assertion 與 stack 指向發現不鎖定。
- 聚焦 EditMode：**33／33 通過**，job `2edb6d6f34a44dfabb04a88e931c6cae`，[focused.json](focused.json)。涵蓋前輪 21 項與新增 12 項：第一玩家、玩家位於障礙遮罩外、薄牆、高速掃掠、起點重疊、有限前進距離、不同 PhysicsScene、偏離路線及穩定狀態 ID。
- 新物理測試初次誤用 Play Mode 的 SceneManager.CreateScene，12 項在 SetUp 失敗，非玩法錯誤；改用專案既有 EditorSceneManager.NewPreviewScene 後全數通過，保留原測試內容。
- 最終完整 PurgersRegression：**726 passed、1 failed、1 skipped**，見 [regression.txt](regression.txt)。唯一失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`；略過既有 Explicit Photon Toggle 測試。
- 完整回歸 MCP job `782eab3f0452443c950f52c75784cadc` 在 Domain Reload 後留下進度34／running；本次以 EnemySuicideTestResults 跨重載持久收集結果。確認實際完成且 Editor idle 後才清除 orphan job，未將 MCP 尚在 running 當作成功。
- 最終 Editor 編譯成功；最後隔離單人測試及停止後 Console error／warning 0。編譯過程的既有 DemoController CS0108、TankPreparationRuntimeVerification CS0252、BattleHudReferenceLayout CS0618 與本次分開，不宣稱專案歷史警告歸零。

## 隔離 Play Mode

入口仍為 `EnemyFlytrapRuntimeVerification.Begin(false)`。使用正式玩家與敵人 Prefab、既有 KCC／PlayerHealth／Combat Decision，未儲存 Scene。

[單人原始結果](single.txt)：**36 項 PASS**（含 Runner 啟動）。

- 發現只開始追蹤，未取得 Capture Gate；追蹤前段會跟上位置變化。
- 方向固定後保持不變，命中前移動輸入有效，側移能讓直線藤蔓落空。
- 落空收招期間無法重新攻擊，結束後回待機，不套吞食後 10 秒冷卻。
- 方向固定後插入 0.02 m 薄牆，測試速度 1000 m/s 仍不穿牆抓到後方玩家。
- 原瞄準玩家前插入另一位正式玩家，實際抓到中途玩家；原玩家不受困。
- 固定方向後在拉入路徑插入高欄杆，藤蔓接觸也不能穿牆拉入。
- 蓄勢中死亡不會延後抓人；Despawn 解救被攔截玩家。
- 既有安全站立／蹲行、牆壁遮擋、嘴邊才扣血、第一間隔、單人半血、吞食冷卻、擊殺解救與剩餘傷害取消、其他鎖保留、不能整除的尾段结算皆保留並通過。

部分情境用腳底 Pose 建立可重現位置，閃避亦驗證真實移動輸入。射擊感應沿用 NotifyGunshot 注入，未宣稱自然滑鼠開槍全流程驗收。

## 受控 Host／Client

`Begin(true)`，同 Editor 獨立兩個 Runner 透過 Photon 隱藏房間連線。測試 Config 副本開 Multiple Peer，未改全域設定。[原始結果](host-client.txt)：**46 項 PASS**。

- 兩端都在 Tracking 保持未受困，Client 取得相同固定方向。
- Client Input Authority 用移動輸入避開，Host 認定落空，Client 接收到 Recovering。
- 命中後 Client 不能自行解除捕食；房間兩人總額100%扣血、死亡及解除鎖同步，死亡後共用10秒冷卻。
- 晚加入／重連取得現有受困與 Captor，擊殺解救同步。

為觀察網路提示，該情境追蹤／固定方向各延長為1秒；晚加入仍沿用300秒吞食窗口，多人致死用1秒／0.2秒加速測試。僅修改測試實例。多人結果完成於最後「大型方塊表面提示」視覺修正前，Gameplay 程式相同；視覺修正後重跑單人及完整回歸。

## 方塊視覺與資產

- 目前使用者方塊外觀比原始設定大，短提示原先埋在模型內。修正為蓄勢時把本機短藤蔓顯示於 Renderer bounds 表面，未移動權威發射起點、碰撞、模型尺寸或人工掛點。
- [固定方向提示](locked-direction.png)、[藤蔓刺出](vine-strike.png) 已實際檢視。截图情境速度降為6 m/s，方便觀察；資產預設仍30 m/s。圖為隔離測試鏡頭，非第一人稱遊玩驗收。
- 預告／刺出藤蔓使用獨立 Renderer 換色，不與既有 EnemyDamageFlash 的方塊本體材質槽互搶。正式動畫／聲音透過既有 Phase UnityEvent／Animator 接口；未新增動畫素材或音效檔。
- Prefab 只補 TendrilOrigin 引用並保存新欄位，保留已有人工尺寸、生命、距離及其他設定。執行時方塊藤蔓 Collider 立即停用後移除，沒有 NetworkObject／Hitbox，不決定命中。
- 已以開始前雜湊確認 `Game.unity`、`_Menu.unity`、`LocalPlayerEnemyCrosshair.cs` 內容未變；它們原本即有未提交修改。完整工作樹的 diff check 在既有 `_Menu` 第4294／4295行空白報錯，本次修改檔的檢查通過。

## 未驗收範圍

尚未驗證獨立程序／Build 多人、自然遭遇出生、實際關卡、各技能逐一互動、高延遲／丟包壓力及效能；不把同 Editor 多 Runner 通過視為獨立多人驗收。0.5秒追蹤、0.25秒固定、30m/s等為可調原型值，需要人工體驗確認。正式設定與動畫接口見 [93](../../93_定點捕食植物.md)。
