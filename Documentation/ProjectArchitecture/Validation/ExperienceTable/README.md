# Inspector 逐級經驗驗證

最後核對日期：2026-10-17（本次修改範圍）。
核對來源：PlayerExperienceRules、PlayerExperienceSettings、PlayerExperienceSettingsEditor、GameLogic、DevelopmentLevelUpRules、LocalPlayerRewardHUD、測試輸出。
相關文件：[160 正式規則與操作](../../160_Phase6_經驗與獎勵數值調整手冊.md)、[索引](../../關鍵詞規則索引.md#experience-table)。

## 已授權範圍與實作

使用者確認逐級 Inspector 設定；超過表格沿用最後一筆，並授權程式、文件與索引同步。
正式資產 Assets/Resources/Progression/PlayerExperienceSettings.asset 已經 Unity AssetDatabase 建立，30 列保留舊曲線至 int.MaxValue。沒有重設既有平衡或保存 Scene／Prefab。
Host GameLogic 保存獨立 Session 表；所有經驗寫入包含 RequiredExperience。F8 共用設定，HUD 讀同步門檻。存檔 schema 不變。

## 測試證據

- 紅燈：job 6abd4f85ac35464f8c7b1bdf0efa4901，6 個實際測試均在預期的新需求斷言失敗，見 [red-mcp.json](red-mcp.json)。最初兩次新腳本未匯入的 total=0 不計入結果；完整匯入後才執行上述有效紅燈。
- 聚焦：job cedf1d14ff8f47edaa6f7ed38fc8eee0，30 通過、0 失敗、0 略過。包含逐級表、固定表尾、下降需求、無效值、大額經驗、獎勵上限、獨立快照、正式資產、F8、舊經驗與能力／存檔相容。見 [focused.xml](focused.xml)／[摘要](focused.txt)。
- 完整 PurgersRegression：job 51ab2ff1eda545feb56919bb4a35e007；RunFinished 實際 total=1016，1013 通過、2 失敗、1 略過。見 [regression.xml](regression.xml)／[摘要](regression.txt)。Domain Reload 期間 MCP 斷線，採本次 TestRunner callback 寫出的完整報告，不以 MCP 工作狀態推斷成功。
- 兩個失敗：BossStageAssetTests.GameSceneContainsBossAssetWiring（引用相同外觀但非同一實例）；ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt（NullReferenceException）。
- 修改前當日 Temp/GrappleFeedback/fade-regression.xml（06:29:21 UTC）已有相同兩個失敗；本次沒有修復或隱藏它們。
- 編譯：新程式及 Fusion 同步欄位已成功匯入，編譯後 Console 無編譯錯誤。既有 CS0108 DemoController.camera、CS0252 TankPreparationRuntimeVerification、CS0618 BattleHudReferenceLayout.spritesheet 保留；完整測試會產生既有 Runtime／資產錯誤，不能宣稱整個 Console 清空。

## Play Mode 與多人

受控同 Editor Host／Client／late Client 檢查 **15/15 通過**，見 [runtime.txt](runtime.txt)。涵蓋初始化、Client 拒絕發放、83 點跨級餘額、待領累加、不同本地表仍讀 Host 門檻、HUD 分母、F8 補差額、晚加入、場景配置接管保留表與失敗重設。這裡的 HUD 是受控元件呈現呼叫，不代表正式畫面的完整視覺驗收。驗證入口 PlayerExperienceRuntimeVerification.Begin()，需要沒有執行中 Runner 的 Play Mode；會建立臨時 Host／Client／late Client，使用測試表，不覆寫正式設定、不寫正式存檔。
此工具即使通過仍只是同一 Editor 的多 Runner 證據；獨立 Host／Client 程序、真實重連、新舊存檔完整流程、自然擊殺與正式 HUD 最終視覺仍需手動驗收。場景接管僅測既有 AdoptSceneConfigurationFrom 入口，不等於完整自然轉場。

## 手動驗收

1. Tools → Purgers → Progression → 開啟逐級經驗設定；檢查各列的等級標籤。
2. 用測試用設定將表格調成 5、15、30，重新啟動 Session。此步會修改正式平衡，測試後恢復自己的數值。
3. 初始顯示 0 / 5，升級後依序顯示 /15、/30；第 4 級以後仍是 /30。
4. 檢查超額保留、待領時仍收經驗、F8 每次只補足下一級、已顯示候選不重抽。
5. 遊戲中修改資產不影響當前 Session；重啟後生效，Game／SafeHouse 切換不換表。
6. 獨立同版 Host／Client 檢查分母一致、晚加入／重連、觀戰／安全屋 HUD、失敗重設與 Host 舊檔讀取。降低門檻後，舊經驗下一次取得正經驗才結算。


場景檔檢查：_Menu、Game、SafeHouse 在測試前後 SHA256 一致；本次未保存它們。新設定由 Editor 建立，所有新腳本 meta 由 Unity 匯入產生。
收尾：已退出 Play Mode（playing=false、compiling=false），在 Unity 選取正式 PlayerExperienceSettings.asset。受控測試後 Console 無 error；僅有 MCP 清除回歸 stale job 的工具警告，完整結果以已保存的 RunFinished XML 為準。
