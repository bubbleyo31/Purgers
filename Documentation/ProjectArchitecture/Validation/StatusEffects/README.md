# 異常狀態顯示驗證

日期：2026-10-18。規則與設定：[105](../../105_異常狀態圖示與持續量條.md)。

## 首版已完成（下方另列位置修訂）

- 純計算 TDD：初始 10 項中 7 項預期失敗；實作後 10／10 通過。獨立 C# Runtime／Editor 編譯零錯誤，後續 Unity 編譯與 Fusion weaving 完成。
- Unity 聚焦 **17／17 通過**：[focused.xml](focused.xml)。包含比例、延長／較短重疊、暫停規則、明確刷新、滿血顯示、不續生命窗、八列無文字與點擊、獨立比例、收合重用、未 Spawn 守衛、正式圖示引用與紫色子彈時間。最後一次在匯入解析度調整後重跑。
- 同 Editor 受控 Host／Client **40 項通過**：[host-client.txt](host-client.txt)。涵蓋 State／Input Authority 分離、滿血暈眩、較短不重填、延長重設分母、按觀察者獨立標記、晚加入、跨 Runner 排除、減速強度與最晚期限分離、清除負面效果、倍率 1 的玩家子彈時間、子彈時間暫停暈眩、取消、重連、死亡／Despawn。
- 正式 Sprite 與 UI 元件離屏渲染：[ui-preview.png](ui-preview.png)。圖示、量條和間距來自實際元件；進度為示例，**不是自然實戰截圖**。
- `_Menu` 血量群組上移 55.70 Canvas 單位；四列下方實測保留 8 單位。場景保存後乾淨、missing scripts = 0、8 個 Sprite 引用完整。
- 最終 Unity Console error = 0。既有 DemoController、TankPreparationRuntimeVerification、BattleHudReferenceLayout 編譯警告未修改。`git diff --check` 本次既有程式路徑沒有空白錯誤。

## 完整回歸

[full-regression.xml](full-regression.xml)：**1095 項，1090 通過、4 失敗、1 Explicit 跳過**。RunFinished 結束時間 2026-10-17 19:19:52Z，具非零測試數。本次 17 項皆通過；以下四項與接手前另一順位的結果相同，不宣稱全套全綠：

1. BossStageAssetTests.GameSceneContainsBossAssetWiring：Boss MapChunk 資產引用。
2. EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules：既有值 0.25，測試預期 0.5。
3. EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners：既有值 10，測試預期大於 10。
4. ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt：既有 NullReferenceException。

完整回歸會 Domain Reload，MCP 工作停在舊進度；以事先啟用、跨 Reload 保存的 RunFinished XML 為證據。已確認 Editor idle 並清理失效工作。首次多人測試使用 16 秒情境，期限在晚加入檢查前到期；調長驗證情境後完成 40 項，正式遊戲持續時間未改。

## 資產成本與剩餘驗收

原 PNG 保留完整核准美術；Unity 匯入上限：雙眼／禁止鈎索 512，六格圖集 1024（實際 1024×683），無 mipmap、RGBA32。三張合計約 4.67 MiB 原始像素，比未限制時約 18 MiB 小；這不是 GPU profiler 實測或 draw call 保證。

獨立程序 Host／Client、自然輸入與關卡實戰、Build、超寬／分割視口、大量怪物效能未驗證。捕食 Feeding／拉取前 Frozen／反射破綻的各來源介面和圖示已接入；本次受控多人未逐一走完整自然捕食／拉取／反射命中流程。多個子彈時間施放者的同時覆蓋與取消，採 Registry 最晚期限規則及比例聚焦測試，仍需自然多施放者驗收。

重跑工具：StatusEffectRuntimeVerification.Begin()（無其他 Runner 的 Play Mode），輸出 Temp/StatusEffects/host-client.txt；StatusEffectVisualVerification.Capture()（Edit Mode），輸出 Temp/StatusEffects/ui-preview.png。都不保存正式場景、Prefab 或存檔，不需要視窗焦點。

## 2026-10-18 位置試調修訂

- 使用者指定：第一人稱血條回原位置，自身異常移到準心下方；第三人稱怪物與隊友異常改到血條上方。圖示、時間來源與同種重疊規則不變。
- `_Menu` HealthBar.y 已由 -912.06366 回復為 Git 原位置 -967.7671；x 保持 142.13245。自己異常掛 PlayerHUDController，以全畫面中心置中，第一列頂端距準心 80 Canvas 單位。可於 StatusEffectHud.asset 的 Self Crosshair Offset 試調。
- 頭頂異常保持由上至下固定順位，最末列底端距血條上緣 5 單位；依實際可見列數收合。隊友名字移到異常列上方，並依整組高度處理上邊界；怪物安全範圍改預留上方空間。
- TDD 新增兩項排版測試，先確認舊版兩項皆失敗（自己錯誤左偏、頭頂列仍位於下方），修改後聚焦 **19／19 通過**。完整回歸中這 19 項也全數通過。
- [本輪完整回歸 XML](layout-full-regression.xml)：**1143 項，1137 通過、4 失敗、2 跳過**，RunFinished 2026-10-18 01:03:46Z；四項失敗仍是上節同名既有項目，未宣稱全綠。以跨 Domain Reload 的 XML 為準，完成後清除 MCP 舊工作。
- [更新排版示例](layout-preview.png)：使用正式 Sprite 與實際 StatusEffectListView 的離屏渲染，並非自然遊戲畫面；中間血條僅示意與狀態獨立，正式血條已回到左下原位。
- 最終場景保存且乾淨、missing scripts = 0、編譯完成、Console error = 0。Scene YAML 的既有 Unity 空白欄位不手改；本輪只透過 Editor 還原指定座標。
- 本輪只有本機呈現位置變更，未重跑原先 40 項受控 Host／Client。自然實戰、頂端大型怪物的備用定位、四種自身異常與救援／換彈提示同時出現、超寬／分割視口仍待視覺驗收。靠近畫面上緣時，安全邊界可能將血條下推，因此不是完全不遮模型的保證。
