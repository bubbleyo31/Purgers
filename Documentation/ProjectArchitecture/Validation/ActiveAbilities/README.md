# E 主動技能原型驗證紀錄

最後核對日期：2026-10-04（Unity 2022.3.62f1，本次技能切片）。
核對來源：Unity MCP Test Runner、Editor compilation state、ActiveAbilityRuntimeVerification 及本工作區差異。
相關文件：[技能規格與手動驗收](../../42_E主動技能規格與強化預留.md)。

## 結果與界線

| 層級 | 實際證據 | 結論 |
|---|---|---|
| C# 編譯 | 最後 scriptCompilationFailed=False，isCompiling=False | 編譯通過 |
| 聚焦 EditMode | job b29779eba15244eea176fb5140a30d16：61 total / 61 passed / 0 failed / 0 skipped | 通過，包含新增技能規則、PhysicsScene、控制、延後傷害、XP、資產及十四項池 |
| 完整 PurgersRegression | job 4129bd0edb2d45b59f39a30a2f45bb68：completed=371，6 項失敗 | 未全綠；六項與修改前相同 |
| 受控 Play Mode | Single Runner + 真實 KCC_Player／Enemy_Melee_A，明確程式提交 E；[日誌](SingleRunner.txt) | 十項裝備／施放、護盾、XP 代價、閃現牆停、延後結算、反彈自傷與敵人撞牆／免疫檢查通過 |
| 獨立 Host／Client | 本輪未執行兩個獨立程序 | 未驗證 Input Authority 自然輸入、網路延遲、晚加入／重連、多人濾鏡／分配及搶拾 |
| 正式美術／動畫／音效 | 原型階段；V4 圖示／LGG／模型接口追加證據見下節 | 不屬於正式視覺驗收 |
| Windows Build | 本輪未建立 | 不以 Editor 編譯替代建置證據 |

完整回歸失敗的 MCP 結果未提供 result.summary，因此依 progress.completed 報告 371 項已執行；progress.total=386 是工具探索數，不冒稱 386 全部執行或通過。原始結果保存在 FocusedEditMode.json 與 PurgersRegression.json。修改前 job c23e840fd6434085a3e571660781a133 的 completed=313、失敗同以下六項。

## 原有六項失敗

- BossStageAssetTests.GameSceneContainsBossAssetWiring：Scene 的 MapChunk_Boss 與測試載入資產不是同一引用。
- DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel：預期 33，實際 13。
- FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies：測試預期原圖集，材質目前引用 _2 圖集。
- PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward：預期 3，實際 8。
- PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards：預期 3，實際 4。
- PlayerExperienceRulesTests.RequirementDoublesFromTen：預期基數 10，實際 5。

未為了測試改回使用者的素材、Boss 引用或經驗數值。

## 先失敗再修正的關鍵紀錄

初始規則／控制／資產／傷害切片先建立聚焦失敗案例，再實作。曾以 fixture 名稱填入 test_names 得到 0 筆，已改 group_names；0 筆不列為通過。最初隔離 PhysicsScene 的設定錯誤亦先修好，再取得預期的功能失敗。

- 閃現×子彈時間：減速前行程曾誤判已碰牆，新增四案例（遠牆不提前停、近牆精確停止、剩餘距離一次走完、正常速度）先失敗，後由 ResolveDashStep 統一真正行程與速度，最終聚焦全過。
- 地面閃現：實際 KCC 腳底貼地零距掃掠曾導致無法移動。縮小並抬高掃掠膠囊，保留正式 KCC 碰撞；實際移動 3.5 m 並停牆前。
- 同 Tick 近牆彈射：投射物整段 Sweep 結束才解除出生排除，曾漏自傷。[修正前失敗](RicochetRed.txt)；修正每段接觸後更新離膛狀態，再以真實投射物一次掃掠驗證 base 10 反彈後 self damage 20。
- 獎勵池：先以十四項與三候選測到舊四項／兩候選失敗，再追加資產。完整回歸曾讀到 Unity 記憶體中的舊四項快取；重新載入資產後，聚焦、完整回歸及最後查詢均確認十四項。未變更原四項設定。
- Enemy_Melee_A 沒有 CapsuleCollider；擊退承接原 Navigator 身形後，驗證一次牆傷、1 秒暈眩及元件免疫。

## 如何重跑受控探針

先等 Unity 編譯、進入 Play Mode，再明確呼叫 ActiveAbilityRuntimeVerification.Begin()。查 Status，完成後離開 Play Mode。它建立並清除暫時 Single Runner 與驗證物件，不把配置存進 Scene／Prefab；產物為 Temp/ActiveAbilityRuntimeVerification.txt。

探針直接提交技能輸入及部分指定傷害，用於權威與生命週期檢查；沒有宣稱每個技能所有命中組合、自然按鍵、獎勵到實戰全流程或獨立多人皆已通過。實際玩家驗收清單在 42 第 7 節。


## Console 與文件檢查

最後不帶 Console 文字／層級過濾重新讀取後，共有 2 error、1 warning；不能把 MCP 受過濾而回傳 0 筆當成乾淨 Console：

- 原 AttackProfessionRuntimeDriver 的必備檢查回報找不到舊 AttackFocusAbility。開工前實際 Prefab 已無此舊模組，本次未改該 Driver 或職業 Prefab；新精準鎖敵的獨立 Runtime 可正常施放。
- 受控 Enemy_Melee_A 探針沒有接 Director，原 Awareness 的 Spawned 因而記 error；沒有指定 Patrol Area 則記 warning。探針刻意停用 AI／導航後直接驗證正式 Enemy 的受控位移，因此兩筆只代表測試環境缺少 AI 配置，不能當作自然敵人 AI 流程驗證。
- C# scriptCompilationFailed=False，最終未有新編譯失敗。保留以上診斷，未為了 Console 零筆而修改原有模組或隱藏錯誤。

Scoped Markdown 連結 288 個皆有對應檔案；git diff --check 通過。原 SupportAerialAbility、TankAirDashAbility、PlayerGrappleMomentumEnergy 無差異；沒有 Scene、玩家 Prefab 或 ProjectSettings 的實質內容變更。

## 2026-10-04 視覺與 HUD 追加驗證

核對來源：Unity MCP、實際 Game camera 的 URP Volume stack、前後資產比對及靜態版面渲染。配置見 [43](../../43_E技能視覺與UI配置.md)。

| 層級 | 本輪證據 | 結果與邊界 |
|---|---|---|
| 編譯 | scriptCompilationFailed=False、isCompiling=False | 新呈現腳本已載入 |
| 新增聚焦 EditMode | 首次 green 3b694d0a697f41bc936a3a335aa35bc5；最終 358ad11b769e40f5a6895339d20c6321，皆 23/23 | 時間條／盾格 10、投射物外觀 4、LGG／回血契約 9 |
| Red → Green | VisualRedEditMode.json：色彩 9 已 green、HUD／外觀 14 缺少實作而 fail；之後 23 全過 | 初始化 total=0 的失敗不列為通過 |
| 完整 PurgersRegression | 9a67720284c34c5995338e5806447953；completed=394，原有同六項失敗 | 尚未全綠；探索數 409 不是執行數 |
| 玩法受控回歸 | [VisualGameplaySingleRunner.txt](VisualGameplaySingleRunner.txt)，56 PASS | 新呈現資產下十項原玩法切片通過 |
| 呈現受控 Play Mode | [VisualSingleRunner.txt](VisualSingleRunner.txt)，38 PASS，7.85 秒 | 五技能時間快照、真實 WeaponCamera LGG、三色啟用／淡出、成功回血一次事件與脈衝；未手動呼叫渲染回呼 |
| 資源清理 | Runner shutdown 後技能 Volume=0、自建 DontSave LGG=0 | 本輪自建調色物件已清理 |
| 作者 Profile | 視覺 Play 前後 SampleSceneProfile.asset 位元組完全一致 | 不回寫共用 Profile |
| 圖示／Prefab | 十張 PNG 的 SHA256 與來源相同，十份 Definition 引用匹配；17 個技能／外觀／StageHUD Prefab Missing Scripts=0 | 三投射物都有有效純視覺 Anchor |
| 靜態版面 | [護盾 1920×1080](../BattleHUD/E-Shield-normal-1920x1080-hp80.png)、[閃現 1280×720](../BattleHUD/E-Blink-normal-1280x720-hp80.png) | 複製 HUD 的指定示例，非真實多人截圖 |
| 獨立多人／Build | 未執行 | Single Runner 不等於遠端 Client、晚加入、重連證據 |

呈現探針第一次在 Fusion 第一個 state 到達前生成 KCC，得到 RuntimeConfig 尚未可讀的失敗；改成與既有探針相同的首輪等待後取得完整通過。這是驗證啟動時序修正，沒有放寬正式生命週期條件。

新增預覽最初強制更新隱藏 Fallback，觸發原字型缺 U+2736 的警告；已改為只更新可見文字。舊 AttackFocus 必備檢查與既有敵人探針缺 Director／Patrol Area 診斷仍依前節列為環境／既有問題。Console MCP 可受篩選而回傳 0，因此編譯狀態另以 EditorUtility 及 Editor.log 核對，未宣稱整份歷史 Console 乾淨。

本輪 _Menu 接線僅 healthSegmentView 與白色 shieldColor；StageHUD 追加 TimedAbilityBar 及引用，保留舊布局。玩家 Prefab、舊空中兩技能未改。投射物只替換示意外觀，正式 VFX 插槽留空；榴彈重頭輕尾為沿速度切線轉向，沒有新增剛體力矩。

最後檔案檢查：本輪 13 份範圍文件的 258 個相對連結皆有效；Assets/Scripts 與 Documentation 的 git diff --check 通過。Unity 序列化新增的 StageHUD 空字串欄位保留 Editor 產生的冒號後空白，因此未宣稱全工作區 whitespace check 全綠，也未手改 YAML 清理；SampleSceneProfile 原有空白保留。技能／外觀 Prefab 的 128 個元件沒有失效序列化引用。最後 Unity 為 EditMode，_Menu scene dirty=False。
