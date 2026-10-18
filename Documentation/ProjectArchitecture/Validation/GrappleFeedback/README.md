# 鈎索世界音效與成功出鈎震動驗證

日期：2026-10-17。正式規則與 Inspector：[30 鈎索系統](../../30_鈎索系統.md#2026-10-17-鈎索世界音效與成功出鈎震動)。

## 範圍與保留

使用者授權實作三個指定音檔、距離／方向 3D 聲與成功出鈎震動。等待「討論定點捕食型新怪物設計」完成後才接手 Editor；等待期間在專案外備妥程式，沒有觸發它使用中的 Editor 編譯。套用前對五份既有腳本比對快照雜湊，保存所有原有未提交修改。

原鈎索移動、Q 切換／按住、Gate、能量與職業能力保持原 owner。本機風聲保留；世界拉動聲不隨釋放後慣性持續，也不由 Support Tether 啟動；本次追加的正常結束淡出例外見下節。

## 已完成證據

- 舊程式：新聚焦測試實際執行 14 項，14 項如預期失敗。第一次新測試未匯入的 total=0 不算證據。
- 新程式：聚焦 14／14 通過，0 失敗；包含純拉動條件、世界定位、獨立 handle、重用、上限、失效目標與清理。
- 專案外 dotnet 編譯 0 error，13 項警告均來自原有檔案；這不替代 Unity Fusion weaving。
- Unity 正式匯入／編譯成功，scriptCompilationFailed=false；本次新增程式無編譯錯誤。
- 已用 Editor API 建立 3 個 Cue（ID 20／21／22），Catalog 從 7 增至 10，保留原七項順序。KCC_Player 只增加三個 Cue 引用 override，未覆寫既有人工數值。未修改 Scene。
- 三個 MP3 匯入成功、mono；長度依序為 0.984／2.976／0.648 秒。Cue 完整 3D、線性衰減 4～60、公用 World Mixer fallback、Doppler 0。
- 玩家 Prefab Missing Script=0，三個 Cue 引用核對成功；震動讀值 0.25 秒／1／1.5°。

## 完整回歸、Runtime 與剩餘邊界

- 最終完整回歸實際完成 998 項：995 通過、2 失敗、1 略過（與第一輪一致）。兩項失敗為接手前已存在的 BossStageAssetTests.GameSceneContainsBossAssetWiring 與 ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt（NullReference）。
- 最終程式已通過 Single Runner 18 項、同 Editor Host／Client 24 項明確檢查，以及各階段狀態等待。成功 Q 實測 CameraEffectsRoot 產生位移；遠端輸入由 Host 確認，只對 Input Authority 震動，晚加入／重連恢復當前 Loop，釋放／離線／Despawn 清理成功。
- 自然操作的音量、方向感、Loop 接點、震動舒適度與獨立程序多人驗收。

## 重跑入口

- EditMode：GrappleFeedbackTests、WorldAudioLoopTests。
- 全回歸：Category PurgersRegression；GrappleFeedbackTestResults.Record(name) 保存跨 Domain Reload 的正式 RunFinished XML。
- _Menu 無現存 Runner 的 Play Mode：GrappleFeedbackRuntimeVerification.Begin(false) 或 Begin(true)。這是固定玩家位置的受控驗證，不是自然鍵鼠飛行證據；只建立暫用 Runner／物件並清理，不保存資產。
- 原始輸出：Temp/GrappleFeedback/，完成後選定證據複製至本資料夾。

## 驗證限制與嘗試紀錄

- 初次多人驗證在測試端直接打開 SupportInteractionTetherActive 卻沒有合法 receiver，既有鈎索自動收繩，因此無法恢復；已保留 host-client-harness-attempt1.txt。移除無效人工狀態後，使用正式 Q 流程通過。特殊 Tether 不播放 Loop 的條件仍由聚焦測試覆蓋，沒有修改原本 Tether 玩法。
- 多人測試固定玩家位置以排除連線等待期間的物理漂移；Host／Client 共用同一 Editor 的音效服務與相機環境。這不是獨立程序雙視角聽覺、網路丟包壓力或自然飛行驗收。
- 測試端暫用牆壁只在 Host 的隔離 PhysicsScene，Client 預測查詢可能出現原有 debugGrapple 的無合法鈎點警告；Host 正式事件與同步結果仍通過。不是新編譯警告。
- 未執行獨立 Build、獨立程序多人與效能 Profiling。聲音的主觀音量、方向感、Loop 接點與震動舒適度待使用者人工確認。
最終證據檔：red.xml／focused-final.xml／regression-final.xml／single.txt／host-client.txt。測試結束後僅整理相機新 API 的 XML 註解位置，沒有改變已驗證的方法內容；另確認 Unity 編譯與 Editor 最終狀態。

最終 Editor 核對：已退出 Play Mode；_Menu 場景 isDirty=false，檔案雜湊與接手時一致；scriptCompilationFailed=false，Console error=0。KCC_Player Missing Script=0，三個 Cue 的 Clip、Catalog 登錄與完整 3D 設定皆有效。

## 2026-10-17 後續：Hooking 淡出與再次射出中斷

使用者追加正常結束要淡出，且下次射出強制中斷；指定淡出 0.1～0.2 秒。本次預設 0.15 秒，PlayerGrapple Inspector 的 Grapple Pull Fade Out Duration 可調，無新 Scene／Prefab 引用配置。死亡、停用、離線／Despawn 仍立即清理。

沿用 GameplayAudioService 的世界 Loop 池，新增 FadeOutWorldLoop，線性音量衰減使用 unscaledDeltaTime，不因重複要求而延長。淡出期間仍持有 handle，結束才回收；PlayerGrapple 正式射出時同步遞增序號，Host 立即切掉舊聲，各端在收到新序號的 Render 清理舊 handle，即使中間 Shooting 快照被跳過也有效。新增 Networked 欄位，Host／Client 必須同版。

- 紅燈測試：原四項通過，新增三項如預期因缺少淡出 API 失敗，見 fade-red.xml。
- 聚焦測試：17／17 通過，包含 0.15 秒音量曲線、重複請求、立即中斷、重用音量恢復、零時間與目標消失清理，見 fade-focused.xml。
- Single Runner：21 項明確檢查通過；同 Editor Host／Client：27 項明確檢查通過，見 fade-single.txt、fade-host-client.txt。
- 中斷 Runtime 情境只對測試實例暫設 2 秒淡出，以涵蓋正常收繩及 PreFire 後仍有尾音的情況；證實下一次正式射出在 2 秒自然結束前清除兩端原 handle，再建立新 Loop。測試完成恢復 0.15 秒，不保存 Prefab。
- 前述 Runtime 仍有原紀錄的固定位置、共用音效服務與相機，以及 Host 暫用牆壁等限制；未代替獨立程序聯機或主觀聽感驗收。
- 本輪完整 PurgersRegression 正式 RunFinished：1001 項，998 通過、2 失敗、1 略過，見 fade-regression.xml。失敗名稱與修改前相同：BossStageAssetTests.GameSceneContainsBossAssetWiring、ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt；不宣稱完整回歸全綠。
- 最終 Editor：scriptCompilationFailed=false，Console error=0，已退出 Play Mode；_Menu isDirty=false，SHA256 仍為 05294960D1A9BF6E2AE2565AD27856F0739E83DCB3357AA6326B27DAF13C791C。玩家 Prefab 的 Grapple Pull Fade Out Duration 實讀 0.15。
