# HUD 編輯模式排版驗證

日期：2026-10-19（Asia/Taipei）  
範圍：HudLayoutWorkspace／HudLayoutSession 與五類預覽；自己區水平位置、TAB 視窗位置。  
操作：[113 HUD 編輯模式排版](../../113_HUD編輯模式排版.md)。

## 聚焦與編譯

- 先新增行為測試：自己區水平位置在施法避讓後仍保留、TAB 真實 Build 消費保存位置。兩項均因定位欄位尚不存在而失敗；首次工具回報 total=0 未計入，實際非零失敗後才實作。
- 最終聚焦 **50／50**；涵蓋設定保存與 Undo、來源變動拒絕覆蓋、未套用不改來源、Scene 清理、既有 Rect 寫回不複製範例、正式技能模板引用不變、Overlay Canvas 轉為隔離預覽，以及既有 Status／Guard／TeamInfo／AbilitySlot 呈現測試。
- [聚焦 XML](focused.xml)、[時間與摘要](focused-summary.txt)。
- Unity 2022.3.62f1 編譯完成，沒有本輪新增的編譯錯誤；原 BattleHudReferenceLayout 的 TextureImporter.spritesheet obsolete 警告不屬本輪。
- 原生草稿建立／關閉實測：`Game.unity` isDirty 前後均 false；結束後同名草稿 root 數為 0，原 Scene 保持開啟。沒有開啟或聚焦 EditorWindow。

## 完整回歸

最終結果：**1260 通過、5 失敗、2 跳過，實際 1267 項**；RunFinished 時間為 2026-10-19 17:09:40（Asia/Taipei）。以 [regression.xml](regression.xml) 與 [regression-summary.txt](regression-summary.txt) 為準；本輪完整回歸不是全綠。最後加入的 Overlay Canvas 隔離測試亦通過，五項失敗與下表一致。

前一輪同變更全套為 1259 通過、5 失敗、2 跳過，實際 1266 項。MCP 在測試跨 Play Mode 後停留於 34 項的舊進度；Unity `IsRunActive` 已 false 且既有 GuardGrowthTestResults 記錄器產生新的 `RunFinished` XML，所以使用 XML 為結論，清除 MCP 過期 job，沒有搶焦點恢復。

| 失敗 | 內容／歸屬 |
|---|---|
| BossStageAssetTests.GameSceneContainsBossAssetWiring | Boss Chunk 引用比對；與較早 momentum-gauge-regression.xml 相同。 |
| EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules | 預期 0.5，實際 0.25；與較早結果相同。 |
| EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners | 預期大於 80，實際 75；與較早結果相同。 |
| ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt | `_Menu` 鏡頭效果引用條件；與較早結果相同。 |
| AbilityToggleInputTests.BulletTimeSecondPressUsesNormalFinishAndResolvesDamageExactlyOnce | LocalHitConfirmed 時 FirstPersonHitCameraShake.Singleton 為 null；本輪比前述較早結果多出的失敗，單獨重跑亦重現，不能直接冒充前四項既有失敗。呼叫鏈在未修改的 PlayerHitFeedbackController／BulletTime 結算測試，本輪不擴大修正。 |

[子彈時間單獨重跑 XML](bullettime-isolated.xml)、[摘要](bullettime-isolated-summary.txt)。完整回歸中雖含受控多人測試，不代表本次新排版已完成自然 Host／Client 操作驗收。

## 五類正式元件離屏圖

下列圖片均由同一 HudLayoutSession 生成，只有示例資料，無實際玩家或網路連線：

- [自己異常／格檔](SelfStatus.png)
- [TAB 隊伍窗](TeamInfo.png)
- [敵人血條／異常](Enemy.png)
- [隊友名條／異常](Teammate.png)
- [既有 StageHUD 能量環與正式技能狀態](AbilityHUD.png)

以 1920×1080 參考平面核對布局，圖示與字體沿正式來源。自動排列的範例子物件不可編輯；可拖曳的是整組容器。

## 邊界與待驗

- 未執行桌面點擊／鍵鼠或 SceneView 聚焦；使用者實際 Rect Tool 拖曳、開啟 Prefab stage 的前景操作仍待自然驗收。
- 未執行獨立 Host／Client、晚加入、重連或 Build 專項驗證；本輪未更動網路與玩法入口。
- 工具不是所有 HUD 的所有狀態模擬器。現有 UI 模式可編輯指定容器並顯示現有美術，技能另提供冷卻／生效範例；其他 HUD 的完整動態資料仍依原 owner。
- 自己區保留施法條避讓；敵人／隊友頭頂位置仍由目標決定；TAB 內部固定四列及動態網格保留。
- 正式 Scene／Prefab 未自動套用新位置；新欄位預設零，既有資產繼續使用原版面。
