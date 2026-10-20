# 電鋸格檔成長驗證

最後核對日期：2026-10-18（本功能局部）  
規則：[109](../../109_電鋸格檔耐力與屬性強化.md)。

## 結果與證據

- Unity 編譯／Fusion weaving 成功，沒有新增可歸因本次的編譯錯誤或警告；保留既有 DemoController.camera、TankPreparationRuntimeVerification 參考比較、LocalHealthSegmentView.cornerRadius、BattleHudReferenceLayout.spritesheet 警告。
- 純規則離線 NUnit 28／28：Unity 隨附 Mono 與專案 NUnit，僅 Fusion.INetworkStruct 使用介面替身，不冒充 Unity 執行。最初 .NET 8 執行器的 CallContext 不相容已排除。
- 正式聚焦 **54／54**：GuardGrowthRulesTests、GuardGrowthIntegrationTests、GameSaveRepositoryTests、RewardGrowthTests、PlayerRewardCatalogTests。覆蓋各階數值、耐力上限／恢復、舊 bit 保留、v4 缺欄位遷移與磁碟往返、獎勵池數量、卡片排版、Spawn 前安全、CanvasRenderer、格檔／異常收合及施法避讓。[RunFinished XML](focused.xml)、[摘要](focused-summary.txt)。
- 同 Editor 受控 Host／Client／晚加入 **43 項通過**：[紀錄](host-client.txt)。經正式 Client Aim 輸入，Host Guard Modifier 決定減傷／耐力，Client 的直接 Modifier 呼叫不改傷害；正式 LocalPlayerHealthSlider 綁定 Client 後顯示同步 60／100，換武器清除。
- 聯機覆蓋：40% 基礎減傷、守住不恢復、3 秒一般等待、25% 最大值／秒、5 秒破防、破防中零耐力升容量、不追溯縮短既有 timer、持續按住自動舉防且不回滿、滿階 90%／400／3.5 秒、每秒 100、晚加入同步、換武器保留。一般輸入封鎖禁止舉防但仍恢復。
- 最終完整 `PurgersRegression`：**1205 通過、4 失敗、2 跳過（總數 1211）**。來源為新鮮 RunFinished XML，不採用 MCP 在 Domain Reload 後殘留 running／completed=0 的工作狀態。[最終 XML](regression-final.xml)、[摘要](regression-final-summary.txt)。

完整回歸失敗項目：

- `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Expected: same as <MapChunk_Boss (MapChunk)>   But was:  <MapChunk_Boss (MapChunk)>
- `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：Expected: 0.5f   But was:  0.25f
- `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：Expected: greater than 10.0f   But was:  10.0f
- `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：System.NullReferenceException : Object reference not set to an instance of an object

以上四個既有問題，已在本次開始前「設計電鋸攻擊距離指示」的 1173 通過／4 失敗／2 跳過紀錄出現。本次未修改其玩法／資產或放寬斷言，不宣稱全回歸綠燈。

## 先失敗後修正的證據

- 舊純規則：enum 16 第一階預期 0.15，實際 0。
- [正式資產 RED](asset-red.xml)：預期基礎 0.4，原 Prefab 0.7；執行 GuardGrowthSetup 後通過。
- 首次聚焦 50／51：新圖形測試反射多載歧義，改成 DeclaredOnly。離屏圖另發現新 Graphic 缺 CanvasRenderer，補必要元件與檢查後重新驗圖及聚焦。
- [輸入封鎖 RED](input-block-red.txt)：正式 Client 已顯示正確 60／100，但原職業 early return 暫停恢復；將輸入封鎖與 Guard 的恢復更新分開後，43 項聯機全通過。
- 初次全回歸 1204 通過／5 失敗／2 跳過；新增失敗是舊 catalog 測試仍期待 33。更新為 36，並分別核對原 18 裝備、12 屬性及 6 被動，重跑完整回歸。[初次 XML](regression-before-catalog-test-update.xml)。

## 資產與視覺

三份 Reward_Guard*.asset 已透過 Editor 建立，catalog 僅追加三筆；Tank Prefab 的 guardDamageReduction 0.7→0.4、brokenCooldownDuration 10→5。保存 Prefab 同時將上一項電鋸距離指示已存在於程式的預設欄位序列化，未調整其數值。未保存 Scene，既有施法條座標／圖示／異常效果未變更。

正式元件離屏示例：

- [低耐力＋四列異常＋技能時間條](hud-guard.png)
- [破防空條與倒數](hud-broken.png)

示例使用正式圖示與 TimedAbilityBar Prefab、固定測試數值，並非自然實戰截圖。格檔＋四列會為可見技能名稱保留 8 單位，上移整組；三列或技能條隱藏時沿原位置。新獎勵專用圖示未製作，沿原卡片 fallback 與文字階級。

## 驗收界線與人工順序

使用 PlayUnfocused 與 Run In Background。透過 MCP、資料查詢及預覽 Scene 相機離屏渲染，沒有聚焦視窗或桌面輸入。受控驗證使用隔離 Runner 與臨時場景物件，不寫正式存檔；結束確認 Runner=0、返回乾淨 `_Menu`。

1. 自然遊玩用電鋸防禦，檢查金條／低耐力警示、放開等待、恢復與滿值收合。
2. 破防後持續按住：5 秒結束自動舉防，耐力只有第一個恢復 Tick 的少量值；放開才能繼續恢復。
3. 領三種強化並換武器／換關，核對階級保留、槍械不入池、滿階離池；舊正式存檔應先備份再驗讀取。
4. 用四種異常、技能時間條、觀戰、不同解析度核對文字與圖示空間。已驗本機來源／換裝；自然觀戰切換尚未獨立操作驗收。
5. Build、獨立多程序 Host／Client、真實斷線重連、自然戰鬥視覺及效能仍待驗，不能由 EditMode 或同 Editor 多 Runner 代替。

## 2026-10-18 格檔尺寸小幅放大

字級 12 → 14、條寬 251 → 279、條高 7 → 8 已套用；異常與施法條尺寸保留。聚焦 3／3，完整回歸 1222 通過、4 項既有失敗、2 跳過；文字量測、離屏圖及最新報表見 [HUD 尺寸驗證](HudSize/README.md)。專用圖示仍只有 V2 預覽，尚未套用。

## 2026-10-19 V2 專用圖示套用

使用者核准後，三項防禦屬性已配置 V2 圖稿與既有粗襯線階級。上方未配置／預覽狀態為當時紀錄；目前資產、13 階級渲染與測試見 [IconsV2](IconsV2/README.md)。
