# 敵人百分比分格血條驗證

日期：2026-10-14。範圍：一般／大型一般敵人的本機百分比分格呈現，Boss 不實作。

## 編譯及 EditMode

- 先以反射式契約測試取得 18 項預期失敗（缺少 EnemyHealthBarRules），job `5b6b9d8349b74b00926307c024660b02`，見 red.json。最初一筆 total=0 已排除，不算通過。
- 最終聚焦 **31/31 通過**，job `4d065902323c46c7bc714d0edd169500`，見 focused.json。包含百分比／部分格、不同 HP 上限、非法比例、視口位移、大型出框／切換緩衝、完全離屏、牆壁／Trigger／部分露出、自身 Collider、不同 PhysicsScene、近裁切、未 Spawn 防護、Graphic 實際 mesh 及獨立資產。
- 過程修正測試自己的 NUnit 集合斷言編譯問題及 EditMode 使用 SceneManager.CreateScene 的錯誤，最終採 EditorSceneManager.NewPreviewScene；不是保留的失敗或產品例外。
- 完整 PurgersRegression：**847 通過、1 失敗、1 Explicit 略過，共 849 項結果（848 項實際執行）**。唯一失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`；略過為需 Photon 的 `AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`，不屬本次血條測試。
- MCP 全套 job 在 Domain Reload 後停於 completed=0，不能作通過依據。確認 Unity TestRunner 已停止後清除孤立工作，使用既有 `EnemySuicideTestResults.Arm` 跨 Reload 收集器重跑（job `2066f18eef32480a8fe2b24d4d366ea8`），實際完整逐項結果見 regression-tests.txt。未修改既有收集器或略過既有失敗。

## Play Mode 與多人

- 隔離空場景，使用正式 TestDamageReceiver、EnemyHealthBarTarget、HUD 與設定／UI Prefab，建立受控盒形目標；不使用正式 AI、自然生成或正式敵人 Prefab。
- Single Runner：**12 項 PASS**，見 single.txt。包含權威扣血後 76%、大型頭出框、牆壁隱藏／恢復、Hidden 排除、死亡與 Despawn 清理。
- 同 Editor 受控 Host／Client：**25 項 PASS**，見 host-client.txt。包含 Client 非 State Authority 且拒絕改血、晚加入讀現有 76%、兩端目標隔離、各自鏡頭／物理遮擋、重連、死亡及 Despawn。
- 敵人血條沒有 Input Authority 指令入口；各玩家僅以自己的相機呈現。測試有真實 Photon Host／Client 及 LocalPlayer，但沒有自然鍵鼠／正式玩家操作。
- 已檢視實際 Unity 960×540 截圖：normal-76.png 為 76% 分格、large-head-outside.png 為近距離大型備用位置、wall-hidden.png 為完全遮擋而無血條。
- 測試結束已停止 Play Mode，回到 `_Menu`，Scene clean；測試前後 `_Menu`／Game／SafeHouse 磁碟雜湊一致。未保存正式 Scene 或配置敵人 Prefab。

## 交付與尚未驗收

已建立 EnemyHealthBarV1.asset 與 EnemyHealthBarDisplay.prefab；正式 HUD／一般敵人手動接線尚未進行，見 [102 設定手冊](../../102_敵人百分比分格血條.md)。

獨立程序 Host／Client、正式模型動作／身體盒、場景切換、超寬與分割視口、密集敵人重疊、自然操作、Build、效能量測及最終美術仍待驗收。有限九點身體盒採樣不代表逐像素遮擋；極窄縫可能保守隱藏，錯誤身體盒／缺少牆壁 Collider 可能誤估可見。

全回歸不是全綠；本次未修正或覆蓋既有 Boss 場景接線問題。

最終重新編譯完成（compiling=false），Console 無 error，未見本次腳本警告；僅既有 VFX DemoController CS0108、TankPreparationRuntimeVerification CS0252 與 MCP WebSocket 重連警告，見 console-final.json。最後一次重新編譯僅更正顯示程式註解的繁體字，未改行為；上述測試涵蓋最終行為。


## 後續接線狀態
使用者已授權並完成正式 Scene 與 9 個一般敵人接線；上述未接線描述屬初次交付紀錄。最新 42 項聚焦、完整回歸及 27 項正式 Prefab Play Mode 證據見 [自動接線驗證](Wiring/README.md)。


## 生命變化顯示期限
後續已加入初始滿血隱藏與 10 秒無變化隱藏；最新 57 項聚焦及 19 項受控多人證據見 [期限驗證](Activity/README.md)。
