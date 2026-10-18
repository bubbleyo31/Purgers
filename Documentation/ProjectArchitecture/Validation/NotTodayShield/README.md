# 不是今天與快速護盾：2026-10-17 局部驗證

最後核對日期：2026-10-17（局部核對）
核對來源：PlayerHealth.Growth、PlayerActionGate.CrowdControl／Capture／ExplosionStatus、PlayerAbilityColorGrading、LocalHealthSegmentView。
相關規格：[168](../../168_角色成長被動與技能掉落.md)、[43](../../43_E技能視覺與UI配置.md)、[167](../../167_已確認HUD美術與整格血量.md)。

## 變更與責任

- 「不是今天」使用原本的權威 NotTodayTimer；攔截致命傷後回復 25% 最大生命、立即清除 StatusEffect、爆炸禁鈎／減速及捕食抓取。3 秒內拒絕這些來源的新控場，到期不恢復舊負面狀態。
- 保留武器動作、自身技能、救援、死亡、System／轉場等正常操作鎖；不建立第二份控制狀態或網路計時。
- 黃光接入子彈時間共用的本機 LGG 合成器；循環預設 0.75 秒，強度 0.9，與其他效果共同正規化。設定在 Resources/Presentation/ActiveAbilityColorGrading。沒有改 Scene／Prefab／原調色 Profile。
- 護盾歸零時清除 HUD 暫時峰值容量。原本生命上限沒有改變；同一次護盾部分受損時仍保留布局，下一次護盾重新計算。
- 獎勵資料與建立工具同步更新「不是今天」描述，未更改獎勵數值或機率。

## 證據

- 失敗先行：`bafacbca10d74000ae2c7ca678b881be`，六項均按預期失敗（缺少黃光規則／護盾容量殘留）。修正後 `4aaa40bda2c44ec781259fc4b2d431d2` 六項通過。
- 同 Editor 受控 Host／Client：[26 項通過](host-client.txt)。檢查權威清除、Client 不可清除、同步免控、到期接受新控場、舊狀態不恢復及實際 URP 渲染。
- 實際相機 389 幀觀察，Gamma 紅色差量範圍 0.000846982～0.198；黃光高峰／低谷及離開保護後無暫存 Volume 均核對。
- 正式 HUD 使用每格 10 HP：100 最大生命原有 10 格；25 護盾自然到期後額外容量為 0、回到 10 格、最大生命仍為 100。截圖已實際開啟檢視。
- [黃光高峰](yellow-peak.png)／[低谷](yellow-trough.png)、[護盾存在](shield-active.png)／[護盾到期](shield-expired.png)。截圖來自隔離測試平台，背景中的模型／相機配置不作正式關卡外觀驗收。
- 最終聚焦：[21／21 通過](focused.txt)，涵蓋 NotTodayShieldTests、AbilityColorGradingTests、HealthWholeCellTests；job `08f4f76be35e48deb645ba8133bf91ae`。
- 完整 PurgersRegression：[RunFinished](regression.txt) **975 通過、2 失敗、1 略過，合計 978**；job `d05153e4ad4f4c4d8cd3aef22a57a7a6`。MCP 跨 Domain Reload 留下的 completed=0 不作通過依據；收到新報告、確認測試結束後才清掉失聯 job。
- 既有失敗：`BossStageAssetTests.GameSceneContainsBossAssetWiring` 第 96 行，MapChunk_Boss 引用相等斷言。
- 本輪另外發現無關測試配置漂移：`ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt` 第 79 行仍 AddComponent，但正式槍械 Prefab 已有 `[DisallowMultipleComponent] FirstPersonReloadCameraMotion`，回傳 null，導致第 80 行反射 Field NullReference。Console 已確認「Can't add ... already added」；本次未修改該測試／元件／Prefab，不將這項歸咎於保命或護盾修改。
- Explicit 略過：`AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`。
- Unity 編譯成功；最終 Console 1 error 是上述換彈鏡頭測試重複新增元件，0 warning，未見本次新增的編譯錯誤。Editor 已退出 Play Mode，_Menu 無未保存 Scene 變更。回歸啟動時採樣的 124 份 Scene／Prefab SHA-256 與完成後一致。

## 測試條件與邊界

- 受控工具：`RewardGrowthRuntimeVerification.BeginNotToday()`，從無 Runner 的 `_Menu` Play Mode 啟動，使用隔離房間的同 Editor Host／Client，正式玩家 Prefab、能力資料、HUD 與自然相機渲染。
- 同 Editor 共用一套 HUD，因此僅在測試期間指定其 Runner 為 Client，結束恢復；暫時隱藏主選單以擷取 Game View，結束恢復。不保存場景。
- 抓取測試使用合法同 Runner 的權威 NetworkObject 作為來源，涵蓋 Gate 清除／拒絕及權限，未把它宣稱成自然捕食敵人整段動畫驗收。
- 第一輪 HUD 選到 Host Runner，沒有觀察到 Client 護盾；第二輪測試誤將正式場景每格 10 HP 當作程式預設 20 HP。修正測試選擇與從正式配置讀取期望格數，沒有修改場景設定或玩法遷就測試。
- 獨立程序多人、保護期間晚加入／重連、自然敵人實戰、完整跨關、Build／Profiler 及閃爍強度的主觀接受度仍需獨立驗收。

## 人工驗收

1. 新一輪取得「不是今天」，遭受捕食或爆炸控制後承受致命傷：生命為最大值 25%，立即恢復可操作，畫面黃色明暗循環。
2. 3 秒內再接觸抓取／爆炸：不受傷、不新增負面行動鎖；3 秒後新的攻擊與控場正常生效，舊控場不回來。換彈及自身施法等正常鎖仍遵守原規則。
3. 比較另一玩家與觀戰畫面，確認黃光只屬於對應本機玩家；搭配子彈時間時調色可合成，到期恢復場景基底。
4. 使用快速護盾，分別等待自然到期與受傷耗盡：淡金色護盾格及額外空格消失，生命上限／原血格保持。再次施放、倒地與角色重綁後沒有舊容量殘留。
