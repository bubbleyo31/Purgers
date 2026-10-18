# SafeHouse 小地圖與投票樣式驗證

日期：2026-10-17（局部核對）。使用者指定 [第一張預覽](approved-first-preview.png)，已透過 Unity Editor 修改並保存 `SafeHouse.unity`。配置與 Inspector 見 [145](../../145_PrePhase4-2_安全屋鍵盤準備與HUD.md) 最新節。

## 已完成

- 等候「討論鈎索音效與震動」完成後才接手 Editor；等待期間的草稿保存在專案外，沒有觸發該工作的重新編譯。
- RED：[4 項中 1 通過／3 預期失敗](red.txt)，舊版缺少獨立秒數與呈現接點。最初兩次匯入前篩選為 total=0，不列為執行證據。
- GREEN：[聚焦 76／76](focused.txt)：SafeHouseHudPresentationTests 4、SafeHouseReadyRulesTests 29、HudLayoutTests 26、MinimapRulesTests 17。
- [實際 Play Mode 隔離檢查](playmode.txt)：讀取傳入狀態顯示出發／準備人數；GameObject.SetActive(false) 實際呼叫 OnDisable，清除新倒數與原狀態文字。沒有建立 Runner，不宣稱多人測試。
- Unity 實際 Graphic／TMP 與既有 Cartography 渲染：[1920×1080](unity-preview-1920.png)、[1280×720](unity-preview-1280.png)。只在臨時場景顯示測試狀態（4 人、2 人準備、5 秒），不是自然遊玩截圖。已檢查排版與文字可讀性；初次圖中 Status 字框 36 小於字型行高 36.21，已改 44 並重繪確認。
- 範圍控制：套用前後比對其他場景元件；另與本輪開始時的場景備份做 [序列化區塊差異](scene-changed-blocks.txt)：28 個既有區塊修改、31 個新 UI 區塊，無移除。既有修改均屬 TopLeftColumn、主控制器新引用，以及 HUD root／ReferenceFrame 的子物件清單。
- Game.unity、_Menu.unity、StageHUD.prefab、LocalMinimapController、SafeHouseFlowController、SafeHouseReadyRules 的 SHA256 與接手前相同。

## 完整回歸與最終狀態

本輪 [完整 PurgersRegression 正式 RunFinished 報告](regression.txt)：1002 passed／2 failed／1 skipped，共 1005 項（含跳過）。兩項失敗與接手前相同：BossStageAssetTests.GameSceneContainsBossAssetWiring（Boss 場景引用）及 ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt（測試 Field 查找 NullReferenceException）。未修改這兩項範圍外功能，不宣稱全回歸全綠。MCP 工作快照因 Domain Reload 停留在 completed=1，最終採持久化正式 RunFinished 報告。

[正式資產檢查](asset-checks.json)：Missing Scripts=0、9 個主 HUD 引用完整、單一控制器與小地圖、35／55 公尺與 M 設定保留。12 人燈號可縮至 16.55 字級，所有投票文字含「取消準備」皆無溢出。最終 Editor 回到乾淨 _Menu，非 Play Mode。

編譯已通過；Console 原有 BattleHudReferenceLayout 的 TextureImporter.spritesheet obsolete 警告，非本次新增。不以 Console 無訊息代替編譯檢查。

## 尚未驗證

本輪沒有重跑獨立 Host／Client、晚加入／重連、自然鍵鼠投票與完整 SafeHouse→Game→SafeHouse 往返；原權威與小地圖程式沒有改動，仍應在正常多人流程接受最終視覺驗收。

1. 正常從 _Menu 進安全屋：左上切角小地圖與原安全屋圖資、玩家／隊友標記正確。
2. 按 M 展開／收合：600×600 中央地圖不受左上框裁切，視野仍為 35／55 公尺。
3. Host 靠近裝置按 E，兩端投票面板出現；準備人數與各玩家燈號一致。
4. TAB 或原準備按鈕切換：文字在「準備／取消準備」間變化；ESC 取消整票。
5. 全員準備時右上只顯示出發倒數；取消準備恢復原投票餘額；等待／載入階段隱藏投票面板。
6. 檢查生命、武器、技能、經驗與獎勵區仍維持原配置。
