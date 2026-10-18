# 隊友辨識驗證紀錄

日期：2026-10-13。範圍：隊友名冊、分格生命／護盾、方向箭頭、地圖接點、URP 輪廓。初次實作沒有接線；同日使用者追加授權後已完成正式資產接線（見下方追加紀錄）。

## 聚焦與回歸

- 先新增測試，force import 後 `ce0861b49a2c413c8ffa8c52013f5a3f` 實際執行 18 項，皆因規則／名冊入口尚不存在而預期失敗。第一次尚未匯入的 total=0 不算有效執行。
- 最終聚焦 **26/26 通過**（隊友 20 + 既有 HUD 相容性 6），job `ee68aa5d1ff04461bec452988d2acdcd`。涵蓋 12 色互異、色槽補空不重排、背後不鏡像、視口偏移、低血量精確門檻，以及實際網格的紅生命／淡金護盾同列。證據：[FocusedEditMode.txt](FocusedEditMode.txt)。
- 第一輪網格測試因反射命中 OnPopulateMesh 同名多載失敗，修正測試指定 VertexHelper 後通過；不是忽略或移除該測試。
- 完整 `PurgersRegression` **773 通過、1 失敗、1 略過（共 775）**，job `474d0e3eece546819df1709d97d5640e`。Unity Domain Reload 使 MCP 工作狀態停留 running，因此使用現有 `EnemySuicideTestResults.Arm` 保存真實 RunFinished 樹；已確認 Editor 不在執行測試後清除孤立 job。證據：[PurgersRegression.txt](PurgersRegression.txt)。
- 唯一失敗仍為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`；略過為 Explicit 的 `AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`，需另外啟動 Photon。不得稱完整回歸全綠。
- 額外 Editor 幾何檢查：1920×1080、2560×1080、640×480 各 12 個同角落箭頭，以非零視口原點計算，均保持邊界內且間距 >=30.5 像素。這是幾何檢查，不是各解析度人工視覺驗收。

## 受控 Host／Client（同一 Editor）

手動在沒有現有 Runner 的 Play Mode 執行 `TeammateRuntimeVerification.Begin()`；狀態完成「通過」，13 項明確 Check 加上有逾時保護的同步等待全數完成。[ControlledHostClient.txt](ControlledHostClient.txt)。

- Host 先建立名冊與名稱，再讓 Client 晚加入；Client 得到 Host 的既有名稱快照。
- 第二玩家分配藍色；Client 與 Host 查得一致，Client 呼叫權威名稱寫入入口被拒。
- 指定世界相機下，前方只有一份名條；轉向背後只有一個箭頭、不重複名條。
- 正式 ReceiveDamage → GameLogic 死亡排程 → Despawn → 原重生流程；Client 收到死亡位置且角色已不在場，重生後名稱／色槽維持。
- Client 斷線清理身份，新 Client 重連取得當前 Host 名冊，Host 顏色不重排。
- 所有暫時 Runner 與物件已清理，退出 Play Mode；不寫正式存檔。
- 本測試以權威名稱寫入接點檢查網路快照，沒有模擬選單改名或經聊天 RPC 傳入文字；該註冊流程沿用既有實作。
- **不等同獨立程序 Host／Client、自然遊玩、SafeHouse 換關流程、最終模型動畫或成品 UI 的人工驗收。**

## 輪廓／編譯／資產

- `TeammateVisualVerification.Capture()` 複製目前 URP Pipeline／Renderer，在 Preview Scene 加入真正的 TeammateOutlineFeature，透過 `RenderPipeline.SubmitRenderRequest` 擷取。檢視 [OutlineURP.png](OutlineURP.png)：左側可見球體為淡藍輪廓，右側被實體遮擋的球體為加強綠輪廓，遮擋面仍不透明，沒有將整個角色填色。
- 預覽結束 finally 還原 QualitySettings.renderPipeline，刪除複本與 Preview Scene；確認正式 `URP-HighFidelity` 恢復、_Menu isDirty=false。未保存任何正式 Renderer 修改。
- 前置直接 CommandBuffer 擷取缺乏完整 URP 相機狀態，不列為遮擋驗證；上面的完整 URP 路徑才是正式圖像證據。
- Shader 支援目前圖形裝置，ShaderUtil 無錯誤。最終 Console 無 Error；現有 BattleHudReferenceLayout 的過時 Sprite API 警告與 MCP 孤立 job 清理紀錄，均不是新隊友程式警告。
- 新 UI Prefab 的設定／字型引用已通過測試，沒有 NetworkBehaviour；角色身體 Renderer 由使用者手動指定，不新增網路玩家元件。
- 檢查期間存在大量其他功能的工作區修改，均保留；未重設其他 Scene／Prefab／程式。

## 剩餘驗收

HUD、頭頂錨點、第三人稱 Renderers 與三個品質等級 Renderer Feature 已接線；後續依 [101 驗收](../../101_隊友辨識與穿牆顯示.md) 進行實機外觀驗收。尚未完成獨立程序多人、實際換關、成品角色／SkinnedMeshRenderer 動畫輪廓、Build、GPU profiling 與最終美術驗收。

## 2026-10-13 追加授權：正式資產自動接線

- 使用者明確要求「配置自動接線」。Editor 起始為非 Play、_Menu 無未保存修改；接線前五份序列化資產快照保存於 `Temp/Teammates/WiringBefore`，未覆寫原工作區變更。
- `_Menu/GameplayHUD Canvas/PlayerHUDCanvas/TeammateDisplay`：唯一 HUD、設定引用有效、Screen Space Overlay、RectTransform 拉滿且 Scale=1。
- `KCC_Player`：新增 TeammateVisualAnchor；Head 下的 TeammateNameAnchor 初始 Root 高度 2.15m，沿用 PlayerLocalView 的 8 個第三人稱 Renderer；沒有手部／武器／LineRenderer。Prefab 無 Missing Script。
- Balanced、HighFidelity、Performant 三份 Forward Renderer 各一個啟用的 TeammateOutlineFeature，Shader 已序列化引用；Renderer Feature Map 依正式 subasset local id 同步，重新匯入後檢查。
- 透過 Unity Editor API 保存；URP 保存時會補齊套件既有序列化欄位及資源引用，Balanced 的舊 SSAO SampleCount 亦由套件遷移成新版欄位。未手動調整既有 SSAO 強度／半徑／品質數值。
- 本輪只做資產配置與引用驗證，無新增 Gameplay 程式；Console Error=0。前述 26 項與 775 項測試為先前實作結果，未宣稱本輪重新執行。檢查紀錄：[WiringChecks.txt](WiringChecks.txt)。
- 正式接線已完成；獨立多人、SafeHouse 換關、不同畫質下的最終外觀與 Build 仍待實機驗收。

## 2026-10-13 血格置中修正

使用者回報頭頂血格偏左。原因為 RectTransform 已置中，但 LocalHealthSegmentView 仍從 Rect 左緣畫固定寬度格子。隊友配置現在依整列實際寬度（包含容量底格，扣除尾端格間空隙）計算起點；受傷殘影共用起點。既有本機 HUD 保持原對齊。

直接呼叫正式 OnPopulateMesh 檢查非透明頂點範圍：HP/maxHP/shield = 100/100/0、20/100/40、1000/1000/200；修正前中心 X 分別 -30.4、-18.4、-0.1782，修正後為 0、0、-0.0000019（浮點誤差）。完成匯入，Console Error=0，程式 diff check 通過；此次為局部排版幾何驗證，未重跑完整多人／回歸套件。
