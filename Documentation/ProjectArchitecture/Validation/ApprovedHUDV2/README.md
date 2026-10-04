# 已確認 HUD V2 驗證紀錄

日期：2026-10-04。範圍為獎勵底圖／配色、方形小技能槽、整格血量與護盾。設定見 [167](../../167_已確認HUD美術與整格血量.md)。

## 結果

| 層級 | 實際證據 | 邊界 |
|---|---|---|
| Red → Green | HealthWholeCellTests 原有實作 6/6 失敗（job 7d3aaa596540420189d7a636c93087af）；改完 6/6 通過（5be1ecd41189459a9bf4c5a53624835a） | 預期缺少整格快照與固定寬度行為 |
| 最終聚焦 EditMode | [原始結果](FocusedEditMode.json)：216cc109233b4bf4ace8d5dfbc89c27f，26/26、0 failed | HealthWholeCell、ApprovedHudVisual、BattleHudReadability、ActiveAbilityHud、BattleHudReferenceLayout |
| 全 PurgersRegression | [原始結果](PurgersRegression.json)：846fe3a70db84e439c882a2899bad95b，430 項已執行、6 失敗 | MCP result.summary 為 null；445 是探索數，不冒稱 445 全跑或 424 通過 |
| Play Mode | [隔離 UI 原始結果](IsolatedPlayMode.json)：13 個 True | 真正逐幀 Update，使用暫時 Slider＋LocalHealthSegmentView；不是網路實戰 |
| 編譯與 shader | scriptCompilationFailed=False，ShaderHasError=False | 最終 Editor 狀態 |
| Console | 最後重新匯入後 GetCountsByType：0 error、1 warning、1 log | 既有 VFX DemoController.camera 的 CS0108；暫時解除 Console 過濾讀取後恢復原設定 |
| 引用 | StageHUD 與含生命 HUD 的 _Menu 根階層，1932 個 Component，Missing Scripts=0、broken object references=0 | 檢查非零 instance ID 卻 null 的引用 |
| Game 繼承 | 唯一 BattleAbilityHUD 槽位 80×80、米白色；SafeHouse 無 BattleAbilityHUD | 以 additive read-only 開場核對後關閉，沒有保存兩場景 |
| Editor 結束狀態 | EditMode、_Menu、dirty=False，暫時 Play 物件已移除 | 保留開工前工作區的其他未提交修改 |
| Build／獨立多人 | 未執行 | 不宣稱 Host／Client、晚加入、重連或自然遊玩流程通過 |

初始化 timeout／total=0 的工具呼叫不算通過；穩定編譯後重新執行得到 26/26。文案檢查曾發現標題高度不足，統一改 20 字級／30 高後通過；描述採 16 字級、相同內距／TopLeft，遍歷目前正式 RewardCatalog，在三張卡中檢查高度與 TMP 每個可見字元左右邊界。

## 既有六項失敗

與 [ActiveAbilities 既有紀錄](../ActiveAbilities/README.md) 相同，本輪未修改其玩法／素材：

- BossStageAssetTests.GameSceneContainsBossAssetWiring：MapChunk_Boss 引用不同。
- DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel：expected 33、actual 13。
- FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies：素材目前為 _2 圖集。
- PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward：expected 3、actual 8。
- PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards：expected 3、actual 4。
- PlayerExperienceRulesTests.RequirementDoublesFromTen：expected 10、actual 5。

## 實際 Unity 圖像

- [1920×1080 獎勵＋技能＋新血量](../BattleHUD/approved-v2-rewards-1920.png)
- [1280×720 同版面](../BattleHUD/approved-v2-rewards-1280.png)
- [高血量：900／1200 HP＋400 盾](../BattleHUD/approved-v2-high-health-1920.png)

以上是複製實際 Graphic／TMP／Sprite 的 Editor RenderTexture，指定 120／240 HP＋80 盾、三個技能文案。高血量沿用每格 20，固定 Rect 內縮窄，不把格子切片。示例暫時隱藏 TimedAbilityBar 以對照批准的預覽；正式 Prefab 的時間條、其他藍色面板／動能／經驗／武器皆保留。這些不是正式玩家相機或多人實戰截圖。

## 人工驗收順序

1. 從 _Menu 啟動遊戲，進 Game 後看左下米白生命、淡金盾及右下小方形槽。
2. 各收到 8、7、5 點正式傷害；真實數字即時變，第三次少一格。反向治療同理；低 HP 只改生命色。
3. 取得盾後確認緊接可見生命格、沒有空行；破盾、到期、死亡、重生不殘留。
4. 打開獎勵選單，檢查各個技能長文案、二／三候選及實際滑鼠選取。確認卡片形狀未重排，文字未越框。
5. 檢查 Q／E 按下、冷卻、不可用、換裝與正式圖示；確認中央效果條仍由原系統管理。
6. 分別在 1920×1080、1280×720 與獨立 Host／Client 執行上述步驟。小地圖、其他面板及轉場的原有流程不在本次改版範圍。

最後重新匯入的唯一警告為既有 Assets/_Project_Assets/VFX/Mirza Beig/Cinematic Explosions FREE/Scripts/DemoController.cs:19，CS0108 camera 隱藏 Component.camera；本輪未改該檔。148 個範圍文件相對連結皆有效，修改的既有 C#／文件 git diff --check 通過。Scene／Prefab 仍保留 Unity 原生序列化，不手改 YAML 空白。
