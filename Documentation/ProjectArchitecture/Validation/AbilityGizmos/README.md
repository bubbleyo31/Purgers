# 十項 E 技能 Gizmos 試行驗證

日期：2026-10-04。只驗本輪新增的 Editor 診斷顯示；不把上一輪玩法／UI 結果當成這次 Runtime Gizmos 的多人證據。

## 編譯與自動檢查

- 先建立測試；在 dirty 場景保留狀態下，直接執行 Grenade_UsesEditedValuesInWorldMetersAndLabels，取得缺少 Builder 的預期失敗。這一步不是 Unity Test Runner。
- 後來 Editor 回報全部載入場景 clean，才啟動正式測試；本輪沒有主動呼叫 SaveScene、SaveOpenScenes 或重載主場景。
- [正式聚焦結果](focused-tests.json)：ActiveAbilityGizmoTests **28／28 通過**，job 2207ae5c48394cd8985cf2e2d246218b。
- [完整回歸結果](regression-tests.json)：PurgersRegression 完成 **422 項／探索 437 項**，job 1441f998584a46108e86fe1fa2d21d02，result=null；仍為歷史同名 6 項失敗，不宣稱全套通過。
- 上述正式測試後，繪製入口改為各具體型別註冊、投射物選取補上有效 Owner。最後 _Menu 再次呈現 dirty，因此未重啟會保存場景的 Test Runner；[最終原始碼直接檢查](final-direct-checks.json) 逐一執行同 28 項測試方法，**28 通過、0 失敗**，dirtyBefore=true／dirtyAfter=true，編譯未失敗。這是直接 NUnit fixture 檢查，不冒充第二次正式 Test Runner。
- 檢查覆蓋未 Spawn 的十份 Prefab／投射物、即時讀序列化數值、世界公尺不受 Scale 放大、治療拾取下限、無空間範圍技能、精準鎖敵球面距離、扇波球距與水平半角、開關、各技能配色、反彈剩餘行程／第四撞停止、自身出生排除解除，以及 Preview Scene 不回退預設 PhysicsScene。

既有失敗：
- BossStageAssetTests.GameSceneContainsBossAssetWiring
- DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel
- FirstPersonMuzzleFlashTests.AtlasMaterialIsUsableWithoutRuntimeTextureOrMaterialCopies
- PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward
- PlayerExperienceRulesTests.OneLargeGrantCanQueueSeveralRewards
- PlayerExperienceRulesTests.RequirementDoublesFromTen

## Scene View 畫面檢查

使用正式技能 Prefab 的暫時複本，在獨立 Preview Scene／未儲存的暫時 Additive Scene 檢查。沒有新增永久 Scene／Prefab 或調整技能值。臨時視窗與物件已關閉清除。Unity 的臨時視窗 culling mask 會受選取切換重設；空白／錯誤範圍的中間截圖未列為成功證據，已移除。

| 技能／情況 | 有效截圖 |
|---|---|
| 榴彈橘色拋物線與半徑參考 | [Grenade](Grenade-review.png) |
| 貫穿砲桃紅射線 | [PiercingCannon](PiercingCannon-review.png) |
| 扇波紅色三維邊界、擊退箭頭；取消選取仍顯示 | [Shockwave](Shockwave-unselected.png) |
| 護盾白色自身文字 | [Shield](Shield-verified.png) |
| 治療包綠色拋物線、拾取文字 | [HealingPack](HealingPack-review.png) |
| 彈射彈藍色路徑、反彈倍率說明 | [Ricochet](Ricochet-final.png) |
| 經驗增加黃色自身文字 | [Experience](Experience-checked.png) |
| 閃現淺青方向與路程 | [Blink](Blink-review.png) |
| 子彈時間紫色球 | [BulletTime](BulletTime-final.png) |
| 精準鎖敵藍綠球面邊界 | [PrecisionLock](PrecisionLock-verified.png) |
| 關閉 Tools 總開關，技能文字／幾何消失 | [Global off](Global-off.png) |
| 僅選取模式下，取消選取消失 | [Selected only](Selected-only-hidden.png) |

截圖上的原生選取圈／Scene View 導航面板不屬於技能 Gizmos，總開關不會關掉 Unity 本身的工具。

## Console 與未完成驗收

編譯未失敗。直接檢查未被 Console UI 篩選隱藏的紀錄，仍有先前 AttackFocusAbility 缺件錯誤、TMP 字元替代、MCP 連線／RenderTexture 警告與 Player Prefab 必備元件提示；未查到本輪 Gizmo 類別的例外。沒有以 read_console 的空篩選結果宣稱整個 Console 乾淨。

本輪沒有進 Play Mode；有效 Owner 移動／瞄準、快照中心、衝刺剩餘距離、飛行投射物、Proxy 與獨立 Host／Client 畫面仍待手動驗收。沒有更新 AGENTS.md、120 文件規則或專案記憶。設定位置見 [43 配置手冊](../../43_E技能視覺與UI配置.md)。
