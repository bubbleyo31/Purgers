# 鉤索啟動落地修正驗證

日期：2026-10-05。範圍：PlayerGrapple 啟動落地取消、一次起跳接續、阻塞逾時及零速度方向邊界。

## 已取得證據

- 先將原落地判定抽成正式呼叫的純規則，在原行為下執行 3 項測試，其中 `LandingBetweenAttachmentAndFirstPullDoesNotRelease` 按預期失敗（Expected False / True）。Job：`e2cfafa1a4e8414ab6a1c19e76947aea`。
- Play Mode 探針發現玩家 Prefab 接近／遠離門檻皆為 0，原本靜止會判定 AutoReleasePassedPoint。追加兩個零速度案例，確認原判定 2/2 失敗後才修正；Job：`91bd0a483adc4ef39f85b09ae3c63319`，見 `stationary-red.json`。
- 最終聚焦 EditMode：GrappleLandingRulesTests 17/17，加既有 GrappleEnergyRulesTests 17/17，共 **34/34** 通過；Job：`03eb5ce2783044608db7b68151b97f64`，見 `focused.json`。
- 最終完整 PurgersRegression：工具回報 completed=465、失敗 1 項 `BossStageAssetTests.GameSceneContainsBossAssetWiring`（Boss MapChunk 物件身分引用不符）。同項也出現在此次方向邊界修改前；未改 Boss 資產。Job：`14ebacbe77bb4014a97184a92707b7af`，見 `regression.json`。失敗 Job 的 result=null，不能推算其 passed/skipped 摘要；progress.total=480 是探索到的測試集合，不是此 Category 實際執行數。
- Unity Editor 2022.3.62f1 編譯完成，`EditorUtility.scriptCompilationFailed=false`。Console 最終狀態另見 `editor-final.json`。

## Play Mode 受控驗證方法與邊界

最終受控檢查 **21/21** 通過，包含直拉、左甩及右甩。

透過 Unity MCP，在 Play Mode 建立獨立 GameMode.Single Runner，正式 Spawn KCC_Player Network Prefab；只在有效 Spawned 後存取 Networked 狀態。設定 KCC 手動更新，使用反射呼叫既有 BeginAttached／TickGrapplePhase 及正式 Simulate，控制 KCC Grounded、速度、Timer 以重現精確邊界。暫時物件只存在本次 Play Mode；退出後恢復 _Menu，無 Scene／Prefab 儲存。

以下為 2026-10-05 歷史結果，其中 Aim 取消的預期已由 [右鍵修正](../GrappleAimInput/README.md) 取代；原始探針與結果僅保留歷史證據。測試讀取實際 KCC DynamicVelocity、保護速度及網路狀態，涵蓋首次拉力前著地、地面附著、後續離地才武裝、正常落地收繩、一次起跳、阻塞逾時、取消設定、Q toggle／hold／Aim 與 Support Tether 分流。最後一輪沿用玩家 Prefab 原有 0／0 方向門檻；測試專用掛點及遮擋遮罩只修改暫時玩家。

`runtime-probe.cs.txt` 是 MCP execute_code 的方法主體，需先建立上述命名 Runner／玩家；`runtime.json` 保存輸出。此為 State Authority 的受控 Runtime 接線驗證，不是自然物理落地、Input Authority 預測、實際回滾、獨立 Host／Client 或晚加入／重連證據。未以新增測試類別為由宣稱上述多人情境通過。

## 待手動驗收

1. 用更新後的相同程式版本重建 Host／Client Session，分別操作兩端玩家。玩家 Prefab 不需新增元件或重新接線。
2. 下墜接近地面時發射至遠處掛點，讓首次拉力前先著地：繩索保持，接續一次既有起跳，再進入拉動。測直拉、左甩、右甩。
3. 地面直接發射與空中連續換鉤：保留地面起跳及空中下墜回正；不得重複灌入起跳衝量。
4. 已正常拉動後再次著地：立即收繩，不再追加拉力、該 Tick 拉動費或新的釋放動能。關閉 Release Normal Grapple On Landing 時仍依原設定維持鉤索。
5. 低天花板／起跳受阻：從首次地面起跳嘗試算起，超過 Grapple Startup Grounded Timeout（預設 0.5 秒）仍著地則收繩，不反覆起跳。此逾時不依落地取消開關停用。
6. 啟動期 Q toggle、hold 放開仍可取消；Aim 不得取消（2026-10-11 規則修正，見 [右鍵驗證](../GrappleAimInput/README.md)）；遮擋、目標失效及正常距離／幾何角度釋放仍生效。Support 拉敵／拉隊友與 Attack 標記不取得自身起跳或拉力。
7. Client 延遲與回滾：確認近地發射不閃斷、不重複起跳；Host 權威結果與 Client 呈現一致。晚加入觀看已附著玩家、重連／重新生成後再次發射，不能繼承上一輪啟動旗標。

本輪沒有進行 Windows Build 或獨立 Host／Client 自然操作。完整回歸的既有 Boss 引用失敗保留為未解問題。
