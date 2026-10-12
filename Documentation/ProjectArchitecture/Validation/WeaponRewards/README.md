# 武器獎勵與換裝驗證

日期：2026-10-10（Asia/Taipei）。來源：本次 Unity MCP 編譯／測試、正式場景 Single Runner，以及獨立 Development Build 的 Host／Client。
規格：[46 武器獎勵與換裝](../../46_武器獎勵與換裝.md)。

## 結果分層

| 層次 | 實際結果／證據 |
|---|---|
| 測試先行 | 最初四項 WeaponRewardTests 實際執行且失敗：三種武器缺少目標欄位、武器 ID 未經 RewardSaveBridge 保存。首次未強制刷新導致 total=0 已判為未執行，未計入結果。 |
| 聚焦 EditMode | 最終 **30/30、0 failed、0 skipped**；含 WeaponRewardTests、PlayerRewardSaveBridgeTests、RewardDraftRulesTests、PlayerRewardCatalogTests、ApprovedHudVisualTests。[原始結果](focused.json)。 |
| 完整 PurgersRegression | MCP 回報完成 **585**／探索 **600**，一項失敗：BossStageAssetTests.GameSceneContainsBossAssetWiring。result=null，因此不推測剩餘十五項是通過或略過，也不宣稱全套通過。[原始結果](regression.json)。 |
| 卡片排版 | 縮短 Support 文案後三卡描述測試通過；三武器在左／中／右標題共九組高度均 28.97 ≤ 30。不改字級、尺寸或 Prefab。 |
| Single Runner | 正式 SafeHouse／Game 場景、隔離存檔，**73 個 PASS**，最終 Status=通過。[完整斷言](single-runner.txt)。 |
| Windows Development Build | job build-f5df27c0af，StandaloneWindows64，成功、0 error／1 warning，45.16 秒。警告為既有 InputManager.developmentStageTimerPauseKey 在 Build 未使用；不屬本次改動。執行檔位於 Temp/WeaponRewards/Build/PurgersWeaponTest.exe，不納入正式資產。 |
| 獨立 Host／Client | 同一份 Build 的兩個獨立程序，以 Photon asia 房間執行；雙端完成標記 [Host.passed](Host.passed)、[Client.passed](Client.passed)。Host 13 條、Client 11 條 PASS 紀錄：[Host](host.txt)、[Client](client.txt)。 |
| 最終 Editor | 已退出 Play Mode，回到 _Menu，Scene 不 dirty；EditorUtility.scriptCompilationFailed=false。現存 InputManager／第三方 DemoController 警告與 MCP WebSocket 警告另列，不宣稱全 Console 零警告。 |

## Single Runner 涵蓋

- 新局 Attack；正式 GameLogic 產生武器候選、領取、扣一次待選。
- 過期與重複請求拒絕；暫時移除目標 Runtime Prefab 的故障注入保留舊 Runtime 及獎勵，復原後可成功領取。故障注入預期產生 missing-prefab LogError，不代表正式資產被移除。
- 開 F 後換裝回待機；Attack F 換回仍受剩餘階段限制，到期恢復。
- Tank F 先消耗格數、離手等待、換回按時間回充，不無條件補滿；純規則測試另覆蓋多格與精確到期邊界。
- 場景根 Tracer 在換裝後消失；EditMode 另驗證不會清掉另一把武器的 Tracer。
- 作用中護盾、Q／E revision 與 NetworkId 保持；精準鎖敵換近戰後仍裝備但停止作用／禁止使用。
- Support 滿彈匣；死亡重生、換 Game 場景保留 Support。
- 隔離寫檔，關閉 Runner 後以保存資料啟動新 Runner，首次裝備即為 Support；失敗回 Attack 並可寫回。

## 獨立多人涵蓋與限制

- Host 從正式獎勵領 Tank 後，Client 延後加入看見正確裝備。
- Client 無法直接修改權威武器；透過既有 GameLogic RPC 領 Support，重送同一版號不重複扣獎勵。
- Client 的 Q／E revision、兩個能力 NetworkId 不變。
- Client 死亡重生、Game／SafeHouse 往返，雙方看見正確武器。
- Client 斷線再加入遵守既有訪客新加入政策：回 Attack；Host 保留 Tank。未新增訪客跨連線／帳號持久化。
- Host 保存自己的 Tank，而非訪客的 Support；整輪失敗同步全員 Attack。
- 測試以 batchmode／nographics 執行，URP 出現無 GPU 的 shader 診斷是此模式限制；這些測試不證明最終武器畫面、音效聽感或自然實戰操作已由使用者驗收。
- 獨立程序仍記錄既有 AttackProfessionRuntimeDriver「找不到 AttackFocusAbility」錯誤；此舊元件缺省狀態與診斷在本次未修改。未因武器獎勵而補回已改為獨立 E 的舊技能。原始大型 Player.log 保留在 Temp/WeaponRewards/Network。
- 此輪沒有重新測量 Support 傷害／治療數值或所有武器攻擊判定；原武器程式及 Prefab 參數不由本次調整。

## 重跑入口

1. EditMode：上述五個聚焦測試類；完整 suite category=PurgersRegression，確認非零測試數。
2. 原 _Menu 場景進 Play Mode，明確呼叫 WeaponRewardRuntimeVerification.Begin()；輪詢 Status，報告為 Temp/WeaponRewards/Runtime/report.txt；完成後退出 Play Mode。全程隔離存檔，不儲存 Scene／Prefab。
3. 建立含 _Menu、SafeHouse、Game 的 Windows Development Build。用全新輸出目錄分別啟動：
   - --weapon-reward-test Host <unique-session> <absolute-output-directory>
   - --weapon-reward-test Client <same-session> <same-output-directory>
4. 兩端都須有 .passed；.failed 或 timeout 不能當成通過。重跑必須使用新目錄，避免舊握手檔造成誤判。日誌與存檔只在隔離輸出目錄。
5. 人工畫面／操作驗收依 [46](../../46_武器獎勵與換裝.md) 的驗收順序。

## 範圍

只保存三份新獎勵與正式 Catalog 追加引用；Scene／玩家／職業／UI Prefab 未由本次保存。工作區原有與同期修改保留。完整回歸中的 Boss 失敗及既有警告未納入本次修復。
