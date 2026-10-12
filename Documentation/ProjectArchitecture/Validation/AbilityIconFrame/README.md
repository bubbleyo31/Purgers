# 技能 Icon 填框驗證

日期：2026-10-07（Asia/Taipei）。範圍：既有獎勵三卡與方形技能槽的本機顯示。

- 先測：第一次因未匯入新檔案得到 total=0，不算通過。強制匯入後 job `987965bc6f0f4ce093c3274f7cc91523` 實際執行 7 項並預期失敗（元件／接線不存在）。
- 實作後幾何測試：job `f758c3ef63a7495abff9a6eb7ac9453d`，6/6 通過；涵蓋長、寬、方圖與矩形／六角形，頂點置中、UV 等比、覆蓋及斜邊範圍。
- 相關測試：job `8409d0b083c64054bc634e09d7de1d90`，13/13 通過。包括 AbilityIconFrameFitTests、AbilitySlotCenteringTests、Phase6RewardHudPrefabTests、ApprovedHudVisualTests。
- 完整 PurgersRegression：job `54b54fbc92344f12b026dea2d495a515`，completed=489，1 項失敗：`BossStageAssetTests.GameSceneContainsBossAssetWiring`。工具在失敗 job 的 result 為 null，不能冒稱取得完整 passed summary。前次 job `b884305895c242be9c43e6cdd74ded4e` 也回報同一失敗。
- Boss 只讀診斷：Game 的 bossChunkPrefab 指向非 AssetDatabase 資產物件（AssetDatabase 路徑空白），測試期待 `Assets/Prefabs/Map/MapChunk_Boss.prefab` 的 MapChunk；Unity equality 與 ReferenceEquals 均 false。本次沒有更動 Game 場景或 Boss 資料。
- Play Mode：暫時複製正式 StageHUD，停用玩法資料控制器，依序套用 15 份 Definition 的 HudIcon 到 4 個既有 Image。Canvas.ForceUpdateCanvases 後檢查 CanvasRenderer.GetMesh() 的頂點數與框中心，60/60 通過；暫時物件已清除，退出 Play Mode。
- Play 驗證過程的初次探針曾因 API 簽名與 Awake 隱藏範本而未成功；修正探針後才取得上述 60 次有效證據。這些不是正式腳本編譯錯誤，也不是自然遊戲流程驗收。
- 最終 scriptCompilationFailed=false；Console 0 error／0 warning；StageHUD Missing Scripts=0；4 個新元件引用有效。
- Prefab diff 僅新增 4 個元件與 4 筆元件列表引用，共 68 行。原 Image／RectTransform、Sprite、材質、技能 Definition 與 Scene 未因本任務變更。
- 最終返回 `Assets/Scenes/_Menu.unity`，Play=false、dirty=false。
- 尚未驗證：自然開獎／選取流程、獨立 Host／Client、Build、使用者最終外觀驗收。此次無網路玩法變更。

[15 個技能的六角／四角實際 Unity 渲染](all-icons-hex-square.png)  
[Inspector 與手動檢查](../../167_已確認HUD美術與整格血量.md)

