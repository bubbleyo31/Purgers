# 技能掉落呈現與換彈動畫驗證

日期：2026-10-16。Unity 2022.3.62f1；既有 `_Menu` 起點，不保存 Scene／Prefab。

## 範圍與結果

- 地上技能：金框、深底、放大圖示、金色標記柱、粗體名稱與可拾取高亮。圖示浮動只有本機效果。
- 小地圖：既有 LocalMinimapController 標記池新增金色實心方塊，遵守原探索、距離、高度及地形視線；只讀同 Runner 的有效 IsWorldLoot／LootPosition；拾取與 Despawn 不等待候選刷新便隱藏。
- 換彈：Rifle／SMG 的本次時間快照驅動 ReloadPlaybackSpeed，保留兩份正式 Controller 原本 speed=1.3；完成、失效、Unbind 及停用重設。沒有改 Animator 全域速度、Clip 或 Scene／Prefab。
- TDD：初始 6 個案例因缺少專屬動畫參數／倍率換算而如預期失敗；最終聚焦 10／10 通過（job `8d1b5722a01f4e58825f82e6abce9221`）。包含兩把槍的實際 Clip normalizedTime 推進、標記池重用、未 Spawn 不讀 Networked。Clip 測試使用無音效接收者的隔離 Animator，停用該測試物件的 fireEvents；正式動畫事件保留。
- 過程發現 Test Runner PerformUndoTask 會復原同群組先前資產修改；將本次資產編輯與測試群組分離後，測試結束再讀參數確認仍存在。沒有清空使用者 Undo 歷史。
- 最終完整 PurgersRegression：954 通過、1 失敗、1 Explicit 略過（合計 956）；新鮮 RunFinished 紀錄見 [regression.txt](regression.txt)。唯一失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring` 的 MapChunk_Boss 資產引用比較；略過為需獨立觸發的多人能力切換測試，不能宣稱完整回歸全綠。MCP job 在 Domain Reload 後停止更新，採實際 callback 檔案，未採 total=0 或卡住進度。
- 受控同 Editor Host／Client：18 項通過，見 [紀錄](host-client.txt)。驗證同步換彈倍率、途中升階不變、下次套用新階、完成與解除綁定重設；掉落同步位置、排除其他 Runner、Client 建立純視覺物件、拾取排除兩端地圖候選。
- [外觀預覽](loot-preview.png) 使用 Unity PreviewRenderUtility 渲染正式 PlayerAbilityLootVisual，已檢視金色外框、標記柱及填色粗體文字。這是隔離呈現預覽，不是自然關卡實戰截圖。

## 手動驗收

1. 開始單人／多人遊戲，替換已裝備的鈎索或專注技能；地上應出現醒目金框與柱、技能圖示、名稱及等級。瞄準可拾取目標時框變亮並顯示 E。
2. 在已探索且無地形遮擋的附近查看小地圖與 M 展開圖：技能為金色正方形，敵人仍紅色、撤離仍綠色；遠方／未探索／遮蔽物品不額外揭露。拾取後方塊消失，進安全屋後不保留上一關標記。
3. 原 LocalMinimapController 的「掉落技能標記」可調顏色與大小；預設 RGB(255,195,45)、9 UI 像素。
4. 分別用 Rifle 與 SMG 測試 10%／15%／25% 換彈加速：動畫、換彈進度與彈藥填入時刻同步。換彈途中取得下一階，當次速度不變；下次才變快。取消／換武器後其他動畫保持原速。
5. 獨立程序 Host／Client、晚加入／重連、自然關卡迷霧與牆面遮擋、音效聽感、多人觀戰及 Build 尚未在本次重驗。既有小地圖探索規則未變；受控資料檢查不能取代上述自然操作驗收。

完整規則與設定：[168](../../168_角色成長被動與技能掉落.md)、[147](../../147_PrePhase4-4_小地圖與戰爭迷霧.md)、[70](../../70_第一人稱模型與動畫.md)。

最終編譯成功，沒有新增歸因於此變更的編譯警告；保留 DemoController.camera、TankPreparationRuntimeVerification 參考比較與 TextureImporter.spritesheet 既有警告。結束後返回 `_Menu` Edit Mode、Scene 非 dirty。最後僅整理 XML 註解後重新編譯，不改測試通過時的行為。

## 2026-10-17 字體／標記柱與索引補充

- 依使用者遊戲截圖修正：名稱、等級、冷卻文字優先使用 NotoSansTC Bold，獨立 SDF 材質 Face Dilate 設為 0.12；保留原字級，降低黑色外描邊至 0.10，未更改共用字型資產。
- 金柱上緣從方框底緣推算，預設兩者 Y 都是 0.375，沒有幾何重疊，不再依不同 SpriteRenderer 的透明排序掩蓋插入部分。
- Unity PreviewRenderUtility 實際渲染 [新版預覽](loot-bold-20261017.png)，包含「衝擊扇波 Lv.1 · 12.3s」展示文字，已檢視名稱與數字均加粗且金柱只接框底。12.3s 為預覽文字，不是捏造的 Gameplay 計時證據。
- 本次編譯成功；既有聚焦 10／10 通過，job `c8ab5aa9a1324d03a59831ae78fe81bb`。未新增重複實作的純外觀單元測試；另透過渲染程式直接檢查金柱上緣等於方框底緣。
- 依本次明確收錄授權，關鍵詞規則索引新增 SKILL-LOOT-MARKER／RELOAD-ANIMATION-SPEED、快速查找與設定／實作／驗證連結，所有新增連結已檢查存在。
- 本次沒有更改網路資料、拾取、冷卻或換彈行為；先前同 Editor Host／Client 證據維持原日期，沒有宣稱重新完成自然實戰或獨立程序驗收。

- 本次完整回歸：954 通過、1 項既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 失敗、1 Explicit 略過，合計 956；見 [本次 RunFinished](regression-20261017.txt)。Editor 回到 _Menu Edit Mode，未保存 Scene／Prefab；沒有新增編譯錯誤。
