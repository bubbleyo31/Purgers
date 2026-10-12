# 空中緩速／子彈時間按下切換驗證

最後核對日期：2026-10-11（局部）。
核對來源：SupportAerialAbility、PlayerActiveAbilityBase、PlayerBulletTimeAbility、AbilityToggleInputTests；Runtime Prefab 僅唯讀。
相關規則：[41 Loadout](../../41_玩家能力Loadout.md)、[42 E 技能](../../42_E主動技能規格與強化預留.md)。

## 已確認規則與修改範圍

- 空中緩速：只在原 GrappleAirborne 且未落地／未冷卻／未被封鎖時按下 E 啟動；放開維持，再按下提前 EndAbility。原落地、離開狀態、職業限制、動量返還、WASD 封鎖與完整冷卻保留。按住跨越啟動條件或冷卻到期不自動重開。
- 子彈時間：作用中再次按下 E 呼叫原 FinishAbility，先停止本 session、開始完整冷卻，再 FlushPendingDamage；沿原傷害／經驗與調色 Owner 收尾。取消不算新施放、不追加後座力；其他 session 不受取消。
- 共用基底 CanEndOnActivePress 預設 false；只有子彈時間覆寫 true，Casting 不受理取消，閃現 Armed 再次施放不變。
- Host／State Authority 執行正式結果，Input Authority 提供 NetInput，各端呈現仍只讀同步狀態。空中緩速舊 Profession 入口新增 LegacyPreviousButtons 網路歷史；正式 Loadout 仍讀 Player 原始 previousButtons。
- Scene／Prefab／Definition 未修改。空中緩速磁碟值：最長 1 s、CD 5 s、倍率 0.5。子彈時間磁碟值：作用 5 s、CD 3 s、前搖 0 s、半徑 50 m、移動倍率 1。不是 C# 建構預設，也不代表所有 Scene Override。

## 自動測試

- [修改前](red-tests.txt)：2 項實際失敗；長按已符合條件仍啟動、子彈時間第二次按鍵沒有結束，與需求相符。
- [修改後聚焦](focused-tests.txt)：36／36，包含 2 項有效 Fusion Spawned 物件的受控 Single Runner 測試（含重疊 session 取消隔離與其他 E 不取消）；另有 5 項延後傷害、14 項技能規則、15 項 HUD／輸入測試。
- [完整 PurgersRegression](regression-tests.txt)：發現 674 項，實際執行 673 項：672 通過、1 失敗；1 項 Explicit 多人測試於一般回歸跳過（另行執行的失敗結果見下節）。既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring` 的 Game Scene Boss 資產引用不符仍失敗，非本次修復範圍。
- 最終格式／文件本機連結及兩項 Prefab 雜湊檢查見 [靜態檢查](static-checks.json)：0 失效連結、資產與修改前逐位元相同。
- Unity 編譯成功，`EditorUtility.scriptCompilationFailed=false`；該次完成後 Console error／warning 為 0。
- MCP 第一次回傳 total=0，作廢；強制匯入並確認新測試載入後重跑。EnterPlayMode／ExitPlayMode 會使 MCP job 狀態遺失，因此以跨 Domain reload 註冊的 TestRunnerApi 回呼保存 NUnit 最終結果，不以 job succeeded 代替測試數。
- 初次測試實例被既有 Attack Runtime 缺少 AttackFocusAbility 的錯誤干擾，改停用不相關職業 Runtime 自動建立；未修補或覆寫人工 Prefab。

## 多人與人工驗收

[受控 Host／Client NUnit 結果](network-tests.txt)：**1 項失敗，不能視為完整多人通過**。同一 Editor 程序的雙 Runner 已連線；[逐階段紀錄](network-progress.txt)顯示下列行為檢查完成：

- Host 啟動、晚加入 Client 收到 Active，Proxy 直接呼叫不能取消 Host 狀態，Host 取消後同步解除。
- Client 重連後讀到已結束，不重新施放。
- 真實 Client NetInput 傳至 Host 的受控固定 Tick 模組入口；兩項能力的首次按下、長按／放開維持、第二次取消均完成同步檢查。
- 子彈時間累積 7 點傷害在 Client 第二次按下後由 Host 結算，Client 生命同步一致，啟動序號不增加。

但 Client 加入／重連期間 Fusion `SimulationConnection.GetObjectData → InvokePlayerJoinedLeft` 記錄 `NullReferenceException`，NUnit 因未處理 Error Log 將整項測試判失敗。未證明根因或它是否影響自然連線，不能宣稱為本次新增／已排除的舊錯誤。此輪不改 SDK 或正式連線系統。測試已標記 Explicit，需要 Photon 連線時以完整方法名稱單獨啟動，普通 PurgersRegression 不依賴外部服務。

受控測試停用 Player／職業 Runtime 自然模擬，專用 Driver 只轉送網路輸入給正式技能模組；測試實例延長作用期限供連線觀察，未保存資產。它不涵蓋自然移動操作、敵人恢復動畫、經驗實際領取、紫色濾鏡畫面與獨立雙程序／兩台機器驗收。

重跑：Unity Test Runner 的 EditMode 明確選取 `AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`。

1. Host 與 Client 分別裝備空中緩速，完成鉤索釋放進入 GrappleAirborne；按一下 E 再放開，應持續到最長時間；第二次新按下立即恢復原動量並開始完整 CD。
2. 落地／離開 GrappleAirborne 仍結束；普通跳躍與地面不能啟動。預先按住 E 再進入合法狀態不能自動啟動。
3. 子彈時間中造成傷害，再按 E；累積傷害應只結算一次、擊殺經驗按既有名單規則、完整 CD 從結束開始。檢查敵人動作恢復與紫色調色正常淡出。
4. 多人重疊時，只結束自己的 session；另一個 session 仍可使對象受影響。晚加入／重連應讀取當下 Active／已結束狀態，不重新施放。
5. 兩項當前 Prefab 時間與倍率由人工保留；子彈時間移動倍率目前為 1，不能將沒有減速誤判為此次回歸。
