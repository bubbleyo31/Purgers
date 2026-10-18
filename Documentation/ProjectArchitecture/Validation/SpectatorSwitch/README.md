# 觀戰切換與全滅轉場驗證

最後核對日期：2026-10-18（局部）。
核對來源：本次 Spectator Selection／HUD／Portrait Provider、StageFlow Wipe、LocalSceneTransition Wipe、AudioTransitionGain、正式 _Menu 接線。
正式規則：[100 UI 觀戰](../../100_UI觀戰與聊天.md)、[144 共用轉場](../../144_PrePhase4-1_共用控制與轉場.md)。

## 實作與資產

- 固定 Q／E 循環同 Runner 健康／倒地隊友，名單沿用 ColorSlot；失效目標遞補、加入不搶視角，觀戰及復活期間消耗按住輸入。
- 底部中央共用頭像、選單同步玩家名稱、選中與倒地狀態；已建立 SpectatorHUD.prefab、共用圖與 SharedPlayerPortrait.asset，Editor 接入 _Menu 既有 ReferenceFrame。
- 全滅提交成功後本機淡黑與淡音 1.5 秒，雙完成後停 2 秒，當前連線玩家全部回報後才交原 LoadScene；本地玩家相機就緒後恢復 1.5 秒。
- 頭像使用可替換 PlayerPortraitProvider；尚無 Steam SDK／帳號同步或玩家選圖介面。

## 證據

| 層級 | 結果與邊界 |
|---|---|
| Red | [red.json](red.json)：9 項完成且預期失敗，缺少 SelectionRules／WipeClock／原 owner 的 Wipe API。最初 total=0 的未執行回傳已排除 |
| 聚焦 EditMode | [focused.xml](focused.xml)：16 通過、0 失敗；候選資格、循環索引、完成當幀不扣停留、雙完成中斷重算、增益合成及多 owner 釋放、Provider 契約 |
| 全 PurgersRegression | [regression.xml](regression.xml)：1073 通過、4 失敗、1 略過（實際測試案例 1078），不是全綠；明細如下 |
| 受控多人 | [host-client.txt](host-client.txt)：同 Editor Host + 2 Client，26 個檢查通過；測試用暫時 Runner／GameLogic／Stage，不改玩家存檔 |
| 正式資產 | _Menu 下唯一 SpectatorHUD；content、cardsRoot、cardTemplate、previousKey、nextKey、waitingText、portraitProvider、fallbackPortrait 全部非空，Missing Scripts=0，場景無未儲存變更 |
| 視覺 | [hud-preview.png](hud-preview.png)：獨立 Preview Scene 離屏渲染，實際 Prefab／字體／Sprite；已檢視共用圖、名稱、倒地及選中標示。不是自然戰鬥畫面驗收 |
| 編譯／Console | 本次腳本可編譯，無本次新增的編譯錯誤或警告。原有 DemoController、BattleHudReferenceLayout、TankPreparationRuntimeVerification 警告未修改 |

Unity MCP 曾回報 initialization timeout／停留在舊進度；以上測試數採本次 TestRunner RunFinished 保存的 XML，非 MCP 的舊 job 進度，且均為非零案例數。

全回歸非本次修改範圍的失敗：

1. `BossStageAssetTests.GameSceneContainsBossAssetWiring`：Boss Chunk 引用不一致。
2. `EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules`：預期 0.5，資產為 0.25。
3. `EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners`：預期 >10，資產為 10。
4. `ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt`：測試 Field reflection 在第 80 行出現 NullReferenceException。

受控多人涵蓋：死亡後觀戰、單候選、Q／E 公開切換入口、循環、晚加入不搶目標、倒地資格、Client 不可寫死亡、正式 HUD 姓名與頭像、控制鎖、目標死亡遞補、無效／離線候選、雙淡出與停留、延遲 Client 回報屏障、可靠 RPC 身分、取消與恢復生命週期。驗證找到並修正 Client `IsPlayerValid` 不等同遠端連線名單的問題，改用該 Runner 的 ActivePlayers 加有效 PlayerObject。

## 尚未覆蓋

- 物理 Q／E、同按、長按、ESC 開關後重新放鍵的自然鍵盤操作，以及復活瞬間長按技能的自然驗收（未模擬桌面鍵鼠或搶焦點）。
- 獨立程序 Host／Client、真正斷線重連；受控測試包含晚加入及離線，但不是完整 reconnect 場景。
- 正式關卡自然全滅 → 失敗存檔 → 真正 LoadScene(SafeHouse) → 重生的端到端遊玩。受控測試以 Networked pending／RPC 驗證到切場前，並在最後回報前停止 Stage 模擬，未載入正式關卡；恢復測試驅動原 BeginLoad／SceneLoadDone 與已就緒的真實 PlayerLocalView。
- 最終遊戲畫面的 HUD 與其餘戰鬥資訊遮擋、實際聽感、Build 及不同解析度，均未冒充本次離屏／增益值驗證。

建議人工順序：三人進關 → A 正式死亡 → Q／E 往返 → B 倒地／復起 → B 死亡或離線 → 最後 C 正式死亡 → 觀察 1.5 秒淡黑淡音、全黑後 2 秒停留、切場期間全黑靜音 → SafeHouse 自己的相機就緒後 1.5 秒恢復 → 持續按住的舊技能鍵不得自動觸發。

所有背景驗證使用現有 Game View 的 PlayUnfocused 與 Run In Background，沒有呼叫 Focus、Show、前景啟用或 Scene View 截圖。
