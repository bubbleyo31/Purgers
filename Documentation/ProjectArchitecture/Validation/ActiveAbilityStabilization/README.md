# E 技能整合穩定化驗證

最後核對日期：2026-10-05（局部核對）
核對來源：本目錄 MCP 原始結果、Assets/Scripts/Player/Ability、Editor/Tests、Tool/Testing。
相關文件：[Loadout](../../41_玩家能力Loadout.md)、[技能規格](../../42_E主動技能規格與強化預留.md)、[視覺配置](../../43_E技能視覺與UI配置.md)、[回歸狀態](../../130_回歸驗證與審查狀態.md)。

## 目前結論

已修復三項能以失敗案例證明的問題，並更新五項過期測試。聚焦 EditMode 77 項完成、無失敗；完整 PurgersRegression 437 項完成，仍有一項 Boss 資產接線失敗。受控 Single Runner 技能與生命週期檢查通過。**尚未達完整回歸零失敗，獨立 Host／Client 也未通過。**

本輪起始 HEAD 為 `09e5be2`，起始 git status 乾淨。後段出現其他工作對 PlayerGrapple、PlayerGrappleVisual、PlayerLocalView、GrappleLandingRules 與相關測試的修改。使用者確認另一個工作正在操作同一個 Editor，要求先完成不需 Editor 的工作，因此停止本輪 Editor 操作。下列測試是同期鈎索變更納入前的 E 技能驗證，不能替代目前全部工作區的最終驗收。

## 已修復的行為

| 問題 | 最小修復 | 失敗與通過證據 |
|---|---|---|
| Runtime 先 Spawn、Owner 引用稍晚可解析；不經輸入／HUD 查詢的 Proxy 可能一直未綁定 | PlayerAbilityRuntime 在有效 Spawn 生命週期內，由 Render 重試未綁定模組；OwnerPlayer 讀取增加生命週期保護 | LifecycleRed 第一項 FAIL → LifecycleGreen 第一項 PASS |
| 第一次收到的投射物命中狀態已到期，仍新播完整壽命爆炸 VFX | 隱藏命中模型照常執行；ImpactTimer 到期時不新播自訂特效、不新建線環 | LifecycleRed 第二項 FAIL → LifecycleGreen 第二項 PASS |
| 蹲姿低橫梁下，固定 1.5 m 的技能發射點可能落到障礙另一側 | 沿目前 KCC 膠囊中心到設定發射點掃掠，截到障礙近側；起點本身嵌入障礙則取消發射，沿用既有 FinishAbility 冷卻流程 | ShotOriginRed 七項 FAIL → FocusedEditMode 含七項 PASS |

起點解析只查目前 Runner 的 PhysicsScene，忽略 Owner Collider，沿用技能的碰撞遮罩及投射物半徑。清空間保留原設定高度。套用於 PlayerProjectileAbility 與 PlayerPiercingCannonAbility。沒有新增序列化欄位、沒有調整正式傷害／冷卻／高度或資產遮罩。期限內已播放的獨立 VFX 仍由既有 lifetime 清理。

Host／State Authority 仍決定技能啟動、生成、傷害與換裝；Render 的補綁定及特效期限判斷不賦予 Client 結果權限。

## 六項基線失敗的處理

完整原始結果見 [BaselineRegression.json](BaselineRegression.json)。

| 測試 | 基線原因 | 處置 |
|---|---|---|
| BossStageAssetTests.GameSceneContainsBossAssetWiring | 預期持久化 MapChunk_Boss.prefab，Game Scene 實際引用同名場景實例；Unity Object 不相等，實例 AssetPath 為空 | 保留原測試及資產，待確認配置意圖 |
| DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel | 舊期望 33，現在 3 級門檻 20、已有 7，應補 13 | 測試改 13 |
| PlayerExperienceRulesTests.RequirementDoublesFromTen | 原始碼現行 DefaultBaseRequirement=5，舊測試仍期望 10 | 改驗 5／10／20，並更名 |
| PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward | 測試初始經驗 8 已超過現行 1 級門檻 | 初始改 3、給 5，維持跨級後剩 3 的行為測試 |
| PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards | 舊給予量 50 現會升到 4 級 | 改給 25，驗 3 級、剩 10、兩次待選 |
| FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies | 材質已引用 StylizedMuzzleFlash_4x3_2，測試仍綁舊圖 | 從實際材質讀圖集；驗持久化資產、近似 4:3、匯入旗標與 shader，保留原素材 |

後五項是測試基準漂移修正；本輪沒有把經驗基數從 10 改成 5，也沒有替換槍口素材。Boss 引用差異是否是配置錯誤，仍需明確意圖，不能單純放寬 Assert 讓回歸變綠。

## 驗證證據

| 層級 | 結果 | 原始檔／Job |
|---|---|---|
| 修改前完整 EditMode | completed=430、discovery total=445、六項失敗 | [BaselineRegression.json](BaselineRegression.json)；84da4e04eeb346bda7bfe08d52bf474d |
| 發射起點 red | 七項失敗，缺少可保證安全起點的解析契約 | [ShotOriginRed.json](ShotOriginRed.json)；9ac37ed5ed0b464d81451a1915a1a7d4 |
| 生命週期 red | 兩項失敗、七項通過 | [LifecycleRed.txt](LifecycleRed.txt) |
| 聚焦 EditMode | completed=77，status=succeeded，無失敗 | [FocusedEditMode.json](FocusedEditMode.json)；25b50e45f8b541369c259ed27fba9038 |
| 生命週期 green | 九項 PASS | [LifecycleGreen.txt](LifecycleGreen.txt) |
| 十技能受控 Single Runner | 56 項斷言 PASS，詳細逐項見檔案 | [GameplaySingleRunner.txt](GameplaySingleRunner.txt) |
| 修復後完整 EditMode | completed=437、discovery total=452，只有 Boss 一項失敗 | [Regression.json](Regression.json)；3ce2bb91775b49328cb9e30b35b79f28 |
| Windows Development Build 第一次 | Succeeded，0 errors／3 warnings | MCP BuildReport：build-a40f4e16f5，UTC 20:23:26–20:24:19 |
| 獨立雙程序第一次 | Host StartGame Error；Client 等待 Host.ready 逾時，未執行多人斷言 | [NetworkHostAttempt.txt](NetworkHostAttempt.txt)、[NetworkClientAttempt.txt](NetworkClientAttempt.txt)、[NetworkStartupDiagnostic.txt](NetworkStartupDiagnostic.txt) |
| Windows Development Build 第二次 | Failed，20 errors／9 warnings；Editor 型別與磁碟鈎索欄位不一致 | MCP BuildReport：build-422b0eced9；待 Editor 使用權釋出後重編 |

MCP 此版本回報 result=null；上表刻意區分 progress.completed 與 discovery total，不把 452 當作實際跑完的項數。聚焦涵蓋 ActiveAbilityShotOriginTests、ActiveAbilityRulesTests、ActiveAbilityPhysicsTests、ActiveControlRulesTests、BulletTimeDamageTests、PlayerAbilityExperienceRulesTests、PlayerExperienceRulesTests、DevelopmentLevelUpRulesTests、FirstPersonMuzzleFlashTests、ActiveAbilityPresentationTests。

首次新增測試曾遇到 NUnit／Unity Assert 名稱衝突，已限定 NUnit；編譯前的零項結果不算 red 或 pass。十技能探測第一次在 HealingPack 可用性失敗，追加 Owner／生命診斷後重跑通過；[首次紀錄](GameplayProbeInitial.txt) 保留，原因未確定，不能宣稱該偶發現象已修復。

生命週期九項包括延後 Owner、過期 VFX、子彈時間延後傷害、換裝結算一次／清除慢速／不重複結算、護盾啟動、死亡清護盾及 Despawn 無殘留。這是實際 Fusion 物件的受控 Single Runner 狀態注入，並非獨立 Client 封包或自然重生驗證。

第一次 Build 的三個警告為建置時仍有未編譯變更提示及探測工具兩個 CS4014。工具已將兩次 LoadScene 改為 await；最後建置因同期鈎索序列化差異失敗，故目前不能宣稱最終 Build 零警告。第二次例示錯誤是 PlayerGrapple.grappleStartupGroundedTimeout 及 PlayerGrappleVisual.thirdPersonRopeOrigin 等欄位版本不一致；沒有修改或回退該批程式。

首次雙程序啟動的 Player log 含 PlayerPrefsException: Could not store preference value，Host 隨後 StartGame Error。沙箱的設定／網路限制是待排查方向，尚未證明是唯一原因。保留日誌，不以測試工具存在代表多人通過。舊探測亦曾記錄既有 AttackProfessionRuntimeDriver 缺少 AttackFocusAbility 的診斷；未為清空 Console 而消音或更動該配置。

## 已提供的重跑入口

先確認 Editor 無其他操作者、沒有需要保存的既有 dirty Scene。不要為 Test Runner 自動保存其他人的配置。

1. Unity MCP refresh／request compilation；等待 Editor ready，查 scriptCompilationFailed 及未被搜尋字串遮蔽的 Console。先確認磁碟與 Editor 的鈎索型別已同步。
2. 執行上述十個 fixture；再執行 category=PurgersRegression。要求非零 completed，保存完整 job 結果；Boss 待決之前預期仍會失敗。
3. Play Mode 明確呼叫 ActiveAbilityLifecycleVerification.Begin()、等待 Status；輸出為 Temp/ActiveAbilityLifecycleVerification.txt。現有 ActiveAbilityRuntimeVerification 也以 Begin() 啟動。兩者分開執行並等待 shutdown；不保存測試物件。
4. 用 Unity MCP 建立新的 Windows Development Build 到 Builds/ActiveAbilityStabilization/Purgers.exe，檢查 BuildReport 與 Console。
5. 在**每輪新的**輸出資料夾及唯一 session 名稱，分別啟動 Host 與 Client。命令參數：`--active-ability-test Host <session> <output-directory>`、`--active-ability-test Client <session> <output-directory>`。背景程序使用隱藏視窗，必要時提供各自 -logFile；依實際環境處理 PlayerPrefs／Photon 連線問題，不修改正式帳號資料。
6. 只有兩端 .complete 且日誌沒有 FAIL 才算受控雙程序驗證完成；不得重用舊 step.* 標記。存檔位於輸出目錄 Saves，Client 只讀。

雙程序探測已準備驗證：晚加入活躍技能、Client 直接啟動／換裝無權、NetInput E 由 Host 啟動一次、TimedHudState 快照、遠端子彈時間影響 Host、Experience 傷害倍率與換裝結算、死亡後新 NetworkId、Game／SafeHouse 切場後新玩家與場景管理器、斷線重連及 shutdown。該工具僅在 Editor／Development Build 編譯，必須有明確命令參數才啟動。這些目前是**待執行項目**。

## 保留的待決與驗收

- **Boss 配置**：Game Scene 的 MapChunk_Boss 應維持場景實例，還是改回 Prefab 資產？本輪沒有動 Scene／Prefab。
- **Q 換裝**：既有 Loadout 重建會連帶重設未替換 E 的 runtime／冷卻。是否應保留 E 狀態屬玩法契約，未擅自更改。
- **投擲物生命週期**：Owner Despawn 時已投出的治療包／手雷依既有程式消失；是否跨死亡保留待確認。
- **同一 NetworkObject 復活**：PreparedDamageSnapshot 以 NetworkId 辨識，不能區分同物件的生命世代。正式玩家重生換新 Object；Enemy Revive API 存在但本輪未找到自然呼叫路徑。保留為未來整合風險，未新建另一套 Health。
- **偶發探測失敗**：首次 HealingPack 可用性失敗重跑未重現，保留診斷並在下一輪再觀察。
- **自然操作／畫面**：尚需真人按鍵、技能多組合、蹲姿低頂射擊、死亡／換裝 HUD 與 VFX 畫面；Headless 狀態斷言不能代替視覺驗收。
- **最終環境**：同期鈎索修改之後的編譯、Console、完整回歸與獨立 Host／Client 待接手 Editor 後重跑。

兩個子代理只做技能交互與生命週期／權威審查，主代理統一修改本輪檔案。未新增外部套件、未自動配置場景資產、未提交 commit。架構文件更新限本輪行為與證據，不把未決設計寫成已批准規格。

## 交付檔案檢查

本輪變更範圍的 git diff --check 通過；十份更新文件共 224 個本機 Markdown 連結檢查無失效（不含網路 URL）。[SourceSnapshot.json](SourceSnapshot.json) 記錄交付時十二份程式／測試的 SHA-256，僅供比對後續漂移，不表示全部檔案已以同一版本完成最終建置。
