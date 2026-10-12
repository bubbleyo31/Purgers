# 衝擊扇波：出手瞄準、整片傾斜與 XYZ 發射偏移

最後核對日期：2026-10-08（本功能局部核對）
核對來源：實際程式、Unity MCP、EditMode、受控 Single／Host／Client。
配置與公式：[44 說明書](../../44_衝擊扇波推進與特效.md)。

使用者確認：前搖結束時依瞄準角度發射，整片波牆隨俯仰傾斜；出發點可配 XYZ。
Origin Offset 使用瞄準座標右／上／前，預設零，出手時快照，出手後不追隨玩家。
Host 計算完整 WaveRotation、解析發射點及傷害；Client、Gizmo 與網格使用同一座標。垂直瞄準也保有 Yaw。
出發點沿用既有 ActiveAbilityPhysics 安全解析，避免偏移跨薄牆；擊退仍走 EnemyStateController 的水平移動。

## EditMode 證據

| 層級 | 結果 |
|---|---|
| 先紅後綠 | [red.json](red.json)：舊程式抬頭／低頭兩項平移測試皆失敗；job 1effbabaaecf4f7699b6b0379d2651ed，completed=2，result=null。 |
| 聚焦測試 | [focused.json](focused.json)：92/92 通過；job a2c476d482c54b98bf2d82282113f27c。 |
| 完整 PurgersRegression | [regression.json](regression.json)：completed=530、discovered=545、status=failed、result=null。只回報 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗；不推算通過／跳過數。job ff2d67902eeb49a69c4165b15f5281fc。 |
| 既有失敗對照 | [上一階段紀錄](../ShockwaveSeparation/README.md) 的修改前／後完整回歸已有同一 Boss 引用失敗。 |

聚焦包含 ShockwavePropagationTests、ShockwaveVisualTests、ActiveAbilityGizmoTests、ActiveAbilityPresentationTests、ActiveAbilityAssetsTests、ActiveAbilityShotOriginTests。
新增抬頭／低頭、±90° 與不同 Yaw 的局部高度邊界、可見頂點與傷害幾何一致、XYZ 座標與 Prefab Scale 隔離、傾斜 Gizmo。
既有薄天花板／厚牆／嵌入錨點／自身 Collider／PhysicsScene 隔離測試一併通過。

首次實作有 Gizmo 終點呼叫參數型別錯誤，Console API 回傳零筆但 Editor.log 有 CS1503；已修正並確認實際新組件載入、scriptCompilationFailed=false。失敗前載入舊組件的測試不當作新程式結果。

## Play Mode 證據

[single-runner.txt](single-runner.txt)：34項 PASS，使用正式玩家與敵人 Prefab 的 Single Runner。
包含前階段24項，加上抬頭／低頭各5項：前搖不立即發射、前搖中改瞄準、XYZ 依出手角度旋轉、出手後轉向不能操控、傾斜掃掠實際扣血。
空中 fixture 在前搖期間固定受控姿態，以隔離重力對採樣位置的影響；不代表自然空中操控手感。

多人初次 [host-client-initial.txt](host-client-initial.txt) 與 [診斷重跑](host-client-diagnostic.txt) 在 Client 出手驗證逾時。
診斷顯示 Prefab 保存 _ActivationSequence=19，實際出手成20；測試寫死等待1，並非輸入未抵達。
改為出手前序號+1，未重設或覆寫資產序號。最終 [host-client.txt](host-client.txt) 19項 PASS：Host 完整瞄準、Client 網格旋轉、晚加入快照、取消同步、重連不補播、Client Input Authority 正式 NetInput 送件及 Despawn 清除。

## 資產與未驗收範圍

此輪沒有呼叫 Scene／Prefab／材質儲存。技能 Prefab 與本階段起始副本 SHA256 相同（F65DA5EF63A1DCEF049056AEE8CDE97A43F120EC312BB0526C41155CF00D0513）；副本已包含 Radius=8、Wave Height=5、_ActivationSequence=19，全部保留。
現值以44與實際 Inspector 為準；先前階段記錄的 Radius=20 是歷史讀值。

未執行獨立程序 Host／Client、Build、正式關卡自然遊玩／網路延遲矩陣或最終美術驗收。
同 Editor 受控 Runner 與 EditMode 不替代上述證據。網格成本、材質及貼圖本輪未增加。

手動續驗：開啟 Shockwave.prefab 的 PlayerShockwaveAbility，調 Origin Offset；在有前搖時先朝一方向按 E，再轉向／抬頭／低頭，確認出手取最新方向且上下同寬。出手後轉身，原波仍沿固定方向前進；靠薄牆測偏移會縮回障礙前。
## 最終 Editor 狀態

[editor-state.json](editor-state.json)：退出 Play Mode，編譯未失敗，_Menu 未 dirty，非資產 Runner=0，扇波 Shader 無錯，視覺 Prefab Missing Script=0。
[console.json](console.json)：本次最終 Console error／warning API 0筆。Editor.log 全編譯仍包含既有 BattleHudReferenceLayout.cs 的 TextureImporter.spritesheet obsolete 警告；非本次新增。
本次程式差異通過 git diff --check。新欄位 Origin Offset 已能由 SerializedObject 讀取，初始為 (0,0,0)；未自動調整人工值。