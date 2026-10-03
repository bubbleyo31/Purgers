# 第一人稱槍口火焰驗證紀錄

日期：2026-09-25。Unity MCP 確认 Editor 為 `Purgers@9aed9340888db32b`、專案 `M:/UnityProject/Purgers`、Unity 2022.3.62f1。配置說明見 [162](../../162_第一人稱槍口火焰.md)。

## 自動與 Editor 證據

| 項目 | 結果／證據 |
| --- | --- |
| 先確認預期失敗 | job `217f921a86e449b2a9ef7bfbecc4e83d`，5 個測試皆因尚無 FirstPersonMuzzleFlashPlayback 而失敗 |
| 最終聚焦 EditMode | job `9af0a3e56c534204addcf3ae51424de8`，total=8、passed=8、failed=0、skipped=0 |
| 序號／容量 | 測試 10,000 次前進序號重播、回捲後追平不重播、初始綁定不補射、int 溢位、封鎖後不補播 |
| 實際 Renderer | 1,000 次 EnsureVisual／PresentFrame 仍只有一個 Renderer、同一 Mesh、同一共享材質 |
| 跟隨／尺寸 | 移動並旋轉鏡頭父節點、移動槍口、FOV 改 42° 後，pivot 仍對準槍口、Quad 保持 0.18 畫面高度 |
| 清理 | EditMode 主動呼叫 OnDisable 清理路徑後，無 Renderer 且自建 Mesh 已釋放；不是 PlayMode 生命週期驗證 |
| 未 Spawn 防護 | 未綁有效 Owner／未 Spawn 的 Rifle 不讀網路狀態、不建立效果 |
| 貼圖／Shader | 圖集 1448×1086、保留 alpha、不開 mipmap、不設 Read/Write；Shader supported=True、ShaderHasError=False |
| 真實材質渲染 | 用 PreviewRenderUtility 與新 Presenter／URP Shader 產出 [EditorRender.png](EditorRender.png)，已目視確認透明輪廓與正確單格 |
| Console | 最終 Error 查詢為 0；開工前即有荊棘 LOD 命名與 MCP WebSocket 等既有 warning |

最早一次工具篩選回報 total=0，已視為未執行並重新 Force Refresh，未拿它當通過證據。測試擴充時曾因普通 MonoBehaviour 在 EditMode 不自動觸發 OnDisable 而失敗，測試改為明確呼叫清理路徑，最終 8 項通過。

## 完整回歸限制

最終 `PurgersRegression` job `8212c59309d0416cae4e5883d9dff036` 回報 failed、progress.completed=213，失敗清單有以下 5 項。MCP 此失敗 job 的 result summary 為 null；progress.total=228 是探索數，不能把它報成 228 個已執行測試。

| 本次未修改模組的失敗測試 | 實際失敗 |
| --- | --- |
| BossStageAssetTests.GameSceneContainsBossAssetWiring | 預期與實際 MapChunk_Boss 非同一引用 |
| DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel | 預期 33、實際 13 |
| PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward | 預期 3、實際 8 |
| PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards | 預期 3、實際 4 |
| PlayerExperienceRulesTests.RequirementDoublesFromTen | 預期 10、實際 5 |

程式現況 `PlayerExperienceRules.DefaultBaseRequirement=5`，與仍預期 10 的測試不一致。此輪未修改經驗值、Boss 資產或它們的測試，不為了綠燈改使用者設定。完整回歸不是全綠。

## 未驗證與接線狀態

2026-09-25 後續美術更新：使用者已回報功能沒問題；以下未接線描述保留為初次交付當時狀態。此次只替換圖集，改用參考圖的尖角、白熱核心與黃橘配色，加上立體厚度、側面明暗與前後遮擋。生成工具為內建 imagegen；提示要求 4×3／12 格、1448×1086、透明 RGBA、固定槍口 pivot、同一發由點燃至消散、無文字或背景。首次輸出尺寸差一像素，經 imagegen 校正後確認尺寸正確。Unity 重新匯入並核對原 GUID、材質引用、alpha 與 Shader，未重跑 Gameplay 測試。舊版保留於 [StylizedMuzzleFlash_v1.png](StylizedMuzzleFlash_v1.png)，上方 EditorRender.png 仍為舊版渲染證據。

- 最終 Editor 讀回：Attack／Support 原 Prefab 各有 0 個 FirstPersonMuzzleFlash，Manager 的 muzzleFlashCamera 為空，符合保留手動接線範圍。
- 未改／儲存既有 Scene 或 ViewModel Prefab；只透過 Editor API 建立新材質與設定新貼圖 importer。
- 未執行遊戲內開火、實際高射速 Profiler、死亡／重生／切場、獨立 Host／Client 或晚加入／重連驗收。
- 本輪 Editor Renderer 測試不代表網路預測端到端正確，亦不代表使用者已接受遊戲內美術效果。
