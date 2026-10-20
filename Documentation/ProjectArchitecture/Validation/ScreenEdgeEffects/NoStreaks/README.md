# 2026-10-20 速度動能增益移除動態流線

使用者要求移除會動的線條。共用 ScreenEdgeEffectsCore.cginc 已移除 speedStreaks 的 32 次迴圈與流線 Alpha／高光合成；速度動能增益保留柔光外框與既有輕微明暗呼吸。正式 Runtime 和 Editor 預覽同步套用。

滿格門檻、淡入淡出、資料來源、治療、受傷及 HUD 分層均未更動。未修改 Scene／Prefab／設定資產。目前既有 momentum 設定已為青綠 tint=(0.1462264,1,0.85263824)、strength=0.35、width=0.067；本輪保留人工值，沒有改回舊橘金預設。

舊 motionRate 序列化欄位保留相容但隱藏；預覽移除無效的「流線速度」控制。歷史預覽圖片／動畫保留原版本。

## 畫面與編譯

- [修改前](momentum-streaks-before.png)
- [移除流線，t=1.3](momentum-no-streaks.png)
- [移除流線，t=2.7](momentum-no-streaks-later.png)

使用正式 Graphic／Shader 與實際 HUD 的離屏相機渲染，沒有搶焦點或進入 Play Mode。兩個時間點目視確認不再有向外移動的線條；中央透明與 HUD 前後層次保持。這是離屏渲染，並非自然實戰錄影。

Runtime／Preview ShaderHasError 均為 false。Console 存在既有地圖無生成區、鈎索無合法目標及 TMP 缺字警告，本輪沒有修改這些來源。

## 測試與邊界

既有 ScreenEdgeEffectsTests 聚焦 16／16 通過，見 [XML](focused.xml)／[摘要](focused-summary.txt)。本輪僅外觀調整，沒有新增鏡像實作的文字斷言或重跑網路測試；上一輪 Host／Client 證據保留於 FullOnly，不冒充本輪重新驗證。

完整回歸 1278 項：1272 通過、4 既有失敗、2 跳過，非全綠；見 [XML](regression.xml)／[RunFinished](regression-summary.txt)。4 項失敗為 BossStageAssetTests.GameSceneContainsBossAssetWiring、EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules、EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt，均已出現在先前基線。MCP 跨 Domain Reload 的 running 輪詢已過期，使用當次 12:21:35 UTC RunFinished 統計，完成後才清除舊 job。自然實戰、Build 與 GPU 效能待驗。

輸出保留於 M 槽專案的本目錄及 Validation/BattleHUD，沒有新增工作樹或獨立 Unity 快取。
