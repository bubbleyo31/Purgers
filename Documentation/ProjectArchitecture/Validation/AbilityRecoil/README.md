# E 技能後座力驗證

本檔保留 V1 歷史結果；最新移動後座力請讀 [V4](../AbilityMotion/README.md)，施放事件沿用 [V3](V3/README.md)（[V2 歷史](V2/README.md)）。

日期：2026-10-08。範圍為第一輪後座力設定、施放接點與 PlayerMovement 速度增量。
設定與使用者人工驗收見 [45 設定手冊](../../45_E技能後座力配置.md)。

- 先新增 AbilityRecoilTests，以反射查找尚未實作的設定類別。第一個 refresh 未匯入外部新檔，測試 total=0，明確不算通過；force refresh 後 9 項均因設定類別不存在而按預期失敗，見 expected-failure.json。
- 實作後聚焦 EditMode 9/9 通過，Job ID 29625015cbe144e783b91175c3613ad5。涵蓋預設停用、獨立時機、完整 Pitch/Yaw 視線、方向正規化／零值回退、無前搖時機及負速度。
- PurgersRegression Job ID ab83b474bcae4d2a9b589c0a039c451a：探索 554、完成 539；狀態 failed、result=null，回報 1 項 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗。不能以探索數當實跑數，也不將其他未回報項目推算為通過。見 regression.json。相同 Boss 引用失敗已記於先前 ActiveAbilityStabilization；本輪不修改相關 Scene／Prefab／測試。
- Unity Editor 唯讀檢查十二顆 E Runtime：Camera Enabled=False、Movement Enabled=False，兩個 Timing 皆為 0（Unconfigured）。沒有序列化保存或自動挑選時機。
- 真實 Fusion Single Runner：AbilityRecoilRuntimeVerification 在 FixedUpdateNetwork 內執行 20 項檢查並通過，見 single-runner.txt。包含權威固定資料、兩個時機分離、前搖內轉向、出手前取樣、零延遲、長按／阻擋／取消不重複、閃現兩次 E 與 Support 成功入口／保存速度。
- Support 探測直接進入成功啟動方法，因此不構成所有天然觸發條件已驗證；Tank 兩個成功分支本輪為靜態檢查，未做自然操作驗收。
- 最後 compileFailed=False，Console error/warning=0；已退出 Play Mode，_Menu 場景 dirty=False。
- 未執行獨立 Host／Client、晚加入／重連、封包延遲、Build 或人工視覺手感驗收。Single Runner 不等同多人證據。

## 重跑

1. Unity MCP run_tests：mode=EditMode、test_names=[AbilityRecoilTests]。
2. Unity MCP run_tests：mode=EditMode、category_names=[PurgersRegression]，需確認 completed 非零並讀取失敗細節。
3. 在無運行中 Runner 的 Play Mode，透過 execute_code 執行 AbilityRecoilRuntimeVerification.Begin()。
4. 讀取 AbilityRecoilRuntimeVerification.Status 與 Temp/AbilityRecoilRuntimeVerification.txt；測試只改暫時實例，結束自行 Shutdown Runner，最後退出 Play Mode。
