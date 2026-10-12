# 衝擊扇波身體命中漏傷修正

最後核對日期：2026-10-08（本功能局部核對）
核對來源：使用者正在執行的場景唯讀診斷、PlayerShockwaveAbility、Unity MCP、受控 Runner。
相關：[44 設定與幾何](../../44_衝擊扇波推進與特效.md)。

## 問題及最小修正

[live-diagnosis.json](live-diagnosis.json) 保存原場景 Enemy_Ranged_C_Beam 的診斷。
相對出手座標：腳底 Root 約 (0.20,-4.47,21.66)，波牆局部高度是0～5，因此 Root 被排除。
實際 Body Collider 中心約 (0.20,2.37,23.35)，六個受擊 Collider 中心都落在完整掃掠內且遮擋為 false，卻仍然因 Root 判定而沒有送出傷害。

ApplyWaveBand 改為以候選 Collider.bounds.center 檢查連續掃掠，與既有遮擋／DamageRequest.HitPoint 共用目標取樣點。
Hit Mask、EnemyActor 身分、State Authority、每波去重、傷害值與水平擊退流程保持。玩家根座標仍供擊退方向與傷害距離使用。
這是受擊 Collider 中心取樣，不宣稱整個 Collider 邊緣擦碰必定命中；沒有把高度放大或取消遮擋。

## 驗證

| 層級 | 結果 |
|---|---|
| 修正前 Play Mode | [red-single.txt](red-single.txt)：先確認 Root 在外、Collider 中心在內；實際扣血檢查失敗，成功重現。 |
| 修正後 Single Runner | [single-runner.txt](single-runner.txt)：38項 PASS。相同相對姿態、正式 EnemyActor／Health、Hit Mask僅Layer9，身體雙 Collider 只扣一次7傷害；反向案例 Root 在內、身體在外不受傷。 |
| 聚焦 EditMode | [focused.json](focused.json)：92/92通過，job 8513224d9af14e259c94e94c6137e639。 |
| 完整 PurgersRegression | [regression.json](regression.json)：completed=530、discovered=545、result=null、status=failed，僅既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用失敗；不是全綠。job 89658aa5449f466e84f933f9a394bcf6。 |

Single Runner 同時保留近遠抵達時機、高速跨Tick、遮擋、範圍外、空中、前搖取樣、上下斜射、死亡及特效清理案例。
診斷與受控重現分開記錄：沒有直接對使用者原場景執行傷害，先取得退出Play Mode同意後才進行隔離測試。
本輪沒有儲存 Scene／Prefab 或修改人工平衡值。獨立程序多人、Build、原自然場景重新遊玩仍未驗收。
同 Editor Photon Host／Client：[host-client.txt](host-client.txt) 19項 PASS；角度／幾何同步、晚加入、重連、Input Authority 正式輸入路由、取消與Despawn。這輪雙Runner測同步流程，新增身體扣血案例在Single Runner驗證。

最終 [editor-state.json](editor-state.json) 與 [console.json](console.json) 記錄退出Play Mode、編譯成功、無live Runner、_Menu未dirty；Console error／warning API 0筆。
技能Prefab與本輪前副本SHA256一致，修改範圍通過git diff --check。