# 換彈鏡頭旋轉驗證

日期：2026-10-17。使用者已確認兩槍既有 Root、只取旋轉、獨立幅度與快速轉頭減弱；初次實作要求 Scene／Prefab 保持人工配置；後續明確授權自動配置兩槍。

## 實作與資產狀態

- 新增可選 FirstPersonReloadCameraMotion 與純狀態 ReloadCameraMotion。
- 已有 ActionAnimator 提供安全的本機換彈來源與 BindingVersion；InputManager／PlayerMovement 僅提供只讀輸入快照／靈敏度。
- FirstPersonHitCameraShake 合成 authored base × reload × hit shake；不改 Look、彈藥、換彈計時、位置、FOV 或網路狀態。
- 後續已依授權完成兩槍 ViewModel Prefab 接線；Scene 使用既有入口，詳見下方自動配置紀錄與 [70 設定位置](../../70_第一人稱模型與動畫.md#reload-camera-motion)。

## 聚焦驗證

- 先新增 8 項測試，確認尚無 ReloadCameraMotion 時全部在明確的缺少型別斷言失敗；紀錄：[red-tests.json](red-tests.json)。
- 初步 EditMode 規則＋整合 15／15 通過：[focused-tests.json](focused-tests.json)。
- 最終聚焦 **16／16 通過，0 失敗**：[focused-final.txt](focused-final.txt)。包含 8 項純規則、7 項隔離整合及 1 項會切入 Play Mode 的 Single Runner 測試。
- 規則涵蓋換彈中途綁定不補播、起始姿態差、角度限幅、359→1 度、快速轉頭減弱與恢復、取消不採用下一動作、重新綁定清理、軸向轉換、時間步長一致性。
- 整合涵蓋兩份目前正式 Prefab 的啟用模型／Reload Clip（只取樣暫時複本）、父節點旋轉不回授、震動重疊及完成、舊 owner 清理不能影響新 owner、未 Spawn 的安全性、幅度限幅後仍可減弱。
- Single Runner 在 `_Menu`、有效 Spawned 的暫時玩家／兩份 Runtime 下驗證：兩槍動畫可產生鏡頭旋轉、注入每秒 1000 度的當幀輸入可減弱、KCC Look 與彈藥不變、Unbind 當幀清除效果。僅測試實例掛元件與手動取樣，不儲存資產；不是自然遊玩／視覺手感驗收。
- Runtime 測試精確預期既有 `[AttackProfessionRuntimeDriver] 找不到 AttackFocusAbility。` 訊息一次；這是既有獨立 E 遷移診斷，未修改該系統或忽略其他錯誤。
- 測試 fixture 已避開 EnterPlayMode Domain Reload 造成匿名回呼閉包遺失；NUnit／Fusion 的同名型別使用明確命名。以實際編譯後 DLL 與 RunFinished 結果確認，不採用過期組件或 `total=0`。

## 完整回歸

本次 RunFinished：**970 passed／1 failed／1 skipped**，實際執行 971 項（含略過共 972），不是全綠。唯一失敗仍是既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`，1 項 Explicit 略過；新增 16 項全部通過。完整樹見 [regression.txt](regression.txt)。MCP job 在 Domain Reload 後保留舊進度，最終採跨重載 collector，並另確認 TestRunner IsRunActive=false、Editor 已回 `_Menu` 的 Edit Mode。

Unity 已編譯載入本輪程式；沒有本輪新增的編譯錯誤或警告。既有 DemoController.camera 隱藏成員、TankPreparationRuntimeVerification 參考比較與 BattleHudReferenceLayout TextureImporter.spritesheet 過時警告仍保留。

初次程式實作階段開工前後對比 **124 份 Scene／Prefab 的 SHA-256 全部一致**，見 [asset-check.json](asset-check.json)。未儲存、覆寫或自動接線正式資產。

## 邊界

- 未做獨立程序 Host／Client、late join／reconnect、Build 或效能分析。本功能未新增 Networked／RPC，已完成接線，仍需各自驗收本機效果與死亡／重生。
- 正式兩槍 Prefab 已配置起調值；元件 XYZ 0.04、上限 4 度、平滑 0.08 秒、轉頭 120～540 度／秒、最低 0.2、減弱平滑 0.06 秒都是可調起始值。
- 觀戰遠端角色本輪不播放此效果。中途換裝到已換彈的來源不補播。
- 最終舒適度、快速轉頭的實際滑鼠感受、F／換裝中斷、與其他效果疊加仍待人工驗收。

## 後續自動配置（2026-10-17）

使用者明確要求自動配置後，透過 Unity PrefabUtility 載入並儲存兩份 ViewModel；未手改 YAML。

- 手與槍械01：根物件新增 FirstPersonReloadCameraMotion；ActionAnimator 指向既有元件，模型為 RifleVine_Arms_Draft03。
- 手與槍械03：根物件新增同元件；ActionAnimator 指向既有元件，模型為 SMGBloom_Arms_Draft01。
- 兩者 Weapon Root 均指向啟用模型底下的 第一人稱手/骨骼.r/骨骼.001.r/骨骼.002.r/骨骼.003.r/Root。
- 已儲存 XYZ 各 0.04、上限 4 度、跟隨 0.08 秒、轉頭 120～540 度／秒、最低 0.2、減弱反應 0.06 秒。
- 儲存後重新載入驗證：每份恰有一個啟用元件、三個引用有效且模型符合 ActionAnimator；Missing Script 均為 0。
- _Menu 既有 FirstPersonHitCameraShake 已啟用且 CameraEffectsRoot 非空；Scene 保持未修改狀態。
- 配置前後 124 份 Scene／Prefab 雜湊比對，僅上述兩份 Prefab 改變；差異僅新增元件、數值及骨骼引用序列化。
- Console 無 ReloadCamera 相關錯誤。本次僅改資產接線，未重跑實作階段的 16 項聚焦／完整回歸，也未宣稱已完成自然操作手感驗收。